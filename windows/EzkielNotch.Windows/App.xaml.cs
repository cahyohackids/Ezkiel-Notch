using System.IO;
using System.Windows;
using EzkielNotch.Core;
using EzkielNotch.Island;
using EzkielNotch.Platform;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch;

public partial class App : Application
{
    private Mutex? _single;
    private NotchModel? _model;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool selfTest = e.Args.Contains("--self-test");

        // Scout senders (a Claude Code hook, Codex, a script, an ezkielnotch:// link) pass their event to the
        // running app and quit without showing anything.
        string? pendingUrl = null;
        if (!selfTest && Platform.ScoutPipe.RunSender(e.Args, out pendingUrl))
        {
            Shutdown();
            return;
        }

        // One island per user session (the self-test runs beside a normal copy).
        if (!selfTest)
        {
            _single = new Mutex(true, @"Local\EzkielNotch.Island", out bool first);
            // Right after an update the old copy is still quitting: wait for it instead of giving up.
            if (!first && e.Args.Contains("--updated"))
            {
                try { first = _single.WaitOne(TimeSpan.FromSeconds(15)); }
                catch (AbandonedMutexException) { first = true; }
            }
            if (!first)
            {
                Shutdown();
                return;
            }
            Platform.Updater.CleanUp();
        }

        // `--data <file>` uses another data file (previews, screenshots). The self-test never touches the real one.
        int d = Array.IndexOf(e.Args, "--data");
        string path = d >= 0 && d + 1 < e.Args.Length ? Path.GetFullPath(e.Args[d + 1])
            : selfTest ? Path.Combine(Path.GetTempPath(), $"ezkiel-notch-selftest-{Environment.ProcessId}", "data.json")
            : DataStore.DefaultPath();
        if (selfTest || d >= 0) Platform.Startup.Disabled = true;

        var store = new DataStore(path);
        _model = new NotchModel(store.Load(), store);
        var window = new IslandWindow(_model) { SelfTesting = selfTest };
        window.Show();

        if (selfTest)
        {
            SelfTest.Run(window, e.Args);
            return;
        }
        Platform.ScoutPipe.Listen(window.Dispatcher, _model.ScoutReceived);
        window.Tray = new Platform.TrayIcon(window.Toggle, () =>
        {
            window.Select(Room.Settings);
            if (window.Presentation != Presentation.Expanded) window.Expand();
            window.TakeKeyboard();
        }, Shutdown);
        window.Tray.Relabel(IslandWindow.HotkeyLabel(_model.Settings.Hotkey));
        if (d < 0)
        {
            Platform.ScoutPipe.RegisterUrlScheme();
            // A hook that points at an older copy of the app gets fixed quietly.
            foreach (var agent in new[] { Platform.AgentLinks.Agent.ClaudeCode, Platform.AgentLinks.Agent.Codex })
                if (Platform.AgentLinks.State(agent) == LinkState.Stale) Platform.AgentLinks.Set(agent, true);
        }
        if (pendingUrl is not null && ScoutParser.Url(pendingUrl) is { } linked) _model.ScoutReceived(linked);
        window.Dispatcher.BeginInvoke(() =>
        {
            switch (store.LastRecovery)
            {
                case DataStore.RestoredBackup r:
                    window.ShowPeek(r.BackupDate is { } at ? L("Restored your data from %@", at.ToString("g", Lang.Culture)) : L("Restored your data"),
                                    L("The damaged file is kept in the data folder"), 8, urgent: true, room: Room.Settings);
                    break;
                case DataStore.StartedFresh:
                    window.ShowPeek(L("Couldn't read your data"), L("It's kept as data.broken-….json in the data folder"), 8,
                                    urgent: true, room: Room.Settings);
                    break;
                default:
                    window.ShowPeek("Ezkiel Notch", e.Args.Contains("--updated")
                        ? L("Updated to %@", Platform.Updater.Current)
                        : L("Hover the top of the screen · %@", IslandWindow.HotkeyLabel(_model.Settings.Hotkey)));
                    break;
            }
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow is IslandWindow { CurrentPane: Rooms.ScratchPane scratch }) scratch.Flush();
        _model?.SaveNow();
        (MainWindow as IslandWindow)?.Tray?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}
