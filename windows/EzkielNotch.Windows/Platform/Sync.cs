using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using EzkielNotch.Core;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Platform;

/// <summary>
/// Sync with other PCs and Macs through a folder the user's cloud drive syncs (OneDrive, iCloud Drive, Dropbox…).
/// The merging is EzkielNotch.Core's SyncMerge (the Mac's rules, same fixtures); this runs it: on launch, ~5 s after
/// local changes, when another device's file changes (a folder watcher, and every 60 s since cloud drives don't
/// always tell), and on "Sync now". The Mac's SyncController does the same.
/// </summary>
public sealed class SyncService
{
    /// <summary>The app's one sync (Settings reads it).</summary>
    public static SyncService? Current { get; set; }

    public SyncConfig Config { get; }
    public bool Syncing { get; private set; }
    /// <summary>Files the cloud drive hasn't downloaded yet, at the last sync.</summary>
    public int Waiting { get; private set; }
    /// <summary>What a first sync with a newly picked folder would do, waiting for "Start syncing".</summary>
    public (string Folder, List<string> Lines)? Preview { get; private set; }
    /// <summary>Status, devices or the preview changed (on the UI thread).</summary>
    public event Action? Changed;
    /// <summary>The island tucks away while the folder dialog is up, and opens Settings again after.</summary>
    public Action? BeforeDialog { get; set; }
    public Action? AfterDialog { get; set; }

    private readonly NotchModel _model;
    private readonly Dispatcher _ui;
    private readonly string _dataPath, _configPath, _basePath;
    private readonly DispatcherTimer _debounce, _periodic;
    private FileSystemWatcher? _watcher;
    private string? _lastSynced;
    private bool _again, _force;
    private DateTime _lastPeek = DateTime.MinValue;

    public SyncService(NotchModel model, string dataPath, Dispatcher ui)
    {
        _model = model;
        _ui = ui;
        _dataPath = dataPath;
        var dir = Path.GetDirectoryName(dataPath)!;
        _configPath = Path.Combine(dir, "sync.json");
        _basePath = Path.Combine(dir, "sync-base.json");
        Config = SyncConfig.Load(_configPath);
        if (Config.DeviceId.Length == 0) Config.DeviceId = Guid.NewGuid().ToString("D").ToUpperInvariant();
        if (Config.DeviceName.Length == 0) Config.DeviceName = Environment.MachineName;
        _debounce = new DispatcherTimer(DispatcherPriority.Background, ui);
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            bool force = _force;
            _force = false;
            if (force || _lastSynced is null || Snapshot() != _lastSynced) Sync();
        };
        _periodic = new DispatcherTimer(TimeSpan.FromSeconds(60), DispatcherPriority.Background, (_, _) => Sync(), ui);
        _periodic.Stop();
        model.Saved += () => { if (Config.Enabled) Schedule(TimeSpan.FromSeconds(5)); };
    }

    private string Snapshot() => _model.Data.SyncedJson().ToJsonString();

    /// <summary>On launch: sync once and start watching.</summary>
    public void Start()
    {
        if (!Config.Enabled) return;
        Watch();
        Sync();
    }

    public void SyncNow() => Sync();

    private void Schedule(TimeSpan delay, bool force = false)
    {
        _force |= force;
        _debounce.Stop();
        _debounce.Interval = delay;
        _debounce.Start();
    }

    private void Watch()
    {
        Unwatch();
        _periodic.Start();
        try
        {
            var dir = SyncRunner.Directory(Config.Folder);
            Directory.CreateDirectory(dir);
            _watcher = new FileSystemWatcher(dir, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
            };
            // A file appearing or being replaced: give the cloud drive a moment to finish writing it.
            void OnFile(object _, FileSystemEventArgs e) => _ui.BeginInvoke(() => Schedule(TimeSpan.FromSeconds(2), force: true));
            _watcher.Changed += OnFile;
            _watcher.Created += OnFile;
            _watcher.Renamed += (s, e) => OnFile(s, e);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception)
        {
            // The folder is gone or offline: the periodic sync says so in the status line.
        }
    }

    private void Unwatch()
    {
        _periodic.Stop();
        _watcher?.Dispose();
        _watcher = null;
    }

    private void Sync()
    {
        if (!Config.Enabled || Config.Folder.Length == 0) return;
        if (Syncing)
        {
            _again = true;
            return;
        }
        Syncing = true;
        Changed?.Invoke();
        var snapshot = _model.Data.SyncedJson();
        var snapText = snapshot.ToJsonString();
        string folder = Config.Folder, id = Config.DeviceId, name = Config.DeviceName, version = Updater.Current;
        Task.Run(() =>
        {
            SyncRunner.Plan? plan = null;
            Exception? error = null;
            try { plan = SyncRunner.MakePlan(folder, id, name, "windows", version, snapshot, SyncRunner.ReadJson(_basePath), DateTime.UtcNow); }
            catch (Exception e) { error = e; }
            _ui.BeginInvoke(() => Finish(plan, error, snapText));
        });
    }

    private void Finish(SyncRunner.Plan? plan, Exception? error, string snapText)
    {
        if (!Config.Enabled) { Done(); return; }
        if (plan is null)
        {
            Fail(error);
            Done();
            return;
        }
        // Edited while the folder was read: start over with the new data, nothing written.
        if (Snapshot() != snapText)
        {
            _again = true;
            Done();
            return;
        }
        if (plan.NewDeviceId) Config.DeviceId = plan.DeviceId;
        // Changes from elsewhere: the data as it was stays next to data.json until the next such sync.
        if (plan.Result.Changes.Count > 0) Backup("data.json.sync-prev", replace: true);
        _model.ApplySynced(plan.Result.Data);
        _lastSynced = Snapshot();
        Waiting = plan.Waiting;
        string root = Config.Folder;
        Task.Run(() =>
        {
            Exception? failed = null;
            try { SyncRunner.Commit(plan, root, _basePath); }
            catch (Exception e) { failed = e; }
            _ui.BeginInvoke(() =>
            {
                if (failed is not null) Fail(failed);
                else
                {
                    Config.LastSync = DateTime.UtcNow;
                    Config.LastError = plan.Unreadable.Count == 0 ? null : L("Couldn't read %@ · it may still be syncing", string.Join(", ", plan.Unreadable));
                    Config.Devices = plan.Devices;
                    Config.Save(_configPath);
                    Announce(plan);
                }
                Done();
            });
        });
    }

    private void Done()
    {
        Syncing = false;
        Changed?.Invoke();
        if (_again)
        {
            _again = false;
            Schedule(TimeSpan.FromSeconds(1), force: true);
        }
    }

    private void Fail(Exception? error)
    {
        Config.LastError = error is SyncRunner.Failure f ? f.Message : error?.Message ?? "Sync failed";
        Config.Save(_configPath);
    }

    /// <summary>"Synced from &lt;device&gt;", only when another device changed something here, at most every 5 minutes.</summary>
    private void Announce(SyncRunner.Plan plan)
    {
        var fromOthers = plan.Result.Changes.Where(c => !string.Equals(c.From, plan.DeviceId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (fromOthers.Count == 0 || DateTime.UtcNow - _lastPeek < TimeSpan.FromMinutes(5)) return;
        _lastPeek = DateTime.UtcNow;
        var name = plan.Devices.FirstOrDefault(d => d.Id == fromOthers[0].From)?.Name;
        _model.SyncPeek(string.IsNullOrEmpty(name) ? L("another device") : name, fromOthers.Count);
    }

    private void Backup(string name, bool replace)
    {
        var target = Path.Combine(Path.GetDirectoryName(_dataPath)!, name);
        if (File.Exists(target) && !replace) return;
        _model.SaveNow();
        try { File.Copy(_dataPath, target, overwrite: true); } catch (Exception) { }
    }

    // MARK: Settings

    /// <summary>The folder dialog. The first sync with it waits for "Start syncing" with a preview of what will merge.</summary>
    public void ChooseFolder()
    {
        BeforeDialog?.Invoke();
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = L("Pick a folder your cloud drive syncs (iCloud Drive, OneDrive, Google Drive, Dropbox…). Every device picks the same folder."),
        };
        if (Environment.GetEnvironmentVariable("OneDrive") is { Length: > 0 } oneDrive && Directory.Exists(oneDrive)) dialog.InitialDirectory = oneDrive;
        bool ok = dialog.ShowDialog() == true;
        AfterDialog?.Invoke();
        if (ok) Prepare(dialog.FolderName);
    }

    /// <summary>Reads the folder and says what the first sync would bring in. Nothing is written yet.</summary>
    public void Prepare(string folder)
    {
        var snapshot = _model.Data.SyncedJson();
        string id = Config.DeviceId, name = Config.DeviceName, version = Updater.Current;
        Task.Run(() =>
        {
            List<string> lines;
            try
            {
                var plan = SyncRunner.MakePlan(folder, id, name, "windows", version, snapshot, SyncRunner.ReadJson(_basePath), DateTime.UtcNow);
                lines = Describe(plan);
            }
            catch (Exception e)
            {
                lines = [L(e is SyncRunner.Failure f ? f.Message : e.Message)];
            }
            _ui.BeginInvoke(() =>
            {
                Preview = (folder, lines);
                Changed?.Invoke();
            });
        });
    }

    public static List<string> Describe(SyncRunner.Plan plan)
    {
        var lines = new List<string>();
        var others = plan.Devices.Where(d => !d.Ignored).Select(d => d.Name.Length == 0 ? L("another device") : d.Name).ToList();
        if (others.Count == 0) lines.Add(L("No other devices in this folder yet. This PC's data becomes the first copy."));
        else
        {
            lines.Add(L("Found: %@", string.Join(", ", others)));
            var summary = SyncRunner.Summary(plan.Result.Changes);
            var names = new Dictionary<string, string>
            {
                ["tasks"] = L("tasks"), ["notes"] = L("notes"), ["water"] = L("drinks"), ["focusLog"] = L("focus sessions"),
            };
            foreach (var kind in SyncMerge.Kinds)
            {
                if (!summary.TryGetValue(kind, out var s)) continue;
                var parts = new List<string>();
                if (s.Added > 0) parts.Add(L("%d new", s.Added));
                if (s.Updated > 0) parts.Add(L("%d updated", s.Updated));
                if (s.Removed > 0) parts.Add(L("%d removed", s.Removed));
                var label = names[kind];
                lines.Add($"{char.ToUpper(label[0], Lang.Culture)}{label[1..]}: {string.Join(" · ", parts)}");
            }
            if (summary.Count == 0) lines.Add(L("Nothing here changes. Your data is sent to the other devices."));
        }
        lines.Add(L("Your data is backed up first as data.json.before-sync."));
        return lines;
    }

    public void ConfirmPreview()
    {
        if (Preview is not { } p) return;
        Backup("data.json.before-sync", replace: false);
        Config.Folder = p.Folder;
        Config.Enabled = true;
        Config.LastError = null;
        Config.Save(_configPath);
        Preview = null;
        Watch();
        Sync();
    }

    public void CancelPreview()
    {
        Preview = null;
        Changed?.Invoke();
    }

    /// <summary>Stop syncing on this device. Its data stays; its file stays in the folder for the others.</summary>
    public void Stop()
    {
        Unwatch();
        _debounce.Stop();
        Config.Enabled = false;
        Config.LastError = null;
        Config.Save(_configPath);
        Preview = null;
        Changed?.Invoke();
    }

    public void SetDeviceName(string name)
    {
        var clean = name.Trim();
        if (clean.Length == 0 || clean == Config.DeviceName) return;
        Config.DeviceName = clean;
        Config.Save(_configPath);
        Sync();
    }

    /// <summary>The status line: the last sync or the problem, in plain words.</summary>
    public string Status
    {
        get
        {
            if (!Config.Enabled) return L("Off · your data stays on this PC");
            if (Syncing) return L("Syncing…");
            if (Config.LastError is { } error) return L(error);
            if (Config.LastSync is not { } at) return L("Waiting for the first sync…");
            var s = L("Synced %@", Lang.Time(at.ToLocalTime()));
            if (Waiting > 0) s += " · " + L("%d files still downloading", Waiting);
            return s;
        }
    }

    public bool IsError => Config.Enabled && !Syncing && Config.LastError is not null;

    /// <summary>Self-test: sync against a folder without the dialog or the preview.</summary>
    public void Simulate(string folder, string name)
    {
        Config.Folder = folder;
        Config.DeviceName = name;
        Config.Enabled = true;
        Config.Save(_configPath);
        Sync();
    }
}
