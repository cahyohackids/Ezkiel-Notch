using System.Text.Json.Nodes;

namespace EzkielNotch.Core;

public enum FocusMode { Focus, Rest }

/// <summary>
/// Wall-clock based focus/break timer (a port of the Mac's FocusClock). It stores an end time instead of
/// counting ticks, so it stays correct across sleep, lag and restarts. All times are UTC.
/// </summary>
public sealed class FocusClock
{
    public FocusMode Mode { get; private set; } = FocusMode.Focus;
    public TimeSpan Duration { get; private set; }
    /// <summary>Set while running.</summary>
    public DateTime? End { get; private set; }
    /// <summary>Set while paused.</summary>
    public TimeSpan? PausedRemaining { get; private set; }

    public FocusClock(int minutes = 25) => Duration = TimeSpan.FromMinutes(Math.Max(1, minutes));

    public bool IsRunning => End is not null;
    public bool IsPaused => PausedRemaining is not null;
    public bool IsBusy => IsRunning || IsPaused;

    public TimeSpan Remaining(DateTime now) =>
        End is { } end ? Max(TimeSpan.Zero, end - now) : PausedRemaining ?? Duration;

    /// <summary>0 at start, 1 when finished.</summary>
    public double Progress(DateTime now) =>
        Duration <= TimeSpan.Zero ? 0 : Math.Clamp(1 - Remaining(now) / Duration, 0, 1);

    public bool IsFinished(DateTime now) => End is { } end && now >= end;

    public void SetMinutes(int minutes, FocusMode? mode = null)
    {
        if (mode is { } m) Mode = m;
        Duration = TimeSpan.FromMinutes(Math.Max(1, minutes));
        Reset();
    }

    public void Start(DateTime now)
    {
        if (IsRunning) return;
        End = now + (PausedRemaining ?? Duration);
        PausedRemaining = null;
    }

    public void Pause(DateTime now)
    {
        if (End is not { } end) return;
        PausedRemaining = Max(TimeSpan.Zero, end - now);
        End = null;
    }

    public void Toggle(DateTime now)
    {
        if (IsRunning) Pause(now); else Start(now);
    }

    public void Reset()
    {
        End = null;
        PausedRemaining = null;
    }

    /// <summary>Call when IsFinished turns true. Flips focus ⇄ rest and returns the finished mode.</summary>
    public FocusMode Complete(int focusMinutes, int breakMinutes)
    {
        var finished = Mode;
        if (finished == FocusMode.Focus) SetMinutes(breakMinutes, FocusMode.Rest);
        else SetMinutes(focusMinutes, FocusMode.Focus);
        return finished;
    }

    /// <summary>"24:59", or "1:00:00" from an hour up. Rounds up, like the Mac.</summary>
    public static string Format(TimeSpan t)
    {
        int total = (int)Math.Ceiling(Math.Max(0, t.TotalSeconds) - 1e-9);
        int h = total / 3600, m = total % 3600 / 60, s = total % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
    }

    public FocusSnapshot Snapshot => new(Mode, Duration.TotalSeconds, End, PausedRemaining?.TotalSeconds);

    public static FocusClock From(FocusSnapshot s)
    {
        var c = new FocusClock(Math.Max(1, (int)Math.Round(s.Duration / 60)))
        {
            Mode = s.Mode,
            Duration = TimeSpan.FromSeconds(Math.Max(60, s.Duration)),
        };
        if (s.End is { } end) c.End = end;
        else if (s.PausedRemaining is { } r) c.PausedRemaining = TimeSpan.FromSeconds(Math.Clamp(r, 0, c.Duration.TotalSeconds));
        return c;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}

/// <summary>What the timer needs to survive a restart. Same keys as the Mac's FocusSnapshot.</summary>
public sealed record FocusSnapshot(FocusMode Mode, double Duration, DateTime? End, double? PausedRemaining)
{
    internal static FocusSnapshot? Read(JsonNode? n)
    {
        if (n is not JsonObject o) return null;
        FocusMode? mode = Json.String(o["mode"]) switch { "focus" => FocusMode.Focus, "rest" => FocusMode.Rest, _ => null };
        if (mode is null || Json.Double(o["duration"]) is not { } duration) return null;
        return new FocusSnapshot(mode.Value, duration, Json.Date(o["end"]), Json.Double(o["pausedRemaining"]));
    }

    internal JsonObject Write()
    {
        var o = new JsonObject { ["mode"] = Mode == FocusMode.Rest ? "rest" : "focus", ["duration"] = Duration };
        if (End is { } end) o["end"] = Json.Date(end);
        if (PausedRemaining is { } r) o["pausedRemaining"] = r;
        return o;
    }
}
