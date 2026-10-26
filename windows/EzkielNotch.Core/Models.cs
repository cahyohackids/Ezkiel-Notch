using System.Text.Json.Nodes;

namespace EzkielNotch.Core;

// The records in data.json. Field names, date format and decoding rules match the Mac app's NotchCore,
// so one file works on both: copy it across and every task, note, clip, drink and focus session comes along.

public sealed record TaskItem(Guid Id, string Title, bool Done, DateTime? Due, bool Reminded, DateTime Created)
{
    public static TaskItem New(string title, DateTime? due = null) => new(Guid.NewGuid(), title, false, due, false, DateTime.UtcNow);
}

public sealed record NoteItem(Guid Id, string Title, string Body, DateTime Updated)
{
    public static NoteItem New() => new(Guid.NewGuid(), "", "", DateTime.UtcNow);

    /// <summary>The explicit title, else the first line of the body.</summary>
    public string DisplayTitle(string untitled)
    {
        var t = Title.Trim();
        if (t.Length > 0) return t;
        var line = Body.Split('\n', '\r').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        return line.Length > 0 ? line : untitled;
    }
}

public sealed record ClipItem(Guid Id, string Text, DateTime Captured)
{
    public static ClipItem New(string text) => new(Guid.NewGuid(), text, DateTime.UtcNow);
}

public sealed record WaterEntry(Guid Id, DateTime Date, int Ml)
{
    public static WaterEntry New(int ml, DateTime? at = null) => new(Guid.NewGuid(), at ?? DateTime.UtcNow, ml);
}

/// <summary>One finished focus session (breaks are not logged).</summary>
public sealed record FocusEntry(Guid Id, DateTime Start, int Minutes)
{
    public static FocusEntry New(DateTime start, int minutes) => new(Guid.NewGuid(), start, minutes);
}

public enum AppLanguage { Auto, En, Id }

/// <summary>
/// The settings the Windows app uses. Everything else in the file's "settings" (the Mac-only ones) is kept
/// as it was read and written back untouched.
/// </summary>
public sealed class NotchSettings
{
    public const int CurrentRevision = 2;

    public bool ExpandOnHover { get; set; } = true;
    public double HoverDelay { get; set; } = 0.25;
    public bool Sounds { get; set; } = true;
    public bool WaterReminders { get; set; } = true;
    public int WaterIntervalMinutes { get; set; } = 60;
    public bool LaunchAtLogin { get; set; }
    public int WaterGoal { get; set; } = 2000;
    public int ActiveHourStart { get; set; } = 8;
    public int ActiveHourEnd { get; set; } = 22;
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public bool HideInFullscreen { get; set; } = true;
    public bool QuietDuringFocus { get; set; } = true;
    public AppLanguage Language { get; set; } = AppLanguage.Auto;
    /// <summary>Chorus (lyrics) beside Purr. Off until the user taps the lyrics button (LRCLIB is only asked then).</summary>
    public bool ShowLyrics { get; set; }
    /// <summary>Roar: unread counts from other apps (only the number, never the message).</summary>
    public bool Roar { get; set; } = true;
    /// <summary>Roar: peek when an app's unread count goes up.</summary>
    public bool RoarPeeks { get; set; } = true;
    /// <summary>Roar: apps the user muted (app ids).</summary>
    public List<string> RoarMuted { get; set; } = [];
    /// <summary>Scout: notifications from AI agents and scripts.</summary>
    public bool Scout { get; set; } = true;

    /// <summary>The settings object as read, Mac-only keys included.</summary>
    internal JsonObject Raw { get; set; } = [];

    internal static NotchSettings Read(JsonNode? node)
    {
        var s = new NotchSettings();
        if (node is not JsonObject o) return s;
        s.Raw = (JsonObject)o.DeepClone();
        s.ExpandOnHover = Json.Bool(o["expandOnHover"]) ?? s.ExpandOnHover;
        s.HoverDelay = Json.Double(o["hoverDelay"]) ?? s.HoverDelay;
        s.Sounds = Json.Bool(o["sounds"]) ?? s.Sounds;
        s.WaterReminders = Json.Bool(o["waterReminders"]) ?? s.WaterReminders;
        s.WaterIntervalMinutes = Json.Int(o["waterIntervalMinutes"]) ?? s.WaterIntervalMinutes;
        s.LaunchAtLogin = Json.Bool(o["launchAtLogin"]) ?? s.LaunchAtLogin;
        s.WaterGoal = Json.Int(o["waterGoal"]) ?? s.WaterGoal;
        s.ActiveHourStart = Json.Int(o["activeHourStart"]) ?? s.ActiveHourStart;
        s.ActiveHourEnd = Json.Int(o["activeHourEnd"]) ?? s.ActiveHourEnd;
        s.FocusMinutes = Json.Int(o["focusMinutes"]) ?? s.FocusMinutes;
        s.BreakMinutes = Json.Int(o["breakMinutes"]) ?? s.BreakMinutes;
        s.HideInFullscreen = Json.Bool(o["hideInFullscreen"]) ?? s.HideInFullscreen;
        s.QuietDuringFocus = Json.Bool(o["quietDuringFocus"]) ?? s.QuietDuringFocus;
        s.ShowLyrics = Json.Bool(o["showLyrics"]) ?? s.ShowLyrics;
        s.Roar = Json.Bool(o["roar"]) ?? s.Roar;
        s.RoarPeeks = Json.Bool(o["roarPeeks"]) ?? s.RoarPeeks;
        s.RoarMuted = o["roarMuted"] is JsonArray muted ? [.. muted.Select(Json.String).OfType<string>()] : s.RoarMuted;
        s.Scout = Json.Bool(o["scout"]) ?? s.Scout;
        s.Language = Json.String(o["language"]) switch
        {
            "en" => AppLanguage.En,
            "id" => AppLanguage.Id,
            _ => AppLanguage.Auto,
        };
        return s;
    }

    internal JsonObject Write()
    {
        var o = (JsonObject)Raw.DeepClone();
        o["expandOnHover"] = ExpandOnHover;
        o["hoverDelay"] = HoverDelay;
        o["sounds"] = Sounds;
        o["waterReminders"] = WaterReminders;
        o["waterIntervalMinutes"] = WaterIntervalMinutes;
        o["launchAtLogin"] = LaunchAtLogin;
        o["waterGoal"] = WaterGoal;
        o["activeHourStart"] = ActiveHourStart;
        o["activeHourEnd"] = ActiveHourEnd;
        o["focusMinutes"] = FocusMinutes;
        o["breakMinutes"] = BreakMinutes;
        o["hideInFullscreen"] = HideInFullscreen;
        o["quietDuringFocus"] = QuietDuringFocus;
        o["showLyrics"] = ShowLyrics;
        o["roar"] = Roar;
        o["roarPeeks"] = RoarPeeks;
        o["roarMuted"] = new JsonArray(RoarMuted.Select(m => (JsonNode?)m).ToArray());
        o["scout"] = Scout;
        o["language"] = Language switch { AppLanguage.En => "en", AppLanguage.Id => "id", _ => "auto" };
        // Without it the Mac would treat these as pre-1.3.1 settings and migrate them again.
        o["defaultsRevision"] ??= CurrentRevision;
        return o;
    }
}

/// <summary>Everything Ezkiel Notch keeps on disk, as one JSON file.</summary>
public sealed class AppData
{
    public List<TaskItem> Tasks { get; set; } = [];
    public List<NoteItem> Notes { get; set; } = [];
    public List<ClipItem> Clips { get; set; } = [];
    public List<WaterEntry> Water { get; set; } = [];
    public List<FocusEntry> FocusLog { get; set; } = [];
    /// <summary>The last Scout events (agents and scripts), newest first.</summary>
    public List<ScoutEvent> ScoutLog { get; set; } = [];
    /// <summary>The Den timer, so a running session survives a restart.</summary>
    public FocusSnapshot? FocusState { get; set; }
    public NotchSettings Settings { get; set; } = new();

    /// <summary>Top-level keys this app doesn't use yet (the shelf, Scout's log, update state…), kept as read.</summary>
    private JsonObject _rest = [];

    private static readonly string[] Known = ["tasks", "notes", "clips", "water", "focusLog", "scoutLog", "focusState", "settings"];

    /// <summary>Parses data.json. Returns null only when the file isn't a JSON object at all.
    /// Like the Mac: one damaged entry costs that entry, never the whole list.</summary>
    public static AppData? FromJson(string text)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(text); }
        catch (Exception) { return null; }
        if (root is not JsonObject o) return null;

        var d = new AppData
        {
            Tasks = Json.List(o["tasks"], ReadTask),
            Notes = Json.List(o["notes"], ReadNote),
            Clips = Json.List(o["clips"], ReadClip),
            Water = Json.List(o["water"], ReadWater),
            FocusLog = Json.List(o["focusLog"], ReadFocus),
            ScoutLog = Json.List(o["scoutLog"], ScoutEvent.Read),
            FocusState = FocusSnapshot.Read(o["focusState"]),
            Settings = NotchSettings.Read(o["settings"]),
        };
        foreach (var (key, value) in o)
            if (!Known.Contains(key)) d._rest[key] = value?.DeepClone();
        return d;
    }

    public string ToJson()
    {
        var o = (JsonObject)_rest.DeepClone();
        o["tasks"] = new JsonArray(Tasks.Select(t =>
        {
            var j = new JsonObject
            {
                ["id"] = Json.Id(t.Id), ["title"] = t.Title, ["done"] = t.Done,
                ["reminded"] = t.Reminded, ["created"] = Json.Date(t.Created),
            };
            if (t.Due is { } due) j["due"] = Json.Date(due);
            return (JsonNode)j;
        }).ToArray());
        o["notes"] = new JsonArray(Notes.Select(n => (JsonNode)new JsonObject
        {
            ["id"] = Json.Id(n.Id), ["title"] = n.Title, ["body"] = n.Body, ["updated"] = Json.Date(n.Updated),
        }).ToArray());
        o["clips"] = new JsonArray(Clips.Select(c => (JsonNode)new JsonObject
        {
            ["id"] = Json.Id(c.Id), ["text"] = c.Text, ["captured"] = Json.Date(c.Captured),
        }).ToArray());
        o["water"] = new JsonArray(Water.Select(w => (JsonNode)new JsonObject
        {
            ["id"] = Json.Id(w.Id), ["date"] = Json.Date(w.Date), ["ml"] = w.Ml,
        }).ToArray());
        o["focusLog"] = new JsonArray(FocusLog.Select(f => (JsonNode)new JsonObject
        {
            ["id"] = Json.Id(f.Id), ["start"] = Json.Date(f.Start), ["minutes"] = f.Minutes,
        }).ToArray());
        o["scoutLog"] = new JsonArray(ScoutLog.Select(e => (JsonNode)e.Write()).ToArray());
        if (FocusState is { } fs) o["focusState"] = fs.Write(); else o.Remove("focusState");
        o["settings"] = Settings.Write();
        // The Mac writes sorted keys; matching it keeps diffs between the two small.
        return Json.Sorted(o)!.ToJsonString(Json.Pretty);
    }

    // Each record needs every non-optional field, as the Mac's decoder does.

    private static TaskItem? ReadTask(JsonObject o) =>
        Json.Guid(o["id"]) is { } id && Json.String(o["title"]) is { } title && Json.Bool(o["done"]) is { } done
        && Json.Bool(o["reminded"]) is { } reminded && Json.Date(o["created"]) is { } created
        && (o["due"] is null || Json.Date(o["due"]) is not null)
            ? new TaskItem(id, title, done, Json.Date(o["due"]), reminded, created) : null;

    private static NoteItem? ReadNote(JsonObject o) =>
        Json.Guid(o["id"]) is { } id && Json.String(o["title"]) is { } title && Json.String(o["body"]) is { } body
        && Json.Date(o["updated"]) is { } updated
            ? new NoteItem(id, title, body, updated) : null;

    private static ClipItem? ReadClip(JsonObject o) =>
        Json.Guid(o["id"]) is { } id && Json.String(o["text"]) is { } text && Json.Date(o["captured"]) is { } captured
            ? new ClipItem(id, text, captured) : null;

    private static WaterEntry? ReadWater(JsonObject o) =>
        Json.Guid(o["id"]) is { } id && Json.Date(o["date"]) is { } date && Json.Int(o["ml"]) is { } ml
            ? new WaterEntry(id, date, ml) : null;

    private static FocusEntry? ReadFocus(JsonObject o) =>
        Json.Guid(o["id"]) is { } id && Json.Date(o["start"]) is { } start && Json.Int(o["minutes"]) is { } minutes
            ? new FocusEntry(id, start, minutes) : null;
}
