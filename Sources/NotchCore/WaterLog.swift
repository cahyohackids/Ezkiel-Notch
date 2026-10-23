import Foundation

public enum WaterLog {
    public static func total(_ entries: [WaterEntry], on day: Date, calendar: Calendar = .current) -> Int {
        entries.filter { calendar.isDate($0.date, inSameDayAs: day) }.reduce(0) { $0 + $1.ml }
    }

    /// Totals for the last `days` days, oldest first, ending with `today`.
    public static func lastDays(_ days: Int, entries: [WaterEntry], today: Date = Date(), calendar: Calendar = .current) -> [(day: Date, ml: Int)] {
        let start = calendar.startOfDay(for: today)
        return (0..<max(1, days)).reversed().compactMap { offset in
            guard let day = calendar.date(byAdding: .day, value: -offset, to: start) else { return nil }
            return (day: day, ml: total(entries, on: day, calendar: calendar))
        }
    }

    /// Consecutive days (ending today or yesterday) where the goal was met.
    public static func streak(_ entries: [WaterEntry], goal: Int, today: Date = Date(), calendar: Calendar = .current) -> Int {
        guard goal > 0 else { return 0 }
        var day = calendar.startOfDay(for: today)
        if total(entries, on: day, calendar: calendar) < goal {
            guard let yesterday = calendar.date(byAdding: .day, value: -1, to: day) else { return 0 }
            day = yesterday
        }
        var count = 0
        while total(entries, on: day, calendar: calendar) >= goal {
            count += 1
            guard let previous = calendar.date(byAdding: .day, value: -1, to: day) else { break }
            day = previous
        }
        return count
    }

    /// Whether a reminder should fire now.
    public static func reminderDue(lastDrink: Date?, lastReminder: Date?, intervalMinutes: Int,
                                   startHour: Int, endHour: Int, now: Date = Date(),
                                   calendar: Calendar = .current) -> Bool {
        let hour = calendar.component(.hour, from: now)
        guard hour >= startHour, hour < endHour else { return false }
        let interval = TimeInterval(max(5, intervalMinutes) * 60)
        if let lastDrink, now.timeIntervalSince(lastDrink) < interval { return false }
        if let lastReminder, now.timeIntervalSince(lastReminder) < interval { return false }
        return true
    }

    /// Drop entries older than `days` to keep the file small.
    public static func pruned(_ entries: [WaterEntry], keepingDays days: Int = 30, now: Date = Date()) -> [WaterEntry] {
        let cutoff = now.addingTimeInterval(-Double(days) * 86_400)
        return entries.filter { $0.date >= cutoff }
    }
}
