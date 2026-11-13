import Foundation

/// This device's sync settings, in sync.json next to data.json. Never inside data.json: a data.json copied to another
/// computer must not bring this device's id along (two devices writing one file is what the design avoids).
public struct SyncConfig: Codable, Equatable {
    public var enabled = false
    /// The folder the user picked (their cloud drive's). The files go in its EzkielNotch-sync subfolder.
    public var folder = ""
    public var deviceId = ""
    public var deviceName = ""
    public var lastSync: Date?
    /// The last problem, in plain words (nil when the last sync worked).
    public var lastError: String?
    /// The other devices seen in the folder at the last sync.
    public var devices: [SyncDevice] = []

    public init() {}

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        enabled = (try? c.decode(Bool.self, forKey: .enabled)) ?? false
        folder = (try? c.decode(String.self, forKey: .folder)) ?? ""
        deviceId = (try? c.decode(String.self, forKey: .deviceId)) ?? ""
        deviceName = (try? c.decode(String.self, forKey: .deviceName)) ?? ""
        lastSync = try? c.decode(Date.self, forKey: .lastSync)
        lastError = try? c.decode(String.self, forKey: .lastError)
        devices = c.lossy([SyncDevice].self, forKey: .devices)
    }

    public static func load(_ url: URL) -> SyncConfig {
        guard let raw = try? Data(contentsOf: url) else { return SyncConfig() }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return (try? decoder.decode(SyncConfig.self, from: raw)) ?? SyncConfig()
    }

    public func save(_ url: URL) {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        guard let data = try? encoder.encode(self) else { return }
        try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try? data.write(to: url, options: .atomic)
    }
}

public struct SyncDevice: Codable, Equatable, Identifiable {
    public var id: String
    public var name: String
    public var platform: String
    /// When that device last wrote its file.
    public var written: Date?
    /// Its file is older than 30 days or from a newer version, so it isn't merged.
    public var ignored: Bool

    public init(id: String, name: String, platform: String, written: Date?, ignored: Bool) {
        self.id = id
        self.name = name
        self.platform = platform
        self.written = written
        self.ignored = ignored
    }
}

public extension AppData {
    /// The synced lists, as data.json writes them.
    var syncedJSON: SyncJSON {
        .object([
            "tasks": SyncJSON.from(tasks) ?? .array([]),
            "notes": SyncJSON.from(notes) ?? .array([]),
            "water": SyncJSON.from(water) ?? .array([]),
            "focusLog": SyncJSON.from(focusLog) ?? .array([]),
        ])
    }

    /// Puts merged lists in. Returns whether anything changed. An entry that can't be read is skipped, never the list.
    @discardableResult
    mutating func applySynced(_ json: SyncJSON) -> Bool {
        func list<T: Decodable>(_ kind: String, _ type: T.Type) -> [T] {
            (json[kind]?.array ?? []).compactMap { $0.decode(T.self) }
        }
        let before = self
        tasks = list("tasks", TaskItem.self)
        notes = list("notes", NoteItem.self)
        water = list("water", WaterEntry.self)
        focusLog = list("focusLog", FocusEntry.self)
        return self != before
    }

    /// Whether this version can load a synced entry. Ones it can't (a newer format, a broken file) are left out of the
    /// merge: never kept here, so never in this device's file or base, so a later sync can't take them for deleted.
    /// The other devices keep them.
    static func canHold(_ kind: String, _ item: SyncJSON) -> Bool {
        switch kind {
        case "tasks": return item.decode(TaskItem.self) != nil
        case "notes": return item.decode(NoteItem.self) != nil
        case "water": return item.decode(WaterEntry.self) != nil
        case "focusLog": return item.decode(FocusEntry.self) != nil
        default: return true
        }
    }
}

/// Reads the sync folder, merges, and writes this device's file. Foundation only, so it runs in the tests too.
public enum SyncRunner {
    public static let folderName = "EzkielNotch-sync"
    /// Even when nothing changed, a device rewrites its file this often so others see it's still around.
    public static let heartbeat: TimeInterval = 3600

    public static func directory(_ root: URL) -> URL { root.appendingPathComponent(folderName, isDirectory: true) }

    public struct Failure: Error, Equatable {
        /// Plain words for the status line.
        public var message: String
    }

    public struct Plan {
        public var result: SyncResult
        /// The id to sync as (a new one when another device turned out to use the same id).
        public var deviceId: String
        public var newDeviceId: Bool
        public var devices: [SyncDevice]
        /// Files in the folder that couldn't be read (half-downloaded, damaged).
        public var unreadable: [String]
        /// Files the cloud drive hasn't downloaded yet (asked for).
        public var waiting: Int
        public var needsWrite: Bool
    }

    /// A device file without the entries this version can't load (see AppData.canHold).
    static func loadable(_ file: SyncJSON) -> SyncJSON {
        guard case .object(var o) = file else { return file }
        for kind in SyncMerge.kinds {
            if let items = o[kind]?.array { o[kind] = .array(items.filter { AppData.canHold(kind, $0) }) }
        }
        return .object(o)
    }

    public static func readJSON(_ url: URL) -> SyncJSON? {
        guard let attrs = try? FileManager.default.attributesOfItem(atPath: url.path),
              (attrs[.size] as? NSNumber)?.intValue ?? 0 <= 20_000_000,
              let data = try? Data(contentsOf: url) else { return nil }
        return SyncJSON.parse(data)
    }

    public static func plan(root: URL, deviceId: String, deviceName: String, platform: String, appVersion: String,
                            local: SyncJSON, base: SyncJSON?, now: Date = Date()) throws -> Plan {
        let fm = FileManager.default
        var isDir: ObjCBool = false
        guard !root.path.isEmpty, fm.fileExists(atPath: root.path, isDirectory: &isDir), isDir.boolValue else {
            throw Failure(message: "The sync folder isn't there. Is the drive connected?")
        }
        let dir = directory(root)
        do {
            try fm.createDirectory(at: dir, withIntermediateDirectories: true)
        } catch {
            throw Failure(message: "Can't write to the sync folder")
        }
        guard let names = try? fm.contentsOfDirectory(atPath: dir.path) else {
            throw Failure(message: "Can't read the sync folder")
        }

        let ownName = "\(deviceId).json"
        var remotes: [SyncJSON] = []
        var own: SyncJSON?
        var unreadable: [String] = []
        var waiting = 0
        for name in names.sorted() {
            if name.hasPrefix(".") {
                // iCloud Drive keeps files it hasn't downloaded as ".<name>.icloud".
                if name.hasSuffix(".json.icloud") {
                    waiting += 1
                    #if os(macOS)
                    let real = String(name.dropFirst().dropLast(".icloud".count))
                    try? fm.startDownloadingUbiquitousItem(at: dir.appendingPathComponent(real))
                    #endif
                }
                continue
            }
            guard name.lowercased().hasSuffix(".json") else { continue }
            guard let file = readJSON(dir.appendingPathComponent(name)), file["deviceId"]?.string != nil else {
                unreadable.append(name)
                continue
            }
            if name == ownName, file["deviceId"]?.string?.uppercased() == deviceId.uppercased() {
                own = file
            } else {
                remotes.append(loadable(file))
            }
        }

        var id = deviceId
        var newDeviceId = false
        if let own {
            if let base, own["written"] == base["written"] {
                // It's what this device wrote last time: already in the base.
            } else {
                // Not what this device wrote: its own older copy (the base was lost) or another computer that was
                // cloned from this one. Its data is merged either way; when a base exists, the other computer keeps
                // the file and this device takes a new id.
                remotes.append(loadable(own))
                if base != nil {
                    id = UUID().uuidString
                    newDeviceId = true
                }
            }
        }

        let result = SyncMerge.merge(SyncInput(deviceId: id, deviceName: deviceName, platform: platform, appVersion: appVersion,
                                               now: now, local: local, base: base, remotes: remotes))

        // The other devices, newest file per device.
        var devices: [String: SyncDevice] = [:]
        for file in remotes {
            guard let fid = file["deviceId"]?.string, fid.uppercased() != id.uppercased() else { continue }
            let written = SyncTime.date(file["written"])
            let ignored = result.skipped.contains(fid)
            let device = SyncDevice(id: fid, name: file["deviceName"]?.string ?? "", platform: file["platform"]?.string ?? "",
                                    written: written, ignored: ignored)
            if let current = devices[fid.uppercased()], (current.written ?? .distantPast) >= (written ?? .distantPast) { continue }
            devices[fid.uppercased()] = device
        }
        let deviceList = devices.values.sorted { ($0.written ?? .distantPast) > ($1.written ?? .distantPast) }

        var needsWrite = newDeviceId || base == nil || own == nil
        if !needsWrite, let base {
            var a = result.file.object ?? [:], b = base.object ?? [:]
            a["written"] = nil
            b["written"] = nil
            let baseWritten = SyncTime.ms(base["written"]) ?? 0
            needsWrite = a != b || SyncTime.ms(now) - baseWritten >= Int64(heartbeat * 1000)
        }
        return Plan(result: result, deviceId: id, newDeviceId: newDeviceId, devices: deviceList,
                    unreadable: unreadable, waiting: waiting, needsWrite: needsWrite)
    }

    /// After data.json has the merged lists: the base first, then this device's file.
    public static func commit(_ plan: Plan, root: URL, baseURL: URL) throws {
        guard plan.needsWrite else { return }
        let data = plan.result.file.encoded(pretty: true)
        do {
            try FileManager.default.createDirectory(at: baseURL.deletingLastPathComponent(), withIntermediateDirectories: true)
            try data.write(to: baseURL, options: .atomic)
        } catch {
            throw Failure(message: "Couldn't save the sync state")
        }
        do {
            try data.write(to: directory(root).appendingPathComponent("\(plan.deviceId).json"), options: .atomic)
        } catch {
            throw Failure(message: "Can't write to the sync folder")
        }
    }

    /// Counts for "what will merge": per list, how many items come in, change and go.
    public static func summary(_ changes: [SyncChange]) -> [String: (added: Int, updated: Int, removed: Int)] {
        var out: [String: (added: Int, updated: Int, removed: Int)] = [:]
        for c in changes {
            var v = out[c.kind] ?? (0, 0, 0)
            switch c.change {
            case "added": v.added += 1
            case "updated": v.updated += 1
            default: v.removed += 1
            }
            out[c.kind] = v
        }
        return out
    }
}
