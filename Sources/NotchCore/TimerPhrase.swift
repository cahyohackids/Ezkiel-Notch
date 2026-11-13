import Foundation

/// A countdown typed in a few words: "timer 10m teh", "10 menit masak", "1h30m", "setengah jam yoga",
/// "masak selama 15 menit" (Indonesian, Malay and English). `parse` returns what the timer is for (the words left
/// over) and how long it runs. The Windows app's TimerPhrase.cs follows the same rules, and both are tested
/// against Tests/fixtures/timer-phrases.json.
public enum TimerPhrase {
    public struct Result: Equatable {
        public let label: String
        public let seconds: Int
    }

    /// The longest timer: a day.
    public static let maxSeconds = 24 * 3600

    private static func rx(_ pattern: String) -> NSRegularExpression {
        // The patterns are constants; a typo should fail the tests, not ship.
        try! NSRegularExpression(pattern: pattern, options: [.caseInsensitive])
    }

    private static let unit = "(hours?|hrs?|jam|minutes?|minit|mins?|menit|mnt|seconds?|secs?|detik|saat|h|m|s)"
    // "10m", "1.5 jam", "30 detik" (a unit letter must not run on into a word: "10 masak" isn't 10 minutes)
    private static let token = rx(#"(\d{1,3}(?:[.,]\d{1,2})?)\s*"# + unit + #"(?![a-z])"#)
    // The next part of "1h30m", "1 jam 30 menit", right where the last one ended.
    private static let next = rx(#"\s*(\d{1,3}(?:[.,]\d{1,2})?)\s*"# + unit + #"(?![a-z])"#)
    // "1 jam 30", "1h30": the minutes after hours may go without a unit.
    private static let bareMinutes = rx(#"\s*(\d{1,2})(?![\d.,:a-z])"#)
    private static let words = rx(#"\b(setengah jam|half an hour|half hour|sejam|an hour)\b"#)
    // "timer 10", "10 teh": a plain number at the start is minutes.
    private static let leadingNumber = rx(#"^\s*(?:(?:set|pasang|setel)\s+(?:a\s+)?)?(?:timer\s+)?(\d{1,3})(?![\d.,:])\b"#)
    private static let keyword = rx(#"^\s*(?:(?:set|pasang|setel)\s+(?:a\s+)?)?timer\b"#)

    private static let leadingFillers: Set<String> = ["timer", "pasang", "set", "setel", "a", "for", "untuk", "selama", "buat", "in", "dalam"]
    private static let trailingFillers: Set<String> = ["timer", "for", "untuk", "selama", "buat", "lagi", "later"]

    public static func parse(_ text: String) -> Result? {
        let ns = text as NSString
        let all = NSRange(location: 0, length: ns.length)
        func value(_ m: NSTextCheckingResult, _ i: Int) -> String { ns.substring(with: m.range(at: i)).lowercased() }

        var span: NSRange
        var seconds: Double
        let t = token.firstMatch(in: text, range: all)
        let w = words.firstMatch(in: text, range: all)
        if let w, t == nil || w.range.location < t!.range.location {
            span = w.range
            seconds = value(w, 1) == "sejam" || value(w, 1) == "an hour" ? 3600 : 1800
        } else if let t {
            span = t.range
            var lastUnit = scale(value(t, 2))
            seconds = number(value(t, 1)) * lastUnit
            var end = t.range.location + t.range.length
            while let m = next.firstMatch(in: text, options: .anchored, range: NSRange(location: end, length: ns.length - end)) {
                lastUnit = scale(value(m, 2))
                seconds += number(value(m, 1)) * lastUnit
                end = m.range.location + m.range.length
            }
            if lastUnit == 3600,
               let m = bareMinutes.firstMatch(in: text, options: .anchored, range: NSRange(location: end, length: ns.length - end)) {
                seconds += number(value(m, 1)) * 60
                end = m.range.location + m.range.length
            }
            span = NSRange(location: t.range.location, length: end - t.range.location)
        } else if let n = leadingNumber.firstMatch(in: text, range: all) {
            span = n.range(at: 1)
            seconds = number(value(n, 1)) * 60
        } else {
            return nil
        }

        let total = Int(seconds.rounded())
        guard total > 0, total <= maxSeconds else { return nil }
        return Result(label: label(ns, without: span), seconds: total)
    }

    /// Text that asks for a timer by name ("timer 10m teh", "set a timer for 5 min"): Hunt's field starts a timer
    /// instead of adding a task.
    public static func command(_ text: String) -> Result? {
        guard keyword.firstMatch(in: text, range: NSRange(location: 0, length: (text as NSString).length)) != nil else { return nil }
        return parse(text)
    }

    private static func scale(_ unit: String) -> Double {
        switch unit {
        case "h", "hr", "hrs", "hour", "hours", "jam": return 3600
        case "s", "sec", "secs", "second", "seconds", "detik", "saat": return 1
        default: return 60
        }
    }

    private static func number(_ s: String) -> Double {
        Double(s.replacingOccurrences(of: ",", with: ".")) ?? 0
    }

    /// The words around the duration, without the ones that only ask for a timer ("timer", "for", "selama"…).
    private static func label(_ text: NSString, without span: NSRange) -> String {
        let left = text.replacingCharacters(in: span, with: " ")
        var parts = left.split(whereSeparator: \.isWhitespace).map(String.init)
        let trim = CharacterSet(charactersIn: ",;:-–·")
        func word(_ s: String) -> String { s.trimmingCharacters(in: trim).lowercased() }
        while let first = parts.first, leadingFillers.contains(word(first)) || word(first).isEmpty { parts.removeFirst() }
        while let last = parts.last, trailingFillers.contains(word(last)) || word(last).isEmpty { parts.removeLast() }
        return parts.joined(separator: " ").trimmingCharacters(in: CharacterSet(charactersIn: " ,;:-–·"))
    }
}
