import Foundation

/// A reminder time typed into the task itself: "ingatkan kul 3", "gym jam 7 malam", "besok jam 9", "dinner 7pm",
/// "15 menit lagi" (Indonesian, Malay and English). `parse` returns the task without the time words and when it's
/// due. The Windows app's DuePhrase.cs follows the same rules, and both are tested against
/// Tests/fixtures/due-phrases.json.
public enum DuePhrase {
    public struct Result: Equatable {
        public let title: String
        public let due: Date
    }

    private static let units = "(menit|minit|mins?|minutes?|jam|hours?|hrs?)"
    private static let periods = "(pagi|subuh|siang|sore|petang|malam)"

    private static func rx(_ pattern: String) -> NSRegularExpression {
        // The patterns are constants; a typo should fail the tests, not ship.
        try! NSRegularExpression(pattern: pattern, options: [.caseInsensitive])
    }

    // "dalam 2 jam", "in 20 min" · "15 menit lagi", "3 jam lagi"
    private static let relativeIn = rx(#"\b(?:dalam|in)\s+(\d{1,3})\s*"# + units + #"\b"#)
    private static let relativeLater = rx(#"\b(\d{1,3})\s*"# + units + #"\s+(?:lagi|later)\b"#)
    // "besok", "esok", "lusa", "hari ini", "malam ini", "tomorrow", "tonight", optionally "besok pagi"
    private static let day = rx(#"\b(besok|esok|tomorrow|lusa|hari ini|today|malam ini|tonight)\b(?:\s+"# + periods + #"\b)?"#)
    // "jam setengah 4" = 3:30
    private static let half = rx(#"\b(?:jam|pukul|pkl|kul)\s+setengah\s+(\d{1,2})(?!\d)"#)
    // "jam 3", "pukul 15.30", "kul 3", "at 9:15am", "@ 12:30"
    private static let prefixed = rx(#"(?:\b(?:jam|pukul|pkl|kul|at)\b|@)\s*(\d{1,2})(?:[.:](\d{2}))?(?!\d|[.:]\d)(?:\s*(am|pm)\b)?"#)
    // "15:30", "9:15am" (a bare dot, like "1.10", is a number, not a time)
    private static let colon = rx(#"\b(\d{1,2}):(\d{2})(?!\d)(?:\s*(am|pm)\b)?"#)
    // "7pm", "10 am", "3.30pm"
    private static let ampm = rx(#"\b(\d{1,2})(?:[.:](\d{2}))?\s*(am|pm)\b"#)
    // A period right after the time: "jam 7 malam", "kul 5 petang"
    private static let periodAfter = rx(#"\s+"# + periods + #"\b"#)
    // A word that only joins the time to the task: "rapat pada jam 4"
    private static let joiner = rx(#"\b(?:pada|on)\s*$"#)

    public static func parse(_ text: String, now: Date = Date(), calendar: Calendar = .current) -> Result? {
        let ns = text as NSString
        let all = NSRange(location: 0, length: ns.length)
        func group(_ m: NSTextCheckingResult, _ i: Int) -> String? {
            let r = m.range(at: i)
            return r.location == NSNotFound ? nil : ns.substring(with: r).lowercased()
        }
        func first(_ e: NSRegularExpression) -> NSTextCheckingResult? { e.firstMatch(in: text, range: all) }

        var spans: [NSRange] = []
        let today = calendar.startOfDay(for: now)
        func at(_ days: Int, _ h: Int, _ m: Int) -> Date {
            let d = calendar.date(byAdding: .day, value: days, to: today)!
            return calendar.date(bySettingHour: h, minute: m, second: 0, of: d)!
        }
        let due: Date

        if let rel = [first(relativeIn), first(relativeLater)].compactMap({ $0 }).min(by: { $0.range.location < $1.range.location }) {
            let n = Int(group(rel, 1)!)!
            if n == 0 { return nil }
            let hours = ["jam", "hour", "hours", "hr", "hrs"].contains(group(rel, 2)!)
            due = now.addingTimeInterval(TimeInterval(n * (hours ? 3600 : 60)))
            spans.append(rel.range)
        } else {
            var offset: Int?
            var period: String?
            if let d = first(day) {
                let word = group(d, 1)!
                switch word {
                case "besok", "esok", "tomorrow": offset = 1
                case "lusa": offset = 2
                default: offset = 0
                }
                if word == "malam ini" || word == "tonight" { period = "malam" }
                if let p = group(d, 2) { period = p }
                spans.append(d.range)
            }

            var time: (h: Int, m: Int)?
            var meridiem: String?
            let halfMatch = first(half)
            if let t = halfMatch ?? first(prefixed) ?? first(colon) ?? first(ampm) {
                var h = Int(group(t, 1)!)!
                var m = 0
                if halfMatch != nil {
                    h -= 1
                    m = 30
                } else {
                    if let mm = group(t, 2) { m = Int(mm)! }
                    meridiem = group(t, 3)
                }
                if (0...23).contains(h), (0...59).contains(m), !(meridiem != nil && (h == 0 || h > 12)) {
                    time = (h, m)
                    var end = t.range.location + t.range.length
                    if let after = periodAfter.firstMatch(in: text, options: .anchored,
                                                          range: NSRange(location: end, length: ns.length - end)) {
                        period = group(after, 1)
                        end = after.range.location + after.range.length
                    }
                    spans.append(NSRange(location: t.range.location, length: end - t.range.location))
                }
            }

            if let time {
                due = resolve(h: time.h, m: time.m, meridiem: meridiem, period: period, offset: offset, now: now, at: at)
            } else {
                // A day on its own: tomorrow morning at 9 (or that day's part: "besok malam" is 19:00).
                guard let o = offset, o != 0 || period != nil else { return nil }
                let h: Int
                switch period {
                case "siang": h = 12
                case "sore", "petang": h = 16
                case "malam": h = 19
                default: h = 9
                }
                due = at(o, h, 0)
            }
        }
        return Result(title: strip(ns, spans), due: due)
    }

    private static func resolve(h: Int, m: Int, meridiem: String?, period: String?, offset: Int?, now: Date,
                                at: (Int, Int, Int) -> Date) -> Date {
        var h = h, offset = offset, known = true
        if meridiem == "pm", h < 12 {
            h += 12
        } else if meridiem == "am", h == 12 {
            h = 0
        } else if meridiem == nil, let period, h <= 12 {
            switch period {
            case "pagi", "subuh": h = h == 12 ? 0 : h
            case "siang": h = h <= 5 ? h + 12 : h
            case "sore", "petang": h = h < 12 ? h + 12 : h
            // "jam 12 malam" is midnight; "jam 2 malam" is 2 at night.
            default: h = h == 12 ? 0 : h >= 5 ? h + 12 : h
            }
            if period == "malam", h == 0, offset == nil { offset = 1 }
        } else if meridiem == nil, (1...11).contains(h) {
            known = false
            // "besok jam 3" means the afternoon; "besok jam 9", the morning.
            if offset != nil, h <= 6 { h += 12 }
        }

        if let o = offset { return at(o, h, m) }
        let today = at(0, h, m)
        if !known {
            // A bare "jam 3": whichever 3 o'clock comes next (15:00 this afternoon, or 03:00 tonight).
            if today > now { return today }
            let later = at(0, h + 12, m)
            if later > now { return later }
            return at(1, h, m)
        }
        return today > now ? today : at(1, h, m)
    }

    /// The text without the time words, and without a "pada" left hanging before them.
    private static func strip(_ text: NSString, _ spans: [NSRange]) -> String {
        var keep = Array(repeating: true, count: text.length)
        for span in spans {
            var start = span.location
            if let j = joiner.firstMatch(in: text as String, range: NSRange(location: 0, length: start)) { start = j.range.location }
            for i in start..<(span.location + span.length) { keep[i] = false }
        }
        var units: [unichar] = []
        for i in 0..<text.length { units.append(keep[i] ? text.character(at: i) : 0x20) }
        let left = String(utf16CodeUnits: units, count: units.count)
        let words = left.split(whereSeparator: \.isWhitespace).joined(separator: " ")
        return words.trimmingCharacters(in: CharacterSet(charactersIn: " ,;:-–·"))
    }
}
