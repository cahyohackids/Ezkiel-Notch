using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        m.Settings.WeatherCity = "Jakarta";
        w.Weather.Simulate(new WeatherClient.Now(29.4, 2, 32, 24, 40, true, DateTime.UtcNow));
        m.Settings.Rally = true;
        m.Settings.RallyCalendarUrl = "https://calendar.example/basic.ics";
        w.Rally.Simulate([
            new Meeting("1", "Design review", DateTime.UtcNow.AddMinutes(20), DateTime.UtcNow.AddMinutes(50), "https://meet.google.com/abc-defg-hij"),
            new Meeting("2", "1:1 with Rina", DateTime.UtcNow.AddHours(2), DateTime.UtcNow.AddHours(2.5), null),
        ]);
        Lang.Choice = AppLanguage.En;
        m.Settings.Language = AppLanguage.En;
        m.SettingsChanged(everything: true);

        List<(string Name, Action Act)> steps =
        [
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
                Check(w.Presentation == Presentation.Lip, "starts closed");
                var shape = w.Island.ShapeRect;
                Check(shape.Height <= 24 && shape.Width < 140, $"closed, it's a small tab for Ezkiel's face ({shape.Width:0}×{shape.Height:0})");
                Check(w.LiveCenter.Content is EzkielEyes, "Ezkiel's eyes are in the closed island");
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
                // A time typed into the task becomes its reminder.
                pane = (HuntPane)w.CurrentPane!;
                pane.Input!.Text = "ingatkan kul 3";
                Check(pane.Typed is { Title: "ingatkan" }, "\"kul 3\" is read as a time while typing");
                pane.Submit();
                var task = m.Data.Tasks[0];
                var local = task.Due?.ToLocalTime();
                Check(task.Title == "ingatkan" && local is { Hour: 3 or 15, Minute: 0 }, $"and becomes the reminder: \"{task.Title}\" at {local:HH:mm}");
                m.DeleteTask(task.Id);
            }),
            ("mirror", () =>
            {
                w.SetMirror(true);
                Check(w.MirrorView.IsVisible, "Mirror opens over the page");
            }),
            ("mirror closed", () =>
            {
                w.SetMirror(false);
                Check(!w.MirrorView.IsVisible, "and closes");
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
            ("stash", () =>
            {
                // Real files (in the self-test's own folder), so thumbnails, sizes and "Missing" are real too.
                var dir = Path.Combine(Path.GetDirectoryName(m.Store!.Path)!, "stash");
                Directory.CreateDirectory(dir);
                var shot = Path.Combine(dir, "Screenshot 2026-10-26 091500.png");
                using (var f = File.Create(shot))
                {
                    var png = new PngBitmapEncoder();
                    png.Frames.Add(BitmapFrame.Create(DemoArtwork()));
                    png.Save(f);
                }
                File.WriteAllText(Path.Combine(dir, "Invoice-October.pdf"), new string('x', 248_000));
                File.WriteAllText(Path.Combine(dir, "notes.txt"), "hello");
                Directory.CreateDirectory(Path.Combine(dir, "Brand assets"));
                int added = m.AddToShelf([Path.Combine(dir, "notes.txt"), Path.Combine(dir, "Brand assets"), Path.Combine(dir, "Invoice-October.pdf"), shot, Path.Combine(dir, "gone.zip")]);
                Check(added == 5, "files and folders go on the shelf");
                Check(m.AddToShelf([shot]) == 0, "the same file isn't stashed twice");
                w.Select(Room.Stash);
            }),
            ("stash ok", () => RoomChecks(w, Room.Stash)),
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
            ("purr", () =>
            {
                w.Media.Simulate(new Platform.NowPlaying("Spotify.exe", "Golden Hour", "JVKE", "this is what ____ feels like", 209, 63, DateTime.UtcNow, true), DemoArtwork());
                w.Select(Room.Purr);
            }),
            ("purr ok", () =>
            {
                RoomChecks(w, Room.Purr);
                Check(w.Island.BodyRect.Height < 220, "Purr without Chorus is the short room");
            }),
            ("chorus", () =>
            {
                m.Settings.ShowLyrics = true;
                var np = w.Media.Current!;
                w.Lyrics.Preview(Lrc.Parse("[00:52.0]It was just two lovers\n[00:56.5]Sittin' in the car, listening to Blonde\n[01:01.0]Fallin' for each other\n[01:05.5]In pink and blue\n[01:10.0]Golden hour"), np.Song);
                w.CurrentPane!.Refresh();
                w.Select(Room.Purr);
            }),
            ("chorus ok", () =>
            {
                RoomChecks(w, Room.Purr);
                Check(w.Island.BodyRect.Height > 240, "Chorus makes Purr taller, like the Mac");
            }),
            ("roar", () =>
            {
                m.Settings.ShowLyrics = false;
                w.Select(Room.Roar);
            }),
            ("roar ok", () =>
            {
                var t = DateTime.UtcNow;
                m.UpdateBadges([
                    new Badge("5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App", "WhatsApp", 3, "3", t.AddMinutes(-2)),
                    new Badge("MSTeams_8wekyb3d8bbwe!MSTeams", "Microsoft Teams", 12, "12", t.AddMinutes(-40)),
                    new Badge("Microsoft.OutlookForWindows_8wekyb3d8bbwe!Microsoft.OutlookforWindows", "Outlook", 1, "1", t.AddHours(-3)),
                ]);
                RoomChecks(w, Room.Roar);
                Check(m.UnreadTotal == 16, "Roar adds up the unread counts");
            }),
            ("scout", () =>
            {
                m.ScoutReceived(ScoutEvent.New("claude", ScoutKind.Done, "Claude finished", "Your turn", @"C:\Users\ezkiel\ezkiel-notch", "a"));
                m.ScoutReceived(ScoutEvent.New("claude", ScoutKind.NeedsYou, "Claude needs you", "Claude needs your permission to use Bash", @"C:\Users\ezkiel\store", "b"));
                m.ScoutReceived(ScoutEvent.New("codex", ScoutKind.Done, "Codex finished", "All tests pass", @"C:\Users\ezkiel\api", "c"));
                m.ShowingScout = true;
                w.CurrentPane!.Refresh();
            }),
            ("scout ok", () =>
            {
                RoomChecks(w, Room.Roar);
                Check(m.LiveAgents.Count == 1 && m.Data.ScoutLog.Count == 3, "Scout keeps the waiting agent live and logs the rest");
                m.ShowingScout = false;
            }),
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
            ("live pill", () =>
            {
                var r = w.Island.BodyRect;
                Check(w.IsLive && r.Width > 200 && r.Height >= 26, $"a running timer keeps the closed island live, like the Mac's wings ({r.Width:0}×{r.Height:0})");
                Check(FullyInside(w, w.LiveRight, minSize: 4), "the countdown fits in the pill");
            }),
            ("peek", () =>
            {
                w.ShowPeek("Focus complete", "3-day streak · take 5 min", room: Room.Den);
                Check(w.Presentation == Presentation.Peek, "a peek shows");
            }),
            ("peek text", () => Check(FullyInside(w, w.PeekTitle), "the peek's text is laid out in full, inside the island")),
            ("pounce peek", () =>
            {
                var shot = m.Data.Shelf.First(r => r.Path.EndsWith(".png")).Path;
                w.ShowPeek("Screenshot", Path.GetFileName(shot), room: Room.Stash, file: shot);
                Check(w.PeekActions.Children.Count == 3, "a Pounce peek offers Copy · Stash · Show");
            }),
            ("pounce peek fits", () => Check(FullyInside(w, w.PeekActions), "the Pounce buttons fit inside the peek")),
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
                // Opening with the shortcut means you're about to type: the island has the keyboard.
                Check(w.IsActive, "opening with the shortcut takes the keyboard (focus.log says how)");
                w.PointerMoved(false);
                Check(w.Presentation == Presentation.Expanded, "and a pointer somewhere else doesn't close it");
                if (w.Selected == Room.Hunt)
                    Check(Keyboard.FocusedElement == ((HuntPane)w.CurrentPane!).Input, "and the caret is in New task");
                w.Toggle();
                Check(w.Presentation == Presentation.Lip, "and closes");
            }),
            ("island", () =>
            {
                // Nothing live, so the closed island is its resting size.
                m.FocusReset();
                m.UpdateBadges([]);
                foreach (var agent in m.LiveAgents) m.DismissAgent(agent);
                if (w.Media.Current?.IsPlaying == true) w.Media.PlayPause();
                m.Settings.Style = "island";
                m.SettingsChanged();
            }),
            ("island closed", () =>
            {
                var r = w.Island.ShapeRect;
                Check(w.Island.Floating && r.Top >= 5 && Math.Abs(r.Height - 34) < 1 && Math.Abs(r.Width - 96) < 1,
                      $"Island style floats below the top edge as a 96×34 pill ({r.Left:0},{r.Top:0} {r.Width:0}×{r.Height:0})");
            }),
            .. new[] { "anime", "cat", "cute", "robot", "lion" }.Select(style => ($"eyes {style}", (Action)(() =>
            {
                m.Settings.EyeStyle = style;
                m.SettingsChanged();
                Check(w.LiveCenter.Content is EzkielEyes { Style: var shown } && shown == style, $"the {style} eyes are in the closed island");
            }))),
            ("settings draft", () =>
            {
                w.Select(Room.Settings);
                w.Expand();
            }),
            ("settings draft ok", () =>
            {
                // A refresh (a search result, an update check) never eats what you typed.
                var pane = (SettingsPane)w.CurrentPane!;
                pane.CityField!.Text = "Kuala Lum";
                pane.Refresh();
                Check(pane.CityField!.Text == "Kuala Lum", "the City field keeps what you typed through a refresh");
                pane.CityField!.Text = "";
                w.Collapse();
            }),
            ("island open", () => w.Expand()),
            ("island open ok", () =>
            {
                RoomChecks(w, w.Selected);
                Check(w.Island.ShapeRect.Top >= 5, "the open island floats too");
                m.Settings.IslandSize = "large";
                m.SettingsChanged();
            }),
            ("island large", () => Check(Math.Abs(w.Island.BodyRect.Width - 480) < 1, $"a large island opens 480 wide ({w.Island.BodyRect.Width:0})")),
            ("island back", () =>
            {
                m.Settings.Style = "notch";
                m.Settings.IslandSize = "medium";
                m.SettingsChanged();
                w.Collapse();
            }),
        ];

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

    /// <summary>A warm gradient square standing in for album art.</summary>
    private static System.Windows.Media.Imaging.BitmapSource DemoArtwork()
    {
        var v = new System.Windows.Media.DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0xFF, 0xB3, 0x47), Color.FromRgb(0xD9, 0x3A, 0x5B), 45), null, new Rect(0, 0, 192, 192));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xF1, 0xC4)), null, new Point(130, 70), 38, 38);
        }
        var bmp = new RenderTargetBitmap(192, 192, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(v);
        bmp.Freeze();
        return bmp;
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
        double want = w.ExpandedHeight(room);
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
