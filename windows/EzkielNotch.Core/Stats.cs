namespace EzkielNotch.Core;

// Den and Oasis numbers, ported from the Mac's FocusStats and WaterLog. Records are stored in UTC;
// "a day" is a local calendar day.

public static class FocusStats
{
    public static int Minutes(IEnumerable<FocusEntry> entries, DateTime day) =>
        entries.Where(e => SameDay(e.Start, day)).Sum(e => e.Minutes);

    /// <summary>Minutes for the last `days` days, oldest first, ending with today.</summary>
    public static List<(DateTime Day, int Minutes)> LastDays(int days, IReadOnlyCollection<FocusEntry> entries, DateTime today) =>
        Days(days, today).Select(d => (d, Minutes(entries, d))).ToList();

    /// <summary>Consecutive days with a finished session, ending today (or yesterday, so the streak
    /// doesn't look broken before today's first session).</summary>
    public static int Streak(IReadOnlyCollection<FocusEntry> entries, DateTime today) =>
        StreakOf(today, d => Minutes(entries, d) > 0);

    public static List<FocusEntry> Pruned(IEnumerable<FocusEntry> entries, DateTime now, int keepingDays = 60) =>
        entries.Where(e => e.Start >= now.ToUniversalTime().AddDays(-keepingDays)).ToList();

    internal static bool SameDay(DateTime utc, DateTime day) => Local(utc).Date == Local(day).Date;

    internal static DateTime Local(DateTime d) => d.Kind == DateTimeKind.Utc ? d.ToLocalTime() : d;

    internal static IEnumerable<DateTime> Days(int days, DateTime today)
    {
        var start = Local(today).Date;
        for (int offset = Math.Max(1, days) - 1; offset >= 0; offset--) yield return start.AddDays(-offset);
    }

    internal static int StreakOf(DateTime today, Func<DateTime, bool> met)
    {
        var day = Local(today).Date;
        if (!met(day)) day = day.AddDays(-1);
        int count = 0;
        while (met(day) && count < 3660)
        {
            count++;
            day = day.AddDays(-1);
        }
        return count;
    }
}

public static class WaterLog
{
    public static int Total(IEnumerable<WaterEntry> entries, DateTime day) =>
        entries.Where(e => FocusStats.SameDay(e.Date, day)).Sum(e => e.Ml);

    public static List<(DateTime Day, int Ml)> LastDays(int days, IReadOnlyCollection<WaterEntry> entries, DateTime today) =>
        FocusStats.Days(days, today).Select(d => (d, Total(entries, d))).ToList();

    /// <summary>Consecutive days (ending today or yesterday) where the goal was met.</summary>
    public static int Streak(IReadOnlyCollection<WaterEntry> entries, int goal, DateTime today) =>
        goal <= 0 ? 0 : FocusStats.StreakOf(today, d => Total(entries, d) >= goal);

    /// <summary>Whether a reminder should fire now (inside the active hours, an interval after the last
    /// drink and the last reminder).</summary>
    public static bool ReminderDue(DateTime? lastDrink, DateTime? lastReminder, int intervalMinutes,
                                   int startHour, int endHour, DateTime now)
    {
        int hour = FocusStats.Local(now).Hour;
        if (hour < startHour || hour >= endHour) return false;
        var interval = TimeSpan.FromMinutes(Math.Max(5, intervalMinutes));
        var utcNow = now.ToUniversalTime();
        if (lastDrink is { } drink && utcNow - drink.ToUniversalTime() < interval) return false;
        if (lastReminder is { } reminder && utcNow - reminder.ToUniversalTime() < interval) return false;
        return true;
    }

    public static List<WaterEntry> Pruned(IEnumerable<WaterEntry> entries, DateTime now, int keepingDays = 30) =>
        entries.Where(e => e.Date >= now.ToUniversalTime().AddDays(-keepingDays)).ToList();
}

/// <summary>Hunt's reminder choices, as on the Mac.</summary>
public enum DueChoice { None, M15, H1, H3, Tomorrow }

public static class Due
{
    public static readonly DueChoice[] All = [DueChoice.None, DueChoice.M15, DueChoice.H1, DueChoice.H3, DueChoice.Tomorrow];

    public static string Label(DueChoice c) => c switch
    {
        DueChoice.M15 => "15m",
        DueChoice.H1 => "1h",
        DueChoice.H3 => "3h",
        DueChoice.Tomorrow => "Tomorrow",
        _ => "No time",
    };

    /// <summary>The due time in UTC; "Tomorrow" is 09:00 local time tomorrow.</summary>
    public static DateTime? Date(DueChoice c, DateTime nowUtc) => c switch
    {
        DueChoice.M15 => nowUtc.AddMinutes(15),
        DueChoice.H1 => nowUtc.AddHours(1),
        DueChoice.H3 => nowUtc.AddHours(3),
        DueChoice.Tomorrow => FocusStats.Local(nowUtc).Date.AddDays(1).AddHours(9).ToUniversalTime(),
        _ => null,
    };
}
