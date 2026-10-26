using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace EzkielNotch.Island;

/// <summary>
/// Ezkiel's lion eyes for the closed island (a port of the Mac's EzkielEyes): amber irises with round pupils under
/// one brow ridge, drawn as a single face so they always move together. They blink now and then, open a little
/// wider on hover, close for a nap, squint when happy, narrow during focus, glance side to side while an AI agent
/// works and glow amber when it needs you. Every change is animated; between moves nothing redraws.
/// </summary>
public sealed class LionEyes : FrameworkElement
{
    public enum Mood { Awake, Happy, Sleeping, Focused, Working, Waiting }

    public static readonly DependencyProperty OpenProperty = Prop(nameof(Open), 1);
    public static readonly DependencyProperty LookProperty = Prop(nameof(Look), 0);
    public static readonly DependencyProperty GlowProperty = Prop(nameof(Glow), 0);
    public static readonly DependencyProperty SmileProperty = Prop(nameof(Smile), 0);
    /// <summary>The blink: multiplies the mood's own openness.</summary>
    public static readonly DependencyProperty LidProperty = Prop(nameof(Lid), 1);

    private static DependencyProperty Prop(string name, double value) => DependencyProperty.Register(
        name, typeof(double), typeof(LionEyes), new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Open { get => (double)GetValue(OpenProperty); set => SetValue(OpenProperty, value); }
    public double Look { get => (double)GetValue(LookProperty); set => SetValue(LookProperty, value); }
    public double Glow { get => (double)GetValue(GlowProperty); set => SetValue(GlowProperty, value); }
    public double Smile { get => (double)GetValue(SmileProperty); set => SetValue(SmileProperty, value); }
    public double Lid { get => (double)GetValue(LidProperty); set => SetValue(LidProperty, value); }

    private Mood _mood = Mood.Awake;
    private bool _hovering;
    private readonly DispatcherTimer _life = new();
    private readonly Random _random = new();
    private int _beats;

    private static readonly Color Gold = Color.FromRgb(0xF5, 0xB3, 0x3D);
    private static readonly Color Deep = Color.FromRgb(0xB8, 0x6B, 0x1A);
    private static readonly Color Rim = Color.FromRgb(0x6B, 0x3D, 0x0F);
    private static readonly Color Lining = Color.FromRgb(0x1F, 0x0F, 0x05);

    public LionEyes(double height = 10)
    {
        Width = height * 3.2;
        Height = height * 1.5;
        IsHitTestVisible = false;
        _life.Tick += (_, _) => Beat();
        Loaded += (_, _) => { Apply(); Schedule(); };
        Unloaded += (_, _) => _life.Stop();
    }

    public void Set(Mood mood, bool hovering)
    {
        if (mood == _mood && hovering == _hovering) return;
        bool moodChanged = mood != _mood;
        _mood = mood;
        _hovering = hovering;
        Apply();
        if (moodChanged)
        {
            Animate(LookProperty, 0, 500);
            Schedule();
        }
    }

    private double Openness => _mood switch
    {
        Mood.Sleeping => 0.04,
        Mood.Happy => 0.5,
        Mood.Focused => 0.72,
        Mood.Waiting => 1.08,
        _ => _hovering ? 1.08 : 1,
    };

    private void Apply()
    {
        Animate(OpenProperty, Openness, 420, new SpringEase { Decay = 7, Frequency = 7 });
        Animate(GlowProperty, _mood == Mood.Waiting ? 1 : 0, 420);
        Animate(SmileProperty, _mood == Mood.Happy ? 1 : 0, 420);
    }

    private void Schedule()
    {
        _life.Stop();
        if (_mood == Mood.Sleeping || SpringEase.ReduceMotion) return;
        _life.Interval = TimeSpan.FromSeconds(2.4 + _random.NextDouble() * 2.4);
        _life.Start();
    }

    /// <summary>Blinks (both eyes at once), and glances: slow side to side while an agent works, now and then otherwise.</summary>
    private void Beat()
    {
        if (!IsVisible) { Schedule(); return; }
        _beats++;
        if (_mood == Mood.Working) Animate(LookProperty, Look > 0 ? -0.75 : 0.75, 900);
        else if (_beats % 3 == 0) Animate(LookProperty, _random.NextDouble() - 0.5, 700);
        if (_mood != Mood.Happy)
        {
            Blink();
            if (_random.NextDouble() < 0.2)
            {
                var again = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(330) };
                again.Tick += (_, _) => { again.Stop(); Blink(); };
                again.Start();
            }
        }
        Schedule();
    }

    private void Blink()
    {
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0.06, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0.06, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(175))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(400)), new SpringEase { Decay = 8, Frequency = 7 }));
        BeginAnimation(LidProperty, a);
    }

    private void Animate(DependencyProperty p, double to, int ms, IEasingFunction? ease = null)
    {
        if (SpringEase.ReduceMotion)
        {
            BeginAnimation(p, null);
            SetValue(p, to);
            return;
        }
        BeginAnimation(p, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? new SineEase { EasingMode = EasingMode.EaseInOut } },
                       HandoffBehavior.SnapshotAndReplace);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double eyeW = w * 0.34, eyeH = h * 0.62, midY = h * 0.6;
        var centers = new[] { new Point(w * 0.27, midY), new Point(w * 0.73, midY) };
        double glow = Glow, smile = Smile, look = Look;
        double stroke = Math.Max(0.9, h * 0.06);

        // Brow ridge and nose bridge: one line that joins the eyes into a face.
        var brow = new StreamGeometry();
        using (var c = brow.Open())
        {
            c.BeginFigure(new Point(centers[0].X - eyeW * 0.62, midY - eyeH * 0.78), false, false);
            c.QuadraticBezierTo(new Point(centers[0].X + eyeW * 0.1, midY - eyeH * 1.0), new Point(w / 2, midY - eyeH * 0.35), true, true);
            c.QuadraticBezierTo(new Point(centers[1].X - eyeW * 0.1, midY - eyeH * 1.0), new Point(centers[1].X + eyeW * 0.62, midY - eyeH * 0.78), true, true);
        }
        var browBrush = new SolidColorBrush(Color.FromArgb((byte)(255 * (0.22 + 0.2 * glow)), Gold.R, Gold.G, Gold.B));
        dc.DrawGeometry(null, new Pen(browBrush, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, brow);
        var bridgeBrush = new LinearGradientBrush(browBrush.Color, Color.FromArgb(0, Gold.R, Gold.G, Gold.B), 90);
        dc.DrawLine(new Pen(bridgeBrush, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                    new Point(w / 2, midY - eyeH * 0.35), new Point(w / 2, midY + eyeH * 0.05));

        double o = Math.Clamp(Open * Lid, 0, 1.15);
        for (int i = 0; i < 2; i++)
        {
            var c = centers[i];
            double side = i == 0 ? -1 : 1;
            // Lion eyes slant: the outer corner sits a little higher than the inner one.
            var inner = new Point(c.X - side * eyeW / 2, c.Y + eyeH * 0.08);
            var outer = new Point(c.X + side * eyeW / 2, c.Y - eyeH * 0.1);
            var left = i == 0 ? outer : inner;
            var right = i == 0 ? inner : outer;
            double top = eyeH * 0.95 * o;
            double bottom = eyeH * (0.55 * o + 0.06) * (1 - smile * 0.9);

            if (o > 0.12)
            {
                var almond = new StreamGeometry();
                using (var g = almond.Open())
                {
                    g.BeginFigure(left, true, true);
                    g.QuadraticBezierTo(new Point(c.X, c.Y - top), right, true, true);
                    g.QuadraticBezierTo(new Point(c.X, c.Y + bottom - smile * eyeH * 0.5), left, true, true);
                }
                almond.Freeze();
                if (glow > 0.01)
                    dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(200 * glow), Gold.R, Gold.G, Gold.B)), h * 0.22), almond);
                dc.PushClip(almond);
                // Lions show almost no white: the whole eye is amber, darker at the rim.
                dc.DrawRectangle(new SolidColorBrush(Rim), null, new Rect(c.X - eyeW, c.Y - eyeH * 1.5, eyeW * 2, eyeH * 3));
                double irisR = eyeH * 0.74;
                var irisC = new Point(c.X + look * eyeW * 0.16, c.Y - eyeH * 0.05);
                var iris = new RadialGradientBrush(Gold, Deep) { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5) };
                dc.DrawEllipse(iris, null, irisC, irisR, irisR);
                double pupilR = irisR * 0.28;
                dc.DrawEllipse(Brushes.Black, null, irisC, pupilR, pupilR);
                double shine = irisR * 0.17;
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)), null,
                               new Point(irisC.X - irisR * 0.42 + shine, irisC.Y - irisR * 0.5 + shine), shine, shine);
                dc.Pop();
                // Dark lining, as lions have.
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(Lining), Math.Max(1, h * 0.09)), almond);
            }
            else
            {
                // Shut: just the lid line, curving down a little.
                var line = new StreamGeometry();
                using (var g = line.Open())
                {
                    g.BeginFigure(left, false, false);
                    g.QuadraticBezierTo(new Point(c.X, c.Y + eyeH * 0.25), right, true, true);
                }
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0xD9, Gold.R, Gold.G, Gold.B)), Math.Max(1, h * 0.08))
                    { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, line);
            }
        }
    }
}
