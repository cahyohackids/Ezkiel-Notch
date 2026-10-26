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

        // One island per user session (the self-test runs beside a normal copy).
        if (!selfTest)
        {
            _single = new Mutex(true, @"Local\EzkielNotch.Island", out bool first);
            if (!first)
            {
                Shutdown();
                return;
            }
        }

        // `--data <file>` uses another data file (previews, screenshots). The self-test never touches the real one.
        int d = Array.IndexOf(e.Args, "--data");
        string path = d >= 0 && d + 1 < e.Args.Length ? Path.GetFullPath(e.Args[d + 1])
            : selfTest ? Path.Combine(Path.GetTempPath(), $"ezkiel-notch-selftest-{Environment.ProcessId}", "data.json")
            : DataStore.DefaultPath();
        if (selfTest || d >= 0) Platform.Startup.Disabled = true;

        var store = new DataStore(path);
        _model = new NotchModel(store.Load(), store);
        var window = new IslandWindow(_model);
        window.Show();

        if (selfTest)
        {
            SelfTest.Run(window, e.Args);
            return;
        }
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
                    window.ShowPeek("Ezkiel Notch", L("Hover the top of the screen · Ctrl+Alt+N"));
                    break;
            }
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow is IslandWindow { CurrentPane: Rooms.ScratchPane scratch }) scratch.Flush();
        _model?.SaveNow();
        _single?.Dispose();
        base.OnExit(e);
    }
}
