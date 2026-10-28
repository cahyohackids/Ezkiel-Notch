using System.Text.RegularExpressions;

namespace EzkielNotch.Core;

/// <summary>
/// A reminder time typed into the task itself: "ingatkan kul 3", "gym jam 7 malam", "besok jam 9", "dinner 7pm",
/// "15 menit lagi" (Indonesian, Malay and English). Parse() returns the task without the time words and when it's
/// due, in local time. The Mac's DuePhrase.swift follows the same rules, and both are tested against
/// Tests/fixtures/due-phrases.json.
/// </summary>
public static class DuePhrase
{
    public sealed record Result(string Title, DateTime Due);

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const string Units = @"(menit|minit|mins?|minutes?|jam|hours?|hrs?)";
    private const string Periods = @"(pagi|subuh|siang|sore|petang|malam)";

    // "dalam 2 jam", "in 20 min" · "15 menit lagi", "3 jam lagi"
    private static readonly Regex RelativeIn = new(@"\b(?:dalam|in)\s+(\d{1,3})\s*" + Units + @"\b", Options);
    private static readonly Regex RelativeLater = new(@"\b(\d{1,3})\s*" + Units + @"\s+(?:lagi|later)\b", Options);
    // "besok", "esok", "lusa", "hari ini", "malam ini", "tomorrow", "tonight", optionally "besok pagi"
    private static readonly Regex Day = new(@"\b(besok|esok|tomorrow|lusa|hari ini|today|malam ini|tonight)\b(?:\s+" + Periods + @"\b)?", Options);
    // "jam setengah 4" = 3:30
    private static readonly Regex Half = new(@"\b(?:jam|pukul|pkl|kul)\s+setengah\s+(\d{1,2})(?!\d)", Options);
    // "jam 3", "pukul 15.30", "kul 3", "at 9:15am", "@ 12:30"
    private static readonly Regex Prefixed = new(@"(?:\b(?:jam|pukul|pkl|kul|at)\b|@)\s*(\d{1,2})(?:[.:](\d{2}))?(?!\d|[.:]\d)(?:\s*(am|pm)\b)?", Options);
    // "15:30", "9:15am" (a bare dot, like "1.10", is a number, not a time)
    private static readonly Regex Colon = new(@"\b(\d{1,2}):(\d{2})(?!\d)(?:\s*(am|pm)\b)?", Options);
    // "7pm", "10 am", "3.30pm"
    private static readonly Regex AmPm = new(@"\b(\d{1,2})(?:[.:](\d{2}))?\s*(am|pm)\b", Options);
    // A period right after the time: "jam 7 malam", "kul 5 petang"
    private static readonly Regex PeriodAfter = new(@"\G\s+" + Periods + @"\b", Options);
    // A word that only joins the time to the task: "rapat pada jam 4"
    private static readonly Regex Joiner = new(@"\b(?:pada|on)\s*$", Options);

    public static Result? Parse(string text, DateTime now)
    {
        var spans = new List<(int Start, int Length)>();
        DateTime? due = null;

        if (Find(text, RelativeIn, RelativeLater) is { } rel)
        {
            int n = int.Parse(rel.Groups[1].Value);
            var unit = rel.Groups[2].Value.ToLowerInvariant();
            due = unit is "jam" or "hour" or "hours" or "hr" or "hrs" ? now.AddHours(n) : now.AddMinutes(n);
            if (n == 0) return null;
            spans.Add((rel.Index, rel.Length));
        }
        else
        {
            int? offset = null;
            string? period = null;
            var day = Day.Match(text);
            if (day.Success)
            {
                var word = day.Groups[1].Value.ToLowerInvariant();
                offset = word switch { "besok" or "esok" or "tomorrow" => 1, "lusa" => 2, _ => 0 };
                if (word is "malam ini" or "tonight") period = "malam";
                if (day.Groups[2].Success) period = day.Groups[2].Value.ToLowerInvariant();
                spans.Add((day.Index, day.Length));
            }

            int? hour = null, minute = null;
            string? meridiem = null;
            var half = Half.Match(text);
            var time = half.Success ? half : Prefixed.Match(text) is { Success: true } p ? p
                     : Colon.Match(text) is { Success: true } c ? c : AmPm.Match(text);
            if (time.Success)
            {
                int h = int.Parse(time.Groups[1].Value);
                int m = half.Success ? 30 : time.Groups[2].Success ? int.Parse(time.Groups[2].Value) : 0;
                if (half.Success) h -= 1;
                if (time != half && time.Groups[3].Success) meridiem = time.Groups[3].Value.ToLowerInvariant();
                if (h is >= 0 and <= 23 && m is >= 0 and <= 59 && !(meridiem is not null && (h == 0 || h > 12)))
                {
                    hour = h;
                    minute = m;
                    int end = time.Index + time.Length;
                    var after = PeriodAfter.Match(text, end);
                    if (after.Success)
                    {
                        period = after.Groups[1].Value.ToLowerInvariant();
                        end = after.Index + after.Length;
                    }
                    spans.Add((time.Index, end - time.Index));
                }
            }

            if (hour is null)
            {
                // A day on its own: tomorrow morning at 9 (or that day's part: "besok malam" is 19:00).
                if (offset is not { } o || (o == 0 && period is null)) return null;
                hour = period switch { "siang" => 12, "sore" or "petang" => 16, "malam" => 19, _ => 9 };
                minute = 0;
                due = now.Date.AddDays(o).AddHours(hour.Value);
            }
            else due = Resolve(hour.Value, minute!.Value, meridiem, period, offset, now);
        }

        if (due is null) return null;
        return new Result(Strip(text, spans), due.Value);
    }

    /// <summary>The first match of any of the patterns, earliest in the text.</summary>
    private static Match? Find(string text, params Regex[] patterns) =>
        patterns.Select(p => p.Match(text)).Where(m => m.Success).OrderBy(m => m.Index).FirstOrDefault();

    private static DateTime Resolve(int h, int m, string? meridiem, string? period, int? offset, DateTime now)
    {
        bool known = true;
        if (meridiem == "pm" && h < 12) h += 12;
        else if (meridiem == "am" && h == 12) h = 0;
        else if (meridiem is null && period is not null && h <= 12)
        {
            h = period switch
            {
                "pagi" or "subuh" => h == 12 ? 0 : h,
                "siang" => h <= 5 ? h + 12 : h,
                "sore" or "petang" => h < 12 ? h + 12 : h,
                // "jam 12 malam" is midnight; "jam 2 malam" is 2 at night.
                _ => h == 12 ? 0 : h >= 5 ? h + 12 : h,
            };
            if (period == "malam" && h == 0 && offset is null) offset = 1;
        }
        else if (meridiem is null && h is >= 1 and <= 11)
        {
            known = false;
            // "besok jam 3" means the afternoon; "besok jam 9", the morning.
            if (offset is not null && h <= 6) h += 12;
        }

        if (offset is { } o) return now.Date.AddDays(o).AddHours(h).AddMinutes(m);
        var at = now.Date.AddHours(h).AddMinutes(m);
        if (!known)
        {
            // A bare "jam 3": whichever 3 o'clock comes next (15:00 this afternoon, or 03:00 tonight).
            var later = at.AddHours(12);
            if (at > now) return at;
            if (later > now) return later;
            return at.AddDays(1);
        }
        return at > now ? at : at.AddDays(1);
    }

    /// <summary>The text without the time words, and without a "pada" left hanging before them.</summary>
    private static string Strip(string text, List<(int Start, int Length)> spans)
    {
        var keep = text.ToCharArray();
        foreach (var (start, length) in spans)
        {
            int s = start;
            var joiner = Joiner.Match(text[..s]);
            if (joiner.Success) s = joiner.Index;
            for (int i = s; i < start + length; i++) keep[i] = '\0';
        }
        var left = new string(keep.Select(ch => ch == '\0' ? ' ' : ch).ToArray());
        left = Regex.Replace(left, @"\s+", " ");
        return left.Trim(' ', ',', ';', ':', '-', '–', '·');
    }
}
