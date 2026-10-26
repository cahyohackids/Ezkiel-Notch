import AppKit
import NotchCore

/// One-click update: download the release's DMG, check its SHA-256 against the release's SHA256SUMS.txt,
/// check the app inside (our bundle ID, the promised version, newer than this one, an intact code seal),
/// copy it next to the running app, then quit and let a tiny script swap the two and reopen.
/// Nothing is replaced until every check has passed; if the swap fails, the old app is put back.
@MainActor
final class AppUpdater: NSObject, ObservableObject {
    enum Phase: Equatable {
        case idle
        case downloading(Double)
        case verifying
        case installing
        case relaunching
        case failed(String)

        var busy: Bool {
            switch self {
            case .downloading, .verifying, .installing, .relaunching: return true
            case .idle, .failed: return false
            }
        }
    }

    @Published private(set) var phase: Phase = .idle

    /// Called right before quitting, so data is saved.
    var beforeQuit: () -> Void = {}

    private var task: Task<Void, Never>?
    private var progressObservation: NSKeyValueObservation?

    /// Where the running app lives, and whether it can be replaced in place.
    static var bundleURL: URL { Bundle.main.bundleURL }

    /// Why a one-click update can't work here (nil when it can).
    static var blocker: String? {
        let path = bundleURL.path
        if path.contains("/AppTranslocation/") { return "Move Ezkiel Notch to Applications first" }
        guard bundleURL.pathExtension == "app" else { return "Only the app from Applications can update itself" }
        let parent = bundleURL.deletingLastPathComponent().path
        if !FileManager.default.isWritableFile(atPath: parent) || !FileManager.default.isWritableFile(atPath: path) {
            return "No permission to replace the app in its folder"
        }
        return nil
    }

    func install(_ release: UpdateChecker.Release, current: String) {
        guard !phase.busy else { return }
        if let blocker = Self.blocker { return fail(blocker) }
        guard let dmgURL = release.dmg, let sumsURL = release.sums else { return fail("This release has no checksum file; use the download page") }
        task = Task { [weak self] in
            await self?.run(release: release, dmgURL: dmgURL, sumsURL: sumsURL, current: current)
        }
    }

    func cancel() {
        task?.cancel()
        progressObservation = nil
        phase = .idle
    }

    private func fail(_ reason: String) {
        progressObservation = nil
        phase = .failed(reason)
    }

    // MARK: Steps

    private func run(release: UpdateChecker.Release, dmgURL: URL, sumsURL: URL, current: String) async {
        let fm = FileManager.default
        let work = fm.temporaryDirectory.appendingPathComponent("EzkielNotch-update-\(UUID().uuidString)", isDirectory: true)
        try? fm.createDirectory(at: work, withIntermediateDirectories: true)
        var mount: URL?
        defer {
            if let mount { Self.detach(mount) }
            if phase != .relaunching { try? fm.removeItem(at: work) }
        }
        do {
            phase = .downloading(0)
            // 1. The checksum list first: no list, no install.
            let (sumsData, sumsResponse) = try await URLSession.shared.data(from: sumsURL)
            guard (sumsResponse as? HTTPURLResponse).map({ (200..<300).contains($0.statusCode) }) ?? true,
                  let sums = String(data: sumsData, encoding: .utf8),
                  let expected = UpdateVerify.checksum(for: dmgURL.lastPathComponent, in: sums) else {
                return fail("The release's checksum list doesn't cover the download")
            }
            // 2. The DMG, with progress.
            let dmg = work.appendingPathComponent(dmgURL.lastPathComponent)
            try await download(dmgURL, to: dmg)
            if Task.isCancelled { return }

            // 3. SHA-256 must match exactly.
            phase = .verifying
            let actual = try await Task.detached { try UpdateVerify.sha256(of: dmg) }.value
            guard actual == expected else { return fail("Checksum doesn't match · nothing was changed") }

            // 4. Open the DMG and check the app inside.
            guard let mounted = Self.attach(dmg, under: work) else { return fail("Couldn't open the downloaded disk image") }
            mount = mounted
            guard let newApp = (try? fm.contentsOfDirectory(at: mounted, includingPropertiesForKeys: nil))?
                .first(where: { $0.pathExtension == "app" }) else { return fail("No app in the disk image") }
            let info = Bundle(url: newApp)?.infoDictionary
            guard UpdateVerify.acceptable(bundleID: info?["CFBundleIdentifier"] as? String,
                                          version: info?["CFBundleShortVersionString"] as? String,
                                          expected: release.version, current: current) else {
                return fail("The downloaded app isn't the expected version")
            }
            guard Self.run("/usr/bin/codesign", ["--verify", "--deep", "--strict", newApp.path]) == 0 else {
                return fail("The downloaded app's code seal is broken")
            }

            // 5. Copy it next to the running app (same volume, so the swap is a rename).
            phase = .installing
            let dest = Self.bundleURL
            let parent = dest.deletingLastPathComponent()
            let stamp = Int(Date().timeIntervalSince1970)
            let staged = parent.appendingPathComponent(".EzkielNotch-update-\(stamp).app")
            let old = parent.appendingPathComponent(".EzkielNotch-old-\(stamp).app")
            guard Self.run("/usr/bin/ditto", [newApp.path, staged.path]) == 0 else {
                try? fm.removeItem(at: staged)
                return fail("Couldn't copy the new version next to the app")
            }
            _ = Self.run("/usr/bin/xattr", ["-dr", "com.apple.quarantine", staged.path])
            Self.detach(mounted)
            mount = nil

            // 6. Hand over to the swap script and quit.
            let script = work.appendingPathComponent("swap.sh")
            try Self.swapScript.write(to: script, atomically: true, encoding: .utf8)
            let swapper = Process()
            swapper.executableURL = URL(fileURLWithPath: "/bin/sh")
            swapper.arguments = [script.path, String(ProcessInfo.processInfo.processIdentifier), dest.path, staged.path, old.path, work.path]
            try swapper.run()
            phase = .relaunching
            beforeQuit()
            try? await Task.sleep(nanoseconds: 300_000_000)
            NSApp.terminate(nil)
        } catch is CancellationError {
            phase = .idle
        } catch {
            fail("Download failed · check your connection")
        }
    }

    /// Waits for the app to quit, swaps the bundles, puts the old one back if anything fails, reopens.
    private static let swapScript = """
    #!/bin/sh
    PID="$1"; DEST="$2"; STAGED="$3"; OLD="$4"; WORK="$5"
    i=0
    while kill -0 "$PID" 2>/dev/null && [ "$i" -lt 100 ]; do sleep 0.2; i=$((i+1)); done
    kill -0 "$PID" 2>/dev/null && kill "$PID" && sleep 1
    if mv "$DEST" "$OLD" && mv "$STAGED" "$DEST"; then
      rm -rf "$OLD"
    else
      [ ! -d "$DEST" ] && [ -d "$OLD" ] && mv "$OLD" "$DEST"
      rm -rf "$STAGED"
    fi
    rm -rf "$WORK"
    open "$DEST"
    """

    private func download(_ url: URL, to destination: URL) async throws {
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            let task = URLSession.shared.downloadTask(with: url) { tmp, response, error in
                if let error { return continuation.resume(throwing: error) }
                guard let tmp, (response as? HTTPURLResponse).map({ (200..<300).contains($0.statusCode) }) ?? true else {
                    return continuation.resume(throwing: URLError(.badServerResponse))
                }
                do {
                    try? FileManager.default.removeItem(at: destination)
                    try FileManager.default.moveItem(at: tmp, to: destination)
                    continuation.resume()
                } catch {
                    continuation.resume(throwing: error)
                }
            }
            progressObservation = task.progress.observe(\.fractionCompleted) { [weak self] progress, _ in
                let fraction = progress.fractionCompleted
                DispatchQueue.main.async {
                    MainActor.assumeIsolated {
                        guard let self, case .downloading = self.phase else { return }
                        self.phase = .downloading(fraction)
                    }
                }
            }
            task.resume()
        }
        progressObservation = nil
    }

    // MARK: Helpers

    @discardableResult
    nonisolated private static func run(_ tool: String, _ args: [String]) -> Int32 {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: tool)
        p.arguments = args
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        guard (try? p.run()) != nil else { return -1 }
        p.waitUntilExit()
        return p.terminationStatus
    }

    nonisolated private static func attach(_ dmg: URL, under work: URL) -> URL? {
        let point = work.appendingPathComponent("mount", isDirectory: true)
        try? FileManager.default.createDirectory(at: point, withIntermediateDirectories: true)
        let ok = run("/usr/bin/hdiutil", ["attach", dmg.path, "-nobrowse", "-readonly", "-noautoopen", "-mountpoint", point.path]) == 0
        return ok ? point : nil
    }

    nonisolated private static func detach(_ point: URL) {
        if run("/usr/bin/hdiutil", ["detach", point.path, "-quiet"]) != 0 {
            run("/usr/bin/hdiutil", ["detach", point.path, "-force", "-quiet"])
        }
    }
}
