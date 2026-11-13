using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EzkielNotch.Core;
using EzkielNotch.Native;
using EzkielNotch.Platform;
using EzkielNotch.Rooms;
using Microsoft.Win32;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Island;

public enum Presentation { Lip, Peek, Expanded }

/// <summary>
/// Owns the island window: where it sits, when it opens, and how it moves.
/// Windows has no notch, so the closed island is a thin black lip at the top centre that leaves title
/// bars and browser tabs alone. Hover it (or press Ctrl+Alt+N) and it grows into the island.
/// The window is a fixed transparent layered window: only the island's pixels take clicks, everything
/// else passes straight through to the app underneath.
/// </summary>
public partial class IslandWindow : Window
{
    private readonly record struct Shape(double Width, double Height, double Radius, double Ear);

    // Body sizes in DIPs (ears are added on both sides).
    private static readonly Shape LipShape = new(180, 6, 3, 4);
    private static readonly Shape LipHoverShape = new(208, 10, 5, 6);
    /// <summary>A peek is as wide as what it says (the Mac's inline cards): its row plus even margins,
    /// between 240 and 470.</summary>
    private Shape PeekShape
    {
        get
        {
            PeekRow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = Math.Clamp(Math.Ceiling(PeekRow.DesiredSize.Width) + 36, 240, 470);
            return IsIsland ? new(w, 46, 23, 0) : new(w, 52, 18, 10);
        }
    }

    /// <summary>Header 34 + tabs 30 + gap 10 + the room's pane + bottom 12, like the Mac's expandedSize.</summary>
    public double ExpandedHeight(Room room) =>
        34 + 30 + 10 + (_panes.TryGetValue(room, out var pane) ? pane.DesiredHeight : RoomInfo.Of(room).PaneHeight) + 12;
    private Shape ExpandedShape(Room room) => IsIsland ? new(IslandExpandedWidth, ExpandedHeight(room), 26, 0) : new(440, ExpandedHeight(room), 22, 13);

    public NotchModel Model { get; }
    public MediaWatcher Media { get; }
    public LyricsClient Lyrics { get; }
    public RoarWatcher Roar { get; }
    public PounceWatcher Pounce { get; }
    public RallyClient Rally { get; } = new();
    public WeatherClient Weather { get; } = new();
    public CameraMirror Mirror { get; }
    public Presentation Presentation { get; private set; } = Presentation.Lip;
    public bool Hovering { get; private set; }
    /// <summary>Keep open (the Mac's pin): moving away, clicking elsewhere or switching apps doesn't close it.
    /// Esc, the close button, the shortcut and unpinning still do.</summary>
    public bool Pinned { get; private set; }
    public bool FullscreenActive { get; private set; }
    public bool HotKeyRegistered { get; private set; }
    /// <summary>Self-test: ignore the real pointer so a resting cursor can't change the outcome.</summary>
    public bool IgnorePointer { get; set; }
    /// <summary>Self-test: no real Roar, Scout or media sources (they'd change what the test sees).</summary>
    public bool SelfTesting { get; init; }
    public Room Selected { get; private set; } = Room.Den;
    public Pane? CurrentPane => PageHost.Child as Pane;
    public string? LastToast { get; private set; }

    internal ScreenSpot Screen { get; private set; }
    private nint _hwnd;
    private HwndSource? _source;
    private readonly DispatcherTimer _pointer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _second = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _fullscreenPoll = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
    private DateTime _insideSince = DateTime.MaxValue, _outsideSince = DateTime.MaxValue;
    private DateTime _lastKey = DateTime.MinValue;
    /// <summary>Opened with the keyboard (shortcut, tray, menu): the pointer being elsewhere doesn't close it.</summary>
    private bool _openedByKeyboard;
    private DispatcherTimer? _peekTimer, _toastTimer;
    private Room? _peekRoom;
    private nint _winEventHook;
    private Win32.WinEventProc? _winEventProc;   // kept alive: the hook calls back into it
    private nint _previousForeground;
    private bool _dragInside;
    private bool _simulatingFullscreen;
    private readonly Dictionary<Room, Pane> _panes = [];
    private readonly HashSet<Room> _stale = [];
    private const int HotKeyId = 0xE21;
    private string? _registeredHotkey;

    /// <summary>The Mac's shortcut choices, with Ctrl+Alt for ⌃⌥ (⌥⌘I becomes Ctrl+Alt+I).</summary>
    public static string HotkeyLabel(string choice) => NotchSettings.HotkeyLabel(choice);

    private void RegisterHotKey()
    {
        var choice = Model.Settings.Hotkey;
        if (_hwnd == 0 || choice == _registeredHotkey) return;
        if (HotKeyRegistered) Win32.UnregisterHotKey(_hwnd, HotKeyId);
        uint vk = choice switch { "controlOptionSpace" => 0x20u, "optionCommandI" => 0x49u, _ => 0x4Eu };
        HotKeyRegistered = Win32.RegisterHotKey(_hwnd, HotKeyId, Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, vk);
        _registeredHotkey = choice;
        if (!HotKeyRegistered) ShowToast(new ToastRequest(L("%@ is taken by another app", HotkeyLabel(choice))));
        Tray?.Relabel(HotkeyLabel(choice));
        RenderPin();
    }

    public TrayIcon? Tray { get; set; }
    public WhiskersWatcher Whiskers { get; private set; } = null!;
    public Updater Updates { get; } = new();

    public IslandWindow(NotchModel model)
    {
        Model = model;
        Media = new MediaWatcher(Dispatcher);
        Lyrics = new LyricsClient(version: typeof(IslandWindow).Assembly.GetName().Version?.ToString(3) ?? "0");
        Roar = new RoarWatcher(Dispatcher);
        Pounce = new PounceWatcher(Dispatcher);
        Pounce.Pounced += (path, screenshot) =>
        {
            if (!(screenshot ? Model.Settings.PounceScreenshots : Model.Settings.PounceDownloads)) return;
            if (Presentation == Presentation.Expanded) return;
            ShowPeek(screenshot ? L("Screenshot") : L("Downloaded"), System.IO.Path.GetFileName(path), 6, room: Room.Stash, file: path,
                     look: PeekLooks.Of(screenshot ? PeekKind.Screenshot : PeekKind.Download));
        };
        Roar.Updated += Model.UpdateBadges;
        Model.ForegroundApp = AppIdentity.Foreground;
        Model.LookingAtRoar = () => Presentation == Presentation.Expanded && Selected == Room.Roar;
        InitializeComponent();
        BuildTabs();
        RenderPin();
        ApplyStyle(animate: false);
        _pointer.Tick += (_, _) => PointerTick();
        _second.Tick += (_, _) => SecondTick();
        _fullscreenPoll.Tick += (_, _) => CheckFullscreen();
        Clock.Text = Lang.Time(DateTime.Now);
        Island.MouseRightButtonUp += (_, e) => { ShowMenu(); e.Handled = true; };
        PeekView.MouseLeftButtonUp += (_, _) => OpenFromPeek();
        // A click on the closed lip opens it. The pointer poll can miss a quick click or a touchpad tap, this can't.
        Island.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (Presentation != Presentation.Lip) return;
            Expand();
            e.Handled = true;
        };
        Body.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (Presentation != Presentation.Expanded) return;
            EngageKeyboard("click");
            // The field you clicked gets the caret once the window really has the keyboard.
            if (FieldUnder(e.OriginalSource as DependencyObject) is { } field)
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { if (!field.IsKeyboardFocused) field.Focus(); });
        };
        PreviewKeyDown += OnKey;

        // Drop files on the island: it opens Stash and keeps them there.
        Root.AllowDrop = true;
        Root.DragEnter += (_, e) =>
        {
            if (StashPane.DraggingOut || !e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.None; return; }
            _dragInside = true;
            e.Effects = DragDropEffects.Copy;
            Select(Room.Stash);
            if (Presentation != Presentation.Expanded) Expand();
            (CurrentPane as StashPane)?.SetTargeted(true);
            e.Handled = true;
        };
        Root.DragOver += (_, e) =>
        {
            e.Effects = !StashPane.DraggingOut && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Root.DragLeave += (_, _) =>
        {
            _dragInside = false;
            (CurrentPane as StashPane)?.SetTargeted(false);
        };
        Root.Drop += (_, e) =>
        {
            _dragInside = false;
            (CurrentPane as StashPane)?.SetTargeted(false);
            if (StashPane.DraggingOut || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
            Model.AddToShelf(files);
            e.Handled = true;
        };
        Deactivated += (_, _) => SetNoActivate(true);

        Model.Changed += OnModelChanged;
        Model.Changed += _ => UpdateLive();
        Model.Changed += _ => UpdateTabBadges();
        Model.Meetings = () => Rally.Upcoming;
        Mirror = new CameraMirror(Dispatcher);
        Mirror.Changed += RenderMirror;
        // Chorus follows the song even while Purr is closed: the closed island sings along too. First, so a new
        // track never shows the last one's line.
        Media.Changed += FollowLyrics;
        Media.Changed += UpdateLive;
        Media.Changed += UpdateTabBadges;
        Lyrics.Changed += () => Dispatcher.BeginInvoke(UpdateLive);
        Grid.SetRow(LiveLyric, 1);
        LiveLyric.Visibility = Visibility.Collapsed;
        LiveView.Children.Add(LiveLyric);
        Media.Changed += RefreshPeekArtwork;
        Prowl.Changed += UpdateLive;
        Power.Changed += UpdateLive;
        Power.Notice += (kind, pct) =>
        {
            // Whiskers: the battery as a ring beside the words (the Mac's ChargeRing and wording). The notice comes
            // before Power.Charging is updated, so "charging" is read from the level: on AC and not yet full.
            double level = pct / 100.0;
            switch (kind)
            {
                case "plugged":
                    ShowPeek(new PeekInfo(pct < 100 ? L("Charging") : L("Connected to power"), L("Charger connected"))
                    { Look = PeekLooks.Of(PeekKind.Plugged), Progress = level, Charging = pct < 100 });
                    break;
                case "unplugged":
                    ShowPeek(new PeekInfo(L("On battery"), L("Charger unplugged")) { Look = PeekLooks.Of(PeekKind.OnBattery, pct), Progress = level });
                    break;
                default:
                    ShowPeek(new PeekInfo(L("Battery low"), kind == "now" ? L("Plug in now") : L("Plug in soon"))
                    { Look = PeekLooks.Of(PeekKind.BatteryLow, pct), Progress = level, Seconds = 5, Urgent = true });
                    break;
            }
        };
        Rally.Changed += () =>
        {
            UpdateLive();
            if (_panes.ContainsKey(Room.Hunt)) OnModelChanged(Room.Hunt);
        };
        Whiskers = new WhiskersWatcher(Dispatcher);
        Whiskers.CapsLock += on => ShowPeek(on ? L("Caps Lock on") : L("Caps Lock off"), "", 1.4, look: PeekLooks.Of(on ? PeekKind.CapsOn : PeekKind.CapsOff));
        Whiskers.Keyboard += name => ShowPeek(name, L("Keyboard"), 1.6, look: PeekLooks.Of(PeekKind.Keyboard));
        Whiskers.Network += (online, wifi) =>
        {
            if (online) ShowPeek(wifi ? L("Wi-Fi connected") : L("Back online"), L("You're online again"), look: PeekLooks.Of(wifi ? PeekKind.WifiOn : PeekKind.Online));
            else ShowPeek(L("Connection lost"), L("Check your network"), look: PeekLooks.Of(PeekKind.Offline));
        };
        Whiskers.Output += (name, headphones, previous) =>
        {
            if (headphones) ShowPeek(name, L("Connected"), look: PeekLooks.Of(PeekKind.HeadphonesOn));
            else if (previous is not null) ShowPeek(L("%@ disconnected", previous), L("Now on %@", name), look: PeekLooks.Of(PeekKind.HeadphonesOff));
        };
        Updates.Changed += () =>
        {
            if (Presentation == Presentation.Expanded && Selected == Room.Settings) CurrentPane?.Refresh();
            // Announce a new version once, with a peek that opens Settings.
            if (Updates.State == Updater.Phase.Available && Updates.Release is { } r && _announced != r.Version)
            {
                _announced = r.Version;
                ShowPeek(L("Ezkiel Notch %@ is out", string.Join('.', AppVersion.Parts(r.Version))), L("Open Settings to update"), 6, room: Room.Settings,
                         look: PeekLooks.Of(PeekKind.Update));
            }
        };
        Weather.Changed += () =>
        {
            UpdateWeatherChip();
            // City search results show in Settings.
            if (Presentation == Presentation.Expanded && Selected == Room.Settings) CurrentPane?.Refresh();
        };
        Model.Peek += p =>
        {
            if (Presentation == Presentation.Expanded) ShowToast(new ToastRequest($"{p.Title} · {p.Subtitle}"));
            // Plays its sound; shows the card only while the island is closed.
            ShowPeek(PeekInfo.From(p));
        };
        Model.Toast += ShowToast;
        Model.Sound += name =>
        {
            if (Model.Settings.Sounds && name.Length > 0) Sounds.Play(name);
            // A task done, a drink, a file stashed: Ezkiel smiles.
            if (name is "Tink" or "Bottle" or "Pop") MakeLionHappy();
        };

        // A new song while the island is closed: a short peek (not in the first seconds after launch).
        var launched = DateTime.UtcNow;
        Media.TrackChanged += np =>
        {
            if (DateTime.UtcNow - launched < TimeSpan.FromSeconds(4) || Presentation == Presentation.Expanded) return;
            // The album art on the left, an equalizer on the right, in the player's colour (the Mac's track peek).
            ShowPeek(new PeekInfo(np.Title, np.Artist) { Room = Room.Purr, Look = PeekLooks.Track(np.AppId), Artwork = true });
        };
    }

    // MARK: Window setup

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source.AddHook(WndProc);

        // A tool window that doesn't take focus until you click into it: no taskbar button, no Alt+Tab.
        var ex = Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE);

        RegisterHotKey();
        ApplyScreenShare();

        Place();
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(Place);
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);

        _winEventProc = (_, _, _, _, _, _, _) => Dispatcher.BeginInvoke(() =>
        {
            CheckFullscreen();
            Model.AgentAppActivated(AppIdentity.Foreground());
        });
        _winEventHook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, 0, _winEventProc, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);

        if (!SelfTesting) Media.Start();
        Power.Start();
        ConfigureSources();
        _pointer.Start();
        _second.Start();
        _fullscreenPoll.Start();
        CheckFullscreen();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (HotKeyRegistered) Win32.UnregisterHotKey(_hwnd, HotKeyId);
        if (_winEventHook != 0) Win32.UnhookWinEvent(_winEventHook);
        base.OnClosed(e);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && wParam == HotKeyId)
        {
            Toggle();
            handled = true;
        }
        else if ((msg == Win32.WM_MOUSEACTIVATE || msg == Win32.WM_POINTERACTIVATE) && Presentation == Presentation.Expanded)
        {
            // A click (or a tap, or a pen) into the open island activates it, so typing goes to its fields,
            // not the app behind.
            RememberForeground();
            SetNoActivate(false);
            handled = true;
            return msg == Win32.WM_MOUSEACTIVATE ? Win32.MA_ACTIVATE : Win32.PA_ACTIVATE;
        }
        return 0;
    }

    /// <summary>Top centre of the primary monitor, in physical pixels; the window itself never resizes.</summary>
    public void Place()
    {
        if (_hwnd == 0) return;
        Screen = ScreenSpot.Primary();
        int w = (int)Math.Round(Width * Screen.Scale), h = (int)Math.Round(Height * Screen.Scale);
        int x = Screen.Bounds.Left + (Screen.Bounds.Width - w) / 2;
        int y = Screen.Bounds.Top;
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, y, w, h, Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
    }

    // MARK: Hide from screen sharing (the Mac's sharingType = .none)

    /// <summary>Teams, Zoom, Meet, screenshots and recordings leave the island out while the setting is on.</summary>
    private void ApplyScreenShare()
    {
        if (_hwnd != 0) ExcludeFromCapture(_hwnd, Model.Settings.HideFromScreenShare);
    }

    /// <summary>Windows 10 2004 and later leave the window out of captures; older builds can only show it black.</summary>
    private static void ExcludeFromCapture(nint hwnd, bool hidden)
    {
        if (!hidden) Win32.SetWindowDisplayAffinity(hwnd, Win32.WDA_NONE);
        else if (!Win32.SetWindowDisplayAffinity(hwnd, Win32.WDA_EXCLUDEFROMCAPTURE)) Win32.SetWindowDisplayAffinity(hwnd, Win32.WDA_MONITOR);
    }

    /// <summary>The island window's display affinity as Windows reports it (WDA_NONE when it can't tell).</summary>
    public uint DisplayAffinity => _hwnd != 0 && Win32.GetWindowDisplayAffinity(_hwnd, out var a) ? a : Win32.WDA_NONE;

    private int _ticks;

    private void SecondTick()
    {
        _ticks++;
        Model.Tick();
        if (DateTime.Now.Second == 0) Clock.Text = Lang.Time(DateTime.Now);
        if (Presentation == Presentation.Expanded) CurrentPane?.SecondTick();
        UpdateLive();
        UpdateTabBadges();
        if (_ticks % 60 == 0) Rally.Tick(DateTime.UtcNow);
        if (_ticks % 600 == 0) _ = Weather.Refresh();
        // Updates: 30 s after launch, then once a day.
        if (!SelfTesting && Model.Settings.CheckUpdates && (_ticks == 30 || _ticks % 3600 == 0)) _ = Updates.Check();
    }

    /// <summary>Sources that read the outside world follow their settings (none of them in the self-test).</summary>
    private string? _announced;

    private void ConfigureSources()
    {
        RegisterHotKey();
        Media.SetSource(Model.Settings.MusicSource);
        FollowLyrics();
        if (SelfTesting) return;
        var s = Model.Settings;
        Whiskers.Configure(s.SystemOverlays, s.ConnectionPeeks);
        Roar.SetEnabled(s.Roar);
        Pounce.Configure(s.PounceScreenshots, s.PounceDownloads);
        Prowl.SetEnabled(s.Prowl);
        Rally.Configure(s.Rally, s.RallyCalendarUrl);
        Weather.Configure(s.WeatherCity.Length > 0 ? s.WeatherLatitude : null, s.WeatherCity.Length > 0 ? s.WeatherLongitude : null);
    }

    // MARK: Chorus (lyrics)

    private string? _lyricsFor;

    /// <summary>Lyrics follow the song while Chorus is on (the Mac asks LRCLIB once per track, never when it's off).
    /// Only a new track or Chorus switching asks again, so a failed fetch doesn't retry on every media event.</summary>
    private void FollowLyrics()
    {
        var song = Model.Settings.ShowLyrics ? Media.Current?.Song : null;
        if (song?.Key == _lyricsFor) return;
        _lyricsFor = song?.Key;
        Lyrics.Load(song);
    }

    // MARK: Weather (in the header, where the clock is)

    private void UpdateWeatherChip()
    {
        WeatherChip.Children.Clear();
        var w = Weather.Current;
        var city = Model.Settings.WeatherCity;
        if (w is null || city.Length == 0)
        {
            WeatherChip.Visibility = Visibility.Collapsed;
            Clock.Visibility = Visibility.Visible;
            return;
        }
        bool f = Model.Settings.Fahrenheit;
        string T(double c) => $"{Math.Round(WeatherClient.Display(c, f)):0}°";
        var (glyph, color) = WeatherGlyph(w.Code, w.IsDay);
        WeatherChip.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 12, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center });
        var temp = Ui.Number(T(w.Temperature), 12, WeatherInk, FontWeights.Medium);
        temp.Margin = new Thickness(3, 0, 0, 0);
        WeatherChip.Children.Add(temp);
        var parts = new List<string> { city, L(WeatherClient.Word(w.Code)), $"{T(w.Low)}–{T(w.High)}" };
        if (w.RainChance is int rain) parts.Add(L("%d%% rain", rain));
        WeatherChip.ToolTip = string.Join(" · ", parts);
        WeatherChip.Visibility = Visibility.Visible;
        // The weather takes the clock's place (the taskbar keeps its own clock).
        Clock.Visibility = Visibility.Collapsed;
    }

    private static readonly Brush WeatherInk = Ui.Frozen(new SolidColorBrush(Color.FromArgb(0xD9, 0xF5, 0xF1, 0xEA)));

    private static (string, Color) WeatherGlyph(int code, bool day) => code switch
    {
        0 => day ? ("\u2600", Color.FromRgb(0xFF, 0xC8, 0x3D)) : ("\u263E", Color.FromRgb(0xD8, 0xDC, 0xFF)),
        1 or 2 => day ? ("\u26C5", Color.FromRgb(0xFF, 0xD2, 0x7A)) : ("\u2601", Color.FromRgb(0xC7, 0xCB, 0xD6)),
        45 or 48 => ("\u2248", Color.FromRgb(0xC7, 0xCB, 0xD6)),
        >= 51 and <= 67 or >= 80 and <= 82 => ("\u2614", Color.FromRgb(0x8A, 0xE3, 0xFF)),
        >= 71 and <= 77 or 85 or 86 => ("\u2744", Color.FromRgb(0xE6, 0xF4, 0xFF)),
        >= 95 and <= 99 => ("\u26A1", Color.FromRgb(0xFF, 0xD6, 0x0A)),
        _ => ("\u2601", Color.FromRgb(0xC7, 0xCB, 0xD6)),
    };

    // MARK: Mirror

    public bool MirrorOpen { get; private set; }

    public void SetMirror(bool on)
    {
        MirrorOpen = on;
        if (on)
        {
            MirrorView.Height = RoomInfo.Of(Selected).PaneHeight;
            if (_panes.TryGetValue(Selected, out var pane)) MirrorView.Height = pane.DesiredHeight;
            MirrorView.Visibility = Visibility.Visible;
            if (!SelfTesting) _ = Mirror.Start();
        }
        else
        {
            MirrorView.Visibility = Visibility.Collapsed;
            _ = Mirror.Stop();
        }
        RenderMirror();
    }

    private void RenderMirror()
    {
        if (!MirrorOpen) return;
        var grid = new Grid();
        if (Mirror.Frame is { } frame)
            grid.Children.Add(new Image
            {
                Source = frame,
                Stretch = Stretch.UniformToFill,
                // A mirror, not a camera: left is left.
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(-1, 1),
            });
        else
        {
            var text = Mirror.State switch
            {
                CameraMirror.Status.NoCamera => L("No camera found"),
                CameraMirror.Status.Denied => L("Camera access is off · Settings › Privacy › Camera"),
                _ => L("Starting the camera…"),
            };
            var label = Ui.Label(text, Ui.Secondary, Ui.Mist);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            grid.Children.Add(label);
        }
        var close = Ui.IconButton("\uE711", L("Close Mirror"), () => SetMirror(false), 26, Ui.Ink);
        close.Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0));
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Margin = new Thickness(0, 8, 8, 0);
        grid.Children.Add(close);
        var note = Ui.Label(L("Only you see this · nothing is recorded"), Ui.Caption, Brushes.White);
        note.HorizontalAlignment = HorizontalAlignment.Center;
        note.VerticalAlignment = VerticalAlignment.Bottom;
        note.Margin = new Thickness(0, 0, 0, 8);
        note.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 0.8 };
        grid.Children.Add(note);
        MirrorView.Child = grid;
    }

    // MARK: Presentation

    public void Expand()
    {
        _peekTimer?.Stop();
        // "The pointer left" counts from now, not from whenever it last left the closed lip.
        if (Presentation != Presentation.Expanded) _outsideSince = Hovering ? DateTime.MaxValue : DateTime.UtcNow;
        Presentation = Presentation.Expanded;
        _liveShown = false;
        Fade(LiveView, 0, 0.08);
        UpdateClosedLyrics();
        ShowRoom(Selected);
        Apply(ExpandedShape(Selected), animate: true, bouncy: true);
        Fade(PeekView, 0, 0.08);
        ReleasePeek();
        Fade(ExpandedView, 1, 0.22, delay: 0.08);
        Root.Opacity = 1;
        Root.IsHitTestVisible = true;
    }

    public void Collapse()
    {
        _peekTimer?.Stop();
        bool wasOpen = Presentation == Presentation.Expanded;
        Presentation = Presentation.Lip;
        _openedByKeyboard = false;
        // Closing on purpose (Esc, the close button, the shortcut) also unpins: the next open is a normal one.
        if (Pinned) SetPinned(false);
        if (wasOpen)
        {
            CurrentPane?.Shown(false);
            HideToast(immediately: true);
            ReleaseKeyboard();
        }
        Fade(ExpandedView, 0, 0.1);
        Fade(PeekView, 0, 0.1);
        ReleasePeek();
        if (MirrorOpen) SetMirror(false);
        _closedShape = null;
        _liveShown = false;
        _liveKey = "";
        UpdateLive();
        UpdateHiddenState();
    }

    /// <summary>Pin or unpin. Pinning opens the island if it's closed; unpinning with the pointer away lets it
    /// close after the usual short wait.</summary>
    public void SetPinned(bool on)
    {
        if (Pinned == on) return;
        Pinned = on;
        RenderPin();
        if (on)
        {
            if (Presentation != Presentation.Expanded) Expand();
        }
        else if (Presentation == Presentation.Expanded && !Hovering) _outsideSince = DateTime.UtcNow;
    }

    /// <summary>The header's two round buttons, as on the Mac: Keep open (pin / pin.fill, gold while pinned) and
    /// Collapse (chevron.up; its tip names the shortcut).</summary>
    private void RenderPin()
    {
        // Segoe's Pin and PinFill glyphs.
        PinHost.Child = Ui.IconButton(Pinned ? "\uE842" : "\uE718",
                                      Pinned ? L("Unpin · close when you move away") : L("Keep the notch open"),
                                      () => SetPinned(!Pinned), 24, Pinned ? Gold : Ui.Mist);
        CollapseHost.Child = Ui.IconButton("\uE70E", L("Collapse (Esc) · %@ toggles", HotkeyLabel(Model.Settings.Hotkey)), Collapse, 24, Ui.Mist);
    }

    /// <summary>Ezkielyna gold (the Mac's Theme.gold).</summary>
    private static readonly Brush Gold = Ui.Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xCF, 0x87)));

    /// <summary>The shortcut, the tray and the menu: opening this way means you're about to type, so the island
    /// takes the keyboard straight away (hovering open doesn't, until you click).</summary>
    public void Toggle()
    {
        if (Presentation == Presentation.Expanded) Collapse();
        else
        {
            Expand();
            TakeKeyboard();
        }
    }

    /// <summary>Takes the keyboard once the open room is laid out, and puts the caret in its first field.</summary>
    public void TakeKeyboard()
    {
        _openedByKeyboard = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (Presentation != Presentation.Expanded) return;
            EngageKeyboard("open");
            CurrentPane?.FocusFirstField();
        });
    }

    /// <summary>A short message under the top edge. While something is fullscreen only urgent ones show.
    /// Clicking it opens its room.</summary>
    public void ShowPeek(string title, string subtitle, double seconds = 3.4, bool urgent = false, Room? room = null, string? app = null, string? file = null, string? link = null,
                         PeekLook? look = null)
        => ShowPeek(new PeekInfo(title, subtitle) { Seconds = seconds, Urgent = urgent, Room = room, App = app, File = file, Link = link, Look = look });

    /// <summary>What the peek on screen is (null when none is).</summary>
    public PeekInfo? CurrentPeek => Presentation == Presentation.Peek ? _peek : null;
    private PeekInfo? _peek;
    private BitmapSource? _peekArt;

    /// <summary>The rich card: the tinted icon on the left, a value, ring or equalizer on the right (the Mac's Peek).
    /// It plays its sound also while the island is open (a toast shows instead); over fullscreen apps only urgent
    /// ones show or sound, like the Mac.</summary>
    public void ShowPeek(PeekInfo p)
    {
        if (FullscreenActive && Model.Settings.HideInFullscreen && !p.Urgent) return;
        if (Model.Settings.Sounds && p.Chime.Length > 0) Sounds.Play(p.Chime);
        if (Presentation == Presentation.Expanded) return;
        string? file = p.File, link = p.Link;
        _peek = p;
        PeekTitle.Text = p.Title;
        PeekSubtitle.Text = p.Subtitle;
        PeekSubtitle.Visibility = p.Subtitle.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PeekLeading.Child = PeekLeft(p);
        PeekTrailing.Child = PeekRight(p);
        PeekTrailing.Visibility = PeekTrailing.Child is null ? Visibility.Collapsed : Visibility.Visible;
        PeekActions.Children.Clear();
        if (file is not null)
        {
            // Pounce: the file right where you need it.
            PeekActions.Children.Add(Ui.TextButton(L("Copy"), () => { Files.Copy([file]); Collapse(); }, Accent.Stash.LightBrush));
            PeekActions.Children.Add(Ui.TextButton("Stash", () => { Model.AddToShelf([file]); Collapse(); }, Accent.Stash.LightBrush));
            PeekActions.Children.Add(Ui.TextButton(L("Show"), () => { Files.Reveal(file); Collapse(); }, Accent.Stash.LightBrush));
        }
        if (link is not null)
            PeekActions.Children.Add(Ui.Pill(L("Join"), () => { Files.Open(link); Collapse(); }, Accent.Rally, prominent: true, glyph: "\uE714"));
        _peekRoom = p.Room;
        PeekView.Cursor = p.Room is null ? null : Cursors.Hand;
        Presentation = Presentation.Peek;
        _liveShown = false;
        Fade(LiveView, 0, 0.08);
        UpdateClosedLyrics();
        Root.Opacity = 1;
        Root.IsHitTestVisible = true;
        Apply(PeekShape, animate: true, bouncy: true);
        Fade(PeekView, 1, 0.2, delay: 0.1);
        _peekTimer?.Stop();
        _peekTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(p.Urgent ? Math.Max(p.Seconds, 6) : p.Seconds) };
        _peekTimer.Tick += (_, _) =>
        {
            // Hovering keeps it; leaving lets it go.
            if (Hovering) return;
            _peekTimer?.Stop();
            if (Presentation == Presentation.Peek) Collapse();
        };
        _peekTimer.Start();
    }

    private void OpenFromPeek()
    {
        if (Presentation != Presentation.Peek) return;
        if (_peekRoom is { } room) Select(room);
        Expand();
    }

    /// <summary>Left of the words (the Mac's order): the album art, an app's own icon, a file's thumbnail, Ezkiel,
    /// then the look's glyph in its tint.</summary>
    private FrameworkElement PeekLeft(PeekInfo p)
    {
        _peekArt = p.Artwork ? Media.Artwork : null;
        if (p.Artwork && Media.Artwork is { } art) return PeekTints.ArtworkTile(art);
        if (p.App is not null && AppIcons.Icon(p.App) is { } icon) return new Image { Source = icon, Width = 26, Height = 26 };
        if (p.File is not null && Files.Thumbnail(p.File, 64) is { } thumb) return new Image { Source = thumb, Width = 26, Height = 26 };
        if (p.Look is not { ShowsLion: false } look) return new Image { Source = Rooms.PurrPane.Lion, Width = 26, Height = 26 };
        return PeekTints.GlyphTile(look.Glyph, p.Tint);
    }

    /// <summary>Right of the words, the first that applies: the battery ring, a big value, the equalizer for a new
    /// song, or the glyph when Ezkiel is on the left.</summary>
    private FrameworkElement? PeekRight(PeekInfo p)
    {
        if (p.Progress is { } level) return new ChargeRing(level, p.Tint, p.Charging, 32);
        if (p.Value is { Length: > 0 } value) return Ui.Number(value, 15, PeekTints.Brush(p.Tint));
        if (p.Artwork) return new EqualizerBars(true, (Media.ArtworkAccent?.Deep) ?? p.Tint, 12);
        if (p.Look is { ShowsLion: true } look) return Ui.Glyph(look.Glyph, 13, PeekTints.Brush(p.Tint));
        return null;
    }

    /// <summary>The album art usually arrives a moment after the new song: put it in the card that's showing.</summary>
    private void RefreshPeekArtwork()
    {
        if (CurrentPeek is not { Artwork: true } p || ReferenceEquals(Media.Artwork, _peekArt)) return;
        PeekLeading.Child = PeekLeft(p);
        PeekTrailing.Child = PeekRight(p);
    }

    /// <summary>A closed card lets go of its content once it has faded, so a ring or equalizer never runs unseen.</summary>
    private void ReleasePeek()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (Presentation == Presentation.Peek) return;
            PeekLeading.Child = null;
            PeekTrailing.Child = null;
            _peekArt = null;
        };
        t.Start();
    }

    private void Apply(Shape s, bool animate, bool bouncy = false)
    {
        // Lay the content out at the final size once; the animation only moves the outline.
        Island.ContentWidth = s.Width;
        Island.ContentHeight = s.Height;
        if (!animate || SpringEase.ReduceMotion)
        {
            Set(IslandSurface.BodyWidthProperty, s.Width);
            Set(IslandSurface.BodyHeightProperty, s.Height);
            Set(IslandSurface.BottomRadiusProperty, s.Radius);
            Set(IslandSurface.EarProperty, s.Ear);
            return;
        }
        var ease = bouncy ? SpringEase.Bouncy : SpringEase.Smooth;
        var duration = TimeSpan.FromMilliseconds(bouncy ? 560 : 420);
        Animate(IslandSurface.BodyWidthProperty, s.Width, duration, ease);
        Animate(IslandSurface.BodyHeightProperty, s.Height, duration, ease);
        Animate(IslandSurface.BottomRadiusProperty, s.Radius, duration, ease);
        Animate(IslandSurface.EarProperty, s.Ear, duration, ease);
    }

    private void Set(DependencyProperty p, double value)
    {
        Island.BeginAnimation(p, null);
        Island.SetValue(p, value);
    }

    private void Animate(DependencyProperty p, double to, TimeSpan duration, IEasingFunction ease)
    {
        var a = new DoubleAnimation(to, duration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
        Island.BeginAnimation(p, a, HandoffBehavior.SnapshotAndReplace);
    }

    private static void Fade(UIElement element, double to, double seconds, double delay = 0)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromSeconds(seconds)) { BeginTime = TimeSpan.FromSeconds(delay) };
        element.BeginAnimation(OpacityProperty, a, HandoffBehavior.SnapshotAndReplace);
        element.IsHitTestVisible = to > 0;
    }

    // MARK: Toasts

    /// <summary>A capsule over the bottom of the page; 2.5 s, or 5 s when it offers Undo. Only while open.</summary>
    public void ShowToast(ToastRequest t)
    {
        LastToast = t.Text;
        if (Presentation != Presentation.Expanded) return;
        ToastText.Text = t.Text;
        ToastAction.Child = null;
        if (t.ActionTitle is not null && t.Action is not null)
        {
            var action = t.Action;
            ToastAction.Child = Ui.TextButton(t.ActionTitle, () =>
            {
                action();
                HideToast();
            }, Accent.Oasis.LightBrush);
        }
        else ToastView.Padding = new Thickness(14, 0, 14, 0);
        if (ToastAction.Child is not null) ToastView.Padding = new Thickness(14, 0, 6, 0);
        Fade(ToastView, 1, 0.16);
        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(t.Action is null ? 2.5 : 5) };
        _toastTimer.Tick += (_, _) => HideToast();
        _toastTimer.Start();
    }

    private void HideToast(bool immediately = false)
    {
        _toastTimer?.Stop();
        if (immediately)
        {
            ToastView.BeginAnimation(OpacityProperty, null);
            ToastView.Opacity = 0;
            ToastView.IsHitTestVisible = false;
        }
        else Fade(ToastView, 0, 0.2);
    }

    // MARK: Keyboard

    /// <summary>Clicking into the open island lets it take the keyboard (to type a task or a note);
    /// closing gives it back to the app you were in.</summary>
    public void EngageKeyboard(string why = "click")
    {
        if (_hwnd == 0) return;
        if (IsActive && Win32.GetForegroundWindow() == _hwnd) return;
        RememberForeground();
        // Without this the window can't be activated at all.
        SetNoActivate(false);
        string how = "direct";
        bool ok = TryForeground();
        if (!ok)
        {
            // Windows' foreground lock: a key press of our own (Alt, which types nothing) unlocks it.
            how = "alt";
            Win32.keybd_event(Win32.VK_MENU, 0, 0, 0);
            ok = TryForeground();
            Win32.keybd_event(Win32.VK_MENU, 0, Win32.KEYEVENTF_KEYUP, 0);
        }
        if (!ok)
        {
            // Last resort: borrow the foreground app's input queue for a moment.
            how = "attach";
            var fg = Win32.GetForegroundWindow();
            uint mine = Win32.GetCurrentThreadId(), theirs = fg == 0 ? 0 : Win32.GetWindowThreadProcessId(fg, out _);
            bool attached = theirs != 0 && theirs != mine && Win32.AttachThreadInput(theirs, mine, true);
            Win32.BringWindowToTop(_hwnd);
            ok = TryForeground();
            if (attached) Win32.AttachThreadInput(theirs, mine, false);
        }
        Activate();
        FocusLog.Write($"{why}: {(ok ? "ok" : "FAILED")} via {how}, active={IsActive}, focus={Keyboard.FocusedElement?.GetType().Name ?? "none"}");
    }

    private bool TryForeground()
    {
        Win32.SetForegroundWindow(_hwnd);
        return Win32.GetForegroundWindow() == _hwnd;
    }

    /// <summary>The text box a click landed in (or on its rounded frame), if any.</summary>
    private static TextBox? FieldUnder(DependencyObject? d)
    {
        for (; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is TextBox t) return t;
        return null;
    }

    private void RememberForeground()
    {
        var fg = Win32.GetForegroundWindow();
        if (fg != 0 && fg != _hwnd) _previousForeground = fg;
    }

    private void ReleaseKeyboard()
    {
        bool had = IsActive;
        Keyboard.ClearFocus();
        SetNoActivate(true);
        if (had && _previousForeground != 0 && Win32.IsWindow(_previousForeground)) Win32.SetForegroundWindow(_previousForeground);
        _previousForeground = 0;
    }

    private void SetNoActivate(bool on)
    {
        if (_hwnd == 0) return;
        var ex = Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
        var next = on ? ex | Win32.WS_EX_NOACTIVATE : ex & ~(nint)Win32.WS_EX_NOACTIVATE;
        if (next != ex) Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, next);
    }

    /// <summary>A text field has the keyboard: leaving the island with the pointer shouldn't close it.</summary>
    private bool Typing => IsActive && Keyboard.FocusedElement is TextBox;

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Presentation != Presentation.Expanded) return;
        _lastKey = DateTime.UtcNow;
        if (e.Key == Key.Escape)
        {
            // Esc leaves the field first (what you typed stays); a second Esc closes.
            if (Keyboard.FocusedElement is TextBox) Focus();
            else Collapse();
            e.Handled = true;
        }
        else if (e.Key == Key.Space && Keyboard.FocusedElement is not TextBox && Selected == Room.Den)
        {
            Model.FocusStartPause();
            e.Handled = true;
        }
        // Ctrl+1…8 switch rooms (the Mac's ⌘1…⌘8).
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key >= Key.D1 && e.Key <= Key.D8)
        {
            Select((Room)(e.Key - Key.D1));
            e.Handled = true;
        }
        // ← → walk through the rooms while no field has the caret.
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.Left or Key.Right && Keyboard.FocusedElement is not TextBox)
        {
            SelectNeighbor(e.Key == Key.Left ? -1 : 1);
            e.Handled = true;
        }
        // Ctrl+, opens Settings (the Mac's ⌘,).
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.OemComma)
        {
            Select(Room.Settings);
            e.Handled = true;
        }
    }

    /// <summary>The room before or after this one, wrapping around.</summary>
    public void SelectNeighbor(int delta)
    {
        int count = RoomInfo.All.Length;
        Select(RoomInfo.All[((int)Selected + delta % count + count) % count].Room);
    }

    // MARK: Pointer

    /// <summary>Is a point (window DIPs) on the island? Transparent pixels pass clicks through on their own;
    /// this is for hover, and for the self-test.</summary>
    public bool OnIsland(Point p)
    {
        var shape = Island.ShapeRect;
        return Island.Shape().FillContains(p) || (shape.Contains(p) && Presentation == Presentation.Lip);
    }

    /// <summary>The hover zone: generous around the thin lip, the island itself when open.</summary>
    private bool InHotZone(Point p)
    {
        var r = Island.ShapeRect;
        return Presentation switch
        {
            Presentation.Lip => !(FullscreenActive && Model.Settings.HideInFullscreen) && p.Y >= -2 && p.Y <= r.Bottom + 8 && p.X >= r.Left - 24 && p.X <= r.Right + 24,
            Presentation.Peek => p.Y <= r.Bottom + 4 && p.X >= r.Left - 6 && p.X <= r.Right + 6,
            _ => p.Y <= r.Bottom + 14 && p.X >= r.Left - 12 && p.X <= r.Right + 12,
        };
    }

    private void PointerTick()
    {
        if (IgnorePointer || _hwnd == 0 || !Win32.GetCursorPos(out var c) || !Win32.GetWindowRect(_hwnd, out var w)) return;
        var p = new Point((c.X - w.Left) / Screen.Scale, (c.Y - w.Top) / Screen.Scale);
        bool inside = InHotZone(p);
        PointerMoved(inside);

        // A click on the lip opens it even with hover-to-open off.
        bool pressed = (Win32.GetAsyncKeyState(Win32.PrimaryButton) & 0x8000) != 0;
        if (Presentation == Presentation.Lip && inside && pressed) Expand();
        // A click anywhere else closes the open island (clicks there go to other apps, so we look).
        // Not while a file is being dragged out of Stash or over the island.
        if (StashPane.DraggingOut || _dragInside) return;
        if (Presentation == Presentation.Expanded && !inside && pressed && !Pinned && !_menuOpen) Collapse();
    }

    /// <summary>Hover state machine (also driven by the self-test).</summary>
    public void PointerMoved(bool inside)
    {
        var now = DateTime.UtcNow;
        if (inside)
        {
            NoteInteraction();
            // Once the pointer has come to the island, the usual hover rules apply again.
            if (Presentation == Presentation.Expanded) _openedByKeyboard = false;
            _outsideSince = DateTime.MaxValue;
            if (_insideSince == DateTime.MaxValue) _insideSince = now;
            if (!Hovering)
            {
                Hovering = true;
                if (Presentation == Presentation.Lip) UpdateLive();
            }
            // Open after a short, deliberate rest, so passing by on the way to a tab doesn't open it.
            var delay = TimeSpan.FromSeconds(Math.Max(0.05, Model.Settings.HoverDelay));
            if (Model.Settings.ExpandOnHover && Presentation != Presentation.Expanded && now - _insideSince > delay) Expand();
        }
        else
        {
            _insideSince = DateTime.MaxValue;
            if (_outsideSince == DateTime.MaxValue) _outsideSince = now;
            if (Hovering)
            {
                Hovering = false;
                if (Presentation == Presentation.Lip) UpdateLive();
            }
            // While you type, the island waits: it stays as long as you typed in the last minute, and an
            // idle field still lets go 20 s after the pointer left. A click elsewhere always closes it.
            var grace = Typing ? TimeSpan.FromSeconds(20) : TimeSpan.FromMilliseconds(500);
            if (Typing && now - _lastKey < TimeSpan.FromSeconds(60)) return;
            if (_openedByKeyboard || Pinned || _menuOpen) return;
            if (StashPane.DraggingOut || _dragInside) return;
            if (Presentation == Presentation.Expanded && now - _outsideSince > grace) Collapse();
        }
    }

    // MARK: Fullscreen

    public void CheckFullscreen()
    {
        if (_hwnd == 0 || _simulatingFullscreen) return;
        var active = Fullscreen.Active(Screen, _hwnd);
        if (active == FullscreenActive) return;
        FullscreenActive = active;
        UpdateHiddenState();
    }

    /// <summary>Self-test: pretend something is fullscreen.</summary>
    public void SimulateFullscreen(bool active)
    {
        // While simulated, the real check (poll and foreground hook) must not undo it.
        _simulatingFullscreen = active;
        FullscreenActive = active;
        UpdateHiddenState();
    }

    /// <summary>Over a fullscreen game, video or slides the lip steps away entirely.</summary>
    private void UpdateHiddenState()
    {
        bool hide = FullscreenActive && Model.Settings.HideInFullscreen && Presentation == Presentation.Lip;
        var a = new DoubleAnimation(hide ? 0 : 1, TimeSpan.FromMilliseconds(180));
        Root.BeginAnimation(OpacityProperty, a);
        Root.IsHitTestVisible = !hide;
    }

    // MARK: Rooms

    private readonly List<(Border Pill, TabGlyph Glyph)> _tabs = [];
    private int _hoveredTab = -1;

    /// <summary>The Mac's tab strip: a glyph per room, the open one on a capsule in its accent (16% fill, 22% hairline),
    /// a faint capsule on hover, and a dot when the room has something going on.</summary>
    private void BuildTabs()
    {
        Tabs.Children.Clear();
        _tabs.Clear();
        foreach (var info in RoomInfo.All)
        {
            var room = info.Room;
            int index = _tabs.Count;
            var glyph = new TabGlyph(room);
            var pill = new Border
            {
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Child = glyph,
                Margin = new Thickness(1, 0, 1, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = $"{info.Name} · {L(info.Subtitle)}",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0.5),
            };
            AutomationProperties.SetName(pill, $"{info.Name}, {L(info.Subtitle)}");
            pill.MouseEnter += (_, _) => { _hoveredTab = index; PaintTabs(); };
            pill.MouseLeave += (_, _) => { if (_hoveredTab == index) _hoveredTab = -1; PaintTabs(); };
            pill.MouseLeftButtonUp += (_, _) => Select(room);
            Tabs.Children.Add(pill);
            _tabs.Add((pill, glyph));
        }
        UpdateTabBadges();
        Select(Selected);
    }

    private static readonly Brush TabHover = Ui.Frozen(new SolidColorBrush(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF)));

    private void PaintTabs()
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            var (pill, glyph) = _tabs[i];
            var accent = RoomInfo.All[i].Accent;
            bool on = i == (int)Selected, hover = i == _hoveredTab;
            pill.Background = on ? accent.Tint(0x29) : hover ? TabHover : Brushes.Transparent;
            pill.BorderBrush = on ? accent.Tint(0x38) : Brushes.Transparent;
            glyph.SetState(on, hover);
        }
    }

    /// <summary>The Mac's tab badges: Den while the timer runs, Purr while music plays, Roar with unread counts or
    /// a live agent, Hunt with an overdue task or a meeting about to start.</summary>
    private void UpdateTabBadges()
    {
        if (_tabs.Count != RoomInfo.All.Length) return;
        var now = DateTime.UtcNow;
        bool Show(Room room) => room switch
        {
            Room.Den => Model.Focus.IsRunning,
            Room.Purr => Media.Current?.IsPlaying ?? false,
            Room.Roar => Model.UnreadTotal > 0 || Model.LiveAgents.Count > 0,
            Room.Hunt => Model.Data.Tasks.Any(t => !t.Done && t.Due is { } due && due <= now) || MeetingSoon is not null,
            _ => false,
        };
        foreach (var info in RoomInfo.All) _tabs[(int)info.Room].Glyph.Badge = Show(info.Room);
    }

    private Pane PaneFor(Room room)
    {
        if (_panes.TryGetValue(room, out var pane)) return pane;
        pane = room switch
        {
            Room.Den => new DenPane(Model),
            Room.Hunt => new HuntPane(Model, Rally, () => SetMirror(true)),
            Room.Scratch => new ScratchPane(Model),
            Room.Oasis => new OasisPane(Model),
            Room.Settings => new SettingsPane(Model) { WeatherSource = () => Weather, UpdaterSource = () => Updates },
            Room.Purr => new PurrPane(Model, Media, Lyrics),
            Room.Roar => new RoarPane(Model),
            Room.Stash => new StashPane(Model),
            _ => throw new ArgumentOutOfRangeException(nameof(room)),
        };
        if (pane is RoarPane roar) roar.OpenSettings += () => Select(Room.Settings);
        if (pane is HuntPane hunt) hunt.OpenSettings += () => Select(Room.Settings);
        if (pane is StashPane stash)
        {
            stash.ShareFiles += files =>
            {
                Collapse();
                Files.Share(_hwnd, files);
            };
            stash.ChooseFiles += () =>
            {
                // The file dialog would sit under the open island, so the island tucks away while it's up.
                Collapse();
                var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = L("Add files to Stash") };
                if (dialog.ShowDialog() == true)
                {
                    Model.AddToShelf(dialog.FileNames);
                    Select(Room.Stash);
                    Expand();
                }
            };
        }
        if (pane is PurrPane purr)
            purr.HeightChanged += () => { if (Presentation == Presentation.Expanded && Selected == Room.Purr) Apply(ExpandedShape(Room.Purr), animate: true); };
        if (pane is ScratchPane scratch) scratch.Copied += () => ShowToast(new ToastRequest(L("Copied")));
        pane.Refresh();
        _panes[room] = pane;
        return pane;
    }

    private void ShowRoom(Room room)
    {
        var pane = PaneFor(room);
        if (_stale.Remove(room)) pane.Refresh();
        if (PageHost.Child != pane)
        {
            CurrentPane?.Shown(false);
            PageHost.Child = pane;
            if (Presentation == Presentation.Expanded && !SpringEase.ReduceMotion)
            {
                // Cross-fade + a short slide, like the Mac's tab switch.
                pane.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
                var slide = new TranslateTransform(0, 8);
                pane.RenderTransform = slide;
                slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = SpringEase.Smooth });
            }
        }
        pane.Shown(true);
    }

    public void Select(Room room)
    {
        if (MirrorOpen && room != Selected) SetMirror(false);
        Selected = room;
        var info = RoomInfo.Of(room);
        TabTitle.Text = info.Name;
        PaintTabs();
        if (Presentation == Presentation.Expanded)
        {
            HideToast(immediately: true);
            ShowRoom(room);
            Apply(ExpandedShape(room), animate: true, bouncy: false);
        }
    }

    private void OnModelChanged(Room? room)
    {
        if (room is null)
        {
            // Language switch: every room and the tabs say it again.
            foreach (var r in _panes.Keys) _stale.Add(r);
            BuildTabs();
            RenderPin();
            Clock.Text = Lang.Time(DateTime.Now);
            UpdateWeatherChip();
            Tray?.Relabel(HotkeyLabel(Model.Settings.Hotkey));
            if (Presentation == Presentation.Expanded) ShowRoom(Selected);
            return;
        }
        if (room == Room.Settings)
        {
            ConfigureSources();
            ApplyScreenShare();
            UpdateWeatherChip();
            ApplyStyle(animate: true);
            // Settings feed Den, Oasis and the rest; the Settings room redraws itself.
            foreach (var r in _panes.Keys) if (r != Room.Settings) _stale.Add(r);
            if (Presentation == Presentation.Expanded && Selected != Room.Settings) ShowRoom(Selected);
            return;
        }
        if (!_panes.TryGetValue(room.Value, out var pane)) return;
        if (Presentation == Presentation.Expanded && Selected == room) pane.Refresh();
        else _stale.Add(room.Value);
    }

    /// <summary>Opens the island on a room and takes the keyboard (the tray, the menus, a second launch).</summary>
    public void OpenRoom(Room room)
    {
        Select(room);
        if (Presentation != Presentation.Expanded) Expand();
        TakeKeyboard();
    }

    /// <summary>While the right-click menu is up, the island stays as it is (the Mac's "menu" blocker).</summary>
    private bool _menuOpen;

    /// <summary>The right-click menu, like the Mac's: open/close, Keep open, Open on hover, Notch or Island.</summary>
    public ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        void Add(string header, Action click, bool? check = null)
        {
            var item = new MenuItem { Header = header, IsChecked = check == true };
            item.Click += (_, _) => click();
            menu.Items.Add(item);
        }
        Add(Presentation == Presentation.Expanded ? L("Close") : L("Open") + $"  ({HotkeyLabel(Model.Settings.Hotkey)})", Toggle);
        Add(L("Keep Open"), () => SetPinned(!Pinned), Pinned);
        menu.Items.Add(new Separator());
        Add(L("Open on Hover"), () =>
        {
            Model.Settings.ExpandOnHover = !Model.Settings.ExpandOnHover;
            Model.SettingsChanged();
        }, Model.Settings.ExpandOnHover);
        menu.Items.Add(new Separator());
        foreach (var (style, name) in new[] { ("notch", "Notch"), ("island", "Island") })
            Add(L(name), () =>
            {
                if (Model.Settings.Style == style) return;
                Model.Settings.Style = style;
                Model.SettingsChanged();
            }, Model.Settings.Style == style);
        menu.Items.Add(new Separator());
        Add(L("Settings…"), () => OpenRoom(Room.Settings));
        Add(L("Quit Ezkiel Notch"), () => Application.Current.Shutdown());
        return menu;
    }

    private void ShowMenu()
    {
        var menu = BuildMenu();
        menu.Opened += (_, _) =>
        {
            _menuOpen = true;
            // The menu is a window of its own: it stays out of screen sharing with the island.
            if (Model.Settings.HideFromScreenShare && PresentationSource.FromVisual(menu) is HwndSource popup)
                ExcludeFromCapture(popup.Handle, true);
        };
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            // "The pointer left" counts from when the menu went away.
            if (!Hovering) _outsideSince = DateTime.UtcNow;
        };
        menu.IsOpen = true;
    }

    // MARK: Self-test support

    /// <summary>Renders the window's content to a PNG at 2× (no screen capture permission needed).</summary>
    public void Snapshot(string path)
    {
        Root.UpdateLayout();
        int w = (int)Math.Ceiling(Root.ActualWidth * 2), h = (int)Math.Ceiling(Root.ActualHeight * 2);
        if (w <= 0 || h <= 0) return;
        var rtb = new RenderTargetBitmap(w, h, 192, 192, PixelFormats.Pbgra32);
        rtb.Render(Root);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        png.Save(file);
    }

    public Rect WindowRectPixels() => Win32.GetWindowRect(_hwnd, out var r) ? new Rect(r.Left, r.Top, r.Width, r.Height) : Rect.Empty;
}
