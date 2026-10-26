import AppKit

/// Pounce: notices new screenshots (Spotlight's kMDItemIsScreenCapture, wherever they are saved)
/// and downloads that finish in ~/Downloads. Each watcher only runs while its setting is on;
/// the first read of Desktop/Downloads makes macOS ask for permission.
@MainActor
final class PounceWatcher {
    var onScreenshot: ((URL) -> Void)?
    var onDownload: ((URL) -> Void)?

    private var query: NSMetadataQuery?
    private var queryObserver: NSObjectProtocol?
    private var seenScreenshots = Set<String>()

    private var downloadsSource: DispatchSourceFileSystemObject?
    private var knownDownloads = Set<String>()
    private var scanWork: DispatchWorkItem?

    /// Browsers and download managers write under these names until the file is complete.
    private static let partialExtensions: Set<String> = ["crdownload", "download", "part", "partial", "tmp", "opdownload"]

    // MARK: Screenshots

    func setScreenshots(_ on: Bool) {
        guard on != (query != nil) else { return }
        if !on {
            query?.stop()
            if let queryObserver { NotificationCenter.default.removeObserver(queryObserver) }
            query = nil
            queryObserver = nil
            return
        }
        let q = NSMetadataQuery()
        q.predicate = NSPredicate(format: "kMDItemIsScreenCapture == 1 AND kMDItemFSCreationDate >= %@", Date() as NSDate)
        q.searchScopes = [NSMetadataQueryUserHomeScope]
        q.notificationBatchingInterval = 0.4
        queryObserver = NotificationCenter.default.addObserver(forName: .NSMetadataQueryDidUpdate, object: q, queue: .main) { [weak self] note in
            let added = note.userInfo?[NSMetadataQueryUpdateAddedItemsKey] as? [NSMetadataItem] ?? []
            let paths = added.compactMap { $0.value(forAttribute: NSMetadataItemPathKey) as? String }
            MainActor.assumeIsolated { self?.screenshotsAdded(paths) }
        }
        query = q
        q.start()
    }

    private func screenshotsAdded(_ paths: [String]) {
        for path in paths where !seenScreenshots.contains(path) {
            seenScreenshots.insert(path)
            onScreenshot?(URL(fileURLWithPath: path))
        }
    }

    // MARK: Downloads

    static var downloadsURL: URL? { FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first }

    func setDownloads(_ on: Bool) {
        guard on != (downloadsSource != nil) else { return }
        if !on {
            downloadsSource?.cancel()
            downloadsSource = nil
            return
        }
        guard let dir = Self.downloadsURL else { return }
        let fd = open(dir.path, O_EVTONLY)
        guard fd >= 0 else { return }
        knownDownloads = Set(Self.completedFiles(in: dir).map(\.lastPathComponent))
        let source = DispatchSource.makeFileSystemObjectSource(fileDescriptor: fd, eventMask: [.write, .rename, .link], queue: .main)
        source.setEventHandler { [weak self] in
            MainActor.assumeIsolated { self?.scheduleScan() }
        }
        source.setCancelHandler { close(fd) }
        downloadsSource = source
        source.resume()
    }

    /// A finished download usually arrives as a rename from its partial name; wait for writes to settle.
    private func scheduleScan() {
        scanWork?.cancel()
        let work = DispatchWorkItem { [weak self] in self?.scanDownloads() }
        scanWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.2, execute: work)
    }

    private func scanDownloads() {
        guard let dir = Self.downloadsURL else { return }
        let files = Self.completedFiles(in: dir)
        let names = Set(files.map(\.lastPathComponent))
        for file in files where !knownDownloads.contains(file.lastPathComponent) {
            onDownload?(file)
        }
        knownDownloads = names
    }

    private static func completedFiles(in dir: URL) -> [URL] {
        let keys: [URLResourceKey] = [.isRegularFileKey, .isPackageKey, .isDirectoryKey]
        let items = (try? FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: keys,
                                                                   options: [.skipsHiddenFiles])) ?? []
        return items.filter { url in
            guard !partialExtensions.contains(url.pathExtension.lowercased()) else { return false }
            let values = try? url.resourceValues(forKeys: Set(keys))
            return values?.isRegularFile == true || values?.isPackage == true
        }
    }
}
