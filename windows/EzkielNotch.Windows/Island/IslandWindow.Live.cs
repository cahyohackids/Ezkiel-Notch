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
    private Shape? _closedShape;
    private EzkielEyes? _eyes;
    private DateTime _happyUntil, _lastInteraction = DateTime.UtcNow;

    // MARK: Style (Notch or Island, the Mac's 1.6 choice)

    public bool IsIsland => Model.Settings.Style == "island";
    private double IslandHeight => Model.Settings.IslandSize switch { "small" => 30, "large" => 38, _ => 34 };
    private double IslandCenter => Model.Settings.IslandSize switch { "small" => 64, "large" => 128, _ => 96 };
    private double IslandExpandedWidth => Model.Settings.IslandSize switch { "small" => 400, "large" => 480, _ => 440 };
    private double IslandGap => Model.Settings.IslandGap switch { "tight" => 3, "roomy" => 10, _ => 6 };
    private bool ShowsFace => Model.Settings.ClosedFace != "off";
    /// <summary>The sparkly eyes bring their "ambient mode" with them.</summary>
    private bool Ambient => Model.Settings.ClosedFace == "eyes" && Model.Settings.EyeStyle == "anime";

    /// <summary>The sparkly eyes' ambient light: their pink, magenta and violet spill softly around the island and
    /// breathe, like a video's colours glowing around its player. Still under Reduce Motion.</summary>
    private static System.Windows.Media.Effects.DropShadowEffect AmbientGlow()
    {
        var glow = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 34, ShadowDepth = 0, Opacity = 0.9, Color = StyledEyes.AnimeMagenta, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance,
        };
        if (SpringEase.ReduceMotion) return glow;
        var breathe = new System.Windows.Media.Animation.DoubleAnimation(0.55, 1, TimeSpan.FromSeconds(3.4))
        { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever, EasingFunction = new System.Windows.Media.Animation.SineEase() };
        var drift = new System.Windows.Media.Animation.ColorAnimationUsingKeyFrames { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
        foreach (var (color, s) in new[] { (StyledEyes.AnimeMagenta, 0.0), (StyledEyes.AnimeViolet, 4.0), (StyledEyes.AnimePink, 8.0), (StyledEyes.AnimeMagenta, 12.0) })
            drift.KeyFrames.Add(new System.Windows.Media.Animation.EasingColorKeyFrame(color, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromSeconds(s)), new System.Windows.Media.Animation.SineEase()));
        glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, breathe);
        glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.ColorProperty, drift);
        return glow;
    }

    /// <summary>Island: a floating pill with a gap above and an optional shadow. Notch: hangs from the top edge.</summary>
    public void ApplyStyle(bool animate)
    {
        Island.Floating = IsIsland;
        Island.Gap = IsIsland ? IslandGap : 0;
        Island.Effect = Ambient ? AmbientGlow()
            : IsIsland && Model.Settings.IslandShadow
            ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.45 }
            : null;
        _eyes = null;
        _liveKey = "";
        _closedShape = null;
        switch (Presentation)
        {
            case Presentation.Expanded: Apply(ExpandedShape(Selected), animate); break;
            case Presentation.Peek: Apply(PeekShape, animate); break;
            default:
                if (!animate) Apply(ClosedShape(), false);
                UpdateLive();
                break;
        }
    }

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

    /// <summary>The closed shape: the island pill (wider while live), or in Notch style the live pill,
    /// a small tab for Ezkiel's face, or just the thin lip when the face is off.</summary>
    private Shape ClosedShape()
    {
        bool live = IsLive;
        if (IsIsland)
        {
            double w = IslandCenter + (live ? 88 : 0), h = IslandHeight;
            if (Hovering) { w += 12; h += 2; }
            return new(w, h, h / 2, 0);
        }
        if (live) return Hovering ? LiveHoverShape : LiveShape;
        if (ShowsFace) return Hovering ? new(116, 24, 10, 7) : new(104, 22, 9, 6);
        return Hovering ? LipHoverShape : LipShape;
    }

    /// <summary>Called on every change that could start or end something live, and once a second.</summary>
    public void UpdateLive()
    {
        if (Presentation != Presentation.Lip) return;
        var shape = ClosedShape();
        if (shape != _closedShape)
        {
            bool grows = _closedShape is null || shape.Width > _closedShape.Value.Width;
            _closedShape = shape;
            Apply(shape, animate: true, bouncy: grows);
        }
        bool show = IsLive || ShowsFace;
        if (show != _liveShown)
        {
            _liveShown = show;
            Fade(LiveView, show ? 1 : 0, show ? 0.22 : 0.1, delay: show ? 0.12 : 0);
            LiveView.IsHitTestVisible = false;
        }
        if (!show) return;
        var key = LiveKey();
        if (key != _liveKey)
        {
            _liveKey = key;
            BuildLive();
        }
        if (_liveTimer is not null) _liveTimer.Text = FocusClock.Format(Model.Focus.Remaining(DateTime.UtcNow));
        _eyes?.Set(EyesMood(), Hovering);
    }

    /// <summary>Ezkiel's mood, as on the Mac: an agent working or waiting first, then happy, napping, focusing.</summary>
    private EzkielEyes.Mood EyesMood()
    {
        if (Model.LiveAgents.FirstOrDefault() is { } agent) return agent.Kind == ScoutKind.NeedsYou ? EzkielEyes.Mood.Waiting : EzkielEyes.Mood.Working;
        var now = DateTime.UtcNow;
        if (now < _happyUntil) return EzkielEyes.Mood.Happy;
        if (Model.Focus.IsRunning && Model.Focus.Mode == FocusMode.Focus) return EzkielEyes.Mood.Focused;
        if (Model.Settings.MascotSleeps && !IsLive && !Hovering && now - _lastInteraction > TimeSpan.FromMinutes(2)) return EzkielEyes.Mood.Sleeping;
        return EzkielEyes.Mood.Awake;
    }

    /// <summary>A task done, a drink, a file stashed: Ezkiel smiles for a moment.</summary>
    public void MakeLionHappy() => _happyUntil = DateTime.UtcNow.AddSeconds(2.5);

    public void NoteInteraction() => _lastInteraction = DateTime.UtcNow;

    /// <summary>What the pill shows, as text: it is rebuilt only when this changes.</summary>
    private string LiveKey()
    {
        var np = Media.Current;
        return string.Join("|", IsLive, Model.Settings.ClosedFace, Model.Settings.EyeStyle, IsIsland, Model.Focus.IsRunning, Model.Focus.Mode,
            np?.IsPlaying == true ? np.TrackKey : "", Media.Artwork is not null,
            Prowl.MicInUse, Prowl.CameraInUse, Model.Settings.Prowl, MeetingSoon?.Title, MeetingMinutes, Model.LiveAgents.Count,
            Model.LiveAgents.FirstOrDefault()?.Kind, Model.UnreadTotal, Power.Charging, Power.Percent, Lang.IsIndonesian);
    }

    private void BuildLive()
    {
        LiveLeft.Children.Clear();
        LiveRight.Children.Clear();
        LiveCenter.Content = null;
        _liveTimer = null;
        var np = Media.Current;
        var face = Model.Settings.ClosedFace;
        bool live = IsLive;

        // Middle: Ezkiel's eyes (the closed island has no camera to make room for).
        if (face == "eyes")
        {
            _eyes ??= new EzkielEyes(IsIsland ? 10 : 9, Model.Settings.EyeStyle);
            _eyes.Style = Model.Settings.EyeStyle;
            LiveCenter.Content = _eyes;
        }
        else if (face == "lion" && !live)
            LiveCenter.Content = new Image { Source = PurrPane.Lion, Width = 18, Height = 18 };
        if (!live) return;

        // Left: artwork while playing, else the lion when that's the face.
        if (np?.IsPlaying == true && Media.Artwork is { } art)
            LiveLeft.Children.Add(new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(4.5),
                Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill },
            });
        else if (face == "lion")
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
        else if (face != "eyes" && Model.LiveAgents.FirstOrDefault() is { } agent)
        {
            // With the eyes on, they show the agent instead (they glance while it works and glow when it waits).
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
            LiveRight.Children.Add(Ui.Glyph("\uE945", 9, teal));
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
