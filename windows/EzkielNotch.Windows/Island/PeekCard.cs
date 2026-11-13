using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using EzkielNotch.Core;

namespace EzkielNotch.Island;

/// <summary>
/// One peek, like the Mac's Peek: the words, where a click goes, and how it looks. The look (glyph and tint)
/// goes on the left; on the right, the first that applies: a battery ring, a big value ("5", "80%"), an
/// equalizer for a new song, or the glyph when Ezkiel takes the left.
/// </summary>
public sealed record PeekInfo(string Title, string Subtitle = "")
{
    public double Seconds { get; init; } = 3.4;
    /// <summary>Still shown while an app is fullscreen.</summary>
    public bool Urgent { get; init; }
    public Room? Room { get; init; }
    /// <summary>Roar and Scout: that app's own icon on the left.</summary>
    public string? App { get; init; }
    /// <summary>Pounce: the file's thumbnail and Copy · Stash · Show.</summary>
    public string? File { get; init; }
    /// <summary>Rally: the Join button.</summary>
    public string? Link { get; init; }
    /// <summary>Glyph, tint, Ezkiel or not, and the Mac sound. None: Ezkiel, as before.</summary>
    public PeekLook? Look { get; init; }
    /// <summary>A big number on the right, in the tint.</summary>
    public string? Value { get; init; }
    /// <summary>0…1: a battery ring on the right.</summary>
    public double? Progress { get; init; }
    /// <summary>The ring's light runs round while the battery charges.</summary>
    public bool Charging { get; init; }
    /// <summary>A new song: the album art on the left, an equalizer on the right.</summary>
    public bool Artwork { get; init; }
    /// <summary>A Mac sound name; by default the look's.</summary>
    public string? Sound { get; init; }
    /// <summary>A finished download: Open · Stash · Show (a screenshot gets Copy instead of Open).</summary>
    public bool OpensFile { get; init; }
    /// <summary>Buttons on the right (a finished quick timer's "+5 min" and "Stop"); each closes the peek.</summary>
    public IReadOnlyList<PeekButton>? Buttons { get; init; }

    public string Chime => Sound ?? Look?.Sound ?? "";
    public Color Tint => PeekTints.Of(Look?.Tint ?? PeekTint.Gold);

    /// <summary>A peek from the model (a task due, Roar, Scout…).</summary>
    public static PeekInfo From(PeekRequest p) => new(p.Title, p.Subtitle)
    {
        Urgent = p.Urgent,
        Room = p.Room,
        App = p.App,
        Link = p.Link,
        Value = p.Value,
        Look = p.Look,
        Sound = p.Sound,
    };
}

/// <summary>A peek's button: its words, what it does, its accent, and whether it's the filled one.</summary>
public sealed record PeekButton(string Title, Action Run, Accent Accent, bool Prominent = false);

/// <summary>Peek tints as colours: the rooms' own accents (Accent.*.Deep), and the Mac's gold, teal and berry.</summary>
public static class PeekTints
{
    public static Color Of(PeekTint t) => t switch
    {
        PeekTint.Den => Accent.Den.Deep,
        PeekTint.Hunt => Accent.Hunt.Deep,
        PeekTint.Rally => Accent.Rally.Deep,
        PeekTint.Oasis => Accent.Oasis.Deep,
        PeekTint.Roar => Accent.Roar.Deep,
        PeekTint.Scout => Accent.Scout.Deep,
        PeekTint.Stash => Accent.Stash.Deep,
        PeekTint.Purr => Accent.Purr.Deep,
        PeekTint.Neutral => Accent.Neutral.Deep,
        _ => Accent.Hex(PeekLooks.Rgb(t)),
    };

    public static Brush Brush(Color c, byte alpha = 0xFF) => Ui.Frozen(new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B)));

    /// <summary>The glyph on a soft square of its own tint (the Mac's tinted icon wing).</summary>
    public static Border GlyphTile(string glyph, Color tint, double size = 28) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size * 0.3),
        Background = Brush(tint, 0x2E),
        Child = Ui.Glyph(glyph, size * 0.5, Brush(tint)),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Album art, rounded like the Mac's (it fills the square).</summary>
    public static Border ArtworkTile(BitmapSource art, double size = 26) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size * 0.23),
        Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill },
        BorderBrush = Ui.Frozen(new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF))),
        BorderThickness = new Thickness(0.5),
        VerticalAlignment = VerticalAlignment.Center,
    };
}

/// <summary>
/// The Mac's ChargeRing: the battery level as a ring in the peek's tint, the number inside, and while it
/// charges a light that runs round (still under Reduce Motion). It fills in once when it appears. The spin
/// only runs while the ring is on screen: the peek drops it when it closes.
/// </summary>
public sealed class ChargeRing : Grid
{
    public double Level { get; }
    public bool Spinning { get; }
    private readonly RingArc _arc;
    private readonly RotateTransform? _turn;

    public ChargeRing(double level, Color tint, bool charging, double size)
    {
        Level = Math.Clamp(level, 0, 1);
        Width = size;
        Height = size;
        VerticalAlignment = VerticalAlignment.Center;
        double line = RingMath.LineWidth(size);
        bool still = SpringEase.ReduceMotion;
        _arc = new RingArc { Tint = tint, Line = line, Level = still ? Level : 0 };
        Children.Add(_arc);
        if (charging && !still)
        {
            _turn = new RotateTransform();
            Children.Add(new RingArc
            {
                Tint = Colors.White, Line = line, Level = 0.22, Highlight = true,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _turn, IsHitTestVisible = false,
            });
            Spinning = true;
        }
        var number = Ui.Number(RingMath.Label(Level), size * 0.34, Ui.Ink);
        number.FontWeight = FontWeights.Bold;
        number.HorizontalAlignment = HorizontalAlignment.Center;
        Children.Add(number);
        AutomationProperties.SetName(this, RingMath.Label(Level) + "%");
        Loaded += (_, _) =>
        {
            if (!still)
                _arc.BeginAnimation(RingArc.LevelProperty, new DoubleAnimation(0, Level, TimeSpan.FromSeconds(0.9)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            _turn?.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.4)) { RepeatBehavior = RepeatBehavior.Forever });
        };
        Unloaded += (_, _) => _turn?.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    /// <summary>A track and an arc from 12 o'clock, clockwise; the highlight is a short arc fading in.</summary>
    internal sealed class RingArc : FrameworkElement
    {
        public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
            nameof(Level), typeof(double), typeof(RingArc), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
        public Color Tint { get; init; }
        public double Line { get; init; } = 3;
        public bool Highlight { get; init; }

        protected override void OnRender(DrawingContext dc)
        {
            double size = Math.Min(ActualWidth, ActualHeight), r = RingMath.Radius(size, Line);
            var c = new Point(ActualWidth / 2, ActualHeight / 2);
            if (!Highlight) dc.DrawEllipse(null, new Pen(PeekTints.Brush(Tint, 0x2E), Line), c, r, r);
            double p = Math.Clamp(Level, 0, 1);
            if (p <= 0.001) return;
            var ((sx, sy), (ex, ey), large) = RingMath.Arc(p, c.X, c.Y, r);
            Brush brush = Highlight
                ? new LinearGradientBrush(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF), 0)
                { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(sx, sy), EndPoint = new Point(ex, ey) }
                : new LinearGradientBrush(Color.FromArgb(0xBF, Tint.R, Tint.G, Tint.B), Tint, 45);
            var pen = new Pen(brush, Line) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (p >= 0.999)
            {
                dc.DrawEllipse(null, pen, c, r, r);
                return;
            }
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(sx, sy), false, false);
                ctx.ArcTo(new Point(ex, ey), new Size(r, r), 0, large, SweepDirection.Clockwise, true, true);
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
    }
}
