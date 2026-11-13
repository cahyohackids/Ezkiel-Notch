using System.Globalization;
using System.Text.Json.Nodes;

namespace EzkielNotch.Core;

// Live activities: quick timers, downloads in progress, and which live thing the closed island shows.
// The Mac's LiveActivity.swift follows the same rules.

/// <summary>A countdown besides Den's focus timer ("Masak 10m"). Stored in data.json under "timers", so it survives
/// a restart: { "id", "label", "seconds" (the length asked for), "ends" } — the Mac's QuickTimer.</summary>
public sealed record QuickTimer(Guid Id, string Label, int Seconds, DateTime Ends)
{
    public static QuickTimer Start(string label, int seconds, DateTime nowUtc) =>
        new(Guid.NewGuid(), label.Trim(), seconds, nowUtc.AddSeconds(seconds));

    public TimeSpan Remaining(DateTime nowUtc) => Ends > nowUtc ? Ends - nowUtc : TimeSpan.Zero;

    internal static QuickTimer? Read(JsonObject o) =>
        Json.Guid(o["id"]) is { } id && Json.String(o["label"]) is { } label && Json.Int(o["seconds"]) is { } seconds
        && Json.Date(o["ends"]) is { } ends
            ? new QuickTimer(id, label, seconds, ends) : null;

    internal JsonObject Write() => new()
    {
        ["id"] = Json.Id(Id), ["label"] = Label, ["seconds"] = Seconds, ["ends"] = Json.Date(Ends),
    };
}

public static class QuickTimers
{
    /// <summary>At most this many run at once.</summary>
    public const int Limit = 8;
    /// <summary>A timer that ended longer ago than this (the app was closed) goes quietly instead of ringing late.</summary>
    public static readonly TimeSpan Stale = TimeSpan.FromHours(1);

    /// <summary>The one the closed island counts down: the soonest still running.</summary>
    public static QuickTimer? Soonest(IEnumerable<QuickTimer> timers, DateTime nowUtc) =>
        timers.Where(t => t.Ends > nowUtc).OrderBy(t => t.Ends).FirstOrDefault();

    /// <summary>The ones still running, the ones that should ring now, and the ones too old to ring.</summary>
    public static (List<QuickTimer> Running, List<QuickTimer> Ring, List<QuickTimer> Stale) Due(IEnumerable<QuickTimer> timers, DateTime nowUtc)
    {
        List<QuickTimer> running = [], ring = [], old = [];
        foreach (var t in timers)
        {
            if (t.Ends > nowUtc) running.Add(t);
            else if (nowUtc - t.Ends > Stale) old.Add(t);
            else ring.Add(t);
        }
        return (running, ring, old);
    }

    /// <summary>"10m", "1h30m", "45s", "2h": a length, short.</summary>
    public static string Short(int seconds)
    {
        int h = seconds / 3600, m = seconds % 3600 / 60, s = seconds % 60;
        var o = "";
        if (h > 0) o += $"{h}h";
        if (m > 0) o += $"{m}m";
        if (s > 0 && h == 0) o += $"{s}s";
        return o.Length == 0 ? "0s" : o;
    }
}

/// <summary>The sounds a quick timer can ring with: Mac sound names (Windows plays its look-alikes, see Chimes), and "none".</summary>
public static class TimerSounds
{
    public static readonly string[] Choices = ["Glass", "Hero", "Ping", "Submarine", "none"];

    /// <summary>A saved choice this version doesn't know rings with Glass.</summary>
    public static string Normalize(string? name) => name is not null && Choices.Contains(name) ? name : "Glass";
}

/// <summary>A file a browser is still writing in the Downloads folder: Chrome and Edge's "x.zip.crdownload",
/// Firefox's "x.zip.part" (the Mac's Safari "x.zip.download" bundle knows its final size).</summary>
public sealed record DownloadSample(string Name, string FinalName, long Bytes, long? Total = null);

public sealed record ActiveDownload(string Name, string FinalName, long Bytes, long? Total, double? BytesPerSecond)
{
    /// <summary>0…1 when the final size is known.</summary>
    public double? Fraction => Total is long t && t > 0 ? Math.Clamp(Bytes / (double)t, 0, 1) : null;
}

public sealed class DownloadTracker
{
    /// <summary>Partial-file endings, lower case, without the dot.</summary>
    public static readonly string[] PartialExtensions = ["crdownload", "part", "download", "partial", "opdownload"];

    private Dictionary<string, (long Bytes, DateTime At, double? Speed)> _last = [];
    private Dictionary<string, string> _finals = [];
    public IReadOnlyList<ActiveDownload> Active { get; private set; } = [];

    /// <summary>"x.zip.crdownload" → "x.zip"; null when the name isn't a browser's partial download.</summary>
    public static string? FinalName(string name)
    {
        if (name.StartsWith('.')) return null;
        int dot = name.LastIndexOf('.');
        if (dot <= 0) return null;
        var ext = name[(dot + 1)..].ToLowerInvariant();
        return PartialExtensions.Contains(ext) ? name[..dot] : null;
    }

    /// <summary>Takes a new reading of the folder: the partial files now, and the names of the complete files beside
    /// them. Returns the downloads that just finished (a partial gone, its final file there), by final name.</summary>
    public List<string> Update(IReadOnlyList<DownloadSample> samples, ISet<string> completed, DateTime now)
    {
        var next = new Dictionary<string, (long Bytes, DateTime At, double? Speed)>();
        var list = new List<ActiveDownload>();
        foreach (var s in samples)
        {
            double? speed = null;
            if (_last.TryGetValue(s.Name, out var before))
            {
                double dt = (now - before.At).TotalSeconds;
                if (dt >= 0.2)
                {
                    // Half the new reading, half the old: steady enough to read, quick enough to follow.
                    double fresh = Math.Max(0, (s.Bytes - before.Bytes) / dt);
                    speed = before.Speed is { } old ? old * 0.5 + fresh * 0.5 : fresh;
                    next[s.Name] = (s.Bytes, now, speed);
                }
                else
                {
                    speed = before.Speed;
                    next[s.Name] = before;
                }
            }
            else next[s.Name] = (s.Bytes, now, null);
            list.Add(new ActiveDownload(s.Name, s.FinalName, s.Bytes, s.Total, speed));
        }
        var present = samples.Select(s => s.Name).ToHashSet();
        var finished = _last.Keys.Where(k => !present.Contains(k))
            .Select(k => _finals.TryGetValue(k, out var f) ? f : null).OfType<string>()
            .Where(completed.Contains).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        _finals = samples.GroupBy(s => s.Name).ToDictionary(g => g.Key, g => g.First().FinalName);
        _last = next;
        Active = [.. list.OrderBy(a => a.Name, StringComparer.Ordinal)];
        return finished;
    }

    /// <summary>The download the closed island shows (the largest), and how many there are.</summary>
    public static (ActiveDownload Download, int Count)? Lead(IReadOnlyList<ActiveDownload> active)
    {
        if (active.Count == 0) return null;
        var top = active.OrderByDescending(a => a.Total ?? a.Bytes).ThenBy(a => a.Name, StringComparer.Ordinal).First();
        return (top, active.Count);
    }

    /// <summary>"820 KB", "12.4 MB", "1.25 GB" (decimal units, like Finder and Explorer's download lists).</summary>
    public static string Bytes(long n)
    {
        double v = Math.Max(0, n);
        var inv = CultureInfo.InvariantCulture;
        if (v < 1000) return $"{(long)v} B";
        if (v < 1_000_000) return $"{(long)Math.Round(v / 1000, MidpointRounding.AwayFromZero)} KB";
        if (v < 1_000_000_000)
        {
            double mb = v / 1_000_000;
            return mb < 100 ? mb.ToString("0.0", inv) + " MB" : $"{(long)Math.Round(mb, MidpointRounding.AwayFromZero)} MB";
        }
        return (v / 1_000_000_000).ToString("0.00", inv) + " GB";
    }

    /// <summary>"1.2 MB/s".</summary>
    public static string Speed(double bytesPerSecond) => Bytes((long)Math.Round(bytesPerSecond, MidpointRounding.AwayFromZero)) + "/s";
}

/// <summary>What the closed island can show while something is live, in the order it wins when several are:
/// Den's focus timer (you started it, it's the whole point), a meeting about to start, the soonest quick timer,
/// a download in progress, an AI agent at work, music, unread messages, charging, then the water ring.</summary>
public enum LiveKind { Focus, Meeting, Timer, Download, Agent, Music, Unread, Charging, Water }

public static class LivePill
{
    /// <summary>A meeting counts down in the closed island from this long before it starts.</summary>
    public static readonly TimeSpan MeetingWindow = TimeSpan.FromMinutes(10);

    /// <summary>The first that applies, in LiveKind's order.</summary>
    public static LiveKind? Pick(IEnumerable<LiveKind> live) => live.Any() ? live.Min() : null;

    /// <summary>Whole minutes until a meeting that starts within the window (at least 1); null outside it or once started.</summary>
    public static int? MeetingMinutes(DateTime startUtc, DateTime nowUtc)
    {
        var lead = startUtc - nowUtc;
        if (lead <= TimeSpan.Zero || lead > MeetingWindow) return null;
        return Math.Max(1, (int)Math.Ceiling(lead.TotalMinutes));
    }
}
