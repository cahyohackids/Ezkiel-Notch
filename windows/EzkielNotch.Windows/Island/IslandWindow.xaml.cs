using System.IO;
using System.Windows;
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
    private static readonly Shape PeekShape = new(320, 52, 18, 10);
    /// <summary>A peek with buttons (Pounce: Copy · Stash · Show).</summary>
    private static readonly Shape PeekWideShape = new(440, 52, 18, 10);

    /// <summary>Header 34 + tabs 30 + gap 10 + the room's pane + bottom 12, like the Mac's expandedSize.</summary>
    public double ExpandedHeight(Room room) =>
        34 + 30 + 10 + (_panes.TryGetValue(room, out var pane) ? pane.DesiredHeight : RoomInfo.Of(room).PaneHeight) + 12;
    private Shape ExpandedShape(Room room) => new(440, ExpandedHeight(room), 22, 13);

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
            ShowPeek(screenshot ? L("Screenshot") : L("Downloaded"), System.IO.Path.GetFileName(path), 6, room: Room.Stash, file: path);
        };
        Roar.Updated += Model.UpdateBadges;
        Model.ForegroundApp = AppIdentity.Foreground;
        Model.LookingAtRoar = () => Presentation == Presentation.Expanded && Selected == Room.Roar;
        InitializeComponent();
        BuildTabs();
        Apply(LipShape, animate: false);
        _pointer.Tick += (_, _) => PointerTick();
        _second.Tick += (_, _) => SecondTick();
        _fullscreenPoll.Tick += (_, _) => CheckFullscreen();
        Clock.Text = Lang.Time(DateTime.Now);
        Island.MouseRightButtonUp += (_, e) => { ShowMenu(); e.Handled = true; };
        PeekView.MouseLeftButtonUp += (_, _) => OpenFromPeek();
        Body.PreviewMouseLeftButtonDown += (_, _) => { if (Presentation == Presentation.Expanded) EngageKeyboard(); };
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
        Model.Meetings = () => Rally.Upcoming;
        Mirror = new CameraMirror(Dispatcher);
        Mirror.Changed += RenderMirror;
        Media.Changed += UpdateLive;
        Prowl.Changed += UpdateLive;
        Power.Changed += UpdateLive;
        Power.Notice += (kind, pct) =>
        {
            // Whiskers: the battery, beside the camera.
            if (kind == "charging") ShowPeek(L("Charging"), $"{pct}%", 2.2);
            else ShowPeek(kind == "critical" ? L("Battery very low") : L("Battery low"), L("%d%% left · plug in soon", pct), 5, urgent: true);
        };
        Rally.Changed += () =>
        {
            UpdateLive();
            if (_panes.ContainsKey(Room.Hunt)) OnModelChanged(Room.Hunt);
        };
        Weather.Changed += () =>
        {
            UpdateWeatherChip();
            // City search results show in Settings.
            if (Presentation == Presentation.Expanded && Selected == Room.Settings) CurrentPane?.Refresh();
        };
        Model.Peek += p =>
        {
            if (Model.Settings.Sounds && p.Sound.Length > 0) Sounds.Play(p.Sound);
            if (Presentation == Presentation.Expanded) ShowToast(new ToastRequest($"{p.Title} · {p.Subtitle}"));
            else ShowPeek(p.Title, p.Subtitle, urgent: p.Urgent, room: p.Room, app: p.App, link: p.Link);
        };
        Model.Toast += ShowToast;
        Model.Sound += name => { if (Model.Settings.Sounds && name.Length > 0) Sounds.Play(name); };

        // A new song while the island is closed: a short peek (not in the first seconds after launch).
        var launched = DateTime.UtcNow;
        Media.TrackChanged += np =>
        {
            if (DateTime.UtcNow - launched < TimeSpan.FromSeconds(4) || Presentation == Presentation.Expanded) return;
            ShowPeek(np.Title, np.Artist, room: Room.Purr);
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

        HotKeyRegistered = Win32.RegisterHotKey(_hwnd, HotKeyId, Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, 0x4E /* N */);

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
        else if (msg == Win32.WM_MOUSEACTIVATE && Presentation == Presentation.Expanded)
        {
            // A click into the open island activates it, so typing goes to its fields, not the app behind.
            RememberForeground();
            SetNoActivate(false);
            handled = true;
            return Win32.MA_ACTIVATE;
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

    private int _ticks;

    private void SecondTick()
    {
        _ticks++;
        Model.Tick();
        if (DateTime.Now.Second == 0) Clock.Text = Lang.Time(DateTime.Now);
        if (Presentation == Presentation.Expanded) CurrentPane?.SecondTick();
        UpdateLive();
        if (_ticks % 60 == 0) Rally.Tick(DateTime.UtcNow);
        if (_ticks % 600 == 0) _ = Weather.Refresh();
    }

    /// <summary>Sources that read the outside world follow their settings (none of them in the self-test).</summary>
    private void ConfigureSources()
    {
        if (SelfTesting) return;
        var s = Model.Settings;
        Roar.SetEnabled(s.Roar);
        Pounce.Configure(s.PounceScreenshots, s.PounceDownloads);
        Prowl.SetEnabled(s.Prowl);
        Rally.Configure(s.Rally, s.RallyCalendarUrl);
        Weather.Configure(s.WeatherCity.Length > 0 ? s.WeatherLatitude : null, s.WeatherCity.Length > 0 ? s.WeatherLongitude : null);
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
        WeatherChip.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 13, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center });
        var temp = Ui.Number(T(w.Temperature), 12.5);
        temp.Margin = new Thickness(4, 0, 0, 0);
        WeatherChip.Children.Add(temp);
        var parts = new List<string> { city, L(WeatherClient.Word(w.Code)), $"{T(w.Low)}–{T(w.High)}" };
        if (w.RainChance is int rain) parts.Add(L("%d%% rain", rain));
        WeatherChip.ToolTip = string.Join(" · ", parts);
        WeatherChip.Visibility = Visibility.Visible;
        // The weather takes the clock's place (the taskbar keeps its own clock).
        Clock.Visibility = Visibility.Collapsed;
    }

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
        Presentation = Presentation.Expanded;
        _liveShown = false;
        Fade(LiveView, 0, 0.08);
        ShowRoom(Selected);
        Apply(ExpandedShape(Selected), animate: true, bouncy: true);
        Fade(PeekView, 0, 0.08);
        Fade(ExpandedView, 1, 0.22, delay: 0.08);
        Root.Opacity = 1;
        Root.IsHitTestVisible = true;
    }

    public void Collapse()
    {
        _peekTimer?.Stop();
        bool wasOpen = Presentation == Presentation.Expanded;
        Presentation = Presentation.Lip;
        if (wasOpen)
        {
            CurrentPane?.Shown(false);
            HideToast(immediately: true);
            ReleaseKeyboard();
        }
        Fade(ExpandedView, 0, 0.1);
        Fade(PeekView, 0, 0.1);
        if (MirrorOpen) SetMirror(false);
        _liveShown = IsLive;
        _liveKey = "";
        Apply(ClosedShape(), animate: true, bouncy: false);
        if (_liveShown)
        {
            UpdateLive();
            Fade(LiveView, 1, 0.22, delay: 0.15);
        }
        UpdateHiddenState();
    }

    public void Toggle()
    {
        if (Presentation == Presentation.Expanded) Collapse(); else Expand();
    }

    /// <summary>A short message under the top edge. While something is fullscreen only urgent ones show.
    /// Clicking it opens its room.</summary>
    public void ShowPeek(string title, string subtitle, double seconds = 3.4, bool urgent = false, Room? room = null, string? app = null, string? file = null, string? link = null)
    {
        if (FullscreenActive && Model.Settings.HideInFullscreen && !urgent) return;
        if (Presentation == Presentation.Expanded) return;
        PeekTitle.Text = title;
        PeekSubtitle.Text = subtitle;
        // An app's own news shows that app's icon; everything else, Ezkiel.
        PeekIcon.Source = app is not null && AppIcons.Icon(app) is { } icon ? icon
            : file is not null && Files.Thumbnail(file, 64) is { } thumb ? thumb : Rooms.PurrPane.Lion;
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
        _peekRoom = room;
        PeekView.Cursor = room is null ? null : Cursors.Hand;
        Presentation = Presentation.Peek;
        _liveShown = false;
        Fade(LiveView, 0, 0.08);
        Root.Opacity = 1;
        Root.IsHitTestVisible = true;
        Apply(file is null && link is null ? PeekShape : PeekWideShape, animate: true, bouncy: true);
        Fade(PeekView, 1, 0.2, delay: 0.1);
        _peekTimer?.Stop();
        _peekTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(urgent ? Math.Max(seconds, 6) : seconds) };
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
    public void EngageKeyboard()
    {
        if (_hwnd == 0 || IsActive) return;
        RememberForeground();
        SetNoActivate(false);
        var fg = Win32.GetForegroundWindow();
        if (Win32.SetForegroundWindow(_hwnd) && fg == _hwnd) return;
        // Windows only lets the foreground app hand over focus: borrow its input queue for a moment.
        uint mine = Win32.GetCurrentThreadId(), theirs = fg == 0 ? 0 : Win32.GetWindowThreadProcessId(fg, out _);
        bool attached = theirs != 0 && theirs != mine && Win32.AttachThreadInput(theirs, mine, true);
        Win32.BringWindowToTop(_hwnd);
        Win32.SetForegroundWindow(_hwnd);
        Activate();
        if (attached) Win32.AttachThreadInput(theirs, mine, false);
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
        if (e.Key == Key.Escape)
        {
            if (Keyboard.FocusedElement is TextBox && CurrentPane is ScratchPane) Keyboard.ClearFocus();
            else Collapse();
            e.Handled = true;
        }
        else if (e.Key == Key.Space && Keyboard.FocusedElement is not TextBox && Selected == Room.Den)
        {
            Model.FocusStartPause();
            e.Handled = true;
        }
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
            Presentation.Lip => !(FullscreenActive && Model.Settings.HideInFullscreen) && p.Y >= -2 && p.Y <= r.Height + 8 && p.X >= r.Left - 24 && p.X <= r.Right + 24,
            Presentation.Peek => p.Y <= r.Height + 4 && p.X >= r.Left - 6 && p.X <= r.Right + 6,
            _ => p.Y <= r.Height + 14 && p.X >= r.Left - 12 && p.X <= r.Right + 12,
        };
    }

    private void PointerTick()
    {
        if (IgnorePointer || _hwnd == 0 || !Win32.GetCursorPos(out var c) || !Win32.GetWindowRect(_hwnd, out var w)) return;
        var p = new Point((c.X - w.Left) / Screen.Scale, (c.Y - w.Top) / Screen.Scale);
        bool inside = InHotZone(p);
        PointerMoved(inside);

        // A click on the lip opens it even with hover-to-open off.
        if (Presentation == Presentation.Lip && inside && (Win32.GetAsyncKeyState(0x01) & 0x8000) != 0) Expand();
        // A click anywhere else closes the open island (clicks there go to other apps, so we look).
        // Not while a file is being dragged out of Stash or over the island.
        if (StashPane.DraggingOut || _dragInside) return;
        if (Presentation == Presentation.Expanded && !inside && (Win32.GetAsyncKeyState(0x01) & 0x8000) != 0) Collapse();
    }

    /// <summary>Hover state machine (also driven by the self-test).</summary>
    public void PointerMoved(bool inside)
    {
        var now = DateTime.UtcNow;
        if (inside)
        {
            _outsideSince = DateTime.MaxValue;
            if (_insideSince == DateTime.MaxValue) _insideSince = now;
            if (!Hovering)
            {
                Hovering = true;
                if (Presentation == Presentation.Lip) Apply(ClosedShape(), animate: true, bouncy: true);
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
                if (Presentation == Presentation.Lip) Apply(ClosedShape(), animate: true);
            }
            // While you type, the island waits (an idle field still lets go after 20 s outside).
            var grace = Typing ? TimeSpan.FromSeconds(20) : TimeSpan.FromMilliseconds(500);
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

    private void BuildTabs()
    {
        Tabs.Children.Clear();
        foreach (var info in RoomInfo.All)
        {
            var room = info.Room;
            var icon = new TextBlock
            {
                Text = info.Glyph,
                FontFamily = (FontFamily)FindResource("Icons"),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var pill = new Border
            {
                CornerRadius = new CornerRadius(14),
                Child = icon,
                Margin = new Thickness(1, 0, 1, 0),
                Cursor = Cursors.Hand,
                ToolTip = $"{info.Name} · {L(info.Subtitle)}",
                Background = Brushes.Transparent,
            };
            pill.MouseLeftButtonUp += (_, _) => Select(room);
            Tabs.Children.Add(pill);
        }
        Select(Selected);
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
            Room.Settings => new SettingsPane(Model) { WeatherSource = () => Weather },
            Room.Purr => new PurrPane(Model, Media, Lyrics),
            Room.Roar => new RoarPane(Model),
            Room.Stash => new StashPane(Model),
            _ => new ComingPane(Model, room),
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
        TabSubtitle.Text = L(info.Subtitle);
        for (int i = 0; i < Tabs.Children.Count; i++)
        {
            var pill = (Border)Tabs.Children[i];
            var icon = (TextBlock)pill.Child;
            var accent = RoomInfo.All[i].Accent.Deep;
            bool on = i == (int)room;
            pill.Background = on ? new SolidColorBrush(Color.FromArgb(0x29, accent.R, accent.G, accent.B)) : Brushes.Transparent;
            icon.Foreground = on ? new SolidColorBrush(accent) : (Brush)FindResource("Mist");
        }
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
            Clock.Text = Lang.Time(DateTime.Now);
            UpdateWeatherChip();
            if (Presentation == Presentation.Expanded) ShowRoom(Selected);
            return;
        }
        if (room == Room.Settings)
        {
            ConfigureSources();
            UpdateWeatherChip();
            // Settings feed Den, Oasis and the rest; the Settings room redraws itself.
            foreach (var r in _panes.Keys) if (r != Room.Settings) _stale.Add(r);
            if (Presentation == Presentation.Expanded && Selected != Room.Settings) ShowRoom(Selected);
            return;
        }
        if (!_panes.TryGetValue(room.Value, out var pane)) return;
        if (Presentation == Presentation.Expanded && Selected == room) pane.Refresh();
        else _stale.Add(room.Value);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Collapse();

    private void ShowMenu()
    {
        var menu = new ContextMenu();
        var open = new MenuItem { Header = Presentation == Presentation.Expanded ? L("Close") : L("Open") + "  (Ctrl+Alt+N)" };
        open.Click += (_, _) => Toggle();
        var settings = new MenuItem { Header = L("Settings…") };
        settings.Click += (_, _) =>
        {
            Select(Room.Settings);
            if (Presentation != Presentation.Expanded) Expand();
        };
        var quit = new MenuItem { Header = L("Quit Ezkiel Notch") };
        quit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(open);
        menu.Items.Add(settings);
        menu.Items.Add(new Separator());
        menu.Items.Add(quit);
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
