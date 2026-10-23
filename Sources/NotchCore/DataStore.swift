import Foundation

/// Reads and writes `AppData` as JSON. Writes are atomic and happen off the main thread.
public final class DataStore {
    public let url: URL
    private let queue = DispatchQueue(label: "store.ezkielyna.notch.datastore", qos: .utility)

    public init(url: URL) {
        self.url = url
    }

    public static func defaultURL() -> URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? URL(fileURLWithPath: NSHomeDirectory()).appendingPathComponent("Library/Application Support")
        return base.appendingPathComponent("EzkielNotch", isDirectory: true).appendingPathComponent("data.json")
    }

    public func load() -> AppData {
        guard let data = try? Data(contentsOf: url) else { return AppData() }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return (try? decoder.decode(AppData.self, from: data)) ?? AppData()
    }

    public func save(_ appData: AppData, synchronously: Bool = false) {
        let target = url
        let work = {
            let encoder = JSONEncoder()
            encoder.dateEncodingStrategy = .iso8601
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            guard let data = try? encoder.encode(appData) else { return }
            try? FileManager.default.createDirectory(at: target.deletingLastPathComponent(), withIntermediateDirectories: true)
            try? data.write(to: target, options: .atomic)
        }
        if synchronously {
            queue.sync(execute: work)
        } else {
            queue.async(execute: work)
        }
    }
}
