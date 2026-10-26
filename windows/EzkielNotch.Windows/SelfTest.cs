using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EzkielNotch.Core;
using EzkielNotch.Island;
using EzkielNotch.Rooms;

namespace EzkielNotch;

/// <summary>
/// `EzkielNotch.exe --self-test [--snapshots dir]` walks the island through every state and room (in English
/// and Indonesian), checks shape, placement, hover, click-through, layout and saving, saves a PNG per step,
/// and exits 0 when everything passed. It works on its own data file (never the real one), and the real
/// pointer is ignored so a cursor resting near the top can't change the outcome.
/// </summary>
internal static class SelfTest
{
    private static readonly List<string> Failures = [];

    public static void Run(IslandWindow w, string[] args)
    {
        int i = Array.IndexOf(args, "--snapshots");
        string? dir = i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        w.IgnorePointer = true;
        var m = w.Model;
        Seed(m);
        Lang.Choice = AppLanguage.En;
        m.Settings.Language = AppLanguage.En;
        m.SettingsChanged(everything: true);

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
            ("hover opens", () => w.PointerMoved(true)),   // still resting after the delay
            ("den", () =>
            {
                Check(w.Presentation == Presentation.Expanded, "a resting pointer opens the island");
                Check(w.OnIsland(new Point(w.Island.ShapeRect.Left + 100, 120)), "the open island takes clicks");
                RoomChecks(w, Room.Den);
            }),
            ("den running", () =>
            {
                m.FocusStartPause();
                Check(m.Focus.IsRunning, "Start runs the Den timer");
            }),
            ("hunt", () => w.Select(Room.Hunt)),
            ("hunt ok", () =>
            {
                RoomChecks(w, Room.Hunt);
                var pane = (HuntPane)w.CurrentPane!;
                int before = m.Data.Tasks.Count;
                pane.Input!.Text = "Book the venue";
                pane.Submit();
                Check(m.Data.Tasks.Count == before + 1 && m.Data.Tasks[0].Title == "Book the venue", "typing a task and pressing Return adds it on top");
                Check(((HuntPane)w.CurrentPane!).Input!.Text.Length == 0, "the field clears after adding");
            }),
            ("hunt undo toast", () =>
            {
                m.DeleteTask(m.Data.Tasks[0].Id);
                Check(w.LastToast == "Task deleted", "deleting a task offers Undo");
            }),
            ("scratch", () => w.Select(Room.Scratch)),
            ("scratch ok", () =>
            {
                RoomChecks(w, Room.Scratch);
                var pane = (ScratchPane)w.CurrentPane!;
                Check(pane.BodyEditor is not null, "the selected note is open in the editor");
                if (pane.BodyEditor is { } editor)
                {
                    editor.Text = "Agenda\n- budget\n- venue";
                    pane.Flush();
                    Check(m.Data.Notes.Any(n => n.Body.StartsWith("Agenda")), "typing in a note saves it");
                }
            }),
            ("catch", () =>
            {
                m.ShowingCatch = true;
                w.CurrentPane!.Refresh();
            }),
            ("oasis", () => w.Select(Room.Oasis)),
            ("oasis ok", () =>
            {
                RoomChecks(w, Room.Oasis);
                int before = m.WaterToday;
                m.AddWater(350);
                Check(m.WaterToday == before + 350, "+350 adds a drink");
            }),
            ("settings", () => w.Select(Room.Settings)),
            ("settings ok", () => RoomChecks(w, Room.Settings)),
            ("roar", () => w.Select(Room.Roar)),
            ("roar ok", () => RoomChecks(w, Room.Roar)),
            ("indonesian", () =>
            {
                m.Settings.Language = AppLanguage.Id;
                m.SettingsChanged(everything: true);
                w.Select(Room.Den);
            }),
            ("den id", () =>
            {
                Check(w.TabSubtitle.Text == "Timer fokus", $"Indonesian follows the Mac's words ({w.TabSubtitle.Text})");
                RoomChecks(w, Room.Den);
            }),
            ("hunt id", () => w.Select(Room.Hunt)),
            ("hunt id ok", () => RoomChecks(w, Room.Hunt)),
            ("scratch id", () =>
            {
                m.ShowingCatch = false;
                w.Select(Room.Scratch);
                w.CurrentPane!.Refresh();
            }),
            ("scratch id ok", () => RoomChecks(w, Room.Scratch)),
            ("oasis id", () => w.Select(Room.Oasis)),
            ("oasis id ok", () => RoomChecks(w, Room.Oasis)),
            ("settings id", () => w.Select(Room.Settings)),
            ("settings id ok", () => RoomChecks(w, Room.Settings)),
            ("saved", () =>
            {
                m.SaveNow();
                var store = m.Store!;
                var back = new DataStore(store.Path).Load();
                Check(back.Tasks.Count == m.Data.Tasks.Count && back.Notes.Count == m.Data.Notes.Count && back.Water.Count == m.Data.Water.Count,
                      "everything is saved to data.json and reads back");
                Check(back.Settings.Language == AppLanguage.Id, "the language choice is saved");
                Check(back.FocusState?.End is not null, "a running Den session is saved with its end time");
                m.Settings.Language = AppLanguage.En;
                m.SettingsChanged(everything: true);
                w.Select(Room.Den);
            }),
            ("leave", () => w.PointerMoved(false)),
            ("left", () => w.PointerMoved(false)),
            ("closed again", () => Check(w.Presentation == Presentation.Lip, "leaving closes it")),
            ("peek", () =>
            {
                w.ShowPeek("Focus complete", "3-day streak · take 5 min", room: Room.Den);
                Check(w.Presentation == Presentation.Peek, "a peek shows");
            }),
            ("peek text", () => Check(FullyInside(w, w.PeekTitle), "the peek's text is laid out in full, inside the island")),
            ("fullscreen", () =>
            {
                w.Collapse();
                w.SimulateFullscreen(true);
                w.ShowPeek("Song", "not urgent");
                Check(w.Presentation == Presentation.Lip, "fullscreen holds back ordinary peeks");
                w.ShowPeek("Task due", "Send the report", urgent: true);
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
                try { Directory.Delete(Path.GetDirectoryName(m.Store!.Path)!, recursive: true); } catch (Exception) { }
                Application.Current.Shutdown(Failures.Count == 0 ? 0 : 1);
                return;
            }
            var (name, act) = steps[n];
            Console.WriteLine($"> {name}");
            try { act(); }
            catch (Exception e) { Check(false, $"{name} threw {e.GetType().Name}: {e.Message}"); }
            After(dir is null ? 0.6 : 0.9, () =>
            {
                if (dir is not null) w.Snapshot(Path.Combine(dir, $"{n:00}-{name.Replace(' ', '-')}.png"));
                After(interval - 0.6 + 0.1, () => Step(n + 1));
            });
        }
        After(1.0, () => Step(0));
    }

    /// <summary>Realistic data for the snapshots: a week of focus and water, tasks, notes and clips.</summary>
    private static void Seed(NotchModel m)
    {
        var d = m.Data;
        var now = DateTime.UtcNow;
        var today = DateTime.Now.Date;
        d.Tasks.Clear();
        d.Tasks.Add(TaskItem.New("Send the invoice to Rina", now.AddHours(2)));
        d.Tasks.Add(TaskItem.New("Call the printer", now.AddMinutes(-20)) with { Reminded = true });
        d.Tasks.Add(TaskItem.New("Reply to the landlord"));
        d.Tasks.Add(TaskItem.New("Water the plants") with { Done = true });
        d.Notes.Clear();
        d.Notes.Add(NoteItem.New() with { Title = "Launch checklist", Body = "Screenshots\nRelease notes\nTell the store", Updated = now.AddMinutes(-12) });
        d.Notes.Add(NoteItem.New() with { Body = "Ideas for the weekend\nhike, ramen", Updated = now.AddDays(-1) });
        d.Clips.Clear();
        d.Clips.Add(ClipItem.New("https://ezkielyna.store/notch") with { Captured = now.AddMinutes(-3) });
        d.Clips.Add(ClipItem.New("Meeting moved to Thursday 15:00, same room.") with { Captured = now.AddHours(-2) });
        d.Water.Clear();
        int[] ml = [1800, 2200, 1500, 2400, 2000, 900];
        for (int k = 0; k < ml.Length; k++) d.Water.Add(WaterEntry.New(ml[k], today.AddDays(-(ml.Length - k)).AddHours(12).ToUniversalTime()));
        d.Water.Add(WaterEntry.New(500, today.AddHours(9).ToUniversalTime()));
        d.Water.Add(WaterEntry.New(700, today.AddHours(11).ToUniversalTime()));
        d.FocusLog.Clear();
        int[] focus = [50, 25, 0, 75, 45, 100];
        for (int k = 0; k < focus.Length; k++)
            if (focus[k] > 0) d.FocusLog.Add(FocusEntry.New(today.AddDays(-(focus.Length - k)).AddHours(10).ToUniversalTime(), focus[k]));
        d.FocusLog.Add(FocusEntry.New(today.AddHours(8).ToUniversalTime(), 25));
        m.SelectedNoteId = d.Notes[0].Id;
    }

    /// <summary>The room is laid out at its own height, inside the island, with nothing spilling out.</summary>
    private static void RoomChecks(IslandWindow w, Room room)
    {
        var body = w.Island.BodyRect;
        double want = IslandWindow.ExpandedHeight(room);
        Check(Math.Abs(body.Width - 440) < 1 && Math.Abs(body.Height - want) < 1, $"{room}: the island is 440×{want:0} ({body.Width:0}×{body.Height:0})");
        var pane = w.CurrentPane;
        Check(pane is not null && pane.Info.Room == room, $"{room}: its page is shown");
        if (pane is null) return;
        pane.UpdateLayout();
        Check(FullyInside(w, pane), $"{room}: the page lies inside the island");
        foreach (var t in Descendants<TextBlock>(pane).Where(t => t.IsVisible && t.ActualWidth > 0 && t.Text.Length > 0))
        {
            if (!FullyInside(w, t, minSize: 1)) Check(false, $"{room}: \"{t.Text}\" spills outside the island");
            // Truncated text is allowed (long titles) but listed, so short labels never get cut by accident.
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (t.TextTrimming != TextTrimming.None && t.TextWrapping == TextWrapping.NoWrap && t.DesiredSize.Width > t.ActualWidth + 0.5)
                Console.WriteLine($"  · {room}: \"{t.Text}\" is shortened ({t.DesiredSize.Width:0} > {t.ActualWidth:0})");
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }

    /// <summary>The element has a real size and its whole box lies inside the island's body.
    /// Children of a scroll area only need to be inside it horizontally.</summary>
    private static bool FullyInside(IslandWindow w, FrameworkElement e, double minSize = 8)
    {
        if (e.ActualHeight < minSize || e.ActualWidth < minSize) return false;
        var topLeft = e.TranslatePoint(new Point(0, 0), w.Island);
        var bottomRight = e.TranslatePoint(new Point(e.ActualWidth, e.ActualHeight), w.Island);
        var body = w.Island.BodyRect;
        body.Inflate(1, 1);
        bool scrolled = Ancestor<ScrollViewer>(e) is not null;
        if (scrolled) return topLeft.X >= body.Left && bottomRight.X <= body.Right;
        return body.Contains(topLeft) && body.Contains(bottomRight);
    }

    private static T? Ancestor<T>(DependencyObject e) where T : DependencyObject
    {
        for (var p = VisualTreeHelper.GetParent(e); p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is T t) return t;
        return null;
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
