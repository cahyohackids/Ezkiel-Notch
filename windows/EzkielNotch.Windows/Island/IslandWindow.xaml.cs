using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EzkielNotch.Native;
using Microsoft.Win32;

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
    private static readonly Shape ExpandedShape = new(440, 252, 22, 13);

    private static readonly (string Name, string Glyph, Color Accent)[] Rooms =
    [
        ("Den", "", Color.FromRgb(0xFF, 0x95, 0x00)),
        ("Hunt", "", Color.FromRgb(0xFF, 0x45, 0x3A)),
        ("Roar", "", Color.FromRgb(0x30, 0xD1, 0x58)),
        ("Scratch", "", Color.FromRgb(0xFF, 0xC4, 0x00)),
        ("Purr", "", Color.FromRgb(0xFF, 0x37, 0x5F)),
        ("Stash", "", Color.FromRgb(0x5E, 0x5C, 0xE6)),
        ("Oasis", "", Color.FromRgb(0x0A, 0x84, 0xFF)),
        ("Settings", "", Color.FromRgb(0x8E, 0x8E, 0x93)),
    ];

    public Presentation Presentation { get; private set; } = Presentation.Lip;
    public bool Hovering { get; private set; }
    public bool FullscreenActive { get; private set; }
    public bool HotKeyRegistered { get; private set; }
    /// <summary>Self-test: ignore the real pointer so a resting cursor can't change the outcome.</summary>
    public bool IgnorePointer { get; set; }

    internal ScreenSpot Screen { get; private set; }
    private nint _hwnd;
    private HwndSource? _source;
    private readonly DispatcherTimer _pointer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _clock = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _fullscreenPoll = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
    private DateTime _insideSince = DateTime.MaxValue, _outsideSince = DateTime.MaxValue;
    private DispatcherTimer? _peekTimer;
    private nint _winEventHook;
    private Win32.WinEventProc? _winEventProc;   // kept alive: the hook calls back into it
    private int _selected;
    private const int HotKeyId = 0xE21;

    public IslandWindow()
    {
        InitializeComponent();
        BuildTabs();
        Apply(LipShape, animate: false);
        _pointer.Tick += (_, _) => PointerTick();
        _clock.Tick += (_, _) => Clock.Text = DateTime.Now.ToString("HH:mm");
        _fullscreenPoll.Tick += (_, _) => CheckFullscreen();
        Clock.Text = DateTime.Now.ToString("HH:mm");
        Island.MouseRightButtonUp += (_, e) => { ShowMenu(); e.Handled = true; };
    }

    // MARK: Window setup

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source.AddHook(WndProc);

        // A tool window that never takes focus: no taskbar button, no Alt+Tab, clicks don't steal focus.
        var ex = Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE);

        HotKeyRegistered = Win32.RegisterHotKey(_hwnd, HotKeyId, Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, 0x4E /* N */);

        Place();
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(Place);
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);

        _winEventProc = (_, _, _, _, _, _, _) => Dispatcher.BeginInvoke(CheckFullscreen);
        _winEventHook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, 0, _winEventProc, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);

        _pointer.Start();
        _clock.Start();
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

    // MARK: Presentation

    public void Expand()
    {
        _peekTimer?.Stop();
        Presentation = Presentation.Expanded;
        Apply(ExpandedShape, animate: true, bouncy: true);
        Fade(PeekView, 0, 0.08);
        Fade(ExpandedView, 1, 0.22, delay: 0.08);
        Root.Opacity = 1;
    }

    public void Collapse()
    {
        _peekTimer?.Stop();
        Presentation = Presentation.Lip;
        Fade(ExpandedView, 0, 0.1);
        Fade(PeekView, 0, 0.1);
        Apply(Hovering ? LipHoverShape : LipShape, animate: true, bouncy: false);
        UpdateHiddenState();
    }

    public void Toggle()
    {
        if (Presentation == Presentation.Expanded) Collapse(); else Expand();
    }

    /// <summary>A short message under the top edge. While something is fullscreen only urgent ones show.</summary>
    public void ShowPeek(string title, string subtitle, double seconds = 3.4, bool urgent = false)
    {
        if (FullscreenActive && !urgent) return;
        if (Presentation == Presentation.Expanded) return;
        PeekTitle.Text = title;
        PeekSubtitle.Text = subtitle;
        Presentation = Presentation.Peek;
        Root.Opacity = 1;
        Apply(PeekShape, animate: true, bouncy: true);
        Fade(PeekView, 1, 0.2, delay: 0.1);
        _peekTimer?.Stop();
        _peekTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _peekTimer.Tick += (_, _) =>
        {
            // Hovering keeps it; leaving lets it go.
            if (Hovering) return;
            _peekTimer?.Stop();
            if (Presentation == Presentation.Peek) Collapse();
        };
        _peekTimer.Start();
    }

    private void Apply(Shape s, bool animate, bool bouncy = false)
    {
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
            Presentation.Lip => !FullscreenActive && p.Y >= -2 && p.Y <= r.Height + 8 && p.X >= r.Left - 24 && p.X <= r.Right + 24,
            Presentation.Peek => p.Y <= r.Height + 4 && p.X >= r.Left - 6 && p.X <= r.Right + 6,
            _ => p.Y <= r.Height + 14 && p.X >= r.Left - 12 && p.X <= r.Right + 12,
        };
    }

    private void PointerTick()
    {
        if (IgnorePointer || _hwnd == 0 || !Win32.GetCursorPos(out var c) || !Win32.GetWindowRect(_hwnd, out var w)) return;
        var p = new Point((c.X - w.Left) / Screen.Scale, (c.Y - w.Top) / Screen.Scale);
        PointerMoved(InHotZone(p));

        // A click anywhere else closes the open island (clicks there go to other apps, so we look).
        if (Presentation == Presentation.Expanded && !InHotZone(p) && (Win32.GetAsyncKeyState(0x01) & 0x8000) != 0) Collapse();
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
                if (Presentation == Presentation.Lip) Apply(LipHoverShape, animate: true, bouncy: true);
            }
            // Open after a short, deliberate rest, so passing by on the way to a tab doesn't open it.
            if (Presentation != Presentation.Expanded && now - _insideSince > TimeSpan.FromMilliseconds(250)) Expand();
        }
        else
        {
            _insideSince = DateTime.MaxValue;
            if (_outsideSince == DateTime.MaxValue) _outsideSince = now;
            if (Hovering)
            {
                Hovering = false;
                if (Presentation == Presentation.Lip) Apply(LipShape, animate: true);
            }
            if (Presentation == Presentation.Expanded && now - _outsideSince > TimeSpan.FromMilliseconds(500)) Collapse();
        }
    }

    // MARK: Fullscreen

    public void CheckFullscreen()
    {
        if (_hwnd == 0) return;
        var active = Fullscreen.Active(Screen, _hwnd);
        if (active == FullscreenActive) return;
        FullscreenActive = active;
        UpdateHiddenState();
    }

    /// <summary>Self-test: pretend something is fullscreen.</summary>
    public void SimulateFullscreen(bool active)
    {
        FullscreenActive = active;
        UpdateHiddenState();
    }

    /// <summary>Over a fullscreen game, video or slides the lip steps away entirely.</summary>
    private void UpdateHiddenState()
    {
        bool hide = FullscreenActive && Presentation == Presentation.Lip;
        var a = new DoubleAnimation(hide ? 0 : 1, TimeSpan.FromMilliseconds(180));
        Root.BeginAnimation(OpacityProperty, a);
        Root.IsHitTestVisible = !hide;
    }

    // MARK: Rooms (placeholders until Phase 1)

    private void BuildTabs()
    {
        for (int i = 0; i < Rooms.Length; i++)
        {
            int index = i;
            var (name, glyph, accent) = Rooms[i];
            var icon = new TextBlock
            {
                Text = glyph,
                FontFamily = (FontFamily)FindResource("Icons"),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var pill = new Border { CornerRadius = new CornerRadius(14), Child = icon, Margin = new Thickness(1, 0, 1, 0), Cursor = Cursors.Hand, ToolTip = name };
            pill.MouseLeftButtonUp += (_, _) => Select(index);
            Tabs.Children.Add(pill);
        }
        Select(0);
    }

    public void Select(int index)
    {
        _selected = index;
        TabTitle.Text = Rooms[index].Name;
        for (int i = 0; i < Tabs.Children.Count; i++)
        {
            var pill = (Border)Tabs.Children[i];
            var icon = (TextBlock)pill.Child;
            var accent = Rooms[i].Accent;
            bool on = i == index;
            pill.Background = on ? new SolidColorBrush(Color.FromArgb(0x29, accent.R, accent.G, accent.B)) : Brushes.Transparent;
            icon.Foreground = on ? new SolidColorBrush(accent) : (Brush)FindResource("Mist");
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Collapse();

    private void ShowMenu()
    {
        var menu = new ContextMenu();
        var open = new MenuItem { Header = Presentation == Presentation.Expanded ? "Close" : "Open  (Ctrl+Alt+N)" };
        open.Click += (_, _) => Toggle();
        var quit = new MenuItem { Header = "Quit Ezkiel Notch" };
        quit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(open);
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
