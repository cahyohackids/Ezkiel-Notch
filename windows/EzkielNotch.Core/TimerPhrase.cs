using System.Globalization;
using System.Text.RegularExpressions;

namespace EzkielNotch.Core;

/// <summary>
/// A countdown typed in a few words: "timer 10m teh", "10 menit masak", "1h30m", "setengah jam yoga",
/// "masak selama 15 menit" (Indonesian, Malay and English). Parse() returns what the timer is for (the words left
/// over) and how long it runs. The Mac's TimerPhrase.swift follows the same rules, and both are tested against
/// Tests/fixtures/timer-phrases.json.
/// </summary>
public static class TimerPhrase
{
    public sealed record Result(string Label, int Seconds);

    /// <summary>The longest timer: a day.</summary>
    public const int MaxSeconds = 24 * 3600;

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const string Unit = @"(hours?|hrs?|jam|minutes?|minit|mins?|menit|mnt|seconds?|secs?|detik|saat|h|m|s)";
    // "10m", "1.5 jam", "30 detik" (a unit letter must not run on into a word: "10 masak" isn't 10 minutes)
    private static readonly Regex Token = new(@"(\d{1,3}(?:[.,]\d{1,2})?)\s*" + Unit + @"(?![a-z])", Options);
    // The next part of "1h30m", "1 jam 30 menit", right where the last one ended.
    private static readonly Regex Next = new(@"\G\s*(\d{1,3}(?:[.,]\d{1,2})?)\s*" + Unit + @"(?![a-z])", Options);
    // "1 jam 30", "1h30": the minutes after hours may go without a unit.
    private static readonly Regex BareMinutes = new(@"\G\s*(\d{1,2})(?![\d.,:a-z])", Options);
    private static readonly Regex Words = new(@"\b(setengah jam|half an hour|half hour|sejam|an hour)\b", Options);
    // "timer 10", "10 teh": a plain number at the start is minutes.
    private static readonly Regex LeadingNumber = new(@"^\s*(?:(?:set|pasang|setel)\s+(?:a\s+)?)?(?:timer\s+)?(\d{1,3})(?![\d.,:])\b", Options);
    private static readonly Regex Keyword = new(@"^\s*(?:(?:set|pasang|setel)\s+(?:a\s+)?)?timer\b", Options);

    private static readonly HashSet<string> LeadingFillers = ["timer", "pasang", "set", "setel", "a", "for", "untuk", "selama", "buat", "in", "dalam"];
    private static readonly HashSet<string> TrailingFillers = ["timer", "for", "untuk", "selama", "buat", "lagi", "later"];

    public static Result? Parse(string text)
    {
        int start, end;
        double seconds;
        var t = Token.Match(text);
        var w = Words.Match(text);
        if (w.Success && (!t.Success || w.Index < t.Index))
        {
            (start, end) = (w.Index, w.Index + w.Length);
            var word = w.Groups[1].Value.ToLowerInvariant();
            seconds = word is "sejam" or "an hour" ? 3600 : 1800;
        }
        else if (t.Success)
        {
            start = t.Index;
            double lastUnit = Scale(t.Groups[2].Value);
            seconds = Number(t.Groups[1].Value) * lastUnit;
            end = t.Index + t.Length;
            for (var m = Next.Match(text, end); m.Success; m = Next.Match(text, end))
            {
                lastUnit = Scale(m.Groups[2].Value);
                seconds += Number(m.Groups[1].Value) * lastUnit;
                end = m.Index + m.Length;
            }
            if (lastUnit == 3600 && BareMinutes.Match(text, end) is { Success: true } bare)
            {
                seconds += Number(bare.Groups[1].Value) * 60;
                end = bare.Index + bare.Length;
            }
        }
        else if (LeadingNumber.Match(text) is { Success: true } n)
        {
            (start, end) = (n.Groups[1].Index, n.Groups[1].Index + n.Groups[1].Length);
            seconds = Number(n.Groups[1].Value) * 60;
        }
        else return null;

        int total = (int)Math.Round(seconds, MidpointRounding.AwayFromZero);
        if (total <= 0 || total > MaxSeconds) return null;
        return new Result(Label(text, start, end), total);
    }

    /// <summary>Text that asks for a timer by name ("timer 10m teh", "set a timer for 5 min"): Hunt's field starts a
    /// timer instead of adding a task.</summary>
    public static Result? Command(string text) => Keyword.IsMatch(text) ? Parse(text) : null;

    private static double Scale(string unit) => unit.ToLowerInvariant() switch
    {
        "h" or "hr" or "hrs" or "hour" or "hours" or "jam" => 3600,
        "s" or "sec" or "secs" or "second" or "seconds" or "detik" or "saat" => 1,
        _ => 60,
    };

    private static double Number(string s) => double.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);

    /// <summary>The words around the duration, without the ones that only ask for a timer ("timer", "for", "selama"…).</summary>
    private static string Label(string text, int start, int end)
    {
        var left = text[..start] + " " + text[end..];
        var parts = left.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        static string Word(string s) => s.Trim(',', ';', ':', '-', '–', '·').ToLowerInvariant();
        while (parts.Count > 0 && (LeadingFillers.Contains(Word(parts[0])) || Word(parts[0]).Length == 0)) parts.RemoveAt(0);
        while (parts.Count > 0 && (TrailingFillers.Contains(Word(parts[^1])) || Word(parts[^1]).Length == 0)) parts.RemoveAt(parts.Count - 1);
        return string.Join(' ', parts).Trim(' ', ',', ';', ':', '-', '–', '·');
    }
}
