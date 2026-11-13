import AppKit
import NotchCore

/// Pounce: notices new screenshots (Spotlight's kMDItemIsScreenCapture, wherever they are saved)
/// and downloads that finish in ~/Downloads. Each watcher only runs while its setting is on;
/// the first read of Desktop/Downloads makes macOS ask for permission.
/// With live downloads on, it also follows the partial files browsers write while downloading
/// (.crdownload, .part, Safari's .download bundle), reading their size once a second only while one exists.
@MainActor
final class PounceWatcher {
    var onScreenshot: ((URL) -> Void)?
    /// A finished download, and whether it was followed while in progress (so it peeks with live downloads on).
    var onDownload: ((URL, Bool) -> Void)?
    /// The downloads in progress changed (sizes, speed, one started or ended).
    var onProgress: (([ActiveDownload]) -> Void)?

    private var query: NSMetadataQuery?
    private var queryObserver: NSObjectProtocol?
    private var seenScreenshots = Set<String>()

    private var downloadsSource: DispatchSourceFileSystemObject?
    private var knownDownloads = Set<String>()
    private var scanWork: DispatchWorkItem?
    private var followsLive = false
    private var tracker = DownloadTracker()
    private var poll: Timer?

    /// Browsers and download managers write under these names until the file is complete.
    private static let partialExtensions: Set<String> = Set(DownloadTracker.partialExtensions + ["tmp"])

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

    /// Self-test: a temp folder with fake partial downloads instead of ~/Downloads.
    static var folderOverride: URL?

    static var downloadsURL: URL? { folderOverride ?? FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first }

    /// `finished`: Pounce on downloads (a peek for every new file). `live`: follow downloads in progress.
    func setDownloads(finished: Bool, live: Bool) {
        let on = finished || live
        if live != followsLive {
            followsLive = live
            if !live { setTracked([]) }
        }
        guard on != (downloadsSource != nil) else {
            if on && live { scheduleScan() }
            return
        }
        if !on {
            downloadsSource?.cancel()
            downloadsSource = nil
            setTracked([])
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
        if live { scanDownloads() }
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
        var followed = Set<String>()
        var inProgress = Set<String>()
        if followsLive {
            let samples = Self.partialSamples(in: dir)
            followed = Set(tracker.update(samples, completed: names))
            inProgress = Set(samples.map(\.finalName))
            setTracked(tracker.active)
            for name in followed.sorted() { onDownload?(dir.appendingPathComponent(name), true) }
        }
        // Firefox puts an empty file under the final name while it downloads: that one waits for its .part to go.
        for file in files where !knownDownloads.contains(file.lastPathComponent)
            && !followed.contains(file.lastPathComponent) && !inProgress.contains(file.lastPathComponent) {
            onDownload?(file, false)
        }
        knownDownloads = names.subtracting(inProgress)
    }

    /// Reports the downloads in progress, and reads their sizes once a second only while there are some.
    private func setTracked(_ active: [ActiveDownload]) {
        if active.isEmpty {
            poll?.invalidate()
            poll = nil
            if !followsLive { tracker = DownloadTracker() }
        } else if poll == nil {
            let t = Timer(timeInterval: 1, repeats: true) { [weak self] _ in
                MainActor.assumeIsolated { self?.scanDownloads() }
            }
            t.tolerance = 0.2
            RunLoop.main.add(t, forMode: .common)
            poll = t
        }
        onProgress?(active)
    }

    /// The partial files in the folder, with their size so far (and Safari's final size).
    static func partialSamples(in dir: URL) -> [DownloadSample] {
        let keys: [URLResourceKey] = [.isDirectoryKey, .fileSizeKey]
        let items = (try? FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: keys,
                                                                   options: [.skipsHiddenFiles])) ?? []
        return items.compactMap { url in
            guard let final = DownloadTracker.finalName(url.lastPathComponent) else { return nil }
            let values = try? url.resourceValues(forKeys: Set(keys))
            if values?.isDirectory == true {
                // Safari: "x.zip.download/" holds the data so far and an Info.plist that knows the total.
                return safariSample(url, finalName: final)
            }
            return DownloadSample(name: url.lastPathComponent, finalName: final, bytes: Int64(values?.fileSize ?? 0))
        }
    }

    private static func safariSample(_ bundle: URL, finalName: String) -> DownloadSample {
        var bytes: Int64 = 0
        var total: Int64?
        let inside = (try? FileManager.default.contentsOfDirectory(at: bundle, includingPropertiesForKeys: [.fileSizeKey])) ?? []
        for file in inside where file.lastPathComponent != "Info.plist" {
            bytes += Int64((try? file.resourceValues(forKeys: [.fileSizeKey]))?.fileSize ?? 0)
        }
        if let data = try? Data(contentsOf: bundle.appendingPathComponent("Info.plist")),
           let plist = try? PropertyListSerialization.propertyList(from: data, format: nil) as? [String: Any] {
            if let t = (plist["DownloadEntryProgressTotalToLoad"] as? NSNumber)?.int64Value, t > 0 { total = t }
            if let so = (plist["DownloadEntryProgressBytesSoFar"] as? NSNumber)?.int64Value { bytes = max(bytes, so) }
        }
        return DownloadSample(name: bundle.lastPathComponent, finalName: finalName, bytes: bytes, total: total)
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
