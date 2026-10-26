using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EzkielNotch.Island;

/// <summary>
/// The island: a black body hanging from the top edge, with rounded bottom corners and two concave
/// "ears" that melt into the screen edge (the same silhouette as the Mac notch). It draws itself,
/// centres horizontally in its bounds, and clips its child to the body. Every size is a dependency
/// property, so one animation moves shape and content together.
/// </summary>
public sealed class IslandSurface : Decorator
{
    public static readonly DependencyProperty BodyWidthProperty = Register(nameof(BodyWidth), 180);
    public static readonly DependencyProperty BodyHeightProperty = Register(nameof(BodyHeight), 6);
    public static readonly DependencyProperty BottomRadiusProperty = Register(nameof(BottomRadius), 3);
    public static readonly DependencyProperty EarProperty = Register(nameof(Ear), 4);

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(IslandSurface),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Register(string name, double value) => DependencyProperty.Register(
        name, typeof(double), typeof(IslandSurface),
        new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double BodyWidth { get => (double)GetValue(BodyWidthProperty); set => SetValue(BodyWidthProperty, value); }
    public double BodyHeight { get => (double)GetValue(BodyHeightProperty); set => SetValue(BodyHeightProperty, value); }
    public double BottomRadius { get => (double)GetValue(BottomRadiusProperty); set => SetValue(BottomRadiusProperty, value); }
    public double Ear { get => (double)GetValue(EarProperty); set => SetValue(EarProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    /// <summary>The outer rectangle of the island (ears included), in this element's coordinates.</summary>
    public Rect ShapeRect
    {
        get
        {
            double w = Math.Max(0, BodyWidth + Ear * 2);
            return new Rect((ActualWidth - w) / 2, 0, w, Math.Max(0, BodyHeight));
        }
    }

    /// <summary>The body without the ears: where content goes.</summary>
    public Rect BodyRect
    {
        get
        {
            var r = ShapeRect;
            return new Rect(r.X + Ear, 0, Math.Max(0, r.Width - Ear * 2), r.Height);
        }
    }

    public Geometry Shape() => Build(ShapeRect, BottomRadius, Ear);

    /// <summary>The silhouette, ported from the Mac's NotchShape.</summary>
    public static Geometry Build(Rect rect, double bottomRadius, double ear)
    {
        double e = Math.Max(0, Math.Min(ear, Math.Min(rect.Width / 4, rect.Height / 2)));
        double left = rect.Left + e, right = rect.Right - e;
        double r = Math.Max(0, Math.Min(bottomRadius, Math.Min((right - left) / 2, rect.Height - e)));
        double top = rect.Top, bottom = rect.Bottom;

        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(rect.Left, top), true, true);
            // left ear (concave)
            c.QuadraticBezierTo(new Point(left, top), new Point(left, top + e), true, true);
            c.LineTo(new Point(left, bottom - r), true, true);
            c.ArcTo(new Point(left + r, bottom), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, true);
            c.LineTo(new Point(right - r, bottom), true, true);
            c.ArcTo(new Point(right, bottom - r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, true);
            c.LineTo(new Point(right, top + e), true, true);
            // right ear (concave)
            c.QuadraticBezierTo(new Point(right, top), new Point(rect.Right, top), true, true);
        }
        g.Freeze();
        return g;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (BodyHeight <= 0.5 || BodyWidth <= 0.5) return;
        dc.DrawGeometry(Fill, null, Shape());
    }

    protected override Size MeasureOverride(Size constraint)
    {
        Child?.Measure(new Size(Math.Max(0, BodyWidth), Math.Max(0, BodyHeight)));
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is not null)
        {
            // Content is laid out at the body's size and clipped to the silhouette, so nothing spills
            // out while the island grows or shrinks.
            var body = BodyRect;
            Child.Arrange(body);
            var clip = Build(new Rect(-Ear, 0, body.Width + Ear * 2, body.Height), BottomRadius, Ear);
            Child.Clip = clip;
        }
        return arrangeSize;
    }
}
