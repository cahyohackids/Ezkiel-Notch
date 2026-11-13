namespace EzkielNotch.Core;

/// <summary>A peek's colour, by name: the Mac's Theme colours and room accents. The window turns it into a colour.</summary>
public enum PeekTint { Gold, Teal, Berry, Neutral, Whiskers, Den, Hunt, Rally, Oasis, Roar, Scout, Stash, Purr, Spotify, AppleMusic }

/// <summary>What a peek is about. Each kind has the Mac's icon and tint (see PeekLooks.Of).</summary>
public enum PeekKind
{
    FocusDone, BreakDone, TaskDue, Meeting, Water,
    Roar, RoarAi,
    CapsOn, CapsOff, Keyboard,
    WifiOn, Online, Offline, HeadphonesOn, HeadphonesOff,
    Charging, Plugged, OnBattery, BatteryLow,
    Screenshot, Download, Update, Restored, DataBroken,
}

/// <summary>How a peek looks: its glyph (Segoe Fluent Icons, Segoe MDL2 code points), its tint, whether
/// Ezkiel shows on the left (then the glyph goes right), and the Mac sound it plays ("" for none).</summary>
public sealed record PeekLook(string Glyph, PeekTint Tint, bool ShowsLion = false, string Sound = "");

/// <summary>The Mac's peeks (AppModel.showPeek calls), one by one, as Windows glyphs.</summary>
public static class PeekLooks
{
    public const string Lightning = "";
    public const string Cup = "";
    public const string Checklist = "";
    public const string Calendar = "";
    public const string Bell = "";
    public const string Up = "";
    public const string Globe = "";
    public const string Wifi = "";
    public const string Ethernet = "";
    public const string Warning = "";
    public const string Headphones = "";
    public const string Camera = "";
    public const string Download = "";
    public const string History = "";
    public const string Music = "";

    public static PeekLook Of(PeekKind kind, int percent = 100) => kind switch
    {
        PeekKind.FocusDone => new(Cup, PeekTint.Den, ShowsLion: true, Sound: "Glass"),
        PeekKind.BreakDone => new(Lightning, PeekTint.Den, ShowsLion: true, Sound: "Glass"),
        PeekKind.TaskDue => new(Checklist, PeekTint.Hunt, ShowsLion: true, Sound: "Ping"),
        PeekKind.Meeting => new(Calendar, PeekTint.Rally, Sound: "Glass"),
        PeekKind.Water => new(Cup, PeekTint.Oasis, ShowsLion: true, Sound: "Purr"),
        PeekKind.Roar => new(Bell, PeekTint.Roar),
        PeekKind.RoarAi => new(Bell, PeekTint.Scout),
        PeekKind.CapsOn => new(Up, PeekTint.Teal),
        PeekKind.CapsOff => new(Up, PeekTint.Neutral),
        PeekKind.Keyboard => new(Globe, PeekTint.Whiskers),
        PeekKind.WifiOn => new(Wifi, PeekTint.Teal),
        PeekKind.Online => new(Ethernet, PeekTint.Teal),
        PeekKind.Offline => new(Warning, PeekTint.Berry, Sound: "Funk"),
        PeekKind.HeadphonesOn => new(Headphones, PeekTint.Teal),
        PeekKind.HeadphonesOff => new(Headphones, PeekTint.Neutral),
        PeekKind.Charging or PeekKind.Plugged => new(Lightning, PeekTint.Teal),
        PeekKind.OnBattery => new(Battery(percent), PeekTint.Gold),
        PeekKind.BatteryLow => new(Battery(percent), PeekTint.Berry, Sound: "Funk"),
        PeekKind.Screenshot => new(Camera, PeekTint.Stash),
        PeekKind.Download => new(Download, PeekTint.Stash),
        PeekKind.Update => new(Download, PeekTint.Gold, ShowsLion: true),
        PeekKind.Restored => new(History, PeekTint.Gold),
        PeekKind.DataBroken => new(Warning, PeekTint.Berry),
        _ => new(Music, PeekTint.Gold),
    };

    /// <summary>The Mac's battery.0/25/50/75/100 steps, as Segoe's Battery0…Battery10.</summary>
    public static string Battery(int percent) => percent switch
    {
        < 13 => "",
        < 38 => "",
        < 63 => "",
        < 88 => "",
        _ => "",
    };

    /// <summary>Scout: the same glyphs as the Roar room's agent rows; Den orange when an agent needs you.</summary>
    public static PeekLook Scout(ScoutEvent e) => new(e.Kind switch
    {
        ScoutKind.NeedsYou => Warning,
        ScoutKind.Working => Lightning,
        ScoutKind.Done => e.Source == "codex" ? "" : "",
        _ => e.Source == "claude" ? Lightning : "",
    }, e.Kind == ScoutKind.NeedsYou ? PeekTint.Den : PeekTint.Scout, Sound: e.Kind == ScoutKind.NeedsYou ? "Tink" : "");

    /// <summary>A new song: the player's own colour (Spotify green, Apple Music pink), else Purr.</summary>
    public static PeekLook Track(string appId)
    {
        var id = appId.ToLowerInvariant();
        var tint = id.Contains("spotify") ? PeekTint.Spotify
            : id.Contains("applemusic") || id.Contains("itunes") ? PeekTint.AppleMusic : PeekTint.Purr;
        return new(Music, tint);
    }

    /// <summary>The Mac's colours (Theme.gold/teal/berry, Accent.*.deep, whiskers' light), as 0xRRGGBB.</summary>
    public static uint Rgb(PeekTint tint) => tint switch
    {
        PeekTint.Gold => 0xFFCF87,
        PeekTint.Teal => 0x6FD6B4,
        PeekTint.Berry => 0xFF5A7A,
        PeekTint.Neutral => 0x8E8E93,
        PeekTint.Whiskers => 0xF5F1EA,
        PeekTint.Den => 0xFF9500,
        PeekTint.Hunt => 0xFF453A,
        PeekTint.Rally => 0xBF5AF2,
        PeekTint.Oasis => 0x0A84FF,
        PeekTint.Roar => 0x30D158,
        PeekTint.Scout => 0x00C7BE,
        PeekTint.Stash => 0x5E5CE6,
        PeekTint.Purr => 0xFF375F,
        PeekTint.Spotify => 0x1ED760,
        PeekTint.AppleMusic => 0xFF4E6A,
        _ => 0xFFCF87,
    };
}

/// <summary>The battery ring's geometry (the Mac's ChargeRing): a track, an arc from 12 o'clock clockwise, the number.</summary>
public static class RingMath
{
    /// <summary>The Mac's stroke: a tenth of the ring, at least 2.4.</summary>
    public static double LineWidth(double size) => Math.Max(2.4, size * 0.1);

    /// <summary>The stroke's centre line stays inside the box.</summary>
    public static double Radius(double size, double lineWidth) => Math.Max(0, (size - lineWidth) / 2);

    /// <summary>"80" for 0.8; clamped to 0…100.</summary>
    public static string Label(double level) => ((int)Math.Round(Math.Clamp(level, 0, 1) * 100)).ToString();

    /// <summary>A point on the circle, a fraction of a turn clockwise from 12 o'clock.</summary>
    public static (double X, double Y) At(double turn, double cx, double cy, double r)
    {
        double a = turn * 2 * Math.PI;
        return (cx + r * Math.Sin(a), cy - r * Math.Cos(a));
    }

    /// <summary>An arc covering <paramref name="fraction"/> of the circle from <paramref name="startTurn"/>, clockwise:
    /// its two ends and whether it's the large arc (more than half).</summary>
    public static ((double X, double Y) Start, (double X, double Y) End, bool Large) Arc(double fraction, double cx, double cy, double r, double startTurn = 0)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        return (At(startTurn, cx, cy, r), At(startTurn + fraction, cx, cy, r), fraction > 0.5);
    }
}

/// <summary>The Mac's sound names, as Windows' own sounds in %WINDIR%\Media: each name its own sound, with
/// fallbacks for older or trimmed Windows installs, then a system sound.</summary>
public static class Chimes
{
    private static readonly Dictionary<string, string[]> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Glass"] = ["Windows Notify Calendar.wav", "chimes.wav"],
        ["Ping"] = ["Windows Notify Messaging.wav", "notify.wav"],
        ["Purr"] = ["Windows Notify Email.wav", "Windows Notify.wav"],
        ["Tink"] = ["Speech On.wav", "Windows Navigation Start.wav"],
        ["Bottle"] = ["Windows Notify System Generic.wav", "ding.wav"],
        ["Pop"] = ["Windows Proximity Notification.wav", "Windows Balloon.wav"],
        ["Funk"] = ["Windows Exclamation.wav", "chord.wav"],
        ["Hero"] = ["tada.wav", "Windows Print complete.wav"],
        ["Basso"] = ["Windows Critical Stop.wav", "Windows Error.wav"],
        ["Blow"] = ["Windows Hardware Insert.wav", "Windows Unlock.wav"],
        ["Frog"] = ["Windows Message Nudge.wav", "Windows Pop-up Blocked.wav"],
        ["Morse"] = ["Speech Misrecognition.wav", "Windows Information Bar.wav"],
        ["Sosumi"] = ["Windows Ding.wav", "Windows Default.wav"],
        ["Submarine"] = ["Windows Proximity Connection.wav", "Windows Hardware Remove.wav"],
    };

    /// <summary>The Mac sound names Windows knows.</summary>
    public static IEnumerable<string> Names => Files.Keys;

    /// <summary>Files to try, in order (empty for an unknown name).</summary>
    public static IReadOnlyList<string> Candidates(string mac) => Files.TryGetValue(mac, out var f) ? f : [];

    /// <summary>When none of the files exist: a system sound (Asterisk, Beep, Exclamation, Hand or Question).</summary>
    public static string Fallback(string mac) => mac switch
    {
        "Funk" or "Basso" => "Hand",
        "Sosumi" or "Frog" or "Blow" => "Exclamation",
        "Tink" or "Pop" or "Morse" => "Beep",
        "Ping" or "Submarine" => "Question",
        _ => "Asterisk",
    };
}
