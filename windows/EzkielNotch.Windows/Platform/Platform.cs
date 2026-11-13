using System.IO;
using System.Media;
using EzkielNotch.Core;
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

/// <summary>The Mac's sound names (Glass, Ping, Tink, Pop, Bottle, Purr, Funk…), each played as its own Windows
/// sound from %WINDIR%\Media (Core's Chimes says which), else a system sound. A missing file or no sound
/// device never throws.</summary>
public static class Sounds
{
    private static readonly Dictionary<string, SoundPlayer?> Players = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The file a name plays (null: none found, the system sound plays instead).</summary>
    public static string? FileFor(string name)
    {
        try
        {
            var media = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
            return Chimes.Candidates(name).Select(f => Path.Combine(media, f)).FirstOrDefault(File.Exists);
        }
        catch (Exception) { return null; }
    }

    public static void Play(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        try
        {
            if (!Players.TryGetValue(name, out var player))
            {
                player = FileFor(name) is { } file ? new SoundPlayer(file) : null;
                Players[name] = player;
            }
            if (player is not null) player.Play();
            else Fallback(name).Play();
        }
        catch (Exception)
        {
            // An unreadable file: the system sound once, and from now on.
            Players[name] = null;
            try { Fallback(name).Play(); } catch (Exception) { }
        }
    }

    private static SystemSound Fallback(string name) => Chimes.Fallback(name) switch
    {
        "Hand" => SystemSounds.Hand,
        "Exclamation" => SystemSounds.Exclamation,
        "Beep" => SystemSounds.Beep,
        "Question" => SystemSounds.Question,
        _ => SystemSounds.Asterisk,
    };
}
