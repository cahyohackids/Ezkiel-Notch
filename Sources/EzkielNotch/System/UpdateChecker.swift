import Foundation
import NotchCore

/// Asks GitHub once a day for the latest release. One small request, no data about you is sent
/// beyond what any web request carries; turn it off in Settings › About.
@MainActor
final class UpdateChecker: ObservableObject {
    struct Release: Equatable {
        let version: String
        let page: URL
    }

    static let latestURL = URL(string: "https://api.github.com/repos/cahyohackids/Ezkiel-Notch/releases/latest")!

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

    nonisolated static func parse(_ data: Data) -> Release? {
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let tag = json["tag_name"] as? String,
              (json["draft"] as? Bool) != true, (json["prerelease"] as? Bool) != true,
              let page = (json["html_url"] as? String).flatMap(URL.init(string:)),
              page.host == "github.com" else { return nil }
        return Release(version: tag.trimmingCharacters(in: CharacterSet(charactersIn: "vV")), page: page)
    }
}
