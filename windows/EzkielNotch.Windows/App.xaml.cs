using System.Windows;
using EzkielNotch.Island;

namespace EzkielNotch;

public partial class App : Application
{
    private Mutex? _single;

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

        var window = new IslandWindow();
        window.Show();

        if (selfTest)
        {
            SelfTest.Run(window, e.Args);
        }
        else
        {
            window.Dispatcher.BeginInvoke(() => window.ShowPeek("Ezkiel Notch", "Hover the top of the screen · Ctrl+Alt+N"),
                                          System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _single?.Dispose();
        base.OnExit(e);
    }
}
