using System.IO;
using System.Windows;
using System.Windows.Threading;
using EzkielNotch.Island;

namespace EzkielNotch;

/// <summary>
/// `EzkielNotch.exe --self-test [--snapshots dir]` walks the island through every state, checks shape,
/// placement, hover and click-through, saves a PNG per step, and exits 0 when everything passed.
/// The real pointer is ignored so a cursor resting near the top can't change the outcome.
/// </summary>
internal static class SelfTest
{
    private static readonly List<string> Failures = [];

    public static void Run(IslandWindow w, string[] args)
    {
        int i = Array.IndexOf(args, "--snapshots");
        string? dir = i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        w.IgnorePointer = true;

        var steps = new List<(string Name, Action Act)>
        {
            ("placed", () =>
            {
                var r = w.WindowRectPixels();
                Check(!r.IsEmpty, "window exists");
                Check(Math.Abs(r.Top - w.Screen.Bounds.Top) < 1, $"window is flush with the top of the monitor ({r.Top} vs {w.Screen.Bounds.Top})");
                double centre = r.Left + r.Width / 2, screenCentre = w.Screen.Bounds.Left + w.Screen.Bounds.Width / 2.0;
                Check(Math.Abs(centre - screenCentre) <= 1, "window is centred horizontally");
                Check(w.Topmost, "window stays on top");
                Check(w.HotKeyRegistered, "Ctrl+Alt+N registered");
            }),
            ("lip", () =>
            {
                Check(w.Presentation == Presentation.Lip, "starts as the lip");
                var shape = w.Island.ShapeRect;
                Check(shape.Height <= 8 && shape.Width < 220, $"lip is thin and small ({shape.Width:0}×{shape.Height:0})");
                Check(w.OnIsland(new Point(shape.Left + shape.Width / 2, 2)), "the lip takes the pointer");
                Check(!w.OnIsland(new Point(10, 200)), "everywhere else passes clicks through");
            }),
            ("hover", () => w.PointerMoved(true)),
            ("hover opens", () =>
            {
                w.PointerMoved(true);   // still resting after the delay
            }),
            ("expanded", () =>
            {
                Check(w.Presentation == Presentation.Expanded, "a resting pointer opens the island");
                var r = w.Island.BodyRect;
                Check(Math.Abs(r.Width - 440) < 1 && Math.Abs(r.Height - 252) < 1, $"open island is 440×252 ({r.Width:0}×{r.Height:0})");
                Check(w.OnIsland(new Point(w.Island.ShapeRect.Left + 100, 120)), "the open island takes clicks");
                Check(FullyInside(w, w.PageTitle) && FullyInside(w, w.PageBody), "the page's text is laid out in full, inside the island");
            }),
            ("tab Roar", () => w.Select(2)),
            ("leave", () => w.PointerMoved(false)),
            ("left", () =>
            {
                w.PointerMoved(false);
            }),
            ("closed again", () => Check(w.Presentation == Presentation.Lip, "leaving closes it")),
            ("peek", () =>
            {
                w.ShowPeek("Telegram", "2 new · 8 unread");
                Check(w.Presentation == Presentation.Peek, "a peek shows");
            }),
            ("peek text", () => Check(FullyInside(w, w.PeekTitle), "the peek's text is laid out in full, inside the island")),
            ("fullscreen", () =>
            {
                w.Collapse();
                w.SimulateFullscreen(true);
                w.ShowPeek("Song", "not urgent");
                Check(w.Presentation == Presentation.Lip, "fullscreen holds back ordinary peeks");
                w.ShowPeek("Battery low", "Plug in soon", urgent: true);
                Check(w.Presentation == Presentation.Peek, "urgent peeks still show");
            }),
            ("fullscreen lip hidden", () =>
            {
                w.Collapse();
                Check(w.Root.Opacity < 0.5 || !w.Root.IsHitTestVisible, "the lip steps away over fullscreen apps");
                w.SimulateFullscreen(false);
            }),
            ("hotkey toggle", () =>
            {
                w.Toggle();
                Check(w.Presentation == Presentation.Expanded, "Ctrl+Alt+N opens");
            }),
            ("hotkey toggle back", () =>
            {
                w.Toggle();
                Check(w.Presentation == Presentation.Lip, "and closes");
            }),
        };

        double interval = dir is null ? 0.6 : 1.0;
        void Step(int n)
        {
            if (n >= steps.Count)
            {
                Console.WriteLine(Failures.Count == 0 ? "SELF-TEST PASSED" : $"SELF-TEST FAILED: {Failures.Count} check(s)");
                File.WriteAllText(Path.Combine(dir ?? Path.GetTempPath(), "self-test-result.txt"),
                                  Failures.Count == 0 ? "PASSED\n" : "FAILED\n" + string.Join("\n", Failures) + "\n");
                Application.Current.Shutdown(Failures.Count == 0 ? 0 : 1);
                return;
            }
            var (name, act) = steps[n];
            Console.WriteLine($"> {name}");
            act();
            After(dir is null ? 0.6 : 0.9, () =>
            {
                if (dir is not null) w.Snapshot(Path.Combine(dir, $"{n:00}-{name.Replace(' ', '-')}.png"));
                After(interval - 0.6 + 0.1, () => Step(n + 1));
            });
        }
        After(1.0, () => Step(0));
    }

    /// <summary>The element has a real size and its whole box lies inside the island's body.</summary>
    private static bool FullyInside(IslandWindow w, FrameworkElement e)
    {
        if (e.ActualHeight < 8 || e.ActualWidth < 8) return false;
        var topLeft = e.TranslatePoint(new Point(0, 0), w.Island);
        var bottomRight = e.TranslatePoint(new Point(e.ActualWidth, e.ActualHeight), w.Island);
        var body = w.Island.BodyRect;
        body.Inflate(1, 1);
        return body.Contains(topLeft) && body.Contains(bottomRight);
    }

    private static void After(double seconds, Action action)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(0.05, seconds)) };
        t.Tick += (_, _) => { t.Stop(); action(); };
        t.Start();
    }

    private static void Check(bool ok, string message)
    {
        Console.WriteLine($"  {(ok ? "✓" : "✗")} {message}");
        if (!ok) Failures.Add(message);
    }
}
