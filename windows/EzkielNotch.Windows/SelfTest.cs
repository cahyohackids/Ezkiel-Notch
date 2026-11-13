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
        var askGateway = new FakeAskGateway();
        // Before any room is drawn: Settings must never read the user's real key.
        Platform.AskHub.Reset(new Platform.MemoryAskKeys(), new AskClient(askGateway));
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

        var fakeDownloads = Path.Combine(Path.GetTempPath(), "ezkiel-live-" + Guid.NewGuid().ToString("N")[..6]);
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
            // Each section on its own, to compare with the Mac's Settings (General · Whiskers · Roar · Features · About).
            .. SettingsPane.Sections.Select(section => ($"settings {SettingsPane.SectionTitle(section).ToLowerInvariant()}", (Action)(() =>
            {
                var pane = (SettingsPane)w.CurrentPane!;
                pane.ShowSection(section);
                RoomChecks(w, Room.Settings);
                Check(SettingsPane.Section == section, $"Settings shows {SettingsPane.SectionTitle(section)}");
                var texts = Descendants<TextBlock>(pane).Select(t => t.Text).ToList();
                foreach (var chip in SettingsPane.Sections)
                    Check(texts.Contains(SettingsPane.SectionTitle(chip)), $"the {SettingsPane.SectionTitle(chip)} chip is on top");
                string row = section switch
                {
                    SettingsSection.General => "Hide from screen sharing",
                    SettingsSection.Whiskers => "Wi-Fi & headphones",
                    SettingsSection.Roar => "Scout",
                    SettingsSection.Features => "Chorus under the notch",
                    _ => "Ezkiel Notch",
                };
                Check(texts.Contains(row), $"{SettingsPane.SectionTitle(section)} has \"{row}\", like the Mac");
            }))),
            ("settings back to general", () => ((SettingsPane)w.CurrentPane!).ShowSection(SettingsSection.General)),
            // Ask: a fake gateway (never the real one) and a key kept in memory (never the user's).
            ("ask no key", () =>
            {
                w.SetAsk(true);
                Check(w.AskOpen && w.AskBox is { Visibility: Visibility.Visible }, "the header button opens the ask box over the page");
                Check(w.AskBox?.ShownError?.Kind == AskErrorKind.NoKey && w.AskBox.ShowsSettingsButton, "without a key it says so, with a Settings button");
            }),
            ("ask streaming", () =>
            {
                Check(w.ExpandedHeight(Room.Settings) >= IslandWindow.AskPaneHeight + 86 && Math.Abs(w.Island.BodyRect.Height - w.ExpandedHeight(Room.Settings)) < 1,
                      $"the island is tall enough for it ({w.Island.BodyRect.Height:0})");
                Check(Platform.AskHub.SaveKey("  sk-selftest-1234 \n"), "a pasted key is saved (trimmed)");
                askGateway.Hold(3);
                w.AskBox!.Input.Text = "What does DPAPI do?";
                w.AskSend();
                Check(w.AskBox.Input.Text.Length == 0, "Return asks and clears the field");
            }),
            ("ask half way", () =>
            {
                var s = Platform.AskHub.Session;
                Check(s.Phase == AskSession.State.Streaming && s.Answer == "DPAPI keeps secrets **per user**", $"the answer streams in (\"{s.Answer}\")");
                Check(w.AskBox!.AnswerText == "DPAPI keeps secrets per user", $"and is drawn as it comes, bold without stars (\"{w.AskBox.AnswerText}\")");
                Check(askGateway.LastAuthorization == "Bearer sk-selftest-1234", "the key goes only in the Authorization header");
                askGateway.Release();
            }),
            ("ask answer", () =>
            {
                var s = Platform.AskHub.Session;
                Check(s.Phase == AskSession.State.Done && s.Answer.EndsWith("nothing leaves the PC"), "the whole answer arrives");
                Check(w.AskBox!.AnswerText.Contains("• encrypted with your login"), "bullets are tidied");
                Check(s.History.Count == 1, "it's remembered for this session");
                int tasks = m.Data.Tasks.Count, notes = m.Data.Notes.Count;
                w.AskMakeTask();
                Check(m.Data.Tasks.Count == tasks + 1 && m.Data.Tasks[0].Title == "What does DPAPI do?", "Make task adds the question to Hunt");
                Check(w.LastToast == "Added to Hunt", "and says so");
                w.AskSaveNote();
                Check(m.Data.Notes.Count == notes + 1 && m.Data.Notes[0].Title == "What does DPAPI do?" && m.Data.Notes[0].Body.StartsWith("DPAPI keeps secrets per user"),
                      "Save as note puts the answer in Scratch, without markdown");
            }),
            ("ask no balance", () =>
            {
                askGateway.Status = 402;
                w.AskBox!.Input.Text = "And one more?";
                w.AskSend();
            }),
            ("ask error shown", () =>
            {
                Check(Platform.AskHub.Session.Error?.Kind == AskErrorKind.NoBalance, "402 means the balance is empty");
                Check(w.AskBox!.ShownError?.Kind == AskErrorKind.NoBalance && w.AskBox.ShowsSettingsButton, "and says so in plain words, with a Settings button");
                askGateway.Status = 200;
                w.OpenAskSettings();
            }),
            ("ask settings", () =>
            {
                Check(!w.AskOpen && w.Selected == Room.Settings, "Settings opens on the key");
                var texts = Descendants<TextBlock>(w.CurrentPane!).Select(t => t.Text).ToList();
                Check(texts.Contains("Ezkielyna API key") && texts.Any(t => t.EndsWith("1234") && t.StartsWith("••••")), "the key row shows the key masked");
                Check(texts.Contains("deepseek-v4.1-flash") && texts.Contains("deepseek-v4-pro-0813"), "and the models to pick from");
                Check(w.AskHotKeyRegistered, "Ctrl+Alt+A registered");
                m.SaveNow();
                var json = File.ReadAllText(m.Store!.Path);
                Check(!json.Contains("sk-selftest") && json.Contains("\"askModel\""), "data.json has the model but never the key");
                w.SetAsk(true);
            }),
            ("ask esc", () =>
            {
                Check(w.AskOpen, "the box opens again");
                Press(w, Key.Escape);
                Check(!w.AskOpen && w.Presentation == Presentation.Expanded, "Esc puts it away and leaves the island open");
                Platform.AskHub.Reset(new Platform.MemoryAskKeys(), new AskClient(askGateway));
            }),
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
                // The header shows only the room's name (like the Mac); its subtitle is the tab's tooltip.
                var tip = ((FrameworkElement)w.Tabs.Children[(int)Room.Den]).ToolTip as string;
                Check(tip == "Den · Timer fokus", $"Indonesian follows the Mac's words ({tip})");
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
            ("settings id", () =>
            {
                SettingsPane.Section = SettingsSection.General;
                w.Select(Room.Settings);
                w.CurrentPane!.Refresh();
            }),
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
                SettingsPane.Section = SettingsSection.Features;
                w.Select(Room.Settings);
                w.Expand();
                w.CurrentPane!.Refresh();
            }),
            ("settings draft ok", () =>
            {
                // A refresh (a search result, an update check) never eats what you typed. The City field shows
                // while no city is set (with one, it's °C · °F · Remove, like the Mac).
                var pane = (SettingsPane)w.CurrentPane!;
                Check(pane.CityField is null, "with a city set, Weather offers °C · °F · Remove instead of the field");
                var city = m.Settings.WeatherCity;
                m.Settings.WeatherCity = "";
                pane.Refresh();
                Check(pane.CityField is not null, "without a city, Weather has the City field");
                if (pane.CityField is not null)
                {
                    pane.CityField.Text = "Kuala Lum";
                    pane.Refresh();
                    Check(pane.CityField!.Text == "Kuala Lum", "the City field keeps what you typed through a refresh");
                    pane.CityField!.Text = "";
                }
                m.Settings.WeatherCity = city;
                SettingsPane.Section = SettingsSection.General;
                pane.Refresh();
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
            // Live activities: a download in progress (fake partial files in a temp folder), quick timers, the meeting countdown.
            ("live download", () =>
            {
                w.Collapse();
                Directory.CreateDirectory(fakeDownloads);
                File.WriteAllBytes(Path.Combine(fakeDownloads, "Ezkiel-stickers.zip.crdownload"), new byte[2_500_000]);
                File.WriteAllBytes(Path.Combine(fakeDownloads, "Lion-pack.exe.part"), new byte[600_000]);
                File.WriteAllBytes(Path.Combine(fakeDownloads, "Lion-pack.exe"), []);   // Firefox's empty placeholder
                m.Settings.PounceDownloads = false;
                m.Settings.LiveDownloads = true;
                m.SettingsChanged();
                w.Downloads.Configure(true, fakeDownloads);
                Check(w.Downloads.Active.Count == 2, $"both partial downloads are followed ({w.Downloads.Active.Count})");
                Check(w.ClosedLive == LiveKind.Download && w.IsLive, "a download in progress is live in the closed island");
            }),
            ("live download pill", () =>
            {
                using (var f = new FileStream(Path.Combine(fakeDownloads, "Ezkiel-stickers.zip.crdownload"), FileMode.Append)) f.Write(new byte[1_500_000]);
                w.Downloads.Scan();
                var lead = w.LeadDownload;
                Check(lead is { Count: 2 } l && l.Download.FinalName == "Ezkiel-stickers.zip" && l.Download.Bytes == 4_000_000,
                      "the largest download leads, with the count and its size so far");
                Check(lead?.Download.BytesPerSecond > 0, "and its speed");
                Check(w.LiveRight.Children.OfType<StackPanel>().Any(), "no total from Chrome: the size and speed show instead of a percent");
            }),
            ("live download fits", () => Check(FullyInside(w, w.LiveRight, minSize: 6) && FullyInside(w, w.LiveLeft, minSize: 6), "the download pill fits inside the island")),
            ("live download done", () =>
            {
                File.Delete(Path.Combine(fakeDownloads, "Lion-pack.exe.part"));
                File.WriteAllBytes(Path.Combine(fakeDownloads, "Lion-pack.exe"), new byte[1_000_000]);
                w.Downloads.Scan();
                Check(w.CurrentPeek is { OpensFile: true } p && p.Subtitle == "Lion-pack.exe" && w.PeekActions.Children.Count == 3,
                      "a finished download peeks with Open · Stash · Show");
            }),
            ("live download peek fits", () =>
            {
                Check(FullyInside(w, w.PeekActions), "the download buttons fit inside the peek");
                w.Collapse();
                w.Downloads.Configure(false);
                m.Settings.LiveDownloads = false;
                m.SettingsChanged();
                try { Directory.Delete(fakeDownloads, recursive: true); } catch (Exception) { }
                Check(w.LeadDownload is null, "off: nothing follows Downloads");
            }),
            ("timer pill", () =>
            {
                Check(m.StartTimer("timer 25m teh") is not null, "typed words start a timer");
                m.StartTimer("Masak", 10 * 60);
                Check(m.Data.Timers.Count == 2 && m.SoonestTimer?.Label == "Masak", "two timers run; the soonest leads");
                Check(w.ClosedLive == LiveKind.Timer && w.IsLive, "the soonest timer counts down in the closed island");
                Check(w.LiveRight.Children.OfType<TextBlock>().Any(t => t.Text is ['0', '9', ':', ..] or ['1', '0', ':', ..]), "as mm:ss");
            }),
            ("timer pill fits", () => Check(FullyInside(w, w.LiveRight, minSize: 6) && FullyInside(w, w.LiveLeft, minSize: 6), "the timer pill fits inside the island")),
            ("timer done peek", () =>
            {
                m.Data.Timers.Add(new QuickTimer(Guid.NewGuid(), "Workout", 30 * 60, DateTime.UtcNow.AddSeconds(-1)));
                m.Tick();
                Check(w.CurrentPeek is { Buttons.Count: 2 } && w.PeekActions.Children.Count == 2, "a finished timer peeks with +5 min · Stop");
                Check(!m.Data.Timers.Any(t => t.Label == "Workout"), "and leaves the running list");
            }),
            ("timer done fits", () =>
            {
                Check(FullyInside(w, w.PeekActions), "the timer buttons fit inside the peek");
                w.CurrentPeek?.Buttons?[0].Run();
                var again = m.Data.Timers.FirstOrDefault(t => t.Label == "Workout");
                Check(again is not null && Math.Abs(again.Remaining(DateTime.UtcNow).TotalSeconds - 300) < 3, "+5 min starts it again for five minutes");
                w.Collapse();
            }),
            ("den timers", () =>
            {
                w.Expand();
                w.Select(Room.Den);
            }),
            ("den timers ok", () =>
            {
                RoomChecks(w, Room.Den);
                Check(w.CurrentPane is DenPane { TimerInput: not null }, "Den has the quick-timer field");
                m.Data.Timers.Clear();
                m.SaveSoon();
                w.Collapse();
            }),
            ("meeting pill", () =>
            {
                w.Rally.Simulate([new Meeting("3", "Standup", DateTime.UtcNow.AddMinutes(7).AddSeconds(-5), DateTime.UtcNow.AddMinutes(22), null)]);
                Check(w.ClosedLive == LiveKind.Meeting && w.IsLive, "a meeting in 7 min counts down in the closed island");
                Check(w.LiveRight.Children.OfType<TextBlock>().Any(t => t.Text == "7 min"), "it says 7 min");
            }),
            ("meeting pill off", () =>
            {
                m.Settings.RallyCountdown = false;
                m.SettingsChanged();
                Check(w.MeetingSoon is null, "Meeting countdown off hides it");
                m.Settings.RallyCountdown = true;
                m.SettingsChanged();
                w.Rally.Simulate([
                    new Meeting("1", "Design review", DateTime.UtcNow.AddMinutes(20), DateTime.UtcNow.AddMinutes(50), "https://meet.google.com/abc-defg-hij"),
                    new Meeting("2", "1:1 with Rina", DateTime.UtcNow.AddHours(2), DateTime.UtcNow.AddHours(2.5), null),
                ]);
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
            // System monitor, with fake readings (the runner's CPU and network vary run to run, and it has no battery).
            ("sysmon den card", () =>
            {
                var x = Enumerable.Range(0, 60).Select(i => (double)i).ToList();
                w.SysMon.Simulate(new Platform.SysMonSampler.Reading
                {
                    Cpu = 34, MemUsed = 11_274_289_152, MemTotal = 17_179_869_184, Down = 1_250_000, Up = 42_000, Online = true,
                    DiskFree = 120_400_000_000, DiskTotal = 255_000_000_000, DiskName = "C:", Battery = 82, OnAC = false, BatteryMinutes = 245,
                },
                cpu: x.Select(i => 28 + 14 * Math.Sin(i / 5) + (i % 9 == 0 ? 18 : 0)), memory: x.Select(i => 62 + i / 10),
                down: x.Select(i => 300_000 + 900_000 * Math.Abs(Math.Sin(i / 7))), up: x.Select(i => 20_000 + 30_000 * Math.Abs(Math.Cos(i / 4))));
                m.Settings.Sysmon = true;
                m.SettingsChanged();
                w.Select(Room.Den);
                w.Expand();
            }),
            ("sysmon den card ok", () =>
            {
                RoomChecks(w, Room.Den);
                Check(w.CurrentPane?.DesiredHeight == RoomInfo.Of(Room.Den).PaneHeight + 60, "Den grows by the system card");
                var card = w.CurrentPane is { } pane ? Descendants<SysMonCard>(pane).FirstOrDefault() : null;
                var texts = card is null ? new List<string>() : Descendants<TextBlock>(card).Select(t => t.Text).ToList();
                Check(texts.Contains("34%") && texts.Contains("1.3 MB/s") && texts.Contains("10.5 GB") && texts.Contains("120 GB"),
                      $"the card shows CPU, memory, network and disk like the Mac ({string.Join(" | ", texts)})");
            }),
            ("sysmon pill cpu", () =>
            {
                m.Settings.SysmonPill = SysPill.Cpu;
                m.SettingsChanged();
                w.Collapse();
            }),
            ("sysmon pill cpu ok", () =>
            {
                var texts = Descendants<TextBlock>(w.LiveRight).Select(t => t.Text).ToList();
                Check(w.SysmonPillShowing && texts.Contains("34%"), $"the CPU pill shows in the closed island ({string.Join(" | ", texts)})");
                m.Settings.SysmonPill = SysPill.Network;
                m.SettingsChanged();
            }),
            ("sysmon pill network", () =>
            {
                var texts = Descendants<TextBlock>(w.LiveRight).Select(t => t.Text).ToList();
                Check(w.SysmonPillShowing && texts.Contains("1.3M"), $"the network pill shows the busier direction ({string.Join(" | ", texts)})");
            }),
            ("sysmon cpu alert", () =>
            {
                var alerts = new SysAlerts();
                var fired = Enumerable.Range(0, 7).Select(i => i * 10.0).Where(t => alerts.Feed("cpu", 97, t)).ToList();
                Check(fired.SequenceEqual([30.0]), $"CPU at 97% peeks once, after 30 s (got {string.Join(", ", fired)})");
                w.ShowCpuPeek(97, "chrome");
                Check(w.CurrentPeek is { Title: "CPU is busy", Room: Room.Den }, "the CPU peek shows and opens Den");
            }),
            ("sysmon off", () =>
            {
                w.Collapse();
                m.Settings.SysmonPill = SysPill.Off;
                m.Settings.Sysmon = false;
                m.SettingsChanged();
                w.SysMon.Simulate(null);
                Check(!w.SysPillOn && !w.ShowsWings, "no pill and no wings once the monitor is off");
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

/// <summary>The self-test's gateway: a scripted answer (one line every 60 ms, waiting half way when asked to),
/// a 402 when told, and two models. Nothing goes to the network.</summary>
internal sealed class FakeAskGateway : System.Net.Http.HttpMessageHandler
{
    public int Status { get; set; } = 200;
    public string? LastAuthorization { get; private set; }
    private int _holdAfter = -1;
    private TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static readonly string[] Lines =
    [
        Chunk("DPAPI keeps "), Chunk("secrets "), Chunk("**per user**"),
        Chunk(" on Windows.\n"), Chunk("- encrypted with your login\n"), Chunk("- nothing leaves the PC"), "", "data: [DONE]",
    ];

    private static string Chunk(string text) =>
        "data: " + new System.Text.Json.Nodes.JsonObject
        {
            ["choices"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
            {
                ["delta"] = new System.Text.Json.Nodes.JsonObject { ["content"] = text },
            }),
        }.ToJsonString();

    public void Hold(int after)
    {
        _holdAfter = after;
        _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Release() => _gate.TrySetResult();

    protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken ct)
    {
        LastAuthorization = request.Headers.Authorization?.ToString();
        System.Net.Http.HttpResponseMessage response;
        if (request.RequestUri!.AbsolutePath.EndsWith("/models"))
            response = new(System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent("{\"data\":[{\"id\":\"deepseek-v4.1-flash\"},{\"id\":\"deepseek-v4-pro-0813\"}]}",
                                                           System.Text.Encoding.UTF8, "application/json"),
            };
        else if (Status != 200)
            response = new((System.Net.HttpStatusCode)Status)
            {
                Content = new System.Net.Http.StringContent("{\"error\":{\"message\":\"Insufficient balance\"}}", System.Text.Encoding.UTF8, "application/json"),
            };
        else
        {
            var content = new System.Net.Http.StreamContent(new ScriptStream(Lines, _holdAfter, _gate.Task));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            response = new(System.Net.HttpStatusCode.OK) { Content = content };
        }
        return Task.FromResult(response);
    }

    private sealed class ScriptStream(string[] lines, int holdAfter, Task gate) : Stream
    {
        private int _next;
        private byte[] _pending = [];
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_pending.Length == 0)
            {
                if (_next >= lines.Length) return 0;
                if (_next == holdAfter) await gate.WaitAsync(ct);
                await Task.Delay(60, ct);
                _pending = System.Text.Encoding.UTF8.GetBytes(lines[_next++] + "\n");
            }
            int n = Math.Min(buffer.Length, _pending.Length);
            _pending.AsMemory(0, n).CopyTo(buffer);
            _pending = _pending[n..];
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
    }
}
