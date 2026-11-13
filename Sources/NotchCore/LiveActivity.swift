import Foundation

// Live activities: quick timers, downloads in progress, and which live thing the closed notch shows.
// The Windows app's LiveActivity.cs follows the same rules.

// MARK: - Quick timers

/// A countdown besides Den's focus timer ("Masak 10m"). Stored in data.json under "timers", so it survives a
/// restart: { "id", "label", "seconds" (the length asked for), "ends" }.
public struct QuickTimer: Codable, Identifiable, Equatable {
    public var id: UUID
    public var label: String
    public var seconds: Int
    public var ends: Date

    public init(id: UUID = UUID(), label: String, seconds: Int, ends: Date) {
        self.id = id
        self.label = label
        self.seconds = seconds
        self.ends = ends
    }

    public static func start(_ label: String, seconds: Int, now: Date = Date()) -> QuickTimer {
        QuickTimer(label: label.trimmingCharacters(in: .whitespacesAndNewlines), seconds: seconds,
                   ends: now.addingTimeInterval(TimeInterval(seconds)))
    }

    public func remaining(at now: Date = Date()) -> TimeInterval { max(0, ends.timeIntervalSince(now)) }
}

public enum QuickTimers {
    /// At most this many run at once.
    public static let limit = 8
    /// A timer that ended longer ago than this (the app was closed) goes quietly instead of ringing late.
    public static let stale: TimeInterval = 3600

    /// The one the closed notch counts down: the soonest still running.
    public static func soonest(_ timers: [QuickTimer], now: Date = Date()) -> QuickTimer? {
        timers.filter { $0.ends > now }.min { $0.ends < $1.ends }
    }

    /// Splits the list: the ones still running, the ones that should ring now, and the ones too old to ring.
    public static func due(_ timers: [QuickTimer], now: Date = Date()) -> (running: [QuickTimer], ring: [QuickTimer], stale: [QuickTimer]) {
        var running: [QuickTimer] = [], ring: [QuickTimer] = [], old: [QuickTimer] = []
        for t in timers {
            if t.ends > now { running.append(t) }
            else if now.timeIntervalSince(t.ends) > stale { old.append(t) }
            else { ring.append(t) }
        }
        return (running, ring, old)
    }

    /// "10m", "1h30m", "45s", "2h": a length, short.
    public static func short(_ seconds: Int) -> String {
        let h = seconds / 3600, m = seconds % 3600 / 60, s = seconds % 60
        var out = ""
        if h > 0 { out += "\(h)h" }
        if m > 0 { out += "\(m)m" }
        if s > 0 && h == 0 { out += "\(s)s" }
        return out.isEmpty ? "0s" : out
    }
}

// MARK: - Downloads in progress

/// A file a browser is still writing in the Downloads folder: Chrome and Edge's "x.zip.crdownload", Firefox's
/// "x.zip.part", Safari's "x.zip.download" bundle (whose Info.plist knows the final size).
public struct DownloadSample: Equatable {
    /// The partial file's own name.
    public var name: String
    /// The name it will have when done.
    public var finalName: String
    public var bytes: Int64
    /// The final size, when the browser says (Safari); nil for Chrome, Edge and Firefox.
    public var total: Int64?

    public init(name: String, finalName: String, bytes: Int64, total: Int64? = nil) {
        self.name = name
        self.finalName = finalName
        self.bytes = bytes
        self.total = total
    }
}

public struct ActiveDownload: Equatable {
    public var name: String
    public var finalName: String
    public var bytes: Int64
    public var total: Int64?
    /// Smoothed, once there are two readings.
    public var bytesPerSecond: Double?

    /// 0…1 when the final size is known.
    public var fraction: Double? {
        guard let total, total > 0 else { return nil }
        return min(1, max(0, Double(bytes) / Double(total)))
    }
}

public struct DownloadTracker {
    /// Partial-file endings, lower case, without the dot.
    public static let partialExtensions = ["crdownload", "part", "download", "partial", "opdownload"]

    private var last: [String: (bytes: Int64, at: Date, speed: Double?)] = [:]
    private var finals: [String: String] = [:]
    public private(set) var active: [ActiveDownload] = []

    public init() {}

    /// "x.zip.crdownload" → "x.zip"; nil when the name isn't a browser's partial download.
    public static func finalName(_ name: String) -> String? {
        guard !name.hasPrefix("."), let dot = name.lastIndex(of: ".") else { return nil }
        let ext = name[name.index(after: dot)...].lowercased()
        guard partialExtensions.contains(ext) else { return nil }
        let base = String(name[..<dot])
        return base.isEmpty ? nil : base
    }

    /// Takes a new reading of the folder: the partial files now, and the names of the complete files beside them.
    /// Returns the downloads that just finished (a partial gone, its final file there), by final name.
    public mutating func update(_ samples: [DownloadSample], completed: Set<String>, now: Date = Date()) -> [String] {
        var next: [String: (bytes: Int64, at: Date, speed: Double?)] = [:]
        var list: [ActiveDownload] = []
        for s in samples {
            var speed: Double?
            if let before = last[s.name] {
                let dt = now.timeIntervalSince(before.at)
                if dt >= 0.2 {
                    // Half the new reading, half the old: steady enough to read, quick enough to follow.
                    let fresh = max(0, Double(s.bytes - before.bytes) / dt)
                    speed = before.speed.map { $0 * 0.5 + fresh * 0.5 } ?? fresh
                    next[s.name] = (s.bytes, now, speed)
                } else {
                    speed = before.speed
                    next[s.name] = before
                }
            } else {
                next[s.name] = (s.bytes, now, nil)
            }
            list.append(ActiveDownload(name: s.name, finalName: s.finalName, bytes: s.bytes, total: s.total, bytesPerSecond: speed))
        }
        let present = Set(samples.map(\.name))
        let finished = last.keys.filter { !present.contains($0) }.compactMap { finals[$0] }.filter { completed.contains($0) }
        finals = Dictionary(samples.map { ($0.name, $0.finalName) }, uniquingKeysWith: { a, _ in a })
        last = next
        active = list.sorted { $0.name < $1.name }
        return Array(Set(finished)).sorted()
    }

    /// The download the closed notch shows (the largest), and how many there are.
    public static func lead(_ active: [ActiveDownload]) -> (download: ActiveDownload, count: Int)? {
        guard let top = active.max(by: { ($0.total ?? $0.bytes, $1.name) < ($1.total ?? $1.bytes, $0.name) }) else { return nil }
        return (top, active.count)
    }

    /// "820 KB", "12.4 MB", "1.25 GB" (decimal units, like Finder and Explorer's download lists).
    public static func bytes(_ n: Int64) -> String {
        let v = Double(max(0, n))
        if v < 1000 { return "\(Int(v)) B" }
        if v < 1_000_000 { return "\(Int((v / 1000).rounded())) KB" }
        if v < 1_000_000_000 {
            let mb = v / 1_000_000
            return mb < 100 ? String(format: "%.1f MB", locale: Locale(identifier: "en_US_POSIX"), mb) : "\(Int(mb.rounded())) MB"
        }
        return String(format: "%.2f GB", locale: Locale(identifier: "en_US_POSIX"), v / 1_000_000_000)
    }

    /// "1.2 MB/s".
    public static func speed(_ bytesPerSecond: Double) -> String { bytes(Int64(bytesPerSecond.rounded())) + "/s" }
}

// MARK: - The closed notch's live pill

/// What the closed notch can show while something is live, in the order it wins when several are:
/// Den's focus timer (you started it, it's the whole point), a meeting about to start, the soonest quick timer,
/// a download in progress, an AI agent at work, music, unread messages, charging, then the water ring.
public enum LiveKind: Int, CaseIterable, Comparable {
    case focus, meeting, timer, download, agent, music, unread, charging, water

    public static func < (a: LiveKind, b: LiveKind) -> Bool { a.rawValue < b.rawValue }
}

public enum LivePill {
    /// A meeting counts down in the closed notch from this long before it starts.
    public static let meetingWindow: TimeInterval = 600

    /// The first that applies, in LiveKind's order.
    public static func pick(_ live: Set<LiveKind>) -> LiveKind? { live.min() }

    /// Whole minutes until a meeting that starts within the window (at least 1); nil outside it or once started.
    public static func meetingMinutes(start: Date, now: Date = Date()) -> Int? {
        let lead = start.timeIntervalSince(now)
        guard lead > 0, lead <= meetingWindow else { return nil }
        return max(1, Int((lead / 60).rounded(.up)))
    }
}

/// The sounds a quick timer can ring with: Mac sound names (Windows plays its own look-alikes, see Chimes),
/// and "none".
public enum TimerSounds {
    public static let choices = ["Glass", "Hero", "Ping", "Submarine", "none"]

    /// A saved choice this version doesn't know rings with Glass.
    public static func normalize(_ name: String?) -> String {
        guard let name, choices.contains(name) else { return "Glass" }
        return name
    }
}
