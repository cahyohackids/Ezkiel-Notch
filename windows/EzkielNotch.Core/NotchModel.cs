using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Core;

/// <summary>The rooms, in tab order (same order and names as the Mac).</summary>
public enum Room { Den, Hunt, Roar, Scratch, Purr, Stash, Oasis, Settings }

/// <summary>A short message under the top edge. Sound is a Mac sound name; Windows maps it.</summary>
public sealed record PeekRequest(string Title, string Subtitle, Room Room, string Sound, bool Urgent);

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
        }
        if (_dirty)
        {
            _dirty = false;
            Data.FocusState = Focus.Snapshot;
            Store?.Save(Data);
        }
    }

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
    public void SettingsChanged(bool everything = false)
    {
        Lang.Choice = Settings.Language;
        _dirty = true;
        Changed?.Invoke(everything ? null : Room.Settings);
    }
}
