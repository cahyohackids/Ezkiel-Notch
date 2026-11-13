using System.Text.Json.Nodes;

namespace EzkielNotch.Core;

// The records in data.json. Field names, date format and decoding rules match the Mac app's NotchCore,
// so one file works on both: copy it across and every task, note, clip, drink and focus session comes along.

// Modified (sync, 1.9): when this version was made. Set by the sync; files from before 1.9 don't have it.

public sealed record TaskItem(Guid Id, string Title, bool Done, DateTime? Due, bool Reminded, DateTime Created, DateTime? Modified = null)
{
    public static TaskItem New(string title, DateTime? due = null) => new(Guid.NewGuid(), title, false, due, false, DateTime.UtcNow);
}

public sealed record NoteItem(Guid Id, string Title, string Body, DateTime Updated, DateTime? Modified = null)
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

/// <summary>A file on the Stash shelf. Bookmark is the Mac's security bookmark (base64), kept as is; Windows uses the path.</summary>
public sealed record ShelfRecord(Guid Id, string Path, string? Bookmark, DateTime Added)
{
    public static ShelfRecord New(string path) => new(Guid.NewGuid(), path, null, DateTime.UtcNow);
    /// <summary>The last part of a Windows or Mac path.</summary>
    public string Name => Path.TrimEnd('\\', '/').Split('\\', '/').Last() is { Length: > 0 } n ? n : Path;
}

public sealed record WaterEntry(Guid Id, DateTime Date, int Ml, DateTime? Modified = null)
{
    public static WaterEntry New(int ml, DateTime? at = null) => new(Guid.NewGuid(), at ?? DateTime.UtcNow, ml);
}

/// <summary>One finished focus session (breaks are not logged).</summary>
public sealed record FocusEntry(Guid Id, DateTime Start, int Minutes, DateTime? Modified = null)
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
    /// <summary>The closed island shows its wings (the live pill) only while something is live. Off: always.</summary>
    public bool MinimalClosed { get; set; } = true;
    /// <summary>Keep the island out of screen sharing, screenshots and recordings.</summary>
    public bool HideFromScreenShare { get; set; }
    public AppLanguage Language { get; set; } = AppLanguage.Auto;
    /// <summary>Chorus (lyrics) beside Purr. Off until the user taps the lyrics button (LRCLIB is only asked then).</summary>
    public bool ShowLyrics { get; set; }
    /// <summary>While Chorus is on, sing along under the closed island too.</summary>
    public bool ClosedLyrics { get; set; } = true;
    /// <summary>Which player Purr follows: "auto", "spotify" or "appleMusic" (see <see cref="Core.MusicSource"/>).
    /// The Mac's "musicAllowed" (its Automation prompt) means nothing here and stays in the file as it was.</summary>
    public string MusicSource { get; set; } = Core.MusicSource.Auto;
    /// <summary>Roar: unread counts from other apps (only the number, never the message).</summary>
    public bool Roar { get; set; } = true;
    /// <summary>Roar: peek when an app's unread count goes up.</summary>
    public bool RoarPeeks { get; set; } = true;
    /// <summary>Roar: apps the user muted (app ids).</summary>
    public List<string> RoarMuted { get; set; } = [];
    /// <summary>Scout: notifications from AI agents and scripts.</summary>
    public bool Scout { get; set; } = true;
    /// <summary>Pounce: peek when a new screenshot appears.</summary>
    public bool PounceScreenshots { get; set; }
    /// <summary>Pounce: peek when a download finishes in Downloads.</summary>
    public bool PounceDownloads { get; set; }
    /// <summary>Prowl: dots in the closed island while a microphone or camera is on.</summary>
    public bool Prowl { get; set; } = true;
    /// <summary>Rally: today's next meetings in Hunt and a peek 5 minutes before.</summary>
    public bool Rally { get; set; }
    /// <summary>Rally: "in 5m" in the closed island.</summary>
    public bool RallyCountdown { get; set; } = true;
    /// <summary>Windows only: the calendar's private iCal address Rally reads (Windows has no calendar access for
    /// apps outside the Store). The Mac ignores it.</summary>
    public string RallyCalendarUrl { get; set; } = "";
    /// <summary>Weather glance: the city picked in Settings (no location permission). Empty = off.</summary>
    public string WeatherCity { get; set; } = "";
    public double WeatherLatitude { get; set; }
    public double WeatherLongitude { get; set; }
    public bool Fahrenheit { get; set; }
    /// <summary>Whiskers: Caps Lock and keyboard-language peeks.</summary>
    public bool SystemOverlays { get; set; } = true;
    /// <summary>Whiskers: network and headphones peeks.</summary>
    public bool ConnectionPeeks { get; set; } = true;
    /// <summary>Ask GitHub once a day whether a newer version exists.</summary>
    public bool CheckUpdates { get; set; } = true;
    /// <summary>The global shortcut, in the Mac's names: controlOptionN = Ctrl+Alt+N, controlOptionSpace = Ctrl+Alt+Space,
    /// optionCommandI = Ctrl+Alt+I on Windows.</summary>
    public string Hotkey { get; set; } = "controlOptionN";

    /// <summary>The Mac's shortcut choices, with Ctrl+Alt for ⌃⌥ (⌥⌘I becomes Ctrl+Alt+I).</summary>
    public static string HotkeyLabel(string choice) => choice switch
    {
        "controlOptionSpace" => "Ctrl+Alt+Space",
        "optionCommandI" => "Ctrl+Alt+I",
        _ => "Ctrl+Alt+N",
    };
    /// <summary>"notch" (hangs from the top edge) or "island" (a floating pill, like the Dynamic Island).</summary>
    public string Style { get; set; } = "notch";
    /// <summary>"small", "medium" or "large".</summary>
    public string IslandSize { get; set; } = "medium";
    /// <summary>"tight", "medium" or "roomy": the space above the island.</summary>
    public string IslandGap { get; set; } = "medium";
    public bool IslandShadow { get; set; } = true;
    /// <summary>See-through island ("Liquid Glass" on the Mac; tinted, without blur, on Windows).</summary>
    public bool IslandGlass { get; set; }
    /// <summary>What the closed island shows of Ezkiel: "eyes", "lion" or "off".</summary>
    public string ClosedFace { get; set; } = "eyes";
    /// <summary>Which eyes: "lion", "anime" (sparkly), "cat", "cute" or "robot" (the Mac's EyeStyle).</summary>
    public string EyeStyle { get; set; } = "lion";
    public static readonly string[] EyeStyles = ["lion", "anime", "cat", "cute", "robot"];
    /// <summary>Ezkiel naps when nothing is going on.</summary>
    public bool MascotSleeps { get; set; } = true;

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
        s.MinimalClosed = Json.Bool(o["minimalClosed"]) ?? s.MinimalClosed;
        // Like the Mac: settings from before 1.3.1 (revision 1) get the calmer closed notch.
        if ((Json.Int(o["defaultsRevision"]) ?? 1) < 2) s.MinimalClosed = true;
        s.HideFromScreenShare = Json.Bool(o["hideFromScreenShare"]) ?? s.HideFromScreenShare;
        s.ShowLyrics = Json.Bool(o["showLyrics"]) ?? s.ShowLyrics;
        s.ClosedLyrics = Json.Bool(o["closedLyrics"]) ?? s.ClosedLyrics;
        // A source from a newer version that this one doesn't know falls back to Auto.
        s.MusicSource = Core.MusicSource.Normalize(Json.String(o["musicSource"]));
        s.Roar = Json.Bool(o["roar"]) ?? s.Roar;
        s.RoarPeeks = Json.Bool(o["roarPeeks"]) ?? s.RoarPeeks;
        s.RoarMuted = o["roarMuted"] is JsonArray muted ? [.. muted.Select(Json.String).OfType<string>()] : s.RoarMuted;
        s.Scout = Json.Bool(o["scout"]) ?? s.Scout;
        s.PounceScreenshots = Json.Bool(o["pounceScreenshots"]) ?? s.PounceScreenshots;
        s.PounceDownloads = Json.Bool(o["pounceDownloads"]) ?? s.PounceDownloads;
        s.Prowl = Json.Bool(o["prowl"]) ?? s.Prowl;
        s.Rally = Json.Bool(o["rally"]) ?? s.Rally;
        s.RallyCountdown = Json.Bool(o["rallyCountdown"]) ?? s.RallyCountdown;
        s.RallyCalendarUrl = Json.String(o["rallyCalendarURL"]) ?? s.RallyCalendarUrl;
        s.WeatherCity = Json.String(o["weatherCity"]) ?? s.WeatherCity;
        s.WeatherLatitude = Json.Double(o["weatherLatitude"]) ?? s.WeatherLatitude;
        s.WeatherLongitude = Json.Double(o["weatherLongitude"]) ?? s.WeatherLongitude;
        s.Fahrenheit = Json.Bool(o["fahrenheit"]) ?? s.Fahrenheit;
        s.SystemOverlays = Json.Bool(o["systemOverlays"]) ?? s.SystemOverlays;
        s.ConnectionPeeks = Json.Bool(o["connectionPeeks"]) ?? s.ConnectionPeeks;
        s.CheckUpdates = Json.Bool(o["checkUpdates"]) ?? s.CheckUpdates;
        s.Style = Json.String(o["style"]) is "island" ? "island" : "notch";
        s.IslandSize = Json.String(o["islandSize"]) is "small" or "large" ? Json.String(o["islandSize"])! : "medium";
        s.IslandGap = Json.String(o["islandGap"]) is "tight" or "roomy" ? Json.String(o["islandGap"])! : "medium";
        s.IslandShadow = Json.Bool(o["islandShadow"]) ?? s.IslandShadow;
        s.IslandGlass = Json.Bool(o["islandGlass"]) ?? s.IslandGlass;
        // Before 1.6 the Mac had "showMascot": off meant no face.
        s.ClosedFace = Json.String(o["closedFace"]) is "eyes" or "lion" or "off" ? Json.String(o["closedFace"])!
            : Json.Bool(o["showMascot"]) == false ? "off" : "eyes";
        // A style from a newer version that this one doesn't know falls back to the lion's.
        s.EyeStyle = Json.String(o["eyeStyle"]) is { } eye && EyeStyles.Contains(eye) ? eye : "lion";
        s.MascotSleeps = Json.Bool(o["mascotSleeps"]) ?? s.MascotSleeps;
        s.Hotkey = Json.String(o["hotkey"]) is "controlOptionN" or "controlOptionSpace" or "optionCommandI" ? Json.String(o["hotkey"])! : s.Hotkey;
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
        o["minimalClosed"] = MinimalClosed;
        o["hideFromScreenShare"] = HideFromScreenShare;
        o["showLyrics"] = ShowLyrics;
        o["closedLyrics"] = ClosedLyrics;
        o["musicSource"] = MusicSource;
        o["roar"] = Roar;
        o["roarPeeks"] = RoarPeeks;
        o["roarMuted"] = new JsonArray(RoarMuted.Select(m => (JsonNode?)m).ToArray());
        o["scout"] = Scout;
        o["pounceScreenshots"] = PounceScreenshots;
        o["pounceDownloads"] = PounceDownloads;
        o["prowl"] = Prowl;
        o["rally"] = Rally;
        o["rallyCountdown"] = RallyCountdown;
        o["rallyCalendarURL"] = RallyCalendarUrl;
        o["weatherCity"] = WeatherCity;
        o["weatherLatitude"] = WeatherLatitude;
        o["weatherLongitude"] = WeatherLongitude;
        o["fahrenheit"] = Fahrenheit;
        o["systemOverlays"] = SystemOverlays;
        o["connectionPeeks"] = ConnectionPeeks;
        o["checkUpdates"] = CheckUpdates;
        o["hotkey"] = Hotkey;
        o["style"] = Style;
        o["islandSize"] = IslandSize;
        o["islandGap"] = IslandGap;
        o["islandShadow"] = IslandShadow;
        o["islandGlass"] = IslandGlass;
        o["closedFace"] = ClosedFace;
        o["eyeStyle"] = EyeStyle;
        o["mascotSleeps"] = MascotSleeps;
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
    public List<ShelfRecord> Shelf { get; set; } = [];
    public List<WaterEntry> Water { get; set; } = [];
    public List<FocusEntry> FocusLog { get; set; } = [];
    /// <summary>The last Scout events (agents and scripts), newest first.</summary>
    public List<ScoutEvent> ScoutLog { get; set; } = [];
    /// <summary>The Den timer, so a running session survives a restart.</summary>
    public FocusSnapshot? FocusState { get; set; }
    public NotchSettings Settings { get; set; } = new();
    /// <summary>The welcome note was offered once (the Mac's key, so a shared data.json doesn't offer it twice).</summary>
    public bool Onboarded { get; set; }

    /// <summary>Top-level keys this app doesn't use yet (the shelf, Scout's log, update state…), kept as read.</summary>
    private JsonObject _rest = [];

    private static readonly string[] Known = ["tasks", "notes", "clips", "shelf", "water", "focusLog", "scoutLog", "focusState", "settings", "onboarded"];

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
            Shelf = Json.List(o["shelf"], ReadShelf),
            Water = Json.List(o["water"], ReadWater),
            FocusLog = Json.List(o["focusLog"], ReadFocus),
            ScoutLog = Json.List(o["scoutLog"], ScoutEvent.Read),
            FocusState = FocusSnapshot.Read(o["focusState"]),
            Settings = NotchSettings.Read(o["settings"]),
            Onboarded = Json.Bool(o["onboarded"]) ?? false,
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
            if (t.Modified is { } modified) j["modified"] = Json.Date(modified);
            return (JsonNode)j;
        }).ToArray());
        o["notes"] = new JsonArray(Notes.Select(n => (JsonNode)WithModified(new JsonObject
        {
            ["id"] = Json.Id(n.Id), ["title"] = n.Title, ["body"] = n.Body, ["updated"] = Json.Date(n.Updated),
        }, n.Modified)).ToArray());
        o["clips"] = new JsonArray(Clips.Select(c => (JsonNode)new JsonObject
        {
            ["id"] = Json.Id(c.Id), ["text"] = c.Text, ["captured"] = Json.Date(c.Captured),
        }).ToArray());
        o["shelf"] = new JsonArray(Shelf.Select(r =>
        {
            var j = new JsonObject { ["id"] = Json.Id(r.Id), ["path"] = r.Path, ["added"] = Json.Date(r.Added) };
            if (r.Bookmark is not null) j["bookmark"] = r.Bookmark;
            return (JsonNode)j;
        }).ToArray());
        o["water"] = new JsonArray(Water.Select(w => (JsonNode)WithModified(new JsonObject
        {
            ["id"] = Json.Id(w.Id), ["date"] = Json.Date(w.Date), ["ml"] = w.Ml,
        }, w.Modified)).ToArray());
        o["focusLog"] = new JsonArray(FocusLog.Select(f => (JsonNode)WithModified(new JsonObject
        {
            ["id"] = Json.Id(f.Id), ["start"] = Json.Date(f.Start), ["minutes"] = f.Minutes,
        }, f.Modified)).ToArray());
        o["scoutLog"] = new JsonArray(ScoutLog.Select(e => (JsonNode)e.Write()).ToArray());
        if (FocusState is { } fs) o["focusState"] = fs.Write(); else o.Remove("focusState");
        o["settings"] = Settings.Write();
        o["onboarded"] = Onboarded;
        // The Mac writes sorted keys; matching it keeps diffs between the two small.
        return Json.Sorted(o)!.ToJsonString(Json.Pretty);
    }

    private static JsonObject WithModified(JsonObject o, DateTime? modified)
    {
        if (modified is { } m) o["modified"] = Json.Date(m);
        return o;
    }

    /// <summary>Sync: the synced lists (tasks, notes, water, focusLog) exactly as data.json writes them.</summary>
    public JsonObject SyncedJson()
    {
        var all = (JsonObject)JsonNode.Parse(ToJson())!;
        var o = new JsonObject();
        foreach (var kind in SyncMerge.Kinds) o[kind] = all[kind]?.DeepClone() ?? new JsonArray();
        return o;
    }

    /// <summary>Sync: puts merged lists in. Returns whether anything changed. An entry that can't be read is skipped,
    /// never the list.</summary>
    public bool ApplySynced(JsonObject merged)
    {
        var tasks = Json.List(merged["tasks"], ReadTask);
        var notes = Json.List(merged["notes"], ReadNote);
        var water = Json.List(merged["water"], ReadWater);
        var focus = Json.List(merged["focusLog"], ReadFocus);
        bool changed = !tasks.SequenceEqual(Tasks) || !notes.SequenceEqual(Notes) || !water.SequenceEqual(Water) || !focus.SequenceEqual(FocusLog);
        Tasks = tasks;
        Notes = notes;
        Water = water;
        FocusLog = focus;
        return changed;
    }

    // Each record needs every non-optional field, as the Mac's decoder does. "modified" is optional (sync, 1.9).

    private static TaskItem? ReadTask(JsonObject o) =>
        Json.Guid(o["id"]) is { } id && Json.String(o["title"]) is { } title && Json.Bool(o["done"]) is { } done
        && Json.Bool(o["reminded"]) is { } reminded && Json.Date(o["created"]) is { } created
        && (o["due"] is null || Json.Date(o["due"]) is not null)
            ? new TaskItem(id, title, done, Json.Date(o["due"]), reminded, created, Json.Date(o["modified"])) : null;

    private static NoteItem? ReadNote(JsonObject o) =>
        Json.Guid(o["id"]) is { } id && Json.String(o["title"]) is { } title && Json.String(o["body"]) is { } body
        && Json.Date(o["updated"]) is { } updated
            ? new NoteItem(id, title, body, updated, Json.Date(o["modified"])) : null;

    private static ClipItem? ReadClip(JsonObject o) =>
        Json.Guid(o["id"]) is { } id && Json.String(o["text"]) is { } text && Json.Date(o["captured"]) is { } captured
            ? new ClipItem(id, text, captured) : null;

    private static ShelfRecord? ReadShelf(JsonObject o) =>
        Json.Guid(o["id"]) is { } id && Json.String(o["path"]) is { } path && Json.Date(o["added"]) is { } added
            ? new ShelfRecord(id, path, Json.String(o["bookmark"]), added) : null;

    private static WaterEntry? ReadWater(JsonObject o) =>
        Json.Guid(o["id"]) is { } id && Json.Date(o["date"]) is { } date && Json.Int(o["ml"]) is { } ml
            ? new WaterEntry(id, date, ml, Json.Date(o["modified"])) : null;

    private static FocusEntry? ReadFocus(JsonObject o) =>
        Json.Guid(o["id"]) is { } id && Json.Date(o["start"]) is { } start && Json.Int(o["minutes"]) is { } minutes
            ? new FocusEntry(id, start, minutes, Json.Date(o["modified"])) : null;
}
