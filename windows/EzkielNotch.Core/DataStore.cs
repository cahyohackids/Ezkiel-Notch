using System.Globalization;
using System.Text;

namespace EzkielNotch.Core;

/// <summary>
/// Reads and writes AppData as %APPDATA%\EzkielNotch\data.json (a port of the Mac's DataStore).
/// Writes are atomic. A file that can't be read is never overwritten: it is set aside as
/// data.broken-&lt;time&gt;.json and the daily backup (data.json.bak) is used if that reads.
/// </summary>
public sealed class DataStore(string path)
{
    public string Path { get; } = path;
    public string BackupPath => Path + ".bak";

    public abstract record Recovery(string Broken);
    /// <summary>data.json couldn't be read; it was kept as Broken and the backup from BackupDate was loaded.</summary>
    public sealed record RestoredBackup(string Broken, DateTime? BackupDate) : Recovery(Broken);
    /// <summary>data.json couldn't be read and there was no usable backup.</summary>
    public sealed record StartedFresh(string Broken) : Recovery(Broken);

    /// <summary>Set by Load() when the file had to be set aside.</summary>
    public Recovery? LastRecovery { get; private set; }

    private readonly object _gate = new();
    private long _queued, _written;

    public static string DefaultPath() =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EzkielNotch", "data.json");

    public AppData Load(DateTime? now = null)
    {
        LastRecovery = null;
        string text;
        try { text = File.ReadAllText(Path, Encoding.UTF8); }
        catch (Exception) { return new AppData(); }

        if (AppData.FromJson(text) is { } data)
        {
            RefreshBackup(now ?? DateTime.Now);
            return data;
        }

        var stamp = (now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var broken = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, $"data.broken-{stamp}.json");
        try { File.Move(Path, broken, overwrite: true); } catch (Exception) { }

        try
        {
            if (AppData.FromJson(File.ReadAllText(BackupPath, Encoding.UTF8)) is { } backup)
            {
                LastRecovery = new RestoredBackup(broken, File.GetLastWriteTime(BackupPath));
                return backup;
            }
        }
        catch (Exception) { }
        LastRecovery = new StartedFresh(broken);
        return new AppData();
    }

    /// <summary>One copy per day of the last file that read fine.</summary>
    private void RefreshBackup(DateTime now)
    {
        try
        {
            if (File.Exists(BackupPath) && now - File.GetLastWriteTime(BackupPath) < TimeSpan.FromHours(20)) return;
            File.Copy(Path, BackupPath, overwrite: true);
            File.SetLastWriteTime(BackupPath, now);
        }
        catch (Exception) { }
    }

    /// <summary>Serializes now, writes on a worker thread. A newer save always wins over an older one.</summary>
    public void Save(AppData data, bool synchronously = false)
    {
        var text = data.ToJson();
        long ticket = Interlocked.Increment(ref _queued);
        void Write()
        {
            lock (_gate)
            {
                if (ticket < _written) return;
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                    var tmp = Path + ".tmp";
                    File.WriteAllText(tmp, text, new UTF8Encoding(false));
                    File.Move(tmp, Path, overwrite: true);
                    _written = ticket;
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"Ezkiel Notch: couldn't save {Path}: {e.Message}");
                }
            }
        }
        if (synchronously) Write(); else Task.Run(Write);
    }
}
