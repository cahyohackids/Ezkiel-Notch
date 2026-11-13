namespace EzkielNotch.Core;

// System monitor: the platform-free half (series, rates from counters, alert rules, formatting), the same as the
// Mac's NotchCore/SysMon.swift. Both test suites read Tests/fixtures/sysmon-alerts.json, so the two apps format and
// alert the same way.

/// <summary>What the closed island shows while the system monitor is on ("off", "cpu" or "network").</summary>
public static class SysPill
{
    public const string Off = "off";
    public const string Cpu = "cpu";
    public const string Network = "network";
    public static readonly string[] All = [Off, Cpu, Network];

    /// <summary>A choice from a newer version that this one doesn't know falls back to none.</summary>
    public static string Normalize(string? value) => value is Cpu or Network ? value : Off;
}

/// <summary>macOS memory pressure. Windows has none and passes null.</summary>
public enum MemoryPressure { Normal, Warning, Critical }

/// <summary>The last <c>capacity</c> samples, oldest first (for sparklines).</summary>
public sealed class SysSeries(int capacity = 60)
{
    private readonly List<double> _values = [];
    public int Capacity { get; } = Math.Max(1, capacity);
    public IReadOnlyList<double> Values => _values;

    public void Append(double value)
    {
        _values.Add(value);
        if (_values.Count > Capacity) _values.RemoveRange(0, _values.Count - Capacity);
    }

    public void Clear() => _values.Clear();
    public double? Last => _values.Count > 0 ? _values[^1] : null;
    public double Peak => _values.Count > 0 ? _values.Max() : 0;

    /// <summary>0…1 for drawing, scaled to the peak but never to less than <paramref name="floor"/>.</summary>
    public List<double> Normalized(double floor)
    {
        double top = Math.Max(Peak, floor);
        return top > 0 ? [.. _values.Select(v => Math.Clamp(v / top, 0, 1))] : [.. _values.Select(_ => 0.0)];
    }
}

public static class SysCounters
{
    /// <summary>Bytes moved between two readings of one counter. A counter that went backwards wrapped (32-bit
    /// counters near the top) or was reset (an adapter came back): a reset gives null.</summary>
    public static ulong? Delta(ulong previous, ulong current, int bits)
    {
        if (current >= previous) return current - previous;
        if (bits != 32 || previous > uint.MaxValue || previous < 3_221_225_472) return null;
        ulong wrapped = (uint.MaxValue - previous) + 1 + current;
        return wrapped <= 2_147_483_648 ? wrapped : null;
    }

    /// <summary>Bytes per second across adapters, by name. Adapters that appear, vanish or reset add nothing.</summary>
    public static double? Rate(IReadOnlyDictionary<string, ulong> previous, IReadOnlyDictionary<string, ulong> current, double seconds, int bits)
    {
        if (seconds <= 0) return null;
        ulong total = 0;
        foreach (var (name, now) in current)
            if (previous.TryGetValue(name, out var before) && Delta(before, now, bits) is { } d) total += d;
        return total / seconds;
    }
}

public static class SysCpu
{
    /// <summary>CPU busy % between two tick readings (busy = kernel − idle + user on Windows). Null when the
    /// counters went backwards or nothing passed.</summary>
    public static double? Percent(ulong previousBusy, ulong previousIdle, ulong busy, ulong idle)
    {
        if (busy < previousBusy || idle < previousIdle) return null;
        double b = busy - previousBusy, i = idle - previousIdle;
        if (b + i <= 0) return null;
        return Math.Clamp(b / (b + i) * 100, 0, 100);
    }
}

public static class SysMemory
{
    /// <summary>The number the memory alert watches. With pressure (macOS) it alone decides; without it (Windows),
    /// the share of RAM in use.</summary>
    public static double AlertLoad(double usedPercent, MemoryPressure? pressure) => pressure switch
    {
        MemoryPressure.Normal => 0,
        MemoryPressure.Warning => 95,
        MemoryPressure.Critical => 100,
        _ => usedPercent,
    };
}

/// <summary>One alert: fires when the value has stayed past <c>On</c> for <c>Sustain</c> seconds; quiet again until it
/// comes back past <c>Off</c> (hysteresis), and never twice within <c>Cooldown</c>.</summary>
public sealed record SysAlertRule(string Id, bool Above, double On, double Off, double Sustain, double Cooldown)
{
    /// <summary>CPU above 90 % for 30 s; again after it drops under 70 %, at most every 15 min.</summary>
    public static readonly SysAlertRule Cpu = new("cpu", true, 90, 70, 30, 900);
    /// <summary>Memory (AlertLoad) at 90 or more for 20 s; again under 80, at most every 30 min.</summary>
    public static readonly SysAlertRule Memory = new("memory", true, 90, 80, 20, 1800);
    /// <summary>Free space on the system drive under 5 GB; again once it's 6 GB or more, at most every 6 h.</summary>
    public static readonly SysAlertRule Disk = new("disk", false, 5, 6, 0, 21_600);
}

/// <summary>The alert rules together: no two system peeks within <c>Spacing</c> seconds, and a pause in sampling
/// longer than <c>MaxGap</c> starts the sustain over.</summary>
public sealed class SysAlerts
{
    public static readonly SysAlertRule[] DefaultRules = [SysAlertRule.Cpu, SysAlertRule.Memory, SysAlertRule.Disk];
    public IReadOnlyDictionary<string, SysAlertRule> Rules { get; }
    public double Spacing { get; }
    public double MaxGap { get; }

    private sealed class State
    {
        public bool Armed = true;
        public double? Since, LastFired, LastSeen;
    }

    private readonly Dictionary<string, State> _states = [];
    private double? _lastAny;

    public SysAlerts(IEnumerable<SysAlertRule>? rules = null, double spacing = 120, double maxGap = 25)
    {
        var map = new Dictionary<string, SysAlertRule>();
        foreach (var r in rules ?? DefaultRules) map.TryAdd(r.Id, r);
        Rules = map;
        Spacing = spacing;
        MaxGap = maxGap;
    }

    /// <summary>A sample for one rule at time <paramref name="t"/> (seconds). True when its peek should show now.</summary>
    public bool Feed(string id, double value, double t)
    {
        if (!Rules.TryGetValue(id, out var rule)) return false;
        if (!_states.TryGetValue(id, out var s)) _states[id] = s = new State();
        if (s.LastSeen is { } seen && t - seen > MaxGap) s.Since = null;
        s.LastSeen = t;
        bool hit = rule.Above ? value >= rule.On : value < rule.On;
        if (!s.Armed)
        {
            bool released = rule.Above ? value < rule.Off : value >= rule.Off;
            if (released) s.Armed = true;
            s.Since = null;
            return false;
        }
        if (!hit)
        {
            s.Since = null;
            return false;
        }
        double since = s.Since ?? t;
        s.Since = since;
        if (t - since < rule.Sustain) return false;
        if (s.LastFired is { } last && t - last < rule.Cooldown) return false;
        if (_lastAny is { } any && t - any < Spacing) return false;
        s.LastFired = t;
        s.Armed = false;
        s.Since = null;
        _lastAny = t;
        return true;
    }

    /// <summary>Forget everything (the monitor was switched off).</summary>
    public void Reset()
    {
        _states.Clear();
        _lastAny = null;
    }
}

/// <summary>Numbers as the card and the pill show them. Always "." and rounded half away from zero, so the Mac and
/// Windows print the same thing.</summary>
public static class SysFormat
{
    private static long RoundAway(double x) => (long)Math.Round(x, MidpointRounding.AwayFromZero);

    private static string OneDecimal(double x, bool trimZero = false)
    {
        long r = RoundAway(x * 10);
        if (trimZero && r % 10 == 0) return (r / 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"{r / 10}.{r % 10}";
    }

    /// <summary>"34%".</summary>
    public static string Percent(double value) => $"{Math.Clamp(RoundAway(value), 0, 100)}%";

    /// <summary>Network speed, decimal units: "0 KB/s", "4.2 KB/s", "820 KB/s", "1.2 MB/s", "12 MB/s", "1.1 GB/s".</summary>
    public static string Rate(double bytesPerSecond) => Scaled(bytesPerSecond, ["KB/s", "MB/s", "GB/s"], " ");

    /// <summary>The closed pill's short form: "0K", "4.2K", "820K", "1.2M", "12M", "1.1G".</summary>
    public static string CompactRate(double bytesPerSecond) => Scaled(bytesPerSecond, ["K", "M", "G"], "");

    private static string Scaled(double bytesPerSecond, string[] units, string sep)
    {
        double v = Math.Max(0, double.IsFinite(bytesPerSecond) ? bytesPerSecond : 0);
        if (v < 50) return $"0{sep}{units[0]}";
        double x = v / 1000;
        for (int i = 0; i < units.Length; i++)
        {
            bool lastUnit = i == units.Length - 1;
            if (x < 9.95) return $"{OneDecimal(x)}{sep}{units[i]}";
            if (x < 999.5 || lastUnit) return $"{RoundAway(x)}{sep}{units[i]}";
            x /= 1000;
        }
        return $"0{sep}{units[0]}";
    }

    /// <summary>Memory, binary units like the RAM on the box: "512 MB", "6.1 GB", "8 GB", "128 GB".</summary>
    public static string Memory(double bytes)
    {
        double b = Math.Max(0, bytes), gib = b / 1_073_741_824;
        if (gib < 0.995) return $"{RoundAway(b / 1_048_576)} MB";
        if (gib < 99.95) return $"{OneDecimal(gib, trimZero: true)} GB";
        return $"{RoundAway(gib)} GB";
    }

    /// <summary>Disk space, decimal units: "850 MB", "4.2 GB", "120 GB", "1.2 TB".</summary>
    public static string Disk(double bytes)
    {
        double b = Math.Max(0, bytes), gb = b / 1e9;
        if (gb < 0.995) return $"{RoundAway(b / 1e6)} MB";
        if (gb < 99.95) return $"{OneDecimal(gb, trimZero: true)} GB";
        if (gb < 999.5) return $"{RoundAway(gb)} GB";
        double tb = b / 1e12;
        if (tb < 9.95) return $"{OneDecimal(tb, trimZero: true)} TB";
        return $"{RoundAway(tb)} TB";
    }

    /// <summary>Battery time: "45m", "2h 05m".</summary>
    public static string Minutes(int minutes)
    {
        int m = Math.Max(0, minutes);
        return m < 60 ? $"{m}m" : $"{m / 60}h {m % 60:00}m";
    }
}
