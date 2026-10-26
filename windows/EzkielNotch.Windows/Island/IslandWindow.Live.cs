using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using EzkielNotch.Core;
using EzkielNotch.Platform;
using EzkielNotch.Rooms;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Island;

/// <summary>
/// The closed island while something is live: the Mac's wings, as a small pill under the top edge.
/// Left: the album art while music plays, else Ezkiel, plus Prowl's dots. Right, the first that applies:
/// the Den countdown, a meeting soon, an AI agent, the equalizer, the unread count, charging, the water ring.
/// Nothing live: back to the thin lip.
/// </summary>
public partial class IslandWindow
{
    private static readonly Shape LiveShape = new(224, 28, 14, 7);
    private static readonly Shape LiveHoverShape = new(236, 31, 15, 8);

    public ProwlWatcher Prowl { get; } = new();
    public PowerWatcher Power { get; } = new();

    private bool _liveShown;
    private string _liveKey = "";
    private TextBlock? _liveTimer;

    /// <summary>Something worth showing while closed (Mac: hasLiveActivity, or unread with Roar on).</summary>
    public bool IsLive
    {
        get
        {
            if (FullscreenActive && Model.Settings.HideInFullscreen) return false;
            var s = Model.Settings;
            return Model.Focus.IsRunning || Media.Current?.IsPlaying == true || Power.Charging
                || Model.LiveAgents.Count > 0 || (s.Prowl && (Prowl.MicInUse || Prowl.CameraInUse))
                || MeetingSoon is not null || (s.Roar && Model.UnreadTotal > 0);
        }
    }

    private Shape ClosedShape() => IsLive ? (Hovering ? LiveHoverShape : LiveShape) : (Hovering ? LipHoverShape : LipShape);

    /// <summary>Called on every change that could start or end something live, and once a second.</summary>
    public void UpdateLive()
    {
        if (Presentation != Presentation.Lip) return;
        bool live = IsLive;
        if (live != _liveShown)
        {
            _liveShown = live;
            Apply(ClosedShape(), animate: true, bouncy: live);
            Fade(LiveView, live ? 1 : 0, live ? 0.22 : 0.1, delay: live ? 0.12 : 0);
            LiveView.IsHitTestVisible = false;
        }
        if (!live) return;
        var key = LiveKey();
        if (key != _liveKey)
        {
            _liveKey = key;
            BuildLive();
        }
        if (_liveTimer is not null) _liveTimer.Text = FocusClock.Format(Model.Focus.Remaining(DateTime.UtcNow));
    }

    /// <summary>What the pill shows, as text: it is rebuilt only when this changes.</summary>
    private string LiveKey()
    {
        var np = Media.Current;
        return string.Join("|", Model.Focus.IsRunning, Model.Focus.Mode, np?.IsPlaying == true ? np.TrackKey : "", Media.Artwork is not null,
            Prowl.MicInUse, Prowl.CameraInUse, Model.Settings.Prowl, MeetingSoon?.Title, MeetingMinutes, Model.LiveAgents.Count,
            Model.LiveAgents.FirstOrDefault()?.Kind, Model.UnreadTotal, Power.Charging, Power.Percent, Lang.IsIndonesian);
    }

    private void BuildLive()
    {
        LiveLeft.Children.Clear();
        LiveRight.Children.Clear();
        _liveTimer = null;
        var np = Media.Current;

        // Left: artwork while playing, else Ezkiel.
        if (np?.IsPlaying == true && Media.Artwork is { } art)
            LiveLeft.Children.Add(new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(4.5),
                Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill },
            });
        else
            LiveLeft.Children.Add(new Image { Source = PurrPane.Lion, Width = 19, Height = 19 });
        if (Model.Settings.Prowl)
        {
            // Green: a camera is on. Orange: a microphone is.
            if (Prowl.CameraInUse) LiveLeft.Children.Add(Dot(Color.FromRgb(0x30, 0xD1, 0x58), L("Camera in use")));
            if (Prowl.MicInUse) LiveLeft.Children.Add(Dot(Color.FromRgb(0xFF, 0x9F, 0x0A), L("Microphone in use")));
        }

        // Right: the first that applies.
        if (Model.Focus.IsRunning)
        {
            var accent = Model.Focus.Mode == FocusMode.Focus ? Accent.Den : Accent.Rest;
            _liveTimer = Ui.Number("", 12, accent.Vertical);
            LiveRight.Children.Add(_liveTimer);
        }
        else if (MeetingSoon is { } meeting)
        {
            var t = Ui.Number(L("in %dm", MeetingMinutes), 12, Accent.Rally.Vertical);
            t.ToolTip = meeting.Title;
            LiveRight.Children.Add(t);
        }
        else if (Model.LiveAgents.FirstOrDefault() is { } agent)
        {
            bool waiting = agent.Kind == ScoutKind.NeedsYou;
            var accent = waiting ? Accent.Den : Accent.Scout;
            LiveRight.Children.Add(Ui.Glyph(RoarPane.Glyph(agent), 11.5, accent.LightBrush));
            if (Model.LiveAgents.Count > 1)
            {
                var n = Ui.Number(Model.LiveAgents.Count.ToString(), 11, accent.LightBrush);
                n.Margin = new Thickness(3, 0, 0, 0);
                LiveRight.Children.Add(n);
            }
        }
        else if (np?.IsPlaying == true)
        {
            LiveRight.Children.Add(new EqualizerBars(true, (Media.ArtworkAccent ?? Accent.Purr).Deep, 12));
        }
        else if (Model.Settings.Roar && Model.UnreadTotal > 0)
        {
            var pill = RoarPane.CountPill(RoarPane.Short(Model.UnreadTotal), Accent.Roar);
            pill.Height = 17;
            pill.MinWidth = 19;
            pill.Padding = new Thickness(5, 0, 5, 0);
            pill.ToolTip = L("%d unread", Model.UnreadTotal) + " · " + string.Join(", ", Model.RoarBadges.Select(b => b.Name));
            LiveRight.Children.Add(pill);
        }
        else if (Power.Charging && Power.Percent is { } pct)
        {
            var teal = new SolidColorBrush(Color.FromRgb(0x40, 0xE0, 0xD0));
            LiveRight.Children.Add(Ui.Glyph("", 9, teal));
            var t = Ui.Number($"{pct}%", 11, teal);
            t.Margin = new Thickness(2, 0, 0, 0);
            LiveRight.Children.Add(t);
        }
        else
        {
            LiveRight.Children.Add(new ProgressRing { Width = 13, Height = 13, Thickness = 2.4, Accent = Accent.Oasis, Progress = Model.WaterProgress });
        }
    }

    private static Ellipse Dot(Color c, string tip) => new()
    {
        Width = 6,
        Height = 6,
        Fill = new SolidColorBrush(c),
        Margin = new Thickness(6, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        ToolTip = tip,
    };

    // MARK: Rally hook (filled in by the calendar)

    /// <summary>The next meeting starting within 5 minutes, when Rally is on.</summary>
    public Meeting? MeetingSoon => !Model.Settings.Rally || !Model.Settings.RallyCountdown ? null : Rally.Upcoming.FirstOrDefault(m => m.Start > DateTime.UtcNow && m.Start - DateTime.UtcNow <= TimeSpan.FromMinutes(5));
    private int MeetingMinutes => MeetingSoon is { } m ? Math.Max(1, (int)Math.Ceiling((m.Start - DateTime.UtcNow).TotalMinutes)) : 0;
}
