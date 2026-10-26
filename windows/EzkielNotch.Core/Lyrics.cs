using System.Globalization;
using System.Net;
using System.Text.Json;

namespace EzkielNotch.Core;

/// <summary>One timed line of a synced (LRC) lyric.</summary>
public sealed record LyricLine(double Time, string Text);

/// <summary>Minimal LRC reader (a port of the Mac's LRC): [mm:ss.xx] tags, several per line allowed,
/// [offset:±ms]; metadata tags ignored. Lines come back sorted by time.</summary>
public static class Lrc
{
    public static List<LyricLine> Parse(string source)
    {
        var lines = new List<LyricLine>();
        double offset = 0;
        foreach (var raw in source.Split('\n', '\r'))
        {
            var rest = raw.AsSpan();
            var stamps = new List<double>();
            while (rest.Length > 0 && rest[0] == '[' && rest.IndexOf(']') is var close and > 0)
            {
                var tag = rest[1..close].ToString();
                if (Timestamp(tag) is { } t) stamps.Add(t);
                else if (tag.StartsWith("offset:", StringComparison.OrdinalIgnoreCase)
                         && double.TryParse(tag[7..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var ms))
                    offset = ms / 1000;
                rest = rest[(close + 1)..];
            }
            var text = rest.ToString().Trim();
            foreach (var t in stamps) lines.Add(new LyricLine(t, text));
        }
        // A positive offset means the lyrics should show earlier.
        return lines.Select(l => l with { Time = Math.Max(0, l.Time - offset) }).OrderBy(l => l.Time).ToList();
    }

    /// <summary>mm:ss, mm:ss.xx or mm:ss:xx → seconds.</summary>
    internal static double? Timestamp(string tag)
    {
        var parts = tag.Split(':');
        if (parts.Length < 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) || minutes < 0) return null;
        var secondsText = parts[1] + (parts.Length == 3 ? "." + parts[2] : "");
        if (!double.TryParse(secondsText, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds < 0 || seconds >= 60) return null;
        return minutes * 60 + seconds;
    }

    /// <summary>Index of the line being sung at `time` (the last line that has started), null before the first.</summary>
    public static int? Index(double time, IReadOnlyList<LyricLine> lines)
    {
        if (lines.Count == 0 || time < lines[0].Time) return null;
        int lo = 0, hi = lines.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (lines[mid].Time <= time) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>"Song - Remastered 2011" / "Song (feat. X)" → "Song".</summary>
    public static string CleanTitle(string title)
    {
        var t = title;
        int dash = t.IndexOf(" - ", StringComparison.Ordinal);
        if (dash >= 0) t = t[..dash];
        foreach (var marker in new[] { " (feat", " (with", " [feat", " (Remaster", " (Live" })
        {
            int i = t.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i >= 0) t = t[..i];
        }
        return t.Trim();
    }
}

public enum LyricsKind { Idle, Loading, Synced, Plain, Instrumental, NotFound, Failed }

public sealed record LyricsState(LyricsKind Kind, IReadOnlyList<LyricLine>? Lines = null, string? Text = null)
{
    public static readonly LyricsState Idle = new(LyricsKind.Idle);
}

/// <summary>What LRCLIB needs to find a song.</summary>
public sealed record SongInfo(string Title, string Artist, string Album, double Duration)
{
    public string Key => $"{Artist}\u0001{Title}\u0001{Album}";
}

/// <summary>Fetches lyrics from LRCLIB (lrclib.net, free, no account), like the Mac's LyricsService. Only the
/// title, artist, album and length of the current song are sent, and only while Chorus is switched on.</summary>
public sealed class LyricsClient
{
    public LyricsState State { get; private set; } = LyricsState.Idle;
    public event Action? Changed;

    private readonly HttpClient _http;
    private readonly Dictionary<string, LyricsState> _cache = [];
    private readonly Queue<string> _order = new();
    private string? _key;
    private CancellationTokenSource? _cts;

    public LyricsClient(HttpClient? http = null, string version = "0")
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"EzkielNotch-Windows/{version} (+https://github.com/cahyohackids/Ezkiel-Notch)");
    }

    public void Load(SongInfo? song)
    {
        if (song is null)
        {
            _cts?.Cancel();
            _key = null;
            Set(LyricsState.Idle);
            return;
        }
        if (song.Key == _key) return;
        _key = song.Key;
        _cts?.Cancel();
        if (_cache.TryGetValue(song.Key, out var cached))
        {
            Set(cached);
            return;
        }
        Set(new LyricsState(LyricsKind.Loading));
        _cts = new CancellationTokenSource();
        _ = FetchAsync(song, _cts.Token);
    }

    /// <summary>Self-test: show these lines without asking LRCLIB.</summary>
    public void Preview(IReadOnlyList<LyricLine> lines, SongInfo song)
    {
        _cts?.Cancel();
        _key = song.Key;
        Set(new LyricsState(LyricsKind.Synced, lines));
    }

    public void Retry(SongInfo? song)
    {
        if (_key is not null) _cache.Remove(_key);
        _key = null;
        Load(song);
    }

    private async Task FetchAsync(SongInfo song, CancellationToken ct)
    {
        LyricsState? result = null;
        try
        {
            var exact = await GetAsync("get?" + Query(("track_name", song.Title), ("artist_name", song.Artist),
                ("album_name", song.Album), ("duration", ((int)Math.Round(song.Duration)).ToString(CultureInfo.InvariantCulture))), ct);
            if (exact.Status == HttpStatusCode.OK && exact.Body is { } body)
                result = StateOf(JsonSerializer.Deserialize<Record>(body, Options));
            if (result is null || result.Kind == LyricsKind.NotFound)
            {
                if (exact.Status is not (HttpStatusCode.OK or HttpStatusCode.NotFound)) throw new HttpRequestException();
                var search = await GetAsync("search?" + Query(("track_name", Lrc.CleanTitle(song.Title)), ("artist_name", song.Artist)), ct);
                if (search.Status != HttpStatusCode.OK || search.Body is null) throw new HttpRequestException();
                var found = JsonSerializer.Deserialize<List<Record>>(search.Body, Options) ?? [];
                result = Best(found, song.Duration) is { } r ? StateOf(r) : new LyricsState(LyricsKind.NotFound);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            result = null;
        }
        if (ct.IsCancellationRequested || _key != song.Key) return;
        if (result is null)
        {
            // Network trouble: don't cache, and forget the key so the next play of this song tries again.
            _key = null;
            Set(new LyricsState(LyricsKind.Failed));
            return;
        }
        _cache[song.Key] = result;
        _order.Enqueue(song.Key);
        if (_order.Count > 60) _cache.Remove(_order.Dequeue());
        Set(result);
    }

    private async Task<(HttpStatusCode Status, string? Body)> GetAsync(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync("https://lrclib.net/api/" + path, ct);
        return (response.StatusCode, response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null);
    }

    private static string Query(params (string Name, string Value)[] items) =>
        string.Join("&", items.Select(i => i.Name + "=" + Uri.EscapeDataString(i.Value)));

    private void Set(LyricsState s)
    {
        State = s;
        Changed?.Invoke();
    }

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    internal sealed record Record(string? SyncedLyrics, string? PlainLyrics, bool? Instrumental, double? Duration);

    internal static LyricsState StateOf(Record? r)
    {
        if (r is null) return new LyricsState(LyricsKind.NotFound);
        if (!string.IsNullOrEmpty(r.SyncedLyrics) && Lrc.Parse(r.SyncedLyrics) is { Count: > 0 } lines) return new LyricsState(LyricsKind.Synced, lines);
        if (r.Instrumental == true) return new LyricsState(LyricsKind.Instrumental);
        if (r.PlainLyrics?.Trim() is { Length: > 0 } plain) return new LyricsState(LyricsKind.Plain, Text: plain);
        return new LyricsState(LyricsKind.NotFound);
    }

    /// <summary>Prefer synced lyrics whose length is closest to the song's.</summary>
    internal static Record? Best(List<Record> records, double duration)
    {
        var close = records.Where(r => duration <= 0 || Math.Abs((r.Duration ?? duration) - duration) <= 4).ToList();
        return close.FirstOrDefault(r => !string.IsNullOrEmpty(r.SyncedLyrics)) ?? close.FirstOrDefault();
    }
}
