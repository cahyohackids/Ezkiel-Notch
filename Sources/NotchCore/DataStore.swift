import Foundation

/// Reads and writes `AppData` as JSON. Writes are atomic and happen off the main thread.
/// A file that can't be read is never overwritten: it is set aside (and the daily backup used if it reads).
public final class DataStore {
    public let url: URL
    private let queue = DispatchQueue(label: "store.ezkielyna.notch.datastore", qos: .utility)

    public enum Recovery: Equatable {
        /// data.json couldn't be read; it was kept as `broken` and the backup from `backupDate` was loaded.
        case restoredBackup(broken: URL, backupDate: Date?)
        /// data.json couldn't be read and there was no usable backup; it was kept as `broken`.
        case startedFresh(broken: URL)
    }

    /// Set by `load()` when the file had to be set aside.
    public private(set) var recovery: Recovery?

    public init(url: URL) {
        self.url = url
    }

    public static func defaultURL() -> URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? URL(fileURLWithPath: NSHomeDirectory()).appendingPathComponent("Library/Application Support")
        return base.appendingPathComponent("EzkielNotch", isDirectory: true).appendingPathComponent("data.json")
    }

    /// One copy per day of the last file that read fine.
    public var backupURL: URL { url.appendingPathExtension("bak") }

    public func load(now: Date = Date()) -> AppData {
        recovery = nil
        let fm = FileManager.default
        guard let raw = try? Data(contentsOf: url) else { return AppData() }
        if let data = Self.decode(raw) {
            refreshBackup(now: now)
            return data
        }
        let stamp = Self.stamp(now)
        let broken = url.deletingLastPathComponent().appendingPathComponent("data.broken-\(stamp).json")
        try? fm.moveItem(at: url, to: broken)
        if let backup = try? Data(contentsOf: backupURL), let data = Self.decode(backup) {
            let date = (try? fm.attributesOfItem(atPath: backupURL.path))?[.modificationDate] as? Date
            recovery = .restoredBackup(broken: broken, backupDate: date)
            return data
        }
        recovery = .startedFresh(broken: broken)
        return AppData()
    }

    private static func decode(_ raw: Data) -> AppData? {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return try? decoder.decode(AppData.self, from: raw)
    }

    private static func stamp(_ date: Date) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyyMMdd-HHmmss"
        return f.string(from: date)
    }

    private func refreshBackup(now: Date) {
        let fm = FileManager.default
        let modified = (try? fm.attributesOfItem(atPath: backupURL.path))?[.modificationDate] as? Date
        if let modified, now.timeIntervalSince(modified) < 20 * 3600 { return }
        try? fm.removeItem(at: backupURL)
        try? fm.copyItem(at: url, to: backupURL)
    }

    public func save(_ appData: AppData, synchronously: Bool = false) {
        let target = url
        let work = {
            let encoder = JSONEncoder()
            encoder.dateEncodingStrategy = .iso8601
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            do {
                let data = try encoder.encode(appData)
                try FileManager.default.createDirectory(at: target.deletingLastPathComponent(), withIntermediateDirectories: true)
                try data.write(to: target, options: .atomic)
            } catch {
                NSLog("Ezkiel Notch: couldn't save %@: %@", target.path, String(describing: error))
            }
        }
        if synchronously {
            queue.sync(execute: work)
        } else {
            queue.async(execute: work)
        }
    }
}
