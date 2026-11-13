using System.IO;

namespace EzkielNotch.Platform;

/// <summary>Errors nothing else caught, next to data.json (crash.log), so a PC where the island misbehaves or quits
/// can say why. Like focus.log: kept under 64 KB, never throws, nothing leaves the machine.</summary>
public static class CrashLog
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EzkielNotch", "crash.log");

    public static void Write(string source, Exception? e)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            if (File.Exists(Path) && new FileInfo(Path).Length > 64 * 1024) File.Move(Path, Path + ".old", overwrite: true);
            File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {source} (v{typeof(CrashLog).Assembly.GetName().Version?.ToString(3)}): {e}{Environment.NewLine}");
        }
        catch (Exception) { }
    }
}
