using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EzkielNotch.Core;
using Windows.Media.Control;

namespace EzkielNotch.Platform;

/// <summary>What's playing, from any app that reports to Windows' media controls (Spotify, Media Player,
/// browsers, …). Position is interpolated from the last report, like the Mac's NowPlaying.</summary>
public sealed record NowPlaying(string AppId, string Title, string Artist, string Album, double Duration,
                                double ReportedPosition, DateTime ReportedAt, bool IsPlaying)
{
    public double Position(DateTime nowUtc)
    {
        double p = IsPlaying ? ReportedPosition + (nowUtc - ReportedAt).TotalSeconds : ReportedPosition;
        return Duration > 0 ? Math.Clamp(p, 0, Duration) : Math.Max(0, p);
    }

    public string TrackKey => $"{AppId}\u0001{Artist}\u0001{Title}";
    public SongInfo Song => new(Title, Artist, Album, Duration);

    /// <summary>"Spotify", "Media Player", "Chrome"… from the app's id.</summary>
    public string PlayerName
    {
        get
        {
            var id = AppId.ToLowerInvariant();
            if (id.Contains("spotify")) return "Spotify";
            if (id.Contains("zunemusic") || id.Contains("mediaplayer")) return "Media Player";
            if (id.Contains("applemusic") || id.Contains("itunes")) return "Apple Music";
            if (id.Contains("chrome")) return "Chrome";
            if (id.Contains("msedge")) return "Edge";
            if (id.Contains("firefox")) return "Firefox";
            if (id.Contains("vlc")) return "VLC";
            var name = AppId.Split('!', '\\', '/').Last();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            int us = name.IndexOf('_');
            if (us > 0) name = name[..us];
            int dot = name.LastIndexOf('.');
            if (dot >= 0 && dot < name.Length - 1) name = name[(dot + 1)..];
            return name.Length > 0 ? char.ToUpper(name[0]) + name[1..] : "Media";
        }
    }
}

/// <summary>Watches Windows' system media sessions (the same source as the volume flyout's player).
/// No permission is needed. Events arrive on WinRT threads and are passed to the UI thread.</summary>
public sealed class MediaWatcher
{
    public NowPlaying? Current { get; private set; }
    public BitmapSource? Artwork { get; private set; }
    /// <summary>The artwork's average colour as an accent, or null (then Purr's own pink).</summary>
    public Island.Accent? ArtworkAccent { get; private set; }
    public event Action? Changed;
    public event Action<NowPlaying>? TrackChanged;
    public bool Available { get; private set; }

    private readonly Dispatcher _ui;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string? _artKey;
    private bool _simulated;

    public MediaWatcher(Dispatcher ui) => _ui = ui;

    public async void Start()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            Available = true;
            _manager.CurrentSessionChanged += (_, _) => _ui.BeginInvoke(Attach);
            _manager.SessionsChanged += (_, _) => _ui.BeginInvoke(Attach);
            Attach();
        }
        catch (Exception)
        {
            Available = false;   // Windows N without Media Feature Pack, or an old build
        }
    }

    private void Attach()
    {
        if (_simulated || _manager is null) return;
        var s = _manager.GetCurrentSession();
        if (!ReferenceEquals(s, _session))
        {
            if (_session is not null)
            {
                _session.MediaPropertiesChanged -= OnMedia;
                _session.PlaybackInfoChanged -= OnPlayback;
                _session.TimelinePropertiesChanged -= OnTimeline;
            }
            _session = s;
            if (_session is not null)
            {
                _session.MediaPropertiesChanged += OnMedia;
                _session.PlaybackInfoChanged += OnPlayback;
                _session.TimelinePropertiesChanged += OnTimeline;
            }
        }
        Refresh();
    }

    private void OnMedia(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ui.BeginInvoke(Refresh);
    private void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => _ui.BeginInvoke(Refresh);
    private void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => _ui.BeginInvoke(Refresh);

    public async void Refresh()
    {
        if (_simulated) return;
        var s = _session;
        if (s is null)
        {
            Set(null);
            return;
        }
        try
        {
            var props = await s.TryGetMediaPropertiesAsync();
            var playback = s.GetPlaybackInfo();
            var timeline = s.GetTimelineProperties();
            if (!ReferenceEquals(s, _session)) return;
            if (props is null || string.IsNullOrWhiteSpace(props.Title))
            {
                Set(null);
                return;
            }
            double duration = (timeline.EndTime - timeline.StartTime).TotalSeconds;
            double position = (timeline.Position - timeline.StartTime).TotalSeconds;
            var np = new NowPlaying(s.SourceAppUserModelId ?? "", props.Title, props.Artist ?? props.AlbumArtist ?? "",
                                    props.AlbumTitle ?? "", Math.Max(0, duration), Math.Max(0, position),
                                    timeline.LastUpdatedTime.UtcDateTime is var at && at.Year > 2000 ? at : DateTime.UtcNow,
                                    playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
            // Some players never update LastUpdatedTime while paused; a paused report is its own anchor.
            if (!np.IsPlaying) np = np with { ReportedAt = DateTime.UtcNow };
            Set(np);
            var key = np.TrackKey;
            if (key != _artKey)
            {
                _artKey = key;
                Artwork = null;
                ArtworkAccent = null;
                if (props.Thumbnail is { } thumb)
                {
                    using var stream = (await thumb.OpenReadAsync()).AsStream();
                    var ms = new MemoryStream();
                    await stream.CopyToAsync(ms);
                    ms.Position = 0;
                    if (key == _artKey) SetArtwork(ms);
                }
                Changed?.Invoke();
            }
        }
        catch (Exception)
        {
            // The player went away mid-read; the next event refreshes.
        }
    }

    private void SetArtwork(Stream data)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = data;
            bmp.DecodePixelWidth = 192;
            bmp.EndInit();
            bmp.Freeze();
            Artwork = bmp;
            ArtworkAccent = AccentOf(bmp);
        }
        catch (Exception)
        {
            Artwork = null;
        }
    }

    private void Set(NowPlaying? np)
    {
        var old = Current;
        Current = np;
        if (np is not null && np.IsPlaying && (old is null || old.TrackKey != np.TrackKey)) TrackChanged?.Invoke(np);
        Changed?.Invoke();
    }

    // MARK: Controls

    public async void PlayPause()
    {
        if (_simulated && Current is { } c) { Set(c with { IsPlaying = !c.IsPlaying, ReportedPosition = c.Position(DateTime.UtcNow), ReportedAt = DateTime.UtcNow }); return; }
        try { if (_session is not null) await _session.TryTogglePlayPauseAsync(); } catch (Exception) { }
    }

    public async void Next()
    {
        try { if (_session is not null) await _session.TrySkipNextAsync(); } catch (Exception) { }
    }

    public async void Previous()
    {
        try { if (_session is not null) await _session.TrySkipPreviousAsync(); } catch (Exception) { }
    }

    public async void Seek(double seconds)
    {
        if (Current is { } c) Set(c with { ReportedPosition = seconds, ReportedAt = DateTime.UtcNow });
        try
        {
            if (_session is not null)
            {
                var start = _session.GetTimelineProperties().StartTime;
                await _session.TryChangePlaybackPositionAsync((start + TimeSpan.FromSeconds(seconds)).Ticks);
            }
        }
        catch (Exception) { }
    }

    /// <summary>Self-test and snapshots: pretend something is playing.</summary>
    public void Simulate(NowPlaying np, BitmapSource? art)
    {
        _simulated = true;
        Artwork = art;
        ArtworkAccent = art is null ? null : AccentOf(art);
        _artKey = np.TrackKey;
        Set(np);
    }

    /// <summary>An accent from the artwork's average colour: lighter on top, the colour below (the Mac's Accent.from).</summary>
    public static Island.Accent? AccentOf(BitmapSource art)
    {
        try
        {
            var small = new TransformedBitmap(new FormatConvertedBitmap(art, PixelFormats.Bgra32, null, 0),
                                              new ScaleTransform(8.0 / art.PixelWidth, 8.0 / art.PixelHeight));
            int w = small.PixelWidth, h = small.PixelHeight;
            var px = new byte[w * h * 4];
            small.CopyPixels(px, w * 4, 0);
            double r = 0, g = 0, b = 0;
            for (int i = 0; i < px.Length; i += 4) { b += px[i]; g += px[i + 1]; r += px[i + 2]; }
            int n = w * h;
            var (hue, sat, val) = ToHsv(r / n / 255, g / n / 255, b / n / 255);
            var deep = FromHsv(hue, Math.Clamp(sat, 0.45, 0.9), Math.Max(0.72, val));
            var light = FromHsv(hue, Math.Clamp(sat * 0.6, 0.25, 0.6), 1);
            return new Island.Accent(light, deep);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static (double H, double S, double V) ToHsv(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h = (h * 60 + 360) % 360;
        return (h, max == 0 ? 0 : d / max, max);
    }

    private static Color FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
