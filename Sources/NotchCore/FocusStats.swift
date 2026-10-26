import Foundation

/// One finished focus session (breaks are not logged).
public struct FocusEntry: Codable, Identifiable, Equatable {
    public var id: UUID
    public var start: Date
    public var minutes: Int

    public init(id: UUID = UUID(), start: Date, minutes: Int) {
        self.id = id
        self.start = start
        self.minutes = minutes
    }
}

/// Den statistics: minutes per day, the last week, and the streak.
public enum FocusStats {
    public static func minutes(_ entries: [FocusEntry], on day: Date, calendar: Calendar = .current) -> Int {
        entries.filter { calendar.isDate($0.start, inSameDayAs: day) }.reduce(0) { $0 + $1.minutes }
    }

    /// Minutes for the last `days` days, oldest first, ending with `today`.
    public static func lastDays(_ days: Int, entries: [FocusEntry], today: Date = Date(),
                                calendar: Calendar = .current) -> [(day: Date, minutes: Int)] {
        let start = calendar.startOfDay(for: today)
        return (0..<max(1, days)).reversed().compactMap { offset in
            guard let day = calendar.date(byAdding: .day, value: -offset, to: start) else { return nil }
            return (day: day, minutes: minutes(entries, on: day, calendar: calendar))
        }
    }

    /// Consecutive days with at least one finished session, ending today (or yesterday,
    /// so the streak doesn't look broken before today's first session).
    public static func streak(_ entries: [FocusEntry], today: Date = Date(), calendar: Calendar = .current) -> Int {
        var day = calendar.startOfDay(for: today)
        if minutes(entries, on: day, calendar: calendar) == 0 {
            guard let yesterday = calendar.date(byAdding: .day, value: -1, to: day) else { return 0 }
            day = yesterday
        }
        var count = 0
        while minutes(entries, on: day, calendar: calendar) > 0 {
            count += 1
            guard let previous = calendar.date(byAdding: .day, value: -1, to: day) else { break }
            day = previous
        }
        return count
    }

    /// Keep the log small: sessions from the last `days` days.
    public static func pruned(_ entries: [FocusEntry], keepingDays days: Int = 60, now: Date = Date()) -> [FocusEntry] {
        let cutoff = now.addingTimeInterval(-Double(days) * 86_400)
        return entries.filter { $0.start >= cutoff }
    }
}
