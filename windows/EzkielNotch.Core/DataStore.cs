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
    /// <summary>An unreadable data.json that couldn't be moved aside yet: where it goes before the next write.</summary>
    private string? _setAside;
    private const int ReadAttempts = 5;
    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>A write failed (disk full, a locked file, no permission): the message, from the writing thread.</summary>
    public event Action<string>? SaveFailed;

    public static string DefaultPath() =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EzkielNotch", "data.json");

    public AppData Load(DateTime? now = null)
    {
        LastRecovery = null;
        // Only a file that isn't there is a fresh start. One that's there but can't be read (locked by a sync or
        // antivirus tool, no permission) gets a few more tries, then is handled like a damaged one: kept, never
        // overwritten by an empty start.
        string? text = null;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                text = File.ReadAllText(Path, Encoding.UTF8);
                break;
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return new AppData(); }
            catch (Exception) when (attempt < ReadAttempts) { Thread.Sleep(ReadRetryDelay); }
            catch (Exception) { break; }
        }

        if (text is not null && AppData.FromJson(text) is { } data)
        {
            RefreshBackup(now ?? DateTime.Now);
            return data;
        }

        var stamp = (now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var broken = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, $"data.broken-{stamp}.json");
        // If it can't be moved aside now (still locked), the first save moves it before writing, or doesn't write.
        try { File.Move(Path, broken, overwrite: true); } catch (Exception) { lock (_gate) _setAside = broken; }

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
                    if (_setAside is { } aside)
                    {
                        // Throws while it's still locked: then nothing is written over it.
                        if (File.Exists(Path)) File.Move(Path, aside, overwrite: true);
                        _setAside = null;
                    }
                    var tmp = Path + ".tmp";
                    File.WriteAllText(tmp, text, new UTF8Encoding(false));
                    File.Move(tmp, Path, overwrite: true);
                    _written = ticket;
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"Ezkiel Notch: couldn't save {Path}: {e.Message}");
                    SaveFailed?.Invoke(e.Message);
                }
            }
        }
        if (synchronously) Write(); else Task.Run(Write);
    }
}
