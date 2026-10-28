using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Core;

/// <summary>The rooms, in tab order (same order and names as the Mac).</summary>
public enum Room { Den, Hunt, Roar, Scratch, Purr, Stash, Oasis, Settings }

/// <summary>A short message under the top edge. Sound is a Mac sound name; Windows maps it.</summary>
public sealed record PeekRequest(string Title, string Subtitle, Room Room, string Sound, bool Urgent, string? App = null, string? Value = null, string? Link = null);

/// <summary>A capsule at the bottom of the open island, optionally with an action (Undo).</summary>
public sealed record ToastRequest(string Text, string? ActionTitle = null, Action? Action = null);

/// <summary>
/// What the rooms show and do, without any UI (a port of the Mac's AppModel for Den, Hunt, Scratch and Oasis).
/// The window listens to Changed, Peek, Toast and Sound; Tick() runs once a second.
/// </summary>
public sealed class NotchModel
{
    public AppData Data { get; }
    public NotchSettings Settings => Data.Settings;
    public FocusClock Focus { get; private set; }
    public DataStore? Store { get; }

    /// <summary>Which room changed (null: everything, e.g. after a language switch).</summary>
    public event Action<Room?>? Changed;
    public event Action<PeekRequest>? Peek;
    public event Action<ToastRequest>? Toast;
    /// <summary>Mac sound names: Glass, Ping, Purr, Tink, Bottle.</summary>
    public event Action<string>? Sound;

    /// <summary>UTC clock; tests replace it.</summary>
    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    public Guid? SelectedNoteId { get; set; }
    public bool ShowingCatch { get; set; }
    /// <summary>Set by AddNote so the editor focuses the new note's title.</summary>
    public Guid? NoteToFocus { get; set; }

    private bool _dirty;
    private DateTime? _lastWaterReminder;
    private readonly DateTime _launch;
    private int _tickCount;

    public NotchModel(AppData data, DataStore? store = null, DateTime? launch = null)
    {
        Data = data;
        Store = store;
        if (store is not null) store.SaveFailed += _ => _saveFailed = true;
        _launch = launch ?? DateTime.UtcNow;
        var now = launch ?? DateTime.UtcNow;
        Data.Water = WaterLog.Pruned(Data.Water, now);
        Data.FocusLog = FocusStats.Pruned(Data.FocusLog, now);
        Focus = Data.FocusState is { } s ? FocusClock.From(s) : new FocusClock(Settings.FocusMinutes);
        SelectedNoteId = Data.Notes.FirstOrDefault()?.Id;
        Lang.Choice = Settings.Language;
    }

    // MARK: Saving

    /// <summary>Marks the data changed; Tick() writes it at most once a second.</summary>
    public void SaveSoon() => _dirty = true;

    public void SaveNow()
    {
        Data.FocusState = Focus.Snapshot;
        Store?.Save(Data, synchronously: true);
        _dirty = false;
    }

    private void Touch(Room room)
    {
        _dirty = true;
        Changed?.Invoke(room);
    }

    public void Tick()
    {
        _tickCount++;
        var now = Now();
        if (Focus.IsFinished(now)) CompleteFocus(now);
        if (_tickCount % 5 == 0)
        {
            CheckTasks(now);
            CheckWater(now);
            ExpireAgents(now);
            CheckMeetings(now);
        }
        if (_dirty)
        {
            _dirty = false;
            Data.FocusState = Focus.Snapshot;
            Store?.Save(Data);
        }
        // Said once a minute at most, on the UI thread (the write happens on another).
        if (_saveFailed && now - _saveWarned > TimeSpan.FromMinutes(1))
        {
            _saveFailed = false;
            _saveWarned = now;
            Toast?.Invoke(new ToastRequest(L("Couldn't save your data · check disk space")));
        }
    }

    private volatile bool _saveFailed;
    private DateTime _saveWarned = DateTime.MinValue;

    // MARK: Den

    public int FocusToday => FocusStats.Minutes(Data.FocusLog, Now());
    public int FocusStreak => FocusStats.Streak(Data.FocusLog, Now());

    public void FocusStartPause()
    {
        Focus.Toggle(Now());
        Touch(Room.Den);
    }

    public void FocusReset()
    {
        Focus.Reset();
        Touch(Room.Den);
    }

    /// <summary>While a session runs, a new length applies to the next one (it never resets the current).</summary>
    public void SetFocusMinutes(int minutes)
    {
        Settings.FocusMinutes = minutes;
        if (Focus.IsBusy) Toast?.Invoke(new ToastRequest(L("Next focus: %d min", minutes)));
        else Focus.SetMinutes(minutes, FocusMode.Focus);
        Touch(Room.Den);
    }

    public void SetBreakMinutes(int minutes)
    {
        Settings.BreakMinutes = minutes;
        if (!Focus.IsBusy && Focus.Mode == FocusMode.Rest) Focus.SetMinutes(minutes, FocusMode.Rest);
        Touch(Room.Den);
    }

    public void ToggleBreak()
    {
        if (Focus.IsBusy) return;
        if (Focus.Mode == FocusMode.Focus) Focus.SetMinutes(Settings.BreakMinutes, FocusMode.Rest);
        else Focus.SetMinutes(Settings.FocusMinutes, FocusMode.Focus);
        Touch(Room.Den);
    }

    private void CompleteFocus(DateTime now)
    {
        int minutes = (int)Math.Round(Focus.Duration.TotalMinutes);
        var finished = Focus.Complete(Settings.FocusMinutes, Settings.BreakMinutes);
        if (finished == FocusMode.Focus)
        {
            Data.FocusLog.Add(FocusEntry.New(now.AddMinutes(-minutes), minutes));
            int streak = FocusStreak;
            Peek?.Invoke(new PeekRequest(L("Focus complete"),
                streak > 1 ? L("%d-day streak · take %d min", streak, Settings.BreakMinutes)
                           : L("Take a %d-minute break", Settings.BreakMinutes),
                Room.Den, "Glass", Urgent: true));
        }
        else
        {
            Peek?.Invoke(new PeekRequest(L("Break complete"), L("Ready to focus again"), Room.Den, "Glass", Urgent: true));
        }
        Touch(Room.Den);
    }

    // MARK: Hunt

    public void AddTask(string title, DateTime? due)
    {
        var clean = title.Trim();
        if (clean.Length == 0) return;
        Data.Tasks.Insert(0, TaskItem.New(clean, due));
        Touch(Room.Hunt);
    }

    public void ToggleTask(Guid id)
    {
        int i = Data.Tasks.FindIndex(t => t.Id == id);
        if (i < 0) return;
        Data.Tasks[i] = Data.Tasks[i] with { Done = !Data.Tasks[i].Done };
        if (Data.Tasks[i].Done) Sound?.Invoke("Tink");
        Touch(Room.Hunt);
    }

    public void DeleteTask(Guid id)
    {
        int i = Data.Tasks.FindIndex(t => t.Id == id);
        if (i < 0) return;
        var removed = Data.Tasks[i];
        Data.Tasks.RemoveAt(i);
        Touch(Room.Hunt);
        Toast?.Invoke(new ToastRequest(L("Task deleted"), L("Undo"), () =>
        {
            Data.Tasks.Insert(Math.Min(i, Data.Tasks.Count), removed);
            Touch(Room.Hunt);
        }));
    }

    public void ClearDoneTasks()
    {
        var before = Data.Tasks.ToList();
        int count = before.Count(t => t.Done);
        if (count == 0) return;
        Data.Tasks.RemoveAll(t => t.Done);
        Touch(Room.Hunt);
        Toast?.Invoke(new ToastRequest(L("Cleared %d done", count), L("Undo"), () =>
        {
            Data.Tasks = before;
            Touch(Room.Hunt);
        }));
    }

    private void CheckTasks(DateTime now)
    {
        for (int i = 0; i < Data.Tasks.Count; i++)
        {
            var t = Data.Tasks[i];
            if (t.Done || t.Reminded || t.Due is not { } due || due > now) continue;
            Data.Tasks[i] = t with { Reminded = true };
            Peek?.Invoke(new PeekRequest(L("Task due"), t.Title, Room.Hunt, "Ping", Urgent: true));
            Touch(Room.Hunt);
            break;
        }
    }

    // MARK: Rally

    /// <summary>Set by the window: today's next meetings.</summary>
    public Func<IReadOnlyList<Meeting>> Meetings { get; set; } = () => [];
    private readonly HashSet<string> _notifiedMeetings = [];

    /// <summary>A peek 5 minutes before a meeting (once), with Join when it has a link.</summary>
    private void CheckMeetings(DateTime now)
    {
        if (!Settings.Rally) return;
        foreach (var m in Meetings())
        {
            var lead = m.Start - now;
            if (lead <= TimeSpan.Zero || lead > TimeSpan.FromMinutes(5) || !_notifiedMeetings.Add(m.Id)) continue;
            Peek?.Invoke(new PeekRequest(m.Title, L("Starts %@ · in %d min", Lang.Time(m.Start), (int)Math.Ceiling(lead.TotalMinutes)),
                                         Room.Hunt, "Glass", Urgent: true, Link: m.Link));
            break;
        }
    }

    // MARK: Scratch

    public void AddNote()
    {
        var note = NoteItem.New();
        ShowingCatch = false;
        Data.Notes.Insert(0, note);
        SelectedNoteId = note.Id;
        NoteToFocus = note.Id;
        Touch(Room.Scratch);
    }

    public void DeleteNote(Guid id)
    {
        int i = Data.Notes.FindIndex(n => n.Id == id);
        if (i < 0) return;
        var removed = Data.Notes[i];
        Data.Notes.RemoveAt(i);
        SelectedNoteId = Data.Notes.FirstOrDefault()?.Id;
        Touch(Room.Scratch);
        Toast?.Invoke(new ToastRequest(L("Note deleted"), L("Undo"), () =>
        {
            Data.Notes.Insert(Math.Min(i, Data.Notes.Count), removed);
            SelectedNoteId = removed.Id;
            Touch(Room.Scratch);
        }));
    }

    /// <summary>Called by the editor after typing pauses. Doesn't raise Changed: the editor already shows it.</summary>
    public void UpdateNote(Guid id, string title, string body)
    {
        int i = Data.Notes.FindIndex(n => n.Id == id);
        if (i < 0 || (Data.Notes[i].Title == title && Data.Notes[i].Body == body)) return;
        Data.Notes[i] = Data.Notes[i] with { Title = title, Body = body, Updated = Now() };
        _dirty = true;
    }

    /// <summary>Catch: the text the UI just read from the clipboard (only when the user pressed Capture).</summary>
    public void Capture(string? text)
    {
        var clean = text?.Trim() ?? "";
        if (clean.Length == 0)
        {
            Toast?.Invoke(new ToastRequest(L("Clipboard has no text")));
            return;
        }
        Data.Clips.RemoveAll(c => c.Text == clean);
        Data.Clips.Insert(0, ClipItem.New(clean));
        if (Data.Clips.Count > 30) Data.Clips.RemoveRange(30, Data.Clips.Count - 30);
        Touch(Room.Scratch);
    }

    public void RemoveClip(Guid id)
    {
        int i = Data.Clips.FindIndex(c => c.Id == id);
        if (i < 0) return;
        var removed = Data.Clips[i];
        Data.Clips.RemoveAt(i);
        Touch(Room.Scratch);
        Toast?.Invoke(new ToastRequest(L("Clip removed"), L("Undo"), () =>
        {
            Data.Clips.Insert(Math.Min(i, Data.Clips.Count), removed);
            Touch(Room.Scratch);
        }));
    }

    // MARK: Stash

    public const int StashLimit = 60;

    /// <summary>Adds files and folders (newest first, no duplicates, at most 60). Returns how many were new.</summary>
    public int AddToShelf(IEnumerable<string> paths)
    {
        var known = Data.Shelf.Select(r => Normal(r.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int added = 0;
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p) || !known.Add(Normal(p))) continue;
            Data.Shelf.Insert(0, ShelfRecord.New(p));
            added++;
        }
        int dropped = Math.Max(0, Data.Shelf.Count - StashLimit);
        if (dropped > 0) Data.Shelf.RemoveRange(StashLimit, dropped);
        if (added > 0) Sound?.Invoke("Pop");
        if (dropped > 0) Toast?.Invoke(new ToastRequest(L("Stash keeps %d items · oldest removed", StashLimit)));
        Touch(Room.Stash);
        return added;
    }

    private static string Normal(string p) => p.Replace('/', '\\').TrimEnd('\\');

    public void RemoveFromShelf(Guid id)
    {
        int i = Data.Shelf.FindIndex(r => r.Id == id);
        if (i < 0) return;
        var removed = Data.Shelf[i];
        Data.Shelf.RemoveAt(i);
        Touch(Room.Stash);
        Toast?.Invoke(new ToastRequest(L("Removed from Stash"), L("Undo"), () =>
        {
            Data.Shelf.Insert(Math.Min(i, Data.Shelf.Count), removed);
            Touch(Room.Stash);
        }));
    }

    public void ClearShelf()
    {
        var before = Data.Shelf.ToList();
        if (before.Count == 0) return;
        Data.Shelf.Clear();
        Touch(Room.Stash);
        Toast?.Invoke(new ToastRequest(L("Stash cleared"), L("Undo"), () =>
        {
            Data.Shelf = before;
            Touch(Room.Stash);
        }));
    }

    // MARK: Oasis

    public int WaterToday => WaterLog.Total(Data.Water, Now());
    public double WaterProgress => Math.Min(1, WaterToday / (double)Math.Max(1, Settings.WaterGoal));

    public void AddWater(int ml)
    {
        int before = WaterToday;
        Data.Water.Add(WaterEntry.New(ml, Now()));
        _lastWaterReminder = null;
        Sound?.Invoke("Bottle");
        if (before < Settings.WaterGoal && WaterToday >= Settings.WaterGoal)
            Toast?.Invoke(new ToastRequest(L("Goal reached · %d ml today", WaterToday)));
        Touch(Room.Oasis);
    }

    public void UndoWater()
    {
        var now = Now();
        int i = Data.Water.FindLastIndex(w => FocusStats.SameDay(w.Date, now));
        if (i < 0)
        {
            Toast?.Invoke(new ToastRequest(L("Nothing to undo today")));
            return;
        }
        int ml = Data.Water[i].Ml;
        Data.Water.RemoveAt(i);
        Touch(Room.Oasis);
        Toast?.Invoke(new ToastRequest(L("Removed %d ml · %d ml today", ml, WaterToday)));
    }

    public void SetWaterGoal(int ml)
    {
        Settings.WaterGoal = ml;
        Touch(Room.Oasis);
    }

    private void CheckWater(DateTime now)
    {
        if (!Settings.WaterReminders) return;
        var lastDrink = Data.Water.Count > 0 ? Data.Water.Max(w => w.Date) : now.AddDays(-1);
        if (!WaterLog.ReminderDue(lastDrink, _lastWaterReminder ?? _launch, Settings.WaterIntervalMinutes,
                                  Settings.ActiveHourStart, Settings.ActiveHourEnd, now)) return;
        _lastWaterReminder = now;
        Peek?.Invoke(new PeekRequest(L("Time for a sip"), L("%d of %d ml today", WaterToday, Settings.WaterGoal),
                                     Room.Oasis, "Purr", Urgent: false));
    }

    // MARK: Settings

    /// <summary>After any settings change. A language change redraws every room.</summary>
    // MARK: Roar

    /// <summary>AI desktop apps: their badges show under Scout, not in the Roar list.</summary>
    public static bool IsAiApp(string id)
    {
        var l = id.ToLowerInvariant();
        return l.Contains("claude") || l.Contains("chatgpt") || l.Contains("openai") || l.Contains("cursor")
            || l.Contains("windsurf") || l.Contains("perplexity");
    }

    /// <summary>The app in front (its id or exe path); set by the window. Its own news never peeks.</summary>
    public Func<string?> ForegroundApp { get; set; } = () => null;
    /// <summary>Set by the window: the Roar room is open (the list already shows it).</summary>
    public Func<bool> LookingAtRoar { get; set; } = () => false;

    public IReadOnlyList<Badge> Badges { get; private set; } = [];
    private readonly Dictionary<string, DateTime> _lastRoarPeek = [];

    private bool Visible(Badge b) => !Settings.RoarMuted.Contains(b.Id);
    public IEnumerable<Badge> RoarBadges => Badges.Where(b => Visible(b) && !IsAiApp(b.Id));
    public IEnumerable<Badge> AiBadges => Badges.Where(b => Visible(b) && IsAiApp(b.Id));
    public int UnreadTotal => RoarBadges.Sum(b => b.Count);
    public bool FocusQuiet => Settings.QuietDuringFocus && Focus.IsRunning && Focus.Mode == FocusMode.Focus;

    /// <summary>The watcher's latest list. A count that went up peeks (not for the app in front, not twice in 20 s).</summary>
    public void UpdateBadges(IReadOnlyList<Badge> badges)
    {
        var before = Badges.ToDictionary(b => b.Id, b => b.Count);
        Badges = badges;
        var now = Now();
        foreach (var b in badges)
        {
            int was = before.GetValueOrDefault(b.Id);
            if (b.Count <= was || !Settings.Roar || !Settings.RoarPeeks || !Visible(b)) continue;
            if (string.Equals(ForegroundApp(), b.Id, StringComparison.OrdinalIgnoreCase) || LookingAtRoar() || FocusQuiet) continue;
            if (_lastRoarPeek.TryGetValue(b.Id, out var last) && now - last < TimeSpan.FromSeconds(20)) continue;
            _lastRoarPeek[b.Id] = now;
            Peek?.Invoke(new PeekRequest(b.Name, was == 0 ? L("%d unread", b.Count) : L("%d new · %d unread", b.Count - was, b.Count),
                                         Room.Roar, "", Urgent: false, App: b.Id, Value: b.Label));
        }
        Changed?.Invoke(Room.Roar);
    }

    public void Mute(Badge b)
    {
        if (Settings.RoarMuted.Contains(b.Id)) return;
        Settings.RoarMuted.Add(b.Id);
        Touch(Room.Roar);
        Toast?.Invoke(new ToastRequest(L("%@ muted", b.Name), L("Undo"), () => Unmute(b.Id)));
    }

    public void Unmute(string id)
    {
        Settings.RoarMuted.RemoveAll(m => m == id);
        Touch(Room.Roar);
    }

    // MARK: Scout

    /// <summary>Agents working or waiting right now, by session.</summary>
    private readonly Dictionary<string, ScoutEvent> _agents = [];
    public bool ShowingScout { get; set; }

    /// <summary>Live agents, the ones waiting for you first.</summary>
    public List<ScoutEvent> LiveAgents => _agents.Values
        .OrderByDescending(e => e.Kind == ScoutKind.NeedsYou).ThenByDescending(e => e.Date).ToList();

    /// <summary>Log rows that aren't also showing as a live agent above them.</summary>
    public List<ScoutEvent> ScoutHistory
    {
        get
        {
            var live = _agents.Values.Select(e => e.Id).ToHashSet();
            return Data.ScoutLog.Where(e => !live.Contains(e.Id)).ToList();
        }
    }

    public void ScoutReceived(ScoutEvent e)
    {
        if (!Settings.Scout) return;
        var key = e.Session ?? $"{e.Source}·{e.Cwd}";
        switch (e.Kind)
        {
            case ScoutKind.Working:
                _agents[key] = e;
                Changed?.Invoke(Room.Roar);
                return;
            case ScoutKind.NeedsYou:
                // Claude's idle reminder after a turn that already finished (and peeked): nothing new.
                if (e.Body.Contains("waiting for your input", StringComparison.OrdinalIgnoreCase) && !_agents.ContainsKey(key)) return;
                _agents[key] = e;
                break;
            case ScoutKind.Done:
                _agents.Remove(key);
                break;
            case ScoutKind.Ended:
                _agents.Remove(key);
                Changed?.Invoke(Room.Roar);
                return;
        }
        Data.ScoutLog.Insert(0, e);
        if (Data.ScoutLog.Count > 40) Data.ScoutLog.RemoveRange(40, Data.ScoutLog.Count - 40);
        Touch(Room.Roar);

        // Looking at the agent already: no peek.
        if (e.AppBundleId is { } app && string.Equals(ForegroundApp(), app, StringComparison.OrdinalIgnoreCase)) return;
        if (LookingAtRoar() && ShowingScout) return;
        if (FocusQuiet && e.Kind != ScoutKind.NeedsYou) return;
        var detail = string.Join(" · ", new[] { e.Project, e.Body.Length == 0 ? null : L(e.Body) }.Where(x => x is not null));
        Peek?.Invoke(new PeekRequest(L(e.Title), detail, Room.Roar, e.Kind == ScoutKind.NeedsYou ? "Tink" : "",
                                     Urgent: e.Kind == ScoutKind.NeedsYou, App: e.AppBundleId));
    }

    /// <summary>An agent that never reported back (a closed terminal, a crash) doesn't stay live:
    /// "needs you" for 30 minutes, "working" for 45.</summary>
    private void ExpireAgents(DateTime now)
    {
        var stale = _agents.Where(p => now - p.Value.Date > TimeSpan.FromMinutes(p.Value.Kind == ScoutKind.NeedsYou ? 30 : 45)).Select(p => p.Key).ToList();
        if (stale.Count == 0) return;
        foreach (var k in stale) _agents.Remove(k);
        Changed?.Invoke(Room.Roar);
    }

    /// <summary>You went to the agent's terminal: its "needs you" has been seen, so it's back to working.</summary>
    public void AgentAppActivated(string? app)
    {
        if (app is null) return;
        var seen = _agents.Where(p => p.Value.Kind == ScoutKind.NeedsYou && string.Equals(p.Value.AppBundleId, app, StringComparison.OrdinalIgnoreCase)).ToList();
        if (seen.Count == 0) return;
        foreach (var (k, e) in seen)
            _agents[k] = e with { Kind = ScoutKind.Working, Title = e.Source == "codex" ? "Codex is working" : "Claude is working", Body = "" };
        Changed?.Invoke(Room.Roar);
    }

    public void DismissAgent(ScoutEvent e)
    {
        foreach (var k in _agents.Where(p => p.Value.Id == e.Id).Select(p => p.Key).ToList()) _agents.Remove(k);
        Changed?.Invoke(Room.Roar);
    }

    public void ClearScoutLog()
    {
        var before = Data.ScoutLog.ToList();
        if (before.Count == 0) return;
        Data.ScoutLog.Clear();
        Touch(Room.Roar);
        Toast?.Invoke(new ToastRequest(L("Scout cleared"), L("Undo"), () =>
        {
            Data.ScoutLog = before;
            Touch(Room.Roar);
        }));
    }

    /// <summary>A short message in the open island (for the UI's own actions, like copying).</summary>
    public void RaiseToast(string text) => Toast?.Invoke(new ToastRequest(text));

    // MARK: Settings

    public void SettingsChanged(bool everything = false)
    {
        Lang.Choice = Settings.Language;
        _dirty = true;
        Changed?.Invoke(everything ? null : Room.Settings);
    }
}
