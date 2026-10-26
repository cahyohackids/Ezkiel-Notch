import AppKit
import NotchCore
import SwiftUI

/// Roar: unread counts from other apps' Dock badges (WhatsApp, Telegram, Teams, Mail, Slack, …).
/// Reads the badge LaunchServices keeps for every running app, the same one `lsappinfo` prints:
/// no permission, no message content, nothing leaves the Mac.
@MainActor
final class BadgeWatcher: ObservableObject {
    struct Badge: Identifiable, Equatable {
        let bundleID: String
        let name: String
        let label: String
        let count: Int
        /// When this count appeared; the list shows the latest first.
        var since: Date
        var id: String { bundleID }
    }

    @Published private(set) var badges: [Badge] = []
    /// An app's count went up. Not called for what is already there when watching starts.
    var onIncrease: ((Badge, _ before: Int) -> Void)?

    private var timer: Timer?
    private var primed = false
    private var reading = false
    private var simulated = false
    /// Paused while the screens sleep or the user switched away (fast user switching).
    private var asleep = false
    private var observers: [NSObjectProtocol] = []
    /// Counts of apps that quit recently: relaunching WhatsApp with 5 unread isn't 5 new messages.
    private var recentlyQuit: [String: (count: Int, at: Date)] = [:]

    init() {
        let ws = NSWorkspace.shared.notificationCenter
        for name in [NSWorkspace.screensDidSleepNotification, NSWorkspace.sessionDidResignActiveNotification] {
            observers.append(ws.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.asleep = true }
            })
        }
        for name in [NSWorkspace.screensDidWakeNotification, NSWorkspace.sessionDidBecomeActiveNotification, NSWorkspace.didWakeNotification] {
            observers.append(ws.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated {
                    guard let self else { return }
                    self.asleep = false
                    // What piled up while away shows in the list, not as a burst of peeks.
                    self.primed = false
                    self.refresh()
                }
            })
        }
    }
    private let queue = DispatchQueue(label: "store.ezkielyna.notch.badges", qos: .utility)

    func setEnabled(_ on: Bool) {
        guard on != (timer != nil) else { return }
        timer?.invalidate()
        timer = nil
        guard on else {
            primed = false
            if !simulated { badges = [] }
            return
        }
        // 12 ms of work for all running apps, off the main thread; badges rarely need to be faster than this.
        let t = Timer(timeInterval: 3, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.refresh() }
        }
        t.tolerance = 1
        RunLoop.main.add(t, forMode: .common)
        timer = t
        refresh()
    }

    func refresh() {
        guard !simulated, !reading, !asleep else { return }
        let own = Bundle.main.bundleIdentifier
        let apps: [(pid: pid_t, id: String, name: String)] = NSWorkspace.shared.runningApplications.compactMap { app in
            guard app.activationPolicy == .regular, let id = app.bundleIdentifier, id != own else { return nil }
            return (app.processIdentifier, id, app.localizedName ?? id)
        }
        reading = true
        queue.async { [weak self] in
            let found = apps.compactMap { app -> (id: String, name: String, label: String)? in
                BadgeReader.label(pid: app.pid, bundleID: app.id).map { (app.id, app.name, $0) }
            }
            let running = Set(apps.map(\.id))
            DispatchQueue.main.async {
                MainActor.assumeIsolated { self?.apply(found, running: running) }
            }
        }
    }

    private func apply(_ found: [(id: String, name: String, label: String)], running: Set<String>) {
        reading = false
        guard timer != nil, !simulated else { return }
        let now = Date()
        let old = Dictionary(badges.map { ($0.bundleID, $0) }, uniquingKeysWith: { a, _ in a })
        for b in badges where !running.contains(b.bundleID) { recentlyQuit[b.bundleID] = (b.count, now) }
        recentlyQuit = recentlyQuit.filter { now.timeIntervalSince($0.value.at) < 600 }
        var next: [Badge] = []
        for f in found {
            guard let count = BadgeLabel.count(f.label) else { continue }
            let before = old[f.id]
            let carried = before == nil ? recentlyQuit.removeValue(forKey: f.id)?.count : nil
            let since = (before?.count == count ? before?.since : nil) ?? Date()
            let badge = Badge(bundleID: f.id, name: f.name, label: f.label, count: count, since: since)
            next.append(badge)
            let previous = before?.count ?? carried ?? 0
            if primed, count > previous { onIncrease?(badge, previous) }
        }
        next.sort { $0.since > $1.since }
        primed = true
        if next != badges { withAnimation(Theme.smooth) { badges = next } }
    }

    /// Self-test and snapshots: fixed badges, no reading.
    func simulate(_ badges: [Badge]) {
        simulated = true
        self.badges = badges
    }
}

/// Reads one running app's Dock badge text ("8", "99+", "•").
enum BadgeReader {
    private typealias CreateASN = @convention(c) (CFAllocator?, pid_t) -> Unmanaged<CFTypeRef>?
    private typealias CopyItem = @convention(c) (Int32, CFTypeRef, CFString) -> Unmanaged<CFTypeRef>?
    /// kLSDefaultSessionID
    private static let session: Int32 = -2

    /// LaunchServices' own lookup (what `lsappinfo` uses). If a future macOS drops it, we fall back to the tool.
    private static let api: (create: CreateASN, copy: CopyItem)? = {
        guard let handle = dlopen("/System/Library/Frameworks/CoreServices.framework/CoreServices", RTLD_LAZY),
              let create = dlsym(handle, "_LSASNCreateWithPid"),
              let copy = dlsym(handle, "_LSCopyApplicationInformationItem") else { return nil }
        return (unsafeBitCast(create, to: CreateASN.self), unsafeBitCast(copy, to: CopyItem.self))
    }()

    static func label(pid: pid_t, bundleID: String) -> String? {
        guard let api else { return lsappinfo(bundleID) }
        guard let asn = api.create(nil, pid)?.takeRetainedValue(),
              let value = api.copy(session, asn, "StatusLabel" as CFString)?.takeRetainedValue(),
              let label = (value as? [String: Any])?["label"] else { return nil }
        return "\(label)"
    }

    /// `lsappinfo info -only StatusLabel <id>` prints `"StatusLabel"={ "label"="8" }`.
    private static func lsappinfo(_ bundleID: String) -> String? {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/lsappinfo")
        p.arguments = ["info", "-only", "StatusLabel", bundleID]
        let out = Pipe()
        p.standardOutput = out
        p.standardError = FileHandle.nullDevice
        guard (try? p.run()) != nil else { return nil }
        let data = out.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        guard let text = String(data: data, encoding: .utf8),
              let range = text.range(of: #""label"="([^"]*)""#, options: .regularExpression) else { return nil }
        return String(text[range].dropFirst(9).dropLast())
    }
}
