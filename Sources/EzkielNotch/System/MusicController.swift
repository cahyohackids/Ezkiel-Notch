import AppKit
import NotchCore

enum MusicPlayer: String {
    case spotify = "com.spotify.client"
    case appleMusic = "com.apple.Music"

    var name: String { self == .spotify ? "Spotify" : "Music" }
    var bundleID: String { rawValue }
}

struct NowPlaying: Equatable {
    var player: MusicPlayer
    var title: String
    var artist: String
    var album: String
    var duration: Double
    var position: Double
    var isPlaying: Bool
    var artworkURL: String?
    var fetchedAt: Date

    /// Position interpolated forward while playing, so the progress bar moves smoothly between polls.
    func position(at date: Date) -> Double {
        guard isPlaying else { return position }
        return min(duration > 0 ? duration : .infinity, position + date.timeIntervalSince(fetchedAt))
    }

    var trackKey: String { "\(player.rawValue)|\(title)|\(artist)" }
}

/// Talks to Spotify / Apple Music through Apple Events.
/// All scripting happens on one serial background queue: the UI never waits on a player.
/// Players that are not running are never queried (so we never launch them by accident).
@MainActor
final class MusicController: ObservableObject {
    enum Status: Equatable { case noPlayer, waiting, playing, denied }

    @Published private(set) var nowPlaying: NowPlaying?
    @Published private(set) var artwork: NSImage?
    @Published private(set) var status: Status = .noPlayer

    var source: MusicSource = .auto
    var fastPolling = false {
        didSet { if fastPolling != oldValue { reschedule(); if fastPolling { refresh() } } }
    }
    var onTrackChange: ((NowPlaying?) -> Void)?

    private let queue = DispatchQueue(label: "store.ezkielyna.notch.music", qos: .userInitiated)
    private var timer: Timer?
    private var inFlight = false
    private var observers: [NSObjectProtocol] = []
    private var artworkKey: String?
    private var lastPlayer: MusicPlayer?
    private var pendingCommandUntil = Date.distantPast

    func start() {
        let center = DistributedNotificationCenter.default()
        for name in ["com.spotify.client.PlaybackStateChanged", "com.apple.Music.playerInfo"] {
            let token = center.addObserver(forName: Notification.Name(name), object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.refresh() }
            }
            observers.append(token)
        }
        let ws = NSWorkspace.shared.notificationCenter
        for name in [NSWorkspace.didLaunchApplicationNotification, NSWorkspace.didTerminateApplicationNotification] {
            let token = ws.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.refresh() }
            }
            observers.append(token)
        }
        reschedule()
        refresh()
    }

    private func reschedule() {
        timer?.invalidate()
        let t = Timer(timeInterval: fastPolling ? 1.0 : 4.0, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.refresh() }
        }
        t.tolerance = fastPolling ? 0.2 : 1.0
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    private var candidates: [MusicPlayer] {
        let running = Set(NSWorkspace.shared.runningApplications.compactMap(\.bundleIdentifier))
        let wanted: [MusicPlayer]
        switch source {
        case .auto: wanted = [.spotify, .appleMusic]
        case .spotify: wanted = [.spotify]
        case .appleMusic: wanted = [.appleMusic]
        }
        return wanted.filter { running.contains($0.bundleID) }
    }

    /// Self-test snapshots: show a made-up track and stop polling the real players.
    func simulate(_ np: NowPlaying?) {
        simulated = np != nil
        nowPlaying = np
        status = np == nil ? .noPlayer : .playing
    }

    private var simulated = false

    func refresh() {
        guard !inFlight, !simulated else { return }
        let players = candidates
        guard !players.isEmpty else {
            apply(nil, status: .noPlayer)
            return
        }
        inFlight = true
        let preferred = lastPlayer
        queue.async { [weak self] in
            var denied = false
            var results: [NowPlaying] = []
            for player in players {
                switch MusicScripts.read(player) {
                case .track(let np): results.append(np)
                case .denied: denied = true
                case .unavailable: break
                }
            }
            let chosen = results.first(where: \.isPlaying)
                ?? results.first(where: { $0.player == preferred })
                ?? results.first
            DispatchQueue.main.async {
                guard let self else { return }
                self.inFlight = false
                if let chosen {
                    self.apply(chosen, status: chosen.isPlaying ? .playing : .waiting)
                } else {
                    self.apply(nil, status: denied ? .denied : .waiting)
                }
            }
        }
    }

    private func apply(_ np: NowPlaying?, status newStatus: Status) {
        // Ignore a stale read that lands right after the user pressed a button.
        if Date() < pendingCommandUntil, let np, let current = nowPlaying, np.trackKey == current.trackKey,
           np.isPlaying != current.isPlaying {
            return
        }
        let previousKey = nowPlaying?.trackKey
        if status != newStatus { status = newStatus }
        if nowPlaying != np { nowPlaying = np }
        if let np { lastPlayer = np.player }
        if let np, np.trackKey != artworkKey {
            artworkKey = np.trackKey
            loadArtwork(for: np)
        } else if np == nil, artwork != nil {
            artwork = nil
            artworkKey = nil
        }
        if let previousKey, let np, previousKey != np.trackKey {
            onTrackChange?(np)
        }
    }

    private func loadArtwork(for np: NowPlaying) {
        let key = np.trackKey
        if np.player == .spotify, let s = np.artworkURL, let url = URL(string: s) {
            URLSession.shared.dataTask(with: url) { [weak self] data, _, _ in
                let image = data.flatMap(NSImage.init(data:))
                DispatchQueue.main.async {
                    guard let self, self.artworkKey == key else { return }
                    self.artwork = image
                }
            }.resume()
        } else if np.player == .appleMusic {
            queue.async { [weak self] in
                let image = MusicScripts.appleMusicArtwork()
                DispatchQueue.main.async {
                    guard let self, self.artworkKey == key else { return }
                    self.artwork = image
                }
            }
        } else {
            artwork = nil
        }
    }

    // MARK: - Transport

    func playPause() {
        guard var np = nowPlaying else { return }
        np.position = np.position(at: Date())
        np.fetchedAt = Date()
        np.isPlaying.toggle()
        nowPlaying = np
        status = np.isPlaying ? .playing : .waiting
        send("playpause", to: np.player)
    }

    func next() {
        guard let np = nowPlaying else { return }
        send("next track", to: np.player)
    }

    func previous() {
        guard let np = nowPlaying else { return }
        send("previous track", to: np.player)
    }

    func seek(to seconds: Double) {
        guard var np = nowPlaying else { return }
        np.position = max(0, min(seconds, np.duration))
        np.fetchedAt = Date()
        nowPlaying = np
        send("set player position to \(String(format: "%.2f", locale: Locale(identifier: "en_US_POSIX"), np.position))", to: np.player)
    }

    func open(_ player: MusicPlayer) {
        if let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: player.bundleID) {
            NSWorkspace.shared.openApplication(at: url, configuration: NSWorkspace.OpenConfiguration())
        }
    }

    private func send(_ command: String, to player: MusicPlayer) {
        pendingCommandUntil = Date().addingTimeInterval(0.8)
        queue.async { [weak self] in
            MusicScripts.run("tell application id \"\(player.bundleID)\" to \(command)")
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.35) {
                self?.refresh()
            }
        }
    }
}

/// AppleScript sources and parsing. Only ever called on the music queue.
enum MusicScripts {
    enum ReadResult {
        case track(NowPlaying)
        case unavailable
        case denied
    }

    private static func source(for player: MusicPlayer) -> String {
        let artwork = player == .spotify ? "artwork url of t" : "\"\""
        let duration = player == .spotify ? "(duration of t) / 1000" : "duration of t"
        return """
        tell application id "\(player.bundleID)"
            if player state is stopped then return {"stopped", "", "", "", 0, 0, ""}
            set t to current track
            return {player state as text, name of t as text, artist of t as text, album of t as text, \(duration), player position, \(artwork)}
        end tell
        """
    }

    // Compiled scripts are cached; NSAppleScript is only touched from the serial music queue.
    private static var cache: [MusicPlayer: NSAppleScript] = [:]

    static func read(_ player: MusicPlayer) -> ReadResult {
        let script: NSAppleScript
        if let cached = cache[player] {
            script = cached
        } else {
            guard let made = NSAppleScript(source: source(for: player)) else { return .unavailable }
            script = made
            cache[player] = made
        }
        var error: NSDictionary?
        let result = script.executeAndReturnError(&error)
        if let error {
            let code = error[NSAppleScript.errorNumber] as? Int ?? 0
            return code == -1743 ? .denied : .unavailable
        }
        guard result.numberOfItems >= 7 else { return .unavailable }
        func text(_ i: Int) -> String { result.atIndex(i)?.stringValue?.trimmingCharacters(in: .whitespacesAndNewlines) ?? "" }
        func number(_ i: Int) -> Double { result.atIndex(i)?.doubleValue ?? 0 }
        let state = text(1).lowercased()
        let title = text(2)
        guard state != "stopped", !title.isEmpty, title != "missing value" else { return .unavailable }
        let art = text(7)
        return .track(NowPlaying(
            player: player,
            title: title,
            artist: text(3) == "missing value" ? "" : text(3),
            album: text(4) == "missing value" ? "" : text(4),
            duration: number(5),
            position: number(6),
            isPlaying: state == "playing",
            artworkURL: art.isEmpty || art == "missing value" ? nil : art,
            fetchedAt: Date()
        ))
    }

    static func appleMusicArtwork() -> NSImage? {
        let src = "tell application id \"com.apple.Music\" to get raw data of artwork 1 of current track"
        var error: NSDictionary?
        guard let result = NSAppleScript(source: src)?.executeAndReturnError(&error), error == nil else { return nil }
        return NSImage(data: result.data)
    }

    static func run(_ source: String) {
        var error: NSDictionary?
        NSAppleScript(source: source)?.executeAndReturnError(&error)
    }
}
