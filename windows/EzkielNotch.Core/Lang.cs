using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace EzkielNotch.Core;

/// <summary>
/// UI text in English or Indonesian, like the Mac's Lang/L(). English is the key; a missing translation shows
/// the English. The Indonesian table is the Mac's (indonesian.json, exported from Indonesian.swift) plus
/// indonesian-windows.json for text only Windows has. Format keys use the Mac's %d / %@ placeholders.
/// </summary>
public static class Lang
{
    public static AppLanguage Choice { get; set; } = AppLanguage.Auto;

    public static bool IsIndonesian => Choice switch
    {
        AppLanguage.Id => true,
        AppLanguage.En => false,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "id",
    };

    public static CultureInfo Culture => IsIndonesian ? CultureInfo.GetCultureInfo("id-ID") : CultureInfo.CurrentCulture;

    private static readonly Lazy<Dictionary<string, string>> Table = new(LoadTable);

    public static IReadOnlyDictionary<string, string> Indonesian => Table.Value;

    private static Dictionary<string, string> LoadTable()
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { "indonesian.json", "indonesian-windows.json" })
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (stream is null) continue;
            var part = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
            foreach (var (k, v) in part) table[k] = v;   // the Windows file wins on purpose
        }
        return table;
    }

    public static string L(string key) => IsIndonesian && Table.Value.TryGetValue(key, out var v) ? v : key;

    /// <summary>L("%d unread", 5): the key is a printf-style format (%d, %@, %.1f, %%), filled in order.</summary>
    public static string L(string key, params object[] args) => Format(L(key), args);

    public static string Format(string format, params object[] args)
    {
        var sb = new StringBuilder(format.Length + 16);
        int next = 0;
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c != '%' || i + 1 >= format.Length)
            {
                sb.Append(c);
                continue;
            }
            // %%, %d, %ld, %@, %.Nf, %s
            int j = i + 1;
            if (format[j] == '%') { sb.Append('%'); i = j; continue; }
            int? precision = null;
            if (format[j] == '.' && j + 2 < format.Length && char.IsDigit(format[j + 1]))
            {
                precision = format[j + 1] - '0';
                j += 2;
            }
            while (j < format.Length && format[j] == 'l') j++;
            if (j >= format.Length || "d@fsi".IndexOf(format[j]) < 0)
            {
                sb.Append(c);
                continue;
            }
            object? arg = next < args.Length ? args[next++] : "";
            sb.Append(format[j] == 'f' && arg is IFormattable f
                ? f.ToString("F" + (precision ?? 6), Culture)
                : arg is IFormattable n ? n.ToString(null, Culture) : arg?.ToString());
            i = j;
        }
        return sb.ToString();
    }

    // MARK: Dates

    /// <summary>One-letter weekday for the week charts (S M T W T F S / M S S R K J S).</summary>
    public static string NarrowWeekday(DateTime day) => Culture.DateTimeFormat.GetDayName(day.DayOfWeek)[..1].ToUpper(Culture);

    /// <summary>"14:05" in the UI language's style.</summary>
    public static string Time(DateTime utcOrLocal) => FocusStats.Local(utcOrLocal).ToString("t", Culture);

    /// <summary>"14:05" today, else "Tue 14:05".</summary>
    public static string DayTime(DateTime when, DateTime now)
    {
        var local = FocusStats.Local(when);
        return local.Date == FocusStats.Local(now).Date ? Time(local) : local.ToString("ddd ", Culture) + Time(local);
    }

    /// <summary>"just now", "5 min ago", "3 h ago", "yesterday", "4 days ago".</summary>
    public static string Relative(DateTime when, DateTime now)
    {
        var span = now.ToUniversalTime() - when.ToUniversalTime();
        if (span < TimeSpan.FromMinutes(1)) return L("just now");
        if (span < TimeSpan.FromHours(1)) return L("%d min ago", (int)span.TotalMinutes);
        if (span < TimeSpan.FromHours(24) && FocusStats.SameDay(when, now)) return L("%d h ago", (int)span.TotalHours);
        int days = (FocusStats.Local(now).Date - FocusStats.Local(when).Date).Days;
        return days <= 1 ? L("yesterday") : L("%d days ago", days);
    }
}
