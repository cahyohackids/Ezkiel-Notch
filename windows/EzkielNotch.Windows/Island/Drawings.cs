using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace EzkielNotch.Island;

/// <summary>Den's ring: a track and an arc that shrinks as the session runs (Fitness-style, one hue).</summary>
public sealed class ProgressRing : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(ProgressRing), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The filled part, 0…1, clockwise from 12 o'clock.</summary>
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public double Thickness { get; set; } = 7;
    public Accent Accent { get; set; } = Accent.Den;

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight), r = (size - Thickness) / 2;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        dc.DrawEllipse(null, new Pen(Ui.Track, Thickness), c, r, r);
        double p = Math.Clamp(Progress, 0, 1);
        if (p <= 0.001) return;
        var pen = new Pen(new LinearGradientBrush(Accent.Light, Accent.Deep, 45), Thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        if (p >= 0.999)
        {
            dc.DrawEllipse(null, pen, c, r, r);
            return;
        }
        double a = p * 2 * Math.PI;
        var start = new Point(c.X, c.Y - r);
        var end = new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(r, r), 0, p > 0.5, SweepDirection.Clockwise, true, true);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }
}

/// <summary>Oasis's drop, filled with a gently waving water level (the Mac's WaterDrop).
/// The wave only moves while the level is between empty and full and the drop is on screen.</summary>
public sealed class WaterDrop : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(WaterDrop), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }

    private double _phase;
    private DateTime _last = DateTime.UtcNow;
    private bool _animating;

    public WaterDrop()
    {
        IsVisibleChanged += (_, _) => UpdateAnimation();
        Unloaded += (_, _) => Stop();
    }

    /// <summary>Animate the level (springy) from where it is now.</summary>
    public void SetLevel(double level, bool animate)
    {
        level = Math.Clamp(level, 0, 1);
        if (!animate || SpringEase.ReduceMotion) { BeginAnimation(LevelProperty, null); Level = level; }
        else BeginAnimation(LevelProperty, new DoubleAnimation(level, TimeSpan.FromMilliseconds(800)) { EasingFunction = SpringEase.Smooth });
        UpdateAnimation(level);
    }

    private void UpdateAnimation(double? target = null)
    {
        double l = target ?? Level;
        bool want = IsVisible && l > 0 && l < 1 && !SpringEase.ReduceMotion;
        if (want == _animating) return;
        _animating = want;
        if (want) CompositionTarget.Rendering += OnFrame; else CompositionTarget.Rendering -= OnFrame;
    }

    private void Stop()
    {
        if (_animating) CompositionTarget.Rendering -= OnFrame;
        _animating = false;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _last).TotalMilliseconds < 1000.0 / 24) return;   // 24 fps is plenty for a slow wave
        _phase += (now - _last).TotalSeconds * 2.2;
        _last = now;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var drop = DropShape(w, h);
        dc.DrawGeometry(Ui.Frozen(new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF))), null, drop);
        double level = Math.Clamp(Level, 0, 1);
        bool moving = level > 0 && level < 1;
        dc.PushClip(drop);
        dc.DrawGeometry(Accent.Oasis.Vertical, null, Wave(w, h, level, moving ? 3 : 0, _phase));
        dc.DrawGeometry(Ui.Frozen(new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF))), null, Wave(w, h, level, moving ? 2 : 0, _phase + 1.6));
        dc.Pop();
        dc.DrawGeometry(null, new Pen(Ui.Frozen(new SolidColorBrush(Color.FromArgb(0x59, Accent.Oasis.Light.R, Accent.Oasis.Light.G, Accent.Oasis.Light.B))), 1), drop);
    }

    private static Geometry DropShape(double w, double h)
    {
        double radius = Math.Min(w / 2, h * 0.42), cy = h - radius;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(w / 2, 0), true, true);
            c.BezierTo(new Point(w * 0.62, cy * 0.32), new Point(w / 2 + radius, cy * 0.62), new Point(w / 2 + radius, cy), true, true);
            c.ArcTo(new Point(w / 2 - radius, cy), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, true);
            c.BezierTo(new Point(w / 2 - radius, cy * 0.62), new Point(w * 0.38, cy * 0.32), new Point(w / 2, 0), true, true);
        }
        g.Freeze();
        return g;
    }

    private static Geometry Wave(double w, double h, double level, double amplitude, double phase)
    {
        double y0 = h * (1 - level * 0.98);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, h), true, true);
            c.LineTo(new Point(0, y0), true, false);
            const int steps = 20;
            for (int i = 0; i <= steps; i++)
            {
                double x = w * i / steps;
                c.LineTo(new Point(x, y0 + Math.Sin((double)i / steps * 2 * Math.PI * 1.3 + phase) * amplitude), true, false);
            }
            c.LineTo(new Point(w, h), true, false);
        }
        g.Freeze();
        return g;
    }
}

/// <summary>Seven capsules with one-letter weekdays under them; today is bold. Optional dashed goal line.</summary>
public sealed class WeekBars : Grid
{
    public WeekBars(IReadOnlyList<(DateTime Day, int Value)> week, Accent accent, double barHeight, double barWidth, int? goal = null, string unit = "")
    {
        double peak = Math.Max(1, Math.Max(goal ?? 0, week.Count == 0 ? 0 : week.Max(d => d.Value)));
        var bars = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = week.Count, VerticalAlignment = VerticalAlignment.Bottom };
        for (int i = 0; i < week.Count; i++)
        {
            var (day, value) = week[i];
            bool today = i == week.Count - 1;
            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, ToolTip = $"{value} {unit}".Trim() };
            col.Children.Add(new Border
            {
                Width = barWidth,
                Height = Math.Max(3, barHeight * value / peak),
                CornerRadius = new CornerRadius(barWidth / 2),
                Background = value > 0 ? accent.Vertical : Ui.Track,
                Opacity = today || value == 0 ? 1 : 0.6,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            var letter = Ui.Label(Core.Lang.NarrowWeekday(day), 8.5, today ? Ui.Ink : Ui.Faint, today ? FontWeights.SemiBold : FontWeights.Normal, trim: false);
            letter.HorizontalAlignment = HorizontalAlignment.Center;
            letter.Margin = new Thickness(0, 2, 0, 0);
            col.Children.Add(letter);
            bars.Children.Add(col);
        }
        if (goal is { } gl && gl > 0)
        {
            // Bars stand on the letters (8.5pt + 2): the goal line is measured from the same base.
            double y = barHeight * gl / peak;
            Children.Add(new System.Windows.Shapes.Line
            {
                X1 = 0,
                X2 = 1,
                Stretch = Stretch.Fill,
                StrokeThickness = 1,
                StrokeDashArray = [2, 3],
                Stroke = Ui.Frozen(new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF))),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 14 + y),
            });
        }
        Children.Add(bars);
    }
}
