using System.IO;
using System.Media;
using Microsoft.Win32;

namespace EzkielNotch.Platform;

/// <summary>Start with Windows: the per-user Run key (no admin, no scheduled task).</summary>
public static class Startup
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "EzkielNotch";

    /// <summary>Self-test and previews never touch the registry.</summary>
    public static bool Disabled { get; set; }

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled
    {
        get
        {
            if (Disabled) return false;
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(Key);
                return k?.GetValue(Name) is string;
            }
            catch (Exception) { return false; }
        }
    }

    /// <summary>Returns what is now true.</summary>
    public static bool Set(bool on)
    {
        if (Disabled) return on;
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            if (on) k.SetValue(Name, Command);
            else k.DeleteValue(Name, throwOnMissingValue: false);
        }
        catch (Exception) { }
        return IsEnabled;
    }

    /// <summary>After an update moves the app, keep the entry pointing at this copy.</summary>
    public static void Repair()
    {
        if (IsEnabled) Set(true);
    }
}

/// <summary>The Mac's sound names, played with Windows' own sounds from %WINDIR%\Media.</summary>
public static class Sounds
{
    private static readonly Dictionary<string, string[]> Map = new()
    {
        ["Glass"] = ["Windows Notify Calendar.wav", "chimes.wav"],
        ["Ping"] = ["Windows Notify Messaging.wav", "notify.wav"],
        ["Purr"] = ["Windows Notify Email.wav", "notify.wav"],
        ["Tink"] = ["Speech On.wav", "Windows Navigation Start.wav"],
        ["Bottle"] = ["Windows Notify System Generic.wav", "ding.wav"],
    };

    private static readonly Dictionary<string, SoundPlayer?> Players = [];

    public static void Play(string name)
    {
        try
        {
            if (!Players.TryGetValue(name, out var player))
            {
                var media = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
                var file = Map.GetValueOrDefault(name, [])
                    .Select(f => Path.Combine(media, f)).FirstOrDefault(File.Exists);
                player = file is null ? null : new SoundPlayer(file);
                player?.LoadAsync();
                Players[name] = player;
            }
            if (player is not null) player.Play();
            else SystemSounds.Asterisk.Play();
        }
        catch (Exception)
        {
            // No sound device: stay quiet.
        }
    }
}
