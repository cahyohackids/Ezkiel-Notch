using System.IO;
using System.Windows.Threading;
using EzkielNotch.Core;

namespace EzkielNotch.Platform;

/// <summary>
/// Live downloads: follows the partial files browsers write in Downloads while downloading (Chrome and Edge's
/// .crdownload, Firefox's .part). A FileSystemWatcher notices them come and go; their size is read once a second
/// only while one exists. Chrome, Edge and Firefox don't say how big the file will be, so the island shows the size
/// so far and the speed. The Mac's PounceWatcher does the same (and reads Safari's final size).
/// </summary>
public sealed class DownloadWatcher
{
    /// <summary>The downloads in progress changed (sizes, speed, one started or ended).</summary>
    public event Action<IReadOnlyList<ActiveDownload>>? Changed;
    /// <summary>A followed download landed: its final path.</summary>
    public event Action<string>? Finished;

    public IReadOnlyList<ActiveDownload> Active => _tracker.Active;
    public string? Folder { get; private set; }

    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _poll;
    private readonly DispatcherTimer _settle;
    private FileSystemWatcher? _watcher;
    private DownloadTracker _tracker = new();

    public DownloadWatcher(Dispatcher ui)
    {
        _ui = ui;
        _poll = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(1) };
        _poll.Tick += (_, _) => Scan();
        // A burst of file events becomes one read.
        _settle = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(400) };
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            Scan();
        };
    }

    /// <summary>On: follow <paramref name="folder"/> (Downloads unless the self-test gives a temp folder). Off: stop.</summary>
    public void Configure(bool on, string? folder = null)
    {
        folder ??= Files.Downloads;
        if (!on || !Directory.Exists(folder))
        {
            Stop();
            return;
        }
        if (_watcher is not null && string.Equals(Folder, folder, StringComparison.OrdinalIgnoreCase)) return;
        Stop();
        Folder = folder;
        try
        {
            var w = new FileSystemWatcher(folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
            FileSystemEventHandler poke = (_, _) => _ui.BeginInvoke(Poke);
            w.Created += poke;
            w.Deleted += poke;
            w.Renamed += (_, _) => _ui.BeginInvoke(Poke);
            w.EnableRaisingEvents = true;
            _watcher = w;
        }
        catch (Exception)
        {
            // No watcher (a network folder, say): the folder is still read once now.
        }
        Scan();
    }

    private void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
        Folder = null;
        _poll.Stop();
        _settle.Stop();
        bool had = _tracker.Active.Count > 0;
        _tracker = new DownloadTracker();
        if (had) Changed?.Invoke([]);
    }

    private void Poke()
    {
        _settle.Stop();
        _settle.Start();
    }

    /// <summary>Reads the folder: the partial files with their sizes, and the names of the complete files.</summary>
    public void Scan()
    {
        if (Folder is not { } folder) return;
        var samples = new List<DownloadSample>();
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder))
            {
                var name = Path.GetFileName(path);
                if (DownloadTracker.FinalName(name) is { } final)
                {
                    long size = 0;
                    try { size = new FileInfo(path).Length; } catch (IOException) { }
                    samples.Add(new DownloadSample(name, final, size));
                }
                else completed.Add(name);
            }
        }
        catch (Exception)
        {
            return;
        }
        var finished = _tracker.Update(samples, completed, DateTime.UtcNow);
        if (_tracker.Active.Count > 0) { if (!_poll.IsEnabled) _poll.Start(); }
        else _poll.Stop();
        Changed?.Invoke(_tracker.Active);
        foreach (var name in finished) Finished?.Invoke(Path.Combine(folder, name));
    }

    /// <summary>Firefox keeps an empty file under the final name while it downloads: Pounce leaves that one alone.</summary>
    public bool InProgress(string path) =>
        _tracker.Active.Any(a => string.Equals(a.FinalName, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
}
