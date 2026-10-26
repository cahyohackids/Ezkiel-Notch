using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EzkielNotch.Island;

/// <summary>
/// The island: a black body hanging from the top edge, with rounded bottom corners and two concave
/// "ears" that melt into the screen edge (the same silhouette as the Mac notch). It draws itself and
/// centres horizontally in its bounds. Its content is laid out once at the target size
/// (<see cref="ContentWidth"/> × <see cref="ContentHeight"/>) and only clipped by the animated
/// silhouette, so nothing re-flows while the island grows or shrinks.
/// </summary>
public sealed class IslandSurface : Decorator
{
    public static readonly DependencyProperty BodyWidthProperty = Register(nameof(BodyWidth), 180);
    public static readonly DependencyProperty BodyHeightProperty = Register(nameof(BodyHeight), 6);
    public static readonly DependencyProperty BottomRadiusProperty = Register(nameof(BottomRadius), 3);
    public static readonly DependencyProperty EarProperty = Register(nameof(Ear), 4);

    /// <summary>The size the content is laid out at: the body size the current animation is heading to.</summary>
    public static readonly DependencyProperty ContentWidthProperty = DependencyProperty.Register(
        nameof(ContentWidth), typeof(double), typeof(IslandSurface),
        new FrameworkPropertyMetadata(180.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));
    public static readonly DependencyProperty ContentHeightProperty = DependencyProperty.Register(
        nameof(ContentHeight), typeof(double), typeof(IslandSurface),
        new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double ContentWidth { get => (double)GetValue(ContentWidthProperty); set => SetValue(ContentWidthProperty, value); }
    public double ContentHeight { get => (double)GetValue(ContentHeightProperty); set => SetValue(ContentHeightProperty, value); }

    /// <summary>Island style: a floating pill, rounded all round, Gap below the top edge (no ears).</summary>
    public static readonly DependencyProperty FloatingProperty = DependencyProperty.Register(
        nameof(Floating), typeof(bool), typeof(IslandSurface),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsArrange));
    public static readonly DependencyProperty GapProperty = Register(nameof(Gap), 0);

    public bool Floating { get => (bool)GetValue(FloatingProperty); set => SetValue(FloatingProperty, value); }
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }

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
            double w = Math.Max(0, BodyWidth + (Floating ? 0 : Ear * 2));
            return new Rect((ActualWidth - w) / 2, Floating ? Gap : 0, w, Math.Max(0, BodyHeight));
        }
    }

    /// <summary>The body without the ears: where content goes.</summary>
    public Rect BodyRect
    {
        get
        {
            var r = ShapeRect;
            double ear = Floating ? 0 : Ear;
            return new Rect(r.X + ear, r.Y, Math.Max(0, r.Width - ear * 2), r.Height);
        }
    }

    public Geometry Shape() => Floating ? Pill(ShapeRect, BottomRadius) : Build(ShapeRect, BottomRadius, Ear);

    /// <summary>The island: a rounded rectangle (a full pill when the radius is half the height).</summary>
    public static Geometry Pill(Rect rect, double radius)
    {
        double r = Math.Max(0, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
        var g = new RectangleGeometry(rect, r, r);
        g.Freeze();
        return g;
    }

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
        Child?.Measure(new Size(Math.Max(0, ContentWidth), Math.Max(0, ContentHeight)));
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is not null)
        {
            // Content keeps its final layout; the animated silhouette (in the child's own coordinates)
            // reveals it as the island grows.
            double x = (arrangeSize.Width - ContentWidth) / 2;
            double y = Floating ? Gap : 0;
            Child.Arrange(new Rect(x, y, Math.Max(0, ContentWidth), Math.Max(0, ContentHeight)));
            var shape = ShapeRect;
            var local = new Rect(shape.X - x, 0, shape.Width, shape.Height);
            Child.Clip = Floating ? Pill(local, BottomRadius) : Build(local, BottomRadius, Ear);
        }
        return arrangeSize;
    }
}
