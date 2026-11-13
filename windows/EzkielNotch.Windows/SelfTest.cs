using System.IO;
using System.Text.Json.Nodes;
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

        var syncFolder = Path.Combine(Path.GetTempPath(), $"ezkiel-sync-selftest-{Environment.ProcessId}");
        JsonObject? syncBefore = null;
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
            ("closed chorus", () =>
            {
                // Made-up synced lines around where the simulated song is now (no LRCLIB in the test).
                m.Settings.ShowLyrics = true;
                m.Settings.ClosedLyrics = true;
                var np = w.Media.Current!;
                double at = np.Position(DateTime.UtcNow);
                w.Lyrics.Preview([new LyricLine(Math.Max(0, at - 5), "It was just two lovers"), new LyricLine(at + 60, "Golden hour")], np.Song);
                m.SettingsChanged();
            }),
            ("closed chorus ok", () =>
            {
                var r = w.Island.BodyRect;
                Check(w.ShowsClosedLyrics, "Chorus sings under the closed island while a song plays");
                Check(Math.Abs(r.Width - ClosedChorus.Width) < 1 && r.Height >= 28 + ClosedChorus.Band - 1,
                      $"the closed island grows a lyric band, like the Mac ({r.Width:0}×{r.Height:0})");
                Check(w.LiveLyric.Text == "It was just two lovers", $"it shows the line being sung ({w.LiveLyric.Text})");
                Check(FullyInside(w, w.LiveLyric), "the lyric line lies inside the island");
                Check(FullyInside(w, w.LiveRight, minSize: 4), "the countdown still fits above it");
            }),
            ("closed chorus off", () =>
            {
                m.Settings.ClosedLyrics = false;
                m.SettingsChanged();
            }),
            ("closed chorus off ok", () =>
            {
                var r = w.Island.BodyRect;
                Check(!w.ShowsClosedLyrics && r.Height < 40 && w.LiveLyric.Visibility != Visibility.Visible,
                      $"\"Chorus under the notch\" off: back to the one-row pill ({r.Width:0}×{r.Height:0})");
                m.Settings.MusicSource = MusicSource.AppleMusic;
                m.SettingsChanged();
                Check(w.Media.Source == MusicSource.AppleMusic, "the Purr source reaches the media watcher");
                m.Settings.MusicSource = MusicSource.Auto;
                m.Settings.ShowLyrics = false;
                m.Settings.ClosedLyrics = true;
                m.SettingsChanged();
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
            ("tinted peek", () =>
            {
                // The Mac's rich card: a tinted icon on the left, a big value on the right.
                w.ShowPeek(new PeekInfo("WhatsApp", "3 new · 5 unread") { Room = Room.Roar, Look = PeekLooks.Of(PeekKind.Roar), Value = "5" });
                Check(w.PeekLeading.Child is Border { Child: TextBlock }, "a peek with a look shows its glyph on the left");
                Check(w.PeekTrailing.Child is TextBlock { Text: "5", Foreground: SolidColorBrush { Color: var valueColor } } && valueColor == Accent.Roar.Deep,
                      "and its value on the right, in Roar's colour");
            }),
            ("tinted peek fits", () => Check(FullyInside(w, w.PeekLeading) && FullyInside(w, w.PeekTrailing, minSize: 6), "the icon and value fit inside the peek")),
            ("charge ring peek", () =>
            {
                w.ShowPeek(new PeekInfo("Charging", "Charger connected") { Look = PeekLooks.Of(PeekKind.Charging), Progress = 0.8, Charging = true });
                Check(w.PeekTrailing.Child is ChargeRing { Level: 0.8 } ring && ring.Spinning == !SpringEase.ReduceMotion,
                      "plugging in shows the battery as a ring (its light runs round unless Reduce Motion is on)");
            }),
            ("charge ring fits", () => Check(FullyInside(w, w.PeekTrailing), "the ring fits inside the peek")),
            ("track peek", () =>
            {
                // A new song: the album art on the left, an equalizer on the right (Purr's fake track and demo art).
                var np = w.Media.Current!;
                w.ShowPeek(new PeekInfo(np.Title, np.Artist) { Room = Room.Purr, Look = PeekLooks.Track(np.AppId), Artwork = true });
                Check(w.PeekLeading.Child is Border { Background: ImageBrush }, "a new song shows its album art in the peek");
                Check(w.PeekTrailing.Child is EqualizerBars, "and an equalizer on the right");
            }),
            ("track peek fits", () =>
            {
                Check(FullyInside(w, w.PeekLeading) && FullyInside(w, w.PeekTrailing, minSize: 6), "the art and equalizer fit inside the peek");
                w.Collapse();
            }),
            ("track peek released", () => Check(w.PeekTrailing.Child is null, "a closed peek lets go of its equalizer (nothing animates unseen)")),
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
            ("pin", () =>
            {
                w.SetPinned(true);
                Check(w.Presentation == Presentation.Expanded && w.Pinned, "Keep open opens the island, pinned");
                w.PointerMoved(false);
            }),
            ("pin holds", () =>
            {
                w.PointerMoved(false);
                Check(w.Presentation == Presentation.Expanded, "pinned, it stays open after the pointer moved away");
                var menu = w.BuildMenu().Items.OfType<MenuItem>().ToList();
                Check(menu.Any(i => i.Header as string == "Keep Open" && i.IsChecked), "the right-click menu shows Keep Open checked");
                Check(menu.Any(i => i.Header as string == "Open on Hover" && i.IsChecked == m.Settings.ExpandOnHover), "and Open on Hover as set");
                Check(menu.Count(i => i.Header as string is "Notch" or "Island" && i.IsChecked) == 1, "and one of Notch or Island checked");
                var tray = Platform.TrayIcon.Entries(w, "Ctrl+Alt+N", () => { }).Where(e => e is not null).Select(e => e!.Value).ToList();
                Check(tray.Any(e => e.Text == "Keep Open" && e.Checked), "the tray menu shows Keep Open checked");
                Check(tray.Count(e => e.Text.Contains(" · ")) == RoomInfo.All.Length - 1, "the tray menu has an entry per room");
                Check(tray.Any(e => e.Text == "Ezkiel Notch website") && tray.Any(e => e.Text == "Mirror"), "and the website and Mirror");
                Check(!tray.Any(e => e.Text.StartsWith("Update to")), "and no update entry without an update");
                w.SetPinned(false);
            }),
            ("unpin closes", () =>
            {
                w.PointerMoved(false);
                Check(w.Presentation == Presentation.Lip && !w.Pinned, "unpinned, it closes once the pointer is away");
                w.SetPinned(true);
            }),
            ("pin esc", () =>
            {
                Keyboard.ClearFocus();
                Press(w, Key.Escape);
                Check(w.Presentation == Presentation.Lip && !w.Pinned, "Esc still closes a pinned island, and unpins it");
            }),
            ("arrows", () =>
            {
                var before = w.Selected;
                w.Expand();
                w.Select(Room.Den);
                Keyboard.ClearFocus();
                Press(w, Key.Right);
                Check(w.Selected == Room.Hunt, "→ goes to the next room");
                Press(w, Key.Left);
                Press(w, Key.Left);
                Check(w.Selected == Room.Settings, "← goes back, and wraps around to Settings");
                w.Select(before);
                w.Collapse();
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
            ("island glass", () =>
            {
                m.Settings.IslandGlass = true;
                m.Settings.EyeStyle = "anime";
                m.SettingsChanged();
                Check(w.Island.Glass, "Liquid Glass makes the island see-through");
            }),
            ("island glass off", () =>
            {
                m.Settings.IslandGlass = false;
                m.SettingsChanged();
                Check(!w.Island.Glass, "and off again");
            }),
            .. new[] { "anime", "cat", "cute", "robot", "lion" }.Select(style => ($"eyes {style}", (Action)(() =>
            {
                m.Settings.EyeStyle = style;
                m.SettingsChanged();
                Check(w.LiveCenter.Content is EzkielEyes { EyeStyle: var shown } && shown == style, $"the {style} eyes are in the closed island");
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
            ("screen share", () =>
            {
                m.Settings.HideFromScreenShare = true;
                m.SettingsChanged();
                Check(w.DisplayAffinity is Native.Win32.WDA_EXCLUDEFROMCAPTURE or Native.Win32.WDA_MONITOR,
                      $"Hide from screen sharing keeps the island out of captures (affinity 0x{w.DisplayAffinity:X})");
            }),
            ("screen share off", () =>
            {
                m.Settings.HideFromScreenShare = false;
                m.SettingsChanged();
                Check(w.DisplayAffinity == Native.Win32.WDA_NONE, $"and off again (affinity 0x{w.DisplayAffinity:X})");
                // Nothing live: with Minimal closed island off the wings show anyway.
                m.Settings.MinimalClosed = false;
                m.SettingsChanged();
            }),
            ("wings always", () =>
            {
                var r = w.Island.BodyRect;
                Check(!w.IsLive && w.ShowsWings && r.Width > 200, $"Minimal closed island off: the wings show with nothing live ({r.Width:0}×{r.Height:0})");
                Check(w.LiveRight.Children.OfType<ProgressRing>().Any(), "and the right wing has the water ring, like the Mac");
                m.Settings.MinimalClosed = true;
                m.SettingsChanged();
            }),
            ("wings minimal", () =>
            {
                var r = w.Island.BodyRect;
                Check(!w.ShowsWings && r.Width < 140, $"and on again: back to the small tab ({r.Width:0}×{r.Height:0})");
            }),
            // 1.9: Sync. Two fake devices in a temp folder; the self-test's own data file, never the real one.
            ("sync two devices", () =>
            {
                var sync = Platform.SyncService.Current!;
                syncBefore = m.Data.SyncedJson();
                if (Directory.Exists(syncFolder)) Directory.Delete(syncFolder, true);
                var folder = SyncRunner.Directory(syncFolder);
                Directory.CreateDirectory(folder);
                var now = SyncTime.String(SyncTime.Ms(DateTime.UtcNow));
                JsonObject File_(string id, string name, string platform, JsonArray tasks, JsonArray notes, JsonArray water) => new()
                {
                    ["format"] = 1, ["deviceId"] = id, ["deviceName"] = name, ["platform"] = platform, ["appVersion"] = "1.9.0",
                    ["written"] = now, ["tasks"] = tasks, ["notes"] = notes, ["water"] = water, ["focusLog"] = new JsonArray(),
                    ["tombstones"] = new JsonObject(),
                };
                var mac = File_("5E1F7E57-0000-4000-8000-0000000000B1", "Studio Mac", "mac",
                    [new JsonObject { ["id"] = "5E1F7E57-0000-4000-8000-000000000011", ["title"] = "Book the venue (from the Mac)", ["done"] = false,
                                      ["reminded"] = false, ["created"] = now, ["modified"] = now }],
                    [], [new JsonObject { ["id"] = "5E1F7E57-0000-4000-8000-000000000012", ["date"] = now, ["ml"] = 330, ["modified"] = now }]);
                var pc = File_("5E1F7E57-0000-4000-8000-0000000000B2", "Laptop", "windows", [],
                    [new JsonObject { ["id"] = "5E1F7E57-0000-4000-8000-000000000013", ["title"] = "Synced note", ["body"] = "Written on the laptop",
                                      ["updated"] = now, ["modified"] = now }], []);
                File.WriteAllText(Path.Combine(folder, "5E1F7E57-0000-4000-8000-0000000000B1.json"), mac.ToJsonString());
                File.WriteAllText(Path.Combine(folder, "5E1F7E57-0000-4000-8000-0000000000B2.json"), pc.ToJsonString());
                sync.Simulate(syncFolder, "Self-test PC");
            }),
            ("sync wait", () => { }),
            ("sync merged", () =>
            {
                var sync = Platform.SyncService.Current!;
                Check(m.Data.Tasks.Any(t => t.Title == "Book the venue (from the Mac)"), "sync brings in the Mac's task");
                Check(m.Data.Notes.Any(n => n.Title == "Synced note"), "sync brings in the laptop's note");
                Check(m.Data.Water.Count(x => x.Id == Guid.Parse("5E1F7E57-0000-4000-8000-000000000012")) == 1, "a drink is counted once");
                Check(sync.Config.Devices.Count == 2, $"both other devices are listed ({sync.Config.Devices.Count})");
                Check(sync.Config.LastError is null, $"no sync error ({sync.Config.LastError})");
                Check(File.Exists(Path.Combine(SyncRunner.Directory(syncFolder), sync.Config.DeviceId + ".json")), "this PC wrote its own file");
                Check(m.Data.Tasks.All(t => t.Modified is not null), "synced items carry a modified time");
                w.Select(Room.Settings);
                w.Expand();
            }),
            ("sync stop", () =>
            {
                var sync = Platform.SyncService.Current!;
                sync.Stop();
                Check(!sync.Config.Enabled, "Stop syncing turns it off");
                if (syncBefore is not null) m.ApplySynced(syncBefore);
                try { Directory.Delete(syncFolder, true); } catch (Exception) { }
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

    /// <summary>A key press, as the island's key handler sees it (modifiers come from the real keyboard: none).</summary>
    private static void Press(IslandWindow w, Key key)
    {
        if (PresentationSource.FromVisual(w) is not { } source) return;
        w.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
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
