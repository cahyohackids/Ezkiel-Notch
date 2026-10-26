import Foundation
import NotchCore

/// Fetches lyrics from LRCLIB (lrclib.net, free, no account). Only the title, artist,
/// album and length of the current song are sent, and only while lyrics are switched on.
@MainActor
final class LyricsService: ObservableObject {
    enum State: Equatable {
        case idle
        case loading
        case synced([LyricLine])
        case plain(String)
        case instrumental
        case notFound
        case failed
    }

    @Published private(set) var state: State = .idle
    @Published private(set) var trackKey: String?

    private var cache: [String: State] = [:]
    private var cacheOrder: [String] = []
    private var task: URLSessionDataTask?

    var syncedLines: [LyricLine]? {
        if case .synced(let lines) = state { return lines }
        return nil
    }

    func load(for np: NowPlaying?) {
        guard let np else {
            task?.cancel()
            trackKey = nil
            state = .idle
            return
        }
        guard np.trackKey != trackKey else { return }
        trackKey = np.trackKey
        task?.cancel()
        if let cached = cache[np.trackKey] {
            state = cached
            return
        }
        state = .loading
        fetch(np, exact: true)
    }

    /// Self-test snapshots: show these lines without asking LRCLIB.
    func preview(_ lines: [LyricLine], for np: NowPlaying) {
        task?.cancel()
        trackKey = np.trackKey
        state = .synced(lines)
    }

    func retry(_ np: NowPlaying?) {
        if let key = trackKey { cache[key] = nil }
        trackKey = nil
        load(for: np)
    }

    private func fetch(_ np: NowPlaying, exact: Bool) {
        let key = np.trackKey
        var c = URLComponents(string: "https://lrclib.net/api/" + (exact ? "get" : "search"))!
        if exact {
            c.queryItems = [
                URLQueryItem(name: "track_name", value: np.title),
                URLQueryItem(name: "artist_name", value: np.artist),
                URLQueryItem(name: "album_name", value: np.album),
                URLQueryItem(name: "duration", value: String(Int(np.duration.rounded())))
            ]
        } else {
            c.queryItems = [
                URLQueryItem(name: "track_name", value: Self.cleanTitle(np.title)),
                URLQueryItem(name: "artist_name", value: np.artist)
            ]
        }
        guard let url = c.url else { return }
        var request = URLRequest(url: url, timeoutInterval: 10)
        request.setValue("EzkielNotch/\(AppModel.version) (https://github.com/cahyohackids/Ezkiel-Notch)", forHTTPHeaderField: "User-Agent")

        let duration = np.duration
        let t = URLSession.shared.dataTask(with: request) { [weak self] data, response, error in
            let status = (response as? HTTPURLResponse)?.statusCode ?? 0
            var result: State?
            if error == nil, status == 200, let data {
                if exact {
                    result = (try? JSONDecoder().decode(LRCLibRecord.self, from: data)).map(Self.state(from:))
                } else if let found = try? JSONDecoder().decode([LRCLibRecord].self, from: data) {
                    result = Self.best(found, duration: duration).map(Self.state(from:)) ?? .notFound
                }
            }
            DispatchQueue.main.async {
                guard let self, self.trackKey == key else { return }
                if let result, result != .notFound || !exact {
                    self.finish(result, key: key)
                } else if exact && (status == 404 || status == 200) {
                    self.fetch(np, exact: false)
                } else {
                    // Network trouble: don't cache, so the next play tries again.
                    self.state = .failed
                }
            }
        }
        task = t
        t.resume()
    }

    private func finish(_ result: State, key: String) {
        state = result
        cache[key] = result
        cacheOrder.append(key)
        if cacheOrder.count > 60 { cache[cacheOrder.removeFirst()] = nil }
    }

    nonisolated private static func state(from r: LRCLibRecord) -> State {
        if let synced = r.syncedLyrics, !synced.isEmpty {
            let lines = LRC.parse(synced)
            if !lines.isEmpty { return .synced(lines) }
        }
        if r.instrumental == true { return .instrumental }
        if let plain = r.plainLyrics?.trimmingCharacters(in: .whitespacesAndNewlines), !plain.isEmpty { return .plain(plain) }
        return .notFound
    }

    /// Prefer synced lyrics whose length is closest to the song's.
    nonisolated private static func best(_ records: [LRCLibRecord], duration: Double) -> LRCLibRecord? {
        let close = records.filter { duration <= 0 || abs(($0.duration ?? duration) - duration) <= 4 }
        return close.first { !($0.syncedLyrics ?? "").isEmpty } ?? close.first
    }

    /// "Song - Remastered 2011" / "Song (feat. X)" → "Song".
    nonisolated static func cleanTitle(_ title: String) -> String {
        var t = title
        if let r = t.range(of: " - ") { t = String(t[..<r.lowerBound]) }
        for marker in [" (feat", " (with", " [feat", " (Remaster", " (Live"] {
            if let r = t.range(of: marker, options: .caseInsensitive) { t = String(t[..<r.lowerBound]) }
        }
        return t.trimmingCharacters(in: .whitespaces)
    }
}

private struct LRCLibRecord: Decodable {
    var duration: Double?
    var instrumental: Bool?
    var plainLyrics: String?
    var syncedLyrics: String?
}
