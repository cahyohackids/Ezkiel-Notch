using System.Globalization;
using System.Text.RegularExpressions;

namespace EzkielNotch.Core;

/// <summary>One upcoming meeting (Rally). Times in UTC.</summary>
public sealed record Meeting(string Id, string Title, DateTime Start, DateTime End, string? Link);

/// <summary>
/// Rally on Windows reads a calendar's private iCal address (Google Calendar, Outlook.com, iCloud all offer one),
/// since Windows has no calendar access for apps outside the Store. This is a small, forgiving iCalendar reader:
/// timed events, TZID or UTC times, DAILY/WEEKLY/MONTHLY/YEARLY recurrences with INTERVAL, BYDAY, UNTIL and COUNT,
/// EXDATE, moved occurrences (RECURRENCE-ID) and cancelled ones.
/// </summary>
public static partial class ICal
{
    /// <summary>Timed events from 5 minutes ago to the end of today, earliest first, at most 3 (like the Mac's Rally).</summary>
    public static List<Meeting> Upcoming(string ics, DateTime nowUtc)
    {
        var localNow = nowUtc.ToLocalTime();
        var windowEnd = localNow.Date.AddDays(1).ToUniversalTime();
        var from = nowUtc.AddMinutes(-5);
        return Occurrences(ics, from, windowEnd)
            .Where(m => m.End > nowUtc)
            .OrderBy(m => m.Start)
            .Take(3)
            .ToList();
    }

    public static List<Meeting> Occurrences(string ics, DateTime fromUtc, DateTime toUtc)
    {
        var events = Parse(ics);
        var overrides = events.Where(e => e.RecurrenceId is not null).ToList();
        var result = new List<Meeting>();
        foreach (var e in events.Where(e => e.RecurrenceId is null))
        {
            if (e.Cancelled || e.AllDay || e.Start is not { } start) continue;
            var length = (e.End ?? start + (e.Duration ?? TimeSpan.FromHours(1))) - start;
            foreach (var s in Expand(e, start, toUtc))
            {
                if (e.ExDates.Contains(s)) continue;
                // A moved or cancelled single occurrence replaces this one.
                var moved = overrides.FirstOrDefault(o => o.Uid == e.Uid && o.RecurrenceId == s);
                if (moved is not null)
                {
                    if (moved.Cancelled || moved.Start is not { } ms) continue;
                    var me = moved.End ?? ms + (moved.Duration ?? length);
                    if (me > fromUtc && ms < toUtc) result.Add(new Meeting($"{e.Uid}@{s:O}", moved.Summary ?? e.Summary ?? "Event", ms, me, JoinLink(moved.Text) ?? JoinLink(e.Text)));
                    continue;
                }
                var end = s + length;
                if (end > fromUtc && s < toUtc) result.Add(new Meeting($"{e.Uid}@{s:O}", e.Summary ?? "Event", s, end, JoinLink(e.Text)));
            }
        }
        // Overrides whose original time is outside the window but which moved into it.
        foreach (var o in overrides)
        {
            if (o.Cancelled || o.Start is not { } os || o.AllDay) continue;
            var oe = o.End ?? os + (o.Duration ?? TimeSpan.FromHours(1));
            if (oe > fromUtc && os < toUtc && result.All(r => r.Id != $"{o.Uid}@{o.RecurrenceId:O}"))
                result.Add(new Meeting($"{o.Uid}@{o.RecurrenceId:O}", o.Summary ?? "Event", os, oe, JoinLink(o.Text)));
        }
        return result;
    }

    // MARK: Join links

    [GeneratedRegex("""https?://[^\s<>"]*(zoom\.us/(j|my|w)/|meet\.google\.com/|teams\.microsoft\.com/l/meetup-join|teams\.live\.com/meet|webex\.com/)[^\s<>"]*""", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();

    /// <summary>The first Zoom / Meet / Teams / Webex link in the text.</summary>
    public static string? JoinLink(string text)
    {
        var m = LinkPattern().Match(text);
        return m.Success ? m.Value.TrimEnd(')', '.', ',', '>', '\\') : null;
    }

    // MARK: Parsing

    private sealed class Event
    {
        public string Uid = Guid.NewGuid().ToString();
        public string? Summary;
        public DateTime? Start, End, RecurrenceId;
        public TimeSpan? Duration;
        public bool AllDay, Cancelled;
        public string? RRule;
        /// <summary>Wall-clock zone of DTSTART, so recurrences keep their local time across DST.</summary>
        public TimeZoneInfo? Zone;
        public readonly HashSet<DateTime> ExDates = [];
        public string Text = "";
    }

    private static List<Event> Parse(string ics)
    {
        var list = new List<Event>();
        Event? e = null;
        foreach (var line in Unfold(ics))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var head = line[..colon];
            var value = line[(colon + 1)..];
            var parts = head.Split(';');
            var name = parts[0].ToUpperInvariant();
            var param = parts.Skip(1).Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
                .ToDictionary(p => p[0].ToUpperInvariant(), p => p[1].Trim('"'), StringComparer.OrdinalIgnoreCase);
            if (name == "BEGIN" && value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase)) { e = new Event(); continue; }
            if (name == "END" && value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (e is not null) list.Add(e);
                e = null;
                continue;
            }
            if (e is null) continue;
            switch (name)
            {
                case "UID": e.Uid = value; break;
                case "SUMMARY": e.Summary = Unescape(value); break;
                case "DTSTART":
                    e.AllDay = param.TryGetValue("VALUE", out var v) && v.Equals("DATE", StringComparison.OrdinalIgnoreCase) || value.Length == 8;
                    e.Start = Time(value, param, out e.Zone);
                    break;
                case "DTEND": e.End = Time(value, param, out _); break;
                case "DURATION": e.Duration = Duration(value); break;
                case "RRULE": e.RRule = value; break;
                case "EXDATE":
                    foreach (var x in value.Split(',')) if (Time(x, param, out _) is { } t) e.ExDates.Add(t);
                    break;
                case "RECURRENCE-ID": e.RecurrenceId = Time(value, param, out _); break;
                case "STATUS": e.Cancelled = value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase); break;
                case "URL" or "LOCATION" or "DESCRIPTION" or "X-GOOGLE-CONFERENCE" or "X-MICROSOFT-SKYPETEAMSMEETINGURL" or "X-MICROSOFT-ONLINEMEETINGCONFLINK":
                    e.Text += Unescape(value) + "\n";
                    break;
            }
        }
        return list;
    }

    private static IEnumerable<string> Unfold(string ics)
    {
        string? current = null;
        foreach (var raw in ics.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t') && current is not null)
            {
                current += raw[1..];
                continue;
            }
            if (current is not null) yield return current;
            current = raw;
        }
        if (current is not null) yield return current;
    }

    private static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\N", "\n").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");

    /// <summary>"20261026T100000Z" (UTC), "20261026T100000" with TZID (that zone) or without (local), "20261026" (a date).</summary>
    private static DateTime? Time(string value, Dictionary<string, string> param, out TimeZoneInfo? zone)
    {
        zone = null;
        value = value.Trim();
        if (value.Length == 8 && DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return DateTime.SpecifyKind(day, DateTimeKind.Local).ToUniversalTime();
        bool utc = value.EndsWith('Z');
        if (!DateTime.TryParseExact(value.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return null;
        if (utc) return DateTime.SpecifyKind(t, DateTimeKind.Utc);
        if (param.TryGetValue("TZID", out var tzid) && Zone(tzid) is { } z)
        {
            zone = z;
            return ToUtc(t, z);
        }
        zone = TimeZoneInfo.Local;
        return ToUtc(t, TimeZoneInfo.Local);
    }

    private static DateTime ToUtc(DateTime wall, TimeZoneInfo z)
    {
        var w = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        // A wall time skipped by a DST change moves forward an hour, as calendars do.
        if (z.IsInvalidTime(w)) w = w.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(w, z);
    }

    private static TimeZoneInfo? Zone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception)
        {
            // Outlook sometimes writes names like "(UTC+07:00) Bangkok, Hanoi, Jakarta".
            return TimeZoneInfo.GetSystemTimeZones().FirstOrDefault(z => z.DisplayName == id || z.StandardName == id);
        }
    }

    private static TimeSpan? Duration(string v)
    {
        var m = Regex.Match(v, @"^([+-])?P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$");
        if (!m.Success) return null;
        int G(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture) : 0;
        return new TimeSpan(G(2) * 7 + G(3), G(4), G(5), G(6));
    }

    // MARK: Recurrence

    private static readonly Dictionary<string, DayOfWeek> Days = new()
    {
        ["SU"] = DayOfWeek.Sunday, ["MO"] = DayOfWeek.Monday, ["TU"] = DayOfWeek.Tuesday, ["WE"] = DayOfWeek.Wednesday,
        ["TH"] = DayOfWeek.Thursday, ["FR"] = DayOfWeek.Friday, ["SA"] = DayOfWeek.Saturday,
    };

    /// <summary>Occurrence starts (UTC) up to `until`, keeping the event's wall-clock time in its own zone.</summary>
    private static IEnumerable<DateTime> Expand(Event e, DateTime start, DateTime until)
    {
        if (e.RRule is null)
        {
            yield return start;
            yield break;
        }
        var rule = e.RRule.Split(';').Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
            .ToDictionary(p => p[0].ToUpperInvariant(), p => p[1], StringComparer.OrdinalIgnoreCase);
        var freq = rule.GetValueOrDefault("FREQ", "").ToUpperInvariant();
        int interval = int.TryParse(rule.GetValueOrDefault("INTERVAL"), out var iv) && iv > 0 ? iv : 1;
        int? count = int.TryParse(rule.GetValueOrDefault("COUNT"), out var c) ? c : null;
        DateTime? end = rule.TryGetValue("UNTIL", out var u) ? Time(u, [], out _) : null;
        var zone = e.Zone ?? TimeZoneInfo.Utc;
        var wall = TimeZoneInfo.ConvertTimeFromUtc(start, zone);
        var byDay = rule.TryGetValue("BYDAY", out var bd)
            ? bd.Split(',').Select(d => d.Trim().ToUpperInvariant()).Select(d => d.Length >= 2 ? d[^2..] : d)
                .Where(Days.ContainsKey).Select(d => Days[d]).ToHashSet()
            : null;

        int emitted = 0;
        for (int step = 0; step < 4000; step++)
        {
            IEnumerable<DateTime> candidates = freq switch
            {
                "DAILY" => [wall.AddDays(step * interval)],
                "WEEKLY" when byDay is { Count: > 0 } => WeekDays(wall, step * interval, byDay),
                "WEEKLY" => [wall.AddDays(7 * step * interval)],
                "MONTHLY" => [wall.AddMonths(step * interval)],
                "YEARLY" => [wall.AddYears(step * interval)],
                _ => step == 0 ? [wall] : [],
            };
            bool any = false;
            foreach (var w in candidates)
            {
                any = true;
                if (w < wall) continue;
                var utc = ToUtc(w, zone);
                if (end is { } en && utc > en) yield break;
                if (count is { } n && emitted >= n) yield break;
                if (utc >= until) yield break;
                emitted++;
                yield return utc;
            }
            if (!any) yield break;
        }
    }

    /// <summary>The chosen weekdays of the week `weeks` weeks after the first, at the event's time of day.</summary>
    private static IEnumerable<DateTime> WeekDays(DateTime first, int weeks, HashSet<DayOfWeek> days)
    {
        // Weeks start on Monday (RFC 5545's default WKST).
        int offset = ((int)first.DayOfWeek + 6) % 7;
        var monday = first.Date.AddDays(-offset).AddDays(7 * weeks);
        for (int i = 0; i < 7; i++)
        {
            var d = monday.AddDays(i);
            if (days.Contains(d.DayOfWeek)) yield return d + first.TimeOfDay;
        }
    }
}
