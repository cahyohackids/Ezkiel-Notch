import Foundation
import NotchCore

/// Asks GitHub once a day for the latest release. One small request, no data about you is sent
/// beyond what any web request carries; turn it off in Settings › About.
@MainActor
final class UpdateChecker: ObservableObject {
    struct Release: Equatable {
        let version: String
        let page: URL
        /// The universal DMG and the release's SHA256SUMS.txt; both needed for a one-click update.
        var dmg: URL? = nil
        var sums: URL? = nil

        var canInstall: Bool { dmg != nil && sums != nil }
    }

    static let githubLatest = URL(string: "https://api.github.com/repos/cahyohackids/Ezkiel-Notch/releases/latest")!

    /// `--update-feed <url>` points the check at a test feed (the updater's end-to-end test); otherwise GitHub.
    static var latestURL: URL {
        let args = ProcessInfo.processInfo.arguments
        if let i = args.firstIndex(of: "--update-feed"), i + 1 < args.count, let url = URL(string: args[i + 1]) { return url }
        return githubLatest
    }

    /// Only GitHub hosts the real releases; a test feed may serve from anywhere it says.
    nonisolated static var trustedHosts: Set<String> {
        ProcessInfo.processInfo.arguments.contains("--update-feed")
            ? [] : ["github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"]
    }

    /// A release newer than this build, once found.
    @Published private(set) var available: Release?
    @Published private(set) var checking = false
    /// Last check that reached GitHub and found nothing newer.
    @Published private(set) var upToDateAt: Date?

    func check(current: String, completion: @escaping (Release?) -> Void) {
        guard !checking else { return }
        checking = true
        var request = URLRequest(url: Self.latestURL, timeoutInterval: 15)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("EzkielNotch/\(current)", forHTTPHeaderField: "User-Agent")
        URLSession.shared.dataTask(with: request) { data, response, _ in
            let release = data.flatMap { Self.parse($0) }
            let ok = (response as? HTTPURLResponse)?.statusCode == 200
            DispatchQueue.main.async {
                MainActor.assumeIsolated {
                    self.checking = false
                    guard ok, let release else { return completion(nil) }
                    if AppVersion.isNewer(release.version, than: current) {
                        self.available = release
                        completion(release)
                    } else {
                        self.available = nil
                        self.upToDateAt = Date()
                        completion(nil)
                    }
                }
            }
        }.resume()
    }

    /// Self-test snapshots: pretend GitHub has this release.
    func simulate(_ release: Release?) { available = release }

    nonisolated static func parse(_ data: Data) -> Release? {
        let trusted = trustedHosts
        func allowed(_ url: URL) -> Bool { trusted.isEmpty || (url.scheme == "https" && trusted.contains(url.host ?? "")) }
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let tag = json["tag_name"] as? String,
              (json["draft"] as? Bool) != true, (json["prerelease"] as? Bool) != true,
              let page = (json["html_url"] as? String).flatMap(URL.init(string:)),
              allowed(page) else { return nil }
        let assets = (json["assets"] as? [[String: Any]]) ?? []
        func asset(_ match: (String) -> Bool) -> URL? {
            assets.first { match(($0["name"] as? String) ?? "") }
                .flatMap { ($0["browser_download_url"] as? String).flatMap(URL.init(string:)) }
                .flatMap { allowed($0) ? $0 : nil }
        }
        let dmg = asset { $0.hasSuffix(".dmg") && $0.contains("universal") }
        let sums = asset { $0 == "SHA256SUMS.txt" }
        return Release(version: tag.trimmingCharacters(in: CharacterSet(charactersIn: "vV")), page: page, dmg: dmg, sums: sums)
    }
}
