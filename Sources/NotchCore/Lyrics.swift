import Foundation

/// One timed line of a synced (LRC) lyric.
public struct LyricLine: Equatable, Codable {
    public var time: Double
    public var text: String

    public init(time: Double, text: String) {
        self.time = time
        self.text = text
    }
}

/// Minimal LRC reader: `[mm:ss.xx]` tags (several per line allowed), `[offset:±ms]`,
/// metadata tags ignored. Lines come back sorted by time.
public enum LRC {
    public static func parse(_ source: String) -> [LyricLine] {
        var lines: [LyricLine] = []
        var offset = 0.0
        for raw in source.split(whereSeparator: \.isNewline) {
            var rest = Substring(raw)
            var stamps: [Double] = []
            while rest.first == "[", let close = rest.firstIndex(of: "]") {
                let tag = rest[rest.index(after: rest.startIndex)..<close]
                if let t = timestamp(tag) {
                    stamps.append(t)
                } else if tag.lowercased().hasPrefix("offset:"), let ms = Double(tag.dropFirst(7).trimmingCharacters(in: .whitespaces)) {
                    offset = ms / 1000
                }
                rest = rest[rest.index(after: close)...]
            }
            let text = rest.trimmingCharacters(in: .whitespaces)
            for t in stamps { lines.append(LyricLine(time: t, text: text)) }
        }
        // A positive offset means the lyrics should show earlier.
        return lines
            .map { LyricLine(time: max(0, $0.time - offset), text: $0.text) }
            .sorted { $0.time < $1.time }
    }

    /// `mm:ss`, `mm:ss.xx` or `mm:ss:xx` → seconds.
    static func timestamp(_ tag: Substring) -> Double? {
        let parts = tag.split(separator: ":", omittingEmptySubsequences: false)
        guard parts.count >= 2, let minutes = Double(parts[0]), minutes >= 0 else { return nil }
        var secondsText = String(parts[1])
        if parts.count == 3 { secondsText += "." + parts[2] }
        guard let seconds = Double(secondsText), seconds >= 0, seconds < 60 else { return nil }
        return minutes * 60 + seconds
    }

    /// Index of the line being sung at `time` (the last line that has started), nil before the first.
    public static func index(at time: Double, in lines: [LyricLine]) -> Int? {
        guard let first = lines.first, time >= first.time else { return nil }
        var lo = 0
        var hi = lines.count - 1
        while lo < hi {
            let mid = (lo + hi + 1) / 2
            if lines[mid].time <= time { lo = mid } else { hi = mid - 1 }
        }
        return lo
    }
}
