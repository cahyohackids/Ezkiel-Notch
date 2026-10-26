using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using EzkielNotch.Core;
using EzkielNotch.Island;
using EzkielNotch.Platform;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Rooms;

/// <summary>Purr: what's playing (any app in Windows' media controls), and Chorus (synced lyrics) beside it.</summary>
public sealed class PurrPane : Pane
{
    private readonly MediaWatcher _media;
    private readonly LyricsClient _lyrics;
    private ProgressBarView? _progress;
    private ChorusView? _chorus;
    private string? _shownKey;
    private bool? _shownPlaying;

    /// <summary>Raised when Chorus opens or closes (the island changes height).</summary>
    public event Action? HeightChanged;

    public PurrPane(NotchModel model, MediaWatcher media, LyricsClient lyrics) : base(model, Room.Purr)
    {
        _media = media;
        _lyrics = lyrics;
        Height = DesiredHeight;
        _media.Changed += () =>
        {
            if (!IsVisible) return;
            var np = _media.Current;
            // A new song or play/pause redraws; position ticks only move the bar.
            if (np?.TrackKey != _shownKey || np?.IsPlaying != _shownPlaying) Refresh();
            else _progress?.Update();
            if (Model.Settings.ShowLyrics) _lyrics.Load(np?.Song);
        };
        _lyrics.Changed += () => Dispatcher.BeginInvoke(() => _chorus?.SetState(_lyrics.State));
    }

    /// <summary>The Mac's paneHeight(.purr, lyrics:): 122, or 170 with Chorus.</summary>
    public override double DesiredHeight => _media?.Current is not null && Model.Settings.ShowLyrics ? 170 : 122;

    private Accent Accent => _media.ArtworkAccent ?? Accent.Purr;

    public override void Refresh()
    {
        Children.Clear();
        Height = DesiredHeight;
        var np = _media.Current;
        _shownKey = np?.TrackKey;
        _shownPlaying = np?.IsPlaying;
        _progress = null;
        _chorus = null;
        if (np is null)
        {
            Children.Add(EmptyState());
            return;
        }
        if (Model.Settings.ShowLyrics)
        {
            _lyrics.Load(np.Song);
            Children.Add(Split(np));
        }
        else Children.Add(Player(np));
    }

    public override void SecondTick() => _progress?.Update();

    // MARK: Layouts

    private Grid Player(NowPlaying np)
    {
        var g = Columns(Px(96), Px(16), Star);
        g.Children.Add(At(Artwork(np, 96), 0));
        var right = new StackPanel();
        right.Children.Add(TitleBlock(np, Ui.Title));
        _progress = new ProgressBarView(_media, Accent) { Margin = new Thickness(0, 6, 0, 4) };
        right.Children.Add(_progress);
        var row = Columns(Auto, Star, Auto);
        row.Children.Add(At(Transport(np, 34), 0));
        var tools = Ui.Row(2, LyricsButton(), OpenButton(np));
        tools.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(At(tools, 2));
        right.Children.Add(row);
        g.Children.Add(At(right, 2));
        return g;
    }

    private Grid Split(NowPlaying np)
    {
        var g = Columns(Px(176), Px(12), Star);
        var left = new StackPanel();
        var head = Columns(Px(48), Px(9), Star);
        head.Children.Add(At(Artwork(np, 48), 0));
        head.Children.Add(At(TitleBlock(np, Ui.Body), 2));
        left.Children.Add(head);
        _progress = new ProgressBarView(_media, Accent) { Margin = new Thickness(0, 10, 0, 6) };
        left.Children.Add(_progress);
        var row = Columns(Auto, Star, Auto);
        row.Children.Add(At(Transport(np, 30), 0));
        row.Children.Add(At(LyricsButton(), 2));
        left.Children.Add(row);
        g.Children.Add(At(left, 0));

        _chorus = new ChorusView(_media, Accent, () => _lyrics.Retry(_media.Current?.Song));
        _chorus.SetState(_lyrics.State);
        g.Children.Add(At(Ui.Card(_chorus, 12), 2));
        return g;
    }

    // MARK: Pieces

    private StackPanel TitleBlock(NowPlaying np, double titleSize)
    {
        var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var player = Ui.Row(5, Ui.Label(np.PlayerName, Ui.Caption, Ui.Mist, FontWeights.Medium), new EqualizerBars(np.IsPlaying, Accent.Deep, 8));
        s.Children.Add(player);
        s.Children.Add(Ui.Label(np.Title, titleSize, weight: FontWeights.SemiBold));
        var sub = string.Join(" · ", new[] { np.Artist, np.Album }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (sub.Length > 0) s.Children.Add(Ui.Label(sub, Ui.Secondary, Ui.Mist));
        return s;
    }

    private FrameworkElement Artwork(NowPlaying np, double size)
    {
        var accent = Accent;
        UIElement content = _media.Artwork is { } art
            ? new Image { Source = art, Stretch = Stretch.UniformToFill }
            : new Grid
            {
                Background = Ui.Frozen(new LinearGradientBrush(Color.FromArgb(0x99, accent.Light.R, accent.Light.G, accent.Light.B),
                                                               Color.FromArgb(0x59, accent.Deep.R, accent.Deep.G, accent.Deep.B), 45)),
                Children = { Ui.Glyph("", size * 0.27, Brushes.White) },
            };
        var radius = size >= 64 ? 10.0 : 8.0;
        var holder = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(radius),
            Background = Brushes.Black,
            Child = content,
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Top,
            // Media Player-style glow in the artwork's own colour.
            Effect = new DropShadowEffect { Color = accent.Deep, BlurRadius = 22, ShadowDepth = 4, Direction = 270, Opacity = np.IsPlaying ? 0.55 : 0.18 },
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(np.IsPlaying ? 1 : 0.94, np.IsPlaying ? 1 : 0.94),
        };
        content.SetValue(UIElement.ClipProperty, new RectangleGeometry(new Rect(0, 0, size, size), radius, radius));
        return holder;
    }

    private StackPanel Transport(NowPlaying np, double play)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        p.Children.Add(Ui.IconButton("", L("Previous track"), _media.Previous, 28));
        var playButton = new Border
        {
            Width = play,
            Height = play,
            CornerRadius = new CornerRadius(play / 2),
            Background = Accent.Vertical,
            Margin = new Thickness(4, 0, 4, 0),
            Child = Ui.Glyph(np.IsPlaying ? "" : "", play * 0.36, Ui.OnAccent),
        };
        p.Children.Add(Ui.Clickable(playButton, _media.PlayPause, tip: np.IsPlaying ? L("Pause") : L("Play"), pressScale: 0.86));
        p.Children.Add(Ui.IconButton("", L("Next track"), _media.Next, 28));
        return p;
    }

    private Border LyricsButton()
    {
        bool on = Model.Settings.ShowLyrics;
        return Ui.IconButton("", on ? L("Hide Chorus (lyrics)") : L("Show Chorus: synced lyrics from LRCLIB"), () =>
        {
            Model.Settings.ShowLyrics = !Model.Settings.ShowLyrics;
            Model.SaveSoon();
            if (!Model.Settings.ShowLyrics) _lyrics.Load(null);
            Refresh();
            HeightChanged?.Invoke();
        }, 26, on ? null : Ui.Mist, on ? Accent : null);
    }

    private Border OpenButton(NowPlaying np)
    {
        var uri = np.PlayerName switch { "Spotify" => "spotify:", "Media Player" => "mswindowsmusic:", _ => null };
        var b = Ui.IconButton("", L("Open %@", np.PlayerName), () => Open(uri), 26, Ui.Mist);
        if (uri is null) b.Visibility = Visibility.Collapsed;
        return b;
    }

    private static void Open(string? uri)
    {
        if (uri is null) return;
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch (Exception) { }
    }

    private Grid EmptyState()
    {
        var g = Columns(Auto, Px(14), Star);
        g.VerticalAlignment = VerticalAlignment.Center;
        g.HorizontalAlignment = HorizontalAlignment.Center;
        g.Children.Add(At(new Image { Source = Lion, Width = 44, Height = 44 }, 0));
        var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        s.Children.Add(Ui.Label(L("Nothing playing"), Ui.Body, weight: FontWeights.SemiBold));
        var hint = Ui.Label(L("Play something in Spotify, Media Player or a browser"), Ui.Secondary, Ui.Mist);
        hint.Margin = new Thickness(0, 1, 0, 6);
        s.Children.Add(hint);
        s.Children.Add(Ui.Row(6,
            Ui.Pill("Spotify", () => Open("spotify:"), new Accent(Accent.Hex(0x7CF0A6), Accent.Hex(0x1DB954)), prominent: true),
            Ui.Pill("Media Player", () => Open("mswindowsmusic:"), Accent.Neutral),
            Ui.IconButton("", L("Refresh"), _media.Refresh, 26, Ui.Mist)));
        g.Children.Add(At(s, 2));
        return g;
    }

    internal static readonly System.Windows.Media.Imaging.BitmapImage Lion = new(new Uri("pack://application:,,,/Assets/lion.png"));
}

/// <summary>The bar with elapsed and remaining time. Drag (or click) to seek.</summary>
internal sealed class ProgressBarView : StackPanel
{
    private readonly MediaWatcher _media;
    private readonly Border _fill, _knob;
    private readonly Grid _track;
    private readonly TextBlock _elapsed, _left;
    private double? _scrub;

    public ProgressBarView(MediaWatcher media, Accent accent)
    {
        _media = media;
        _track = new Grid { Height = 12, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        _track.Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Ui.Frozen(new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF))) });
        _fill = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Ui.Frozen(new LinearGradientBrush(accent.Light, accent.Deep, 0)),
        };
        _knob = new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(5.5), Background = Ui.Ink, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Hidden };
        _track.Children.Add(_fill);
        _track.Children.Add(_knob);
        Children.Add(_track);
        var times = new Grid { Margin = new Thickness(0, 1, 0, 0) };
        _elapsed = Ui.Number("0:00", Ui.Caption, Ui.Mist);
        _elapsed.FontWeight = FontWeights.Medium;
        _left = Ui.Number("", Ui.Caption, Ui.Mist);
        _left.FontWeight = FontWeights.Medium;
        _left.HorizontalAlignment = HorizontalAlignment.Right;
        times.Children.Add(_elapsed);
        times.Children.Add(_left);
        Children.Add(times);

        _track.MouseLeftButtonDown += (_, e) => { _track.CaptureMouse(); Scrub(e); e.Handled = true; };
        _track.MouseMove += (_, e) => { if (_track.IsMouseCaptured) Scrub(e); };
        _track.MouseLeftButtonUp += (_, e) =>
        {
            if (!_track.IsMouseCaptured) return;
            _track.ReleaseMouseCapture();
            if (_scrub is { } s) _media.Seek(s);
            _scrub = null;
            Update();
            e.Handled = true;
        };
        SizeChanged += (_, _) => Update();
        Update();
    }

    private void Scrub(MouseEventArgs e)
    {
        var np = _media.Current;
        if (np is null || np.Duration <= 0 || _track.ActualWidth <= 0) return;
        _scrub = Math.Clamp(e.GetPosition(_track).X / _track.ActualWidth, 0, 1) * np.Duration;
        Update();
    }

    public void Update()
    {
        var np = _media.Current;
        if (np is null) return;
        double position = _scrub ?? np.Position(DateTime.UtcNow);
        double fraction = np.Duration > 0 ? Math.Min(1, position / np.Duration) : 0;
        double w = _track.ActualWidth;
        _fill.Width = Math.Max(4, w * fraction);
        _knob.Visibility = _scrub is null ? Visibility.Hidden : Visibility.Visible;
        _knob.Margin = new Thickness(Math.Max(0, w * fraction - 5.5), 0, 0, 0);
        _elapsed.Text = Time(position);
        // Live streams report no length: no countdown then.
        _left.Text = np.Duration > 0 ? "-" + Time(Math.Max(0, np.Duration - position)) : "";
    }

    private static string Time(double seconds)
    {
        int s = double.IsFinite(seconds) ? (int)seconds : 0;
        return $"{s / 60}:{s % 60:00}";
    }
}

/// <summary>Three small bars that bounce while music plays.</summary>
internal sealed class EqualizerBars : StackPanel
{
    private readonly Rectangle[] _bars = new Rectangle[3];
    private readonly System.Windows.Threading.DispatcherTimer? _timer;
    private readonly Random _random = new();

    public EqualizerBars(bool playing, Color color, double height)
    {
        Orientation = Orientation.Horizontal;
        Height = height;
        VerticalAlignment = VerticalAlignment.Center;
        for (int i = 0; i < 3; i++)
        {
            _bars[i] = new Rectangle { Width = 2, Height = height * 0.4, RadiusX = 1, RadiusY = 1, Fill = new SolidColorBrush(color), Margin = new Thickness(0, 0, 1.5, 0), VerticalAlignment = VerticalAlignment.Bottom };
            Children.Add(_bars[i]);
        }
        if (!playing || Island.SpringEase.ReduceMotion) return;
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _timer.Tick += (_, _) =>
        {
            if (!IsVisible) return;
            foreach (var b in _bars) b.Height = height * (0.3 + _random.NextDouble() * 0.7);
        };
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }
}

/// <summary>Chorus: synced lines light up as they're sung and the list glides to keep the current one centred;
/// click a line to jump there. Plain lyrics scroll; other states say what happened.</summary>
internal sealed class ChorusView : Grid
{
    private readonly MediaWatcher _media;
    private readonly Accent _accent;
    private readonly Action _retry;
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private ScrollViewer? _scroll;
    private StackPanel? _list;
    private Border? _topPad, _bottomPad;
    private IReadOnlyList<LyricLine>? _lines;
    private int? _current = -1;
    private double _from, _to;
    private DateTime _glideStart;
    private bool _gliding;

    public ChorusView(MediaWatcher media, Accent accent, Action retry)
    {
        _media = media;
        _accent = accent;
        _retry = retry;
        ClipToBounds = true;
        _timer.Tick += (_, _) => Follow();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => { _timer.Stop(); StopGlide(); };
        SizeChanged += (_, _) => { Pad(); Follow(force: true); };
    }

    public void SetState(LyricsState state)
    {
        Children.Clear();
        _lines = null;
        _scroll = null;
        _current = -1;
        switch (state.Kind)
        {
            case LyricsKind.Synced:
                _lines = state.Lines;
                _list = new StackPanel { Margin = new Thickness(12, 0, 12, 0) };
                _topPad = new Border();
                _bottomPad = new Border();
                _list.Children.Add(_topPad);
                for (int i = 0; i < _lines!.Count; i++)
                {
                    var line = _lines[i];
                    var t = Ui.Label(line.Text.Length == 0 ? "♪" : line.Text, Ui.Body, Ui.Mist, FontWeights.Normal, trim: false);
                    t.TextWrapping = TextWrapping.Wrap;
                    t.Margin = new Thickness(0, 4, 0, 4);
                    _list.Children.Add(Ui.Clickable(new Border { Child = t, Background = Brushes.Transparent }, () => _media.Seek(line.Time), pressScale: 0.98));
                }
                _list.Children.Add(_bottomPad);
                _scroll = Ui.Scroll(_list);
                Children.Add(_scroll);
                Pad();
                Dispatcher.BeginInvoke(() => Follow(force: true), System.Windows.Threading.DispatcherPriority.Loaded);
                break;
            case LyricsKind.Plain:
                var s = new StackPanel { Margin = new Thickness(12) };
                s.Children.Add(Ui.Label(L("Not synced"), Ui.Caption, Ui.Faint, FontWeights.Medium));
                var text = Ui.Label(state.Text ?? "", Ui.Body, Ui.Ink, trim: false);
                text.TextWrapping = TextWrapping.Wrap;
                text.Opacity = 0.85;
                text.Margin = new Thickness(0, 6, 0, 0);
                s.Children.Add(text);
                Children.Add(Ui.Scroll(s));
                break;
            case LyricsKind.Loading:
                Children.Add(Message("", L("Finding lyrics…"), null, retry: false));
                break;
            case LyricsKind.Instrumental:
                Children.Add(Message("", L("Instrumental"), L("Just the music"), retry: false));
                break;
            case LyricsKind.NotFound:
                Children.Add(Message("", L("No lyrics for this song"), L("LRCLIB doesn't have it yet"), retry: true));
                break;
            case LyricsKind.Failed:
                Children.Add(Message("", L("Couldn't load lyrics"), L("Check your connection"), retry: true));
                break;
        }
    }

    private StackPanel Message(string glyph, string title, string? subtitle, bool retry)
    {
        var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        s.Children.Add(Ui.Glyph(glyph, 17, _accent.DeepBrush));
        var t = Ui.Label(title, Ui.Body, weight: FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 4, 0, 0);
        s.Children.Add(t);
        if (subtitle is not null)
        {
            var sub = Ui.Label(subtitle, Ui.Caption, Ui.Mist);
            sub.HorizontalAlignment = HorizontalAlignment.Center;
            s.Children.Add(sub);
        }
        if (retry)
        {
            var b = Ui.TextButton(L("Try again"), _retry, _accent.LightBrush);
            b.HorizontalAlignment = HorizontalAlignment.Center;
            s.Children.Add(b);
        }
        return s;
    }

    /// <summary>Half the viewport above and below: the first and last lines can sit in the middle too.</summary>
    private void Pad()
    {
        if (_topPad is null || _bottomPad is null) return;
        double h = Math.Max(0, ActualHeight / 2 - 12);
        _topPad.Height = h;
        _bottomPad.Height = h;
    }

    private void Follow(bool force = false)
    {
        if (_lines is null || _list is null || _scroll is null || _media.Current is not { } np) return;
        var index = Lrc.Index(np.Position(DateTime.UtcNow) + 0.15, _lines);
        if (index == _current && !force) return;
        _current = index;
        for (int i = 0; i < _lines.Count; i++)
        {
            var t = (TextBlock)((Border)_list.Children[i + 1]).Child;
            bool now = i == index;
            t.Foreground = now ? Ui.Ink : i < (index ?? 0) ? Ui.Faint : Ui.Mist;
            t.FontWeight = now ? FontWeights.SemiBold : FontWeights.Normal;
            t.FontSize = now ? Ui.Body + 1.5 : Ui.Body;
        }
        _list.UpdateLayout();
        var target = (FrameworkElement)_list.Children[(index ?? 0) + 1];
        double y = target.TranslatePoint(new Point(0, 0), _list).Y + target.ActualHeight / 2 - _scroll.ViewportHeight / 2;
        GlideTo(Math.Max(0, y), animate: !force);
    }

    private void GlideTo(double y, bool animate)
    {
        if (_scroll is null) return;
        if (!animate || Island.SpringEase.ReduceMotion)
        {
            StopGlide();
            _scroll.ScrollToVerticalOffset(y);
            return;
        }
        _from = _scroll.VerticalOffset;
        _to = y;
        _glideStart = DateTime.UtcNow;
        if (!_gliding)
        {
            _gliding = true;
            CompositionTarget.Rendering += OnGlide;
        }
    }

    private void OnGlide(object? sender, EventArgs e)
    {
        if (_scroll is null) { StopGlide(); return; }
        double t = Math.Min(1, (DateTime.UtcNow - _glideStart).TotalMilliseconds / 420);
        double eased = 1 - Math.Pow(1 - t, 3);
        _scroll.ScrollToVerticalOffset(_from + (_to - _from) * eased);
        if (t >= 1) StopGlide();
    }

    private void StopGlide()
    {
        if (_gliding) CompositionTarget.Rendering -= OnGlide;
        _gliding = false;
    }
}
