using System.IO;

namespace EzkielNotch.Platform;

/// <summary>A small record of each attempt to take the keyboard, next to data.json (focus.log), so a PC where
/// typing doesn't reach the island can say why. Kept under 64 KB.</summary>
public static class FocusLog
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EzkielNotch", "focus.log");

    public static void Write(string line)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            if (File.Exists(Path) && new FileInfo(Path).Length > 64 * 1024) File.Move(Path, Path + ".old", overwrite: true);
            File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch (Exception) { }
    }
}
