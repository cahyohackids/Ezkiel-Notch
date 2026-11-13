import AppKit
import Foundation
import NotchCore
import SystemConfiguration

/// Sync with other Macs and PCs through a folder the user's cloud drive syncs (iCloud Drive, OneDrive, Dropbox…).
/// The merging is NotchCore's SyncMerge; this runs it: on launch, ~5 s after local changes, when another device's
/// file changes (a folder watcher, and every 60 s since cloud drives don't always tell), and on "Sync now".
@MainActor
final class SyncController: ObservableObject {
    @Published private(set) var config: SyncConfig
    @Published private(set) var syncing = false
    /// Files the cloud drive hasn't downloaded yet, at the last sync.
    @Published private(set) var waiting = 0
    /// What a first sync with a newly picked folder would do, waiting for "Start syncing".
    @Published private(set) var preview: Preview?

    struct Preview: Equatable {
        var folder: String
        var lines: [String]
    }

    weak var model: AppModel?
    let configURL: URL
    let baseURL: URL
    let dataURL: URL
    private let queue = DispatchQueue(label: "store.ezkielyna.notch.sync", qos: .utility)
    private var debounce: DispatchWorkItem?
    private var timer: Timer?
    private var watcher: DispatchSourceFileSystemObject?
    private var lastSynced: SyncJSON?
    private var again = false
    private var lastPeek = Date.distantPast

    init(dataURL: URL) {
        self.dataURL = dataURL
        let dir = dataURL.deletingLastPathComponent()
        configURL = dir.appendingPathComponent("sync.json")
        baseURL = dir.appendingPathComponent("sync-base.json")
        var c = SyncConfig.load(configURL)
        if c.deviceId.isEmpty { c.deviceId = UUID().uuidString }
        // The computer's name from Sharing settings (Host.current() can stall on a DNS lookup).
        if c.deviceName.isEmpty { c.deviceName = (SCDynamicStoreCopyComputerName(nil, nil) as String?) ?? "Mac" }
        config = c
    }

    /// On launch: sync once and start watching.
    func start() {
        guard config.enabled else { return }
        watch()
        sync()
    }

    // MARK: Triggers

    /// AppModel's data changed: sync ~5 s after the last change, if a synced list changed.
    func localChanged() {
        guard config.enabled else { return }
        schedule(after: 5)
    }

    private func schedule(after delay: TimeInterval, force: Bool = false) {
        debounce?.cancel()
        let work = DispatchWorkItem { [weak self] in
            guard let self, let model = self.model else { return }
            if force || self.lastSynced == nil || model.data.syncedJSON != self.lastSynced { self.sync() }
        }
        debounce = work
        DispatchQueue.main.asyncAfter(deadline: .now() + delay, execute: work)
    }

    func syncNow() { sync() }

    private func watch() {
        unwatch()
        let t = Timer(timeInterval: 60, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.sync() }
        }
        t.tolerance = 10
        RunLoop.main.add(t, forMode: .common)
        timer = t

        let dir = SyncRunner.directory(URL(fileURLWithPath: config.folder))
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let fd = open(dir.path, O_EVTONLY)
        guard fd >= 0 else { return }
        let source = DispatchSource.makeFileSystemObjectSource(fileDescriptor: fd, eventMask: [.write, .rename, .delete], queue: .main)
        source.setEventHandler { [weak self] in
            MainActor.assumeIsolated {
                guard let self else { return }
                // A file appearing or being replaced: give the cloud drive a moment to finish writing it.
                self.debounce?.cancel()
                let work = DispatchWorkItem { [weak self] in self?.sync() }
                self.debounce = work
                DispatchQueue.main.asyncAfter(deadline: .now() + 2, execute: work)
            }
        }
        source.setCancelHandler { close(fd) }
        source.resume()
        watcher = source
    }

    private func unwatch() {
        timer?.invalidate()
        timer = nil
        watcher?.cancel()
        watcher = nil
    }

    // MARK: Syncing

    private func sync() {
        guard config.enabled, !config.folder.isEmpty, let model else { return }
        if syncing {
            again = true
            return
        }
        syncing = true
        let snapshot = model.data.syncedJSON
        let cfg = config, baseURL = baseURL, version = AppModel.version
        queue.async {
            let outcome = Result {
                try SyncRunner.plan(root: URL(fileURLWithPath: cfg.folder), deviceId: cfg.deviceId, deviceName: cfg.deviceName,
                                    platform: "mac", appVersion: version, local: snapshot,
                                    base: SyncRunner.readJSON(baseURL))
            }
            DispatchQueue.main.async { self.finish(outcome, snapshot: snapshot) }
        }
    }

    private func finish(_ outcome: Result<SyncRunner.Plan, Error>, snapshot: SyncJSON) {
        guard let model, config.enabled else { return done() }
        let plan: SyncRunner.Plan
        switch outcome {
        case .failure(let error):
            fail(error)
            return done()
        case .success(let p):
            plan = p
        }
        // Edited while the folder was read: start over with the new data, nothing written.
        guard model.data.syncedJSON == snapshot else {
            again = true
            return done()
        }
        if plan.newDeviceId { config.deviceId = plan.deviceId }
        var data = model.data
        if data.applySynced(plan.result.data) {
            // Changes from elsewhere: the data as it was stays next to data.json until the next such sync.
            if !plan.result.changes.isEmpty { backup(to: "data.json.sync-prev", replacing: true) }
            model.applySynced(data)
        }
        lastSynced = model.data.syncedJSON
        waiting = plan.waiting
        let root = URL(fileURLWithPath: config.folder), baseURL = baseURL
        queue.async {
            let error: Error? = { do { try SyncRunner.commit(plan, root: root, baseURL: baseURL); return nil } catch { return error } }()
            DispatchQueue.main.async {
                if let error {
                    self.fail(error)
                } else {
                    self.config.lastSync = Date()
                    self.config.lastError = plan.unreadable.isEmpty ? nil : L("Couldn't read %@ · it may still be syncing", plan.unreadable.joined(separator: ", "))
                    self.config.devices = plan.devices
                    self.config.save(self.configURL)
                    self.announce(plan)
                }
                self.done()
            }
        }
    }

    private func done() {
        syncing = false
        if again {
            again = false
            schedule(after: 1, force: true)
        }
    }

    private func fail(_ error: Error) {
        config.lastError = (error as? SyncRunner.Failure)?.message ?? error.localizedDescription
        config.save(configURL)
    }

    /// "Synced from <device>", only when another device changed something here, at most every 5 minutes.
    private func announce(_ plan: SyncRunner.Plan) {
        let fromOthers = plan.result.changes.filter { $0.from.uppercased() != plan.deviceId.uppercased() }
        guard let first = fromOthers.first, Date().timeIntervalSince(lastPeek) > 300 else { return }
        lastPeek = Date()
        let name = plan.devices.first { $0.id == first.from }?.name ?? ""
        model?.showSyncPeek(from: name.isEmpty ? L("another device") : name, changes: fromOthers.count)
    }

    private func backup(to name: String, replacing: Bool) {
        let fm = FileManager.default
        let target = dataURL.deletingLastPathComponent().appendingPathComponent(name)
        if fm.fileExists(atPath: target.path) {
            guard replacing else { return }
            try? fm.removeItem(at: target)
        }
        model?.saveNow()
        try? fm.copyItem(at: dataURL, to: target)
    }

    // MARK: Settings

    /// The folder picker. The first sync with it waits for "Start syncing" with a preview of what will merge.
    func chooseFolder() {
        guard let model else { return }
        model.block("syncPanel")
        model.collapse(explicit: false)
        let picker = NSOpenPanel()
        picker.title = L("Pick a folder your cloud drive syncs")
        picker.message = L("iCloud Drive, OneDrive, Google Drive, Dropbox… Every device picks the same folder.")
        picker.prompt = L("Use this folder")
        picker.canChooseDirectories = true
        picker.canChooseFiles = false
        picker.canCreateDirectories = true
        picker.directoryURL = FileManager.default.urls(for: .libraryDirectory, in: .userDomainMask).first?
            .appendingPathComponent("Mobile Documents/com~apple~CloudDocs")
        NSApp.activate(ignoringOtherApps: true)
        picker.begin { [weak self] response in
            DispatchQueue.main.async {
                guard let self else { return }
                model.unblock("syncPanel")
                model.expand(.settings)
                if response == .OK, let url = picker.url { self.prepare(folder: url.path) }
            }
        }
    }

    /// Reads the folder and says what the first sync would bring in. Nothing is written yet.
    func prepare(folder: String) {
        guard let model else { return }
        let snapshot = model.data.syncedJSON
        let cfg = config, baseURL = baseURL, version = AppModel.version
        queue.async {
            let outcome = Result {
                try SyncRunner.plan(root: URL(fileURLWithPath: folder), deviceId: cfg.deviceId, deviceName: cfg.deviceName,
                                    platform: "mac", appVersion: version, local: snapshot, base: SyncRunner.readJSON(baseURL))
            }
            DispatchQueue.main.async {
                switch outcome {
                case .failure(let error):
                    self.preview = Preview(folder: folder, lines: [L((error as? SyncRunner.Failure)?.message ?? error.localizedDescription)])
                case .success(let plan):
                    self.preview = Preview(folder: folder, lines: Self.describe(plan))
                }
            }
        }
    }

    static func describe(_ plan: SyncRunner.Plan) -> [String] {
        var lines: [String] = []
        let others = plan.devices.filter { !$0.ignored }.map { $0.name.isEmpty ? L("another device") : $0.name }
        if others.isEmpty {
            lines.append(L("No other devices in this folder yet. This Mac's data becomes the first copy."))
        } else {
            lines.append(L("Found: %@", others.joined(separator: ", ")))
            let summary = SyncRunner.summary(plan.result.changes)
            let names = ["tasks": L("tasks"), "notes": L("notes"), "water": L("drinks"), "focusLog": L("focus sessions")]
            for kind in SyncMerge.kinds {
                guard let s = summary[kind], let name = names[kind] else { continue }
                var parts: [String] = []
                if s.added > 0 { parts.append(L("%d new", s.added)) }
                if s.updated > 0 { parts.append(L("%d updated", s.updated)) }
                if s.removed > 0 { parts.append(L("%d removed", s.removed)) }
                lines.append("\(name.prefix(1).uppercased() + name.dropFirst()): \(parts.joined(separator: " · "))")
            }
            if summary.isEmpty { lines.append(L("Nothing here changes. Your data is sent to the other devices.")) }
        }
        lines.append(L("Your data is backed up first as data.json.before-sync."))
        return lines
    }

    func confirmPreview() {
        guard let preview else { return }
        backup(to: "data.json.before-sync", replacing: false)
        config.folder = preview.folder
        config.enabled = true
        config.lastError = nil
        config.save(configURL)
        self.preview = nil
        watch()
        sync()
    }

    func cancelPreview() { preview = nil }

    /// Stop syncing on this device. Its data stays; its file stays in the folder for the others (who keep the data).
    func stop() {
        unwatch()
        debounce?.cancel()
        config.enabled = false
        config.lastError = nil
        config.save(configURL)
        preview = nil
    }

    func setDeviceName(_ name: String) {
        let clean = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !clean.isEmpty, clean != config.deviceName else { return }
        config.deviceName = clean
        config.save(configURL)
        sync()
    }

    /// The status line: the last sync or the problem, in plain words.
    var status: String {
        guard config.enabled else { return L("Off · your data stays on this Mac") }
        if syncing { return L("Syncing…") }
        if let error = config.lastError { return L(error) }
        guard let at = config.lastSync else { return L("Waiting for the first sync…") }
        var s = L("Synced %@", at.formatted(date: .omitted, time: .shortened))
        if waiting > 0 { s += " · " + L("%d files still downloading", waiting) }
        return s
    }

    // MARK: Self-test

    /// Sync against a folder without the picker or the preview (the self-test's fake devices).
    func simulate(folder: String, name: String) {
        config.folder = folder
        config.deviceName = name
        config.enabled = true
        config.save(configURL)
        sync()
    }
}
