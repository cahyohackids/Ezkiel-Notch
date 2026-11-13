using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Core;

/// <summary>Quick timers ("Masak 10m"), besides Den's focus timer: the Mac's AppModel quick-timer part.</summary>
public sealed partial class NotchModel
{
    /// <summary>A timer just ended: the window shows the peek with "+5 min" and "Stop", and rings.</summary>
    public event Action<QuickTimer>? TimerDone;

    /// <summary>Running timers, soonest first.</summary>
    public List<QuickTimer> Timers => [.. Data.Timers.OrderBy(t => t.Ends)];

    /// <summary>The one the closed island counts down.</summary>
    public QuickTimer? SoonestTimer => QuickTimers.Soonest(Data.Timers, Now());

    /// <summary>Starts a timer (the oldest goes when there are already the most allowed). Null when the length is 0.</summary>
    public QuickTimer? StartTimer(string label, int seconds)
    {
        if (seconds <= 0) return null;
        var t = QuickTimer.Start(label, Math.Min(seconds, TimerPhrase.MaxSeconds), Now());
        Data.Timers.Add(t);
        while (Data.Timers.Count > QuickTimers.Limit) Data.Timers.Remove(Data.Timers.OrderBy(x => x.Ends).Last());
        if (!Data.Timers.Contains(t)) return null;
        Touch(Room.Den);
        return t;
    }

    /// <summary>Starts a timer from typed words ("timer 10m teh", "1h30m"). Null when they hold no length.</summary>
    public QuickTimer? StartTimer(string text) =>
        TimerPhrase.Parse(text) is { } r ? StartTimer(r.Label, r.Seconds) : null;

    public void CancelTimer(Guid id)
    {
        if (Data.Timers.RemoveAll(t => t.Id == id) > 0) Touch(Room.Den);
    }

    /// <summary>"+5 min" on a timer that just rang: the same label again, five minutes from now.</summary>
    public QuickTimer? SnoozeTimer(QuickTimer rang, int minutes = 5)
    {
        Data.Timers.RemoveAll(t => t.Id == rang.Id);
        return StartTimer(rang.Label, minutes * 60);
    }

    /// <summary>A timer's name as shown: its label, or "Timer".</summary>
    public static string TimerName(QuickTimer t) => t.Label.Length > 0 ? t.Label : L("Timer");

    private void CheckTimers(DateTime now)
    {
        if (Data.Timers.Count == 0 || !Data.Timers.Any(t => t.Ends <= now)) return;
        var (running, ring, _) = QuickTimers.Due(Data.Timers, now);
        Data.Timers = running;
        Touch(Room.Den);
        foreach (var t in ring.OrderBy(t => t.Ends)) TimerDone?.Invoke(t);
    }
}
