using System.Windows;
using System.Windows.Media;

namespace EzkielNotch.Island;

/// <summary>
/// The anime, cat, cute and robot eyes (the Mac's StyledEyesCanvas). Same lids, look, glow and smile as the
/// lion's, so moods read the same. Soft glows are gradients and wide translucent strokes (a DrawingContext has
/// no blur).
/// </summary>
internal static class StyledEyes
{
    public static void Draw(DrawingContext dc, string style, double w, double h, double o, double look, double glow, double smile, double twinkle, double wide)
    {
        var centers = new[] { new Point(w * 0.27, h * 0.56), new Point(w * 0.73, h * 0.56) };
        for (int i = 0; i < 2; i++)
        {
            double side = i == 0 ? -1 : 1;
            switch (style)
            {
                case "anime": Anime(dc, centers[i], side, o, h, w, look, glow, smile, twinkle); break;
                case "cat": Cat(dc, centers[i], side, o, h, w, look, glow, smile, wide); break;
                case "cute": Cute(dc, centers[i], o, h, look, glow, smile); break;
                default: Robot(dc, centers[i], o, h, w, look, glow, smile); break;
            }
        }
    }

    private static Color A(Color c, double opacity) => Color.FromArgb((byte)Math.Clamp(255 * opacity, 0, 255), c.R, c.G, c.B);
    private static SolidColorBrush Brush(Color c, double opacity = 1) => new(A(c, opacity));
    private static Pen RoundPen(Brush b, double width) => new(b, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

    /// <summary>A soft blob: a radial gradient that fades to nothing at its edge.</summary>
    private static void Soft(DrawingContext dc, Color c, double opacity, Point center, double rx, double ry)
    {
        var b = new RadialGradientBrush(A(c, opacity), A(c, 0));
        dc.DrawEllipse(b, null, center, rx, ry);
    }

    // MARK: Anime: the clip's eyes. A tall iris, black under the lid, violet, then glowing hot pink at the
    // bottom; a big white ✕ star with a pink halo; glitter; a black lash band with a flick and spiky lashes.

    public static readonly Color AnimePink = Color.FromRgb(0xFF, 0x66, 0x99);
    private static readonly Color AnimeRose = Color.FromRgb(0xF2, 0x38, 0x73);
    public static readonly Color AnimeMagenta = Color.FromRgb(0xDB, 0x29, 0x8F);
    public static readonly Color AnimeViolet = Color.FromRgb(0x6B, 0x24, 0x9E);
    private static readonly Color AnimeNight = Color.FromRgb(0x12, 0x05, 0x1A);
    private static readonly Color Lash = Color.FromRgb(0x21, 0x14, 0x26);
    /// <summary>Where the glitter sits in the iris (fractions of its radii), and how big each speck is.</summary>
    private static readonly (double X, double Y, double R)[] Glitter =
    [
        (-0.55, -0.4, 0.05), (0.48, -0.55, 0.045), (0.66, 0.08, 0.04), (-0.4, 0.58, 0.035),
        (0.3, 0.64, 0.045), (-0.7, 0.12, 0.03), (0.08, -0.72, 0.04), (-0.18, -0.5, 0.03),
        (0.58, 0.42, 0.03), (-0.62, -0.08, 0.025),
    ];

    private static void Anime(DrawingContext dc, Point c, double side, double o, double h, double w, double look, double glow, double smile, double twinkle)
    {
        // Drawn in the eye's own frame: u runs from the inner corner (-) to the outer one (+), v downward,
        // so the same drawing mirrors for the other eye.
        double W = w * 0.36, H = h * 0.62;
        var centre = new Point(c.X, c.Y + h * 0.02);
        Point P(double u, double v) => new(centre.X + side * u * W, centre.Y + v * H);
        double lift = o, lower = 1 - smile * 0.8;

        // The pink glow under each eye.
        Soft(dc, AnimeMagenta, 0.55 + 0.2 * smile, P(0.05, 0.7), W * 0.62, H * 0.32);

        var inner = P(-0.5, 0.12);
        var outer = P(0.5, 0.06);
        if (o <= 0.12)
        {
            // Shut: the lash band curves down to a point, lashes pointing down.
            var shut = new StreamGeometry();
            using (var g = shut.Open())
            {
                g.BeginFigure(inner, true, true);
                g.BezierTo(P(-0.2, 0.62), P(0.3, 0.6), P(0.62, 0.14), true, true);
                g.BezierTo(P(0.3, 0.42), P(-0.2, 0.44), inner, true, true);
            }
            dc.DrawGeometry(Brush(Lash), null, shut);
            foreach (var u in new[] { -0.1, 0.12, 0.34 })
                dc.DrawLine(RoundPen(Brush(Lash), Math.Max(0.6, h * 0.04)), P(u, 0.5), P(u + 0.06, 0.78));
            return;
        }

        // The opening: a sharp inner corner, the upper lid high and wide, the lower lid a gentle curve.
        var upperC1 = P(-0.3, 0.12 - 0.58 * lift);
        var upperC2 = P(0.3, 0.06 - 0.64 * lift);
        var lowerC1 = P(0.3, 0.06 + 0.46 * lift * lower);
        var lowerC2 = P(-0.25, 0.12 + 0.4 * lift * lower);
        var eye = new StreamGeometry();
        using (var g = eye.Open())
        {
            g.BeginFigure(inner, true, true);
            g.BezierTo(upperC1, upperC2, outer, true, true);
            g.BezierTo(lowerC1, lowerC2, inner, true, true);
        }
        eye.Freeze();

        // A tall oval iris; its top hides under the lid.
        var irisC = P(0.04 + look * 0.12 * side, 0.08);
        double rx = W * 0.31, ry = H * 0.48;
        var glowC = new Point(irisC.X, irisC.Y + ry * 0.35);
        dc.PushClip(eye);
        // Lavender whites in the lid's shadow, darker under it.
        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x6B, 0x5C, 0x80), Color.FromRgb(0xCC, 0xBF, 0xE0), 90), null,
                         new Rect(c.X - W, centre.Y - H, W * 2, H * 2));
        // The iris glows past its edge at the bottom.
        Soft(dc, AnimePink, 0.75, glowC, rx * 1.6, ry * 0.7);
        dc.DrawEllipse(Brush(AnimeNight), null, irisC, rx, ry);
        var irisBrush = new RadialGradientBrush(new GradientStopCollection
        {
            new GradientStop(AnimePink, 0), new GradientStop(AnimeRose, 0.4), new GradientStop(AnimeMagenta, 0.65),
            new GradientStop(AnimeViolet, 0.85), new GradientStop(A(AnimeViolet, 0), 1),
        })
        { MappingMode = BrushMappingMode.Absolute, Center = glowC, GradientOrigin = glowC, RadiusX = ry * 1.1, RadiusY = ry * 1.1 };
        dc.DrawEllipse(irisBrush, null, irisC, rx, ry);
        // The lid's shadow darkens the top of the iris.
        var shade = new LinearGradientBrush(A(AnimeNight, 0.95), A(AnimeNight, 0), 90)
        { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(irisC.X, irisC.Y - ry), EndPoint = new Point(irisC.X, irisC.Y + ry * 0.1) };
        dc.DrawEllipse(shade, new Pen(Brush(AnimeNight), Math.Max(0.5, h * 0.03)), irisC, rx, ry);
        // Glitter: specks that catch the light in turn as the star twinkles.
        for (int i = 0; i < Glitter.Length; i++)
        {
            var g = Glitter[i];
            double phase = i % 2 == 0 ? twinkle : 1 - twinkle;
            double r = rx * g.R * 1.2 * (0.8 + 0.5 * phase);
            dc.DrawEllipse(Brush(Colors.White, 0.45 + 0.5 * phase), null, new Point(irisC.X + g.X * rx, irisC.Y + g.Y * ry), r, r);
        }
        // The big ✕ star with its pink halo, flaring and turning as it twinkles.
        var starC = new Point(irisC.X, irisC.Y + ry * 0.12);
        double starR = rx * (0.78 + 0.22 * twinkle), angle = 45 + twinkle * 12;
        dc.DrawGeometry(Brush(Color.FromRgb(0xFF, 0xB3, 0xD9), 0.35), null, Star(starC, starR * 1.3, angle, 0.34));
        dc.DrawGeometry(Brush(Color.FromRgb(0xFF, 0xB3, 0xD9), 0.6), null, Star(starC, starR * 1.12, angle, 0.24));
        dc.DrawGeometry(Brushes.White, null, Star(starC, starR, angle, 0.12));
        dc.Pop();
        if (glow > 0.01) dc.DrawGeometry(null, new Pen(Brush(AnimePink, 0.7 * glow), h * 0.2), eye);

        // The lash band: thin at the inner corner, thick over the eye, ending in a flick past the outer corner.
        var band = new StreamGeometry();
        using (var g = band.Open())
        {
            g.BeginFigure(inner, true, true);
            g.BezierTo(upperC1, upperC2, outer, true, true);
            g.QuadraticBezierTo(P(0.66, 0.0), P(0.74, 0.16), true, true);
            g.QuadraticBezierTo(P(0.7, -0.08), P(0.6, -0.2 * lift - 0.02), true, true);
            g.BezierTo(P(0.32, 0.02 - 0.84 * lift), P(-0.32, 0.02 - 0.72 * lift), P(-0.54, 0.04), true, true);
        }
        dc.DrawGeometry(Brush(Lash), null, band);
        // Short lashes sweeping out from the outer half of the band.
        foreach (var (u, len) in new[] { (0.3, 0.18), (0.45, 0.24), (0.58, 0.2) })
        {
            double baseV = 0.06 - (1 - Math.Pow(u / 0.62, 2)) * 0.62 * lift;
            var spike = new StreamGeometry();
            using (var g = spike.Open())
            {
                g.BeginFigure(P(u - 0.04, baseV + 0.03), true, true);
                g.QuadraticBezierTo(P(u + 0.02, baseV - len * 0.5 * lift), P(u + 0.12, baseV - len * lift), true, true);
                g.QuadraticBezierTo(P(u + 0.07, baseV - len * 0.35 * lift), P(u + 0.05, baseV + 0.05), true, true);
            }
            dc.DrawGeometry(Brush(Lash), null, spike);
        }
        // The lower lid: a fine line along the outer two thirds of it.
        var (q1, q2, q3) = SplitBezier(outer, lowerC1, lowerC2, inner, 0.68);
        var lowerLid = new StreamGeometry();
        using (var g = lowerLid.Open())
        {
            g.BeginFigure(outer, false, false);
            g.BezierTo(q1, q2, q3, true, true);
        }
        dc.DrawGeometry(null, RoundPen(Brush(Lash, 0.9), Math.Max(0.5, h * 0.035)), lowerLid);
    }

    /// <summary>The first part of a cubic Bézier, up to t (de Casteljau): its two controls and end point.</summary>
    private static (Point, Point, Point) SplitBezier(Point p0, Point p1, Point p2, Point p3, double t)
    {
        static Point L(Point a, Point b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        var a = L(p0, p1, t);
        var b = L(p1, p2, t);
        var c = L(p2, p3, t);
        var d = L(a, b, t);
        var e = L(b, c, t);
        return (a, d, L(d, e, t));
    }

    /// <summary>A four-point star with concave sides. Pinch is how far the waist between two points sits from the
    /// middle (a fraction of the radius): small for the clip's thin ✕, larger for a plump sparkle.</summary>
    public static Geometry Star(Point center, double r, double degrees, double pinch = 0.3)
    {
        Point At(double a, double d) => new(center.X + d * Math.Cos(a), center.Y + d * Math.Sin(a));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            for (int k = 0; k < 4; k++)
            {
                double a = degrees * Math.PI / 180 + k * Math.PI / 2 - Math.PI / 2;
                if (k == 0) ctx.BeginFigure(At(a, r), true, true);
                // Each side bows in toward the middle, so the points come out long and sharp.
                ctx.QuadraticBezierTo(At(a + Math.PI / 10, r * (pinch + 0.12)), At(a + Math.PI / 4, r * pinch), true, true);
                ctx.QuadraticBezierTo(At(a + Math.PI / 2 - Math.PI / 10, r * (pinch + 0.12)), At(a + Math.PI / 2, r), true, true);
            }
        }
        g.Freeze();
        return g;
    }

    // MARK: Cat: green-gold almond eyes, a slit pupil that widens when you come close

    private static void Cat(DrawingContext dc, Point c, double side, double o, double h, double w, double look, double glow, double smile, double wide)
    {
        double eyeW = w * 0.32, eyeH = h * 0.64;
        var inner = new Point(c.X - side * eyeW / 2, c.Y + eyeH * 0.12);
        var outer = new Point(c.X + side * eyeW / 2, c.Y - eyeH * 0.18);
        var left = side < 0 ? outer : inner;
        var right = side < 0 ? inner : outer;
        var lime = Color.FromRgb(0xBD, 0xE6, 0x54);
        var amber = Color.FromRgb(0xE6, 0xB3, 0x33);
        if (o <= 0.12)
        {
            var line = new StreamGeometry();
            using (var g = line.Open())
            {
                g.BeginFigure(left, false, false);
                g.QuadraticBezierTo(new Point(c.X, c.Y + eyeH * 0.3), right, true, true);
            }
            dc.DrawGeometry(null, RoundPen(Brush(lime, 0.85), Math.Max(1, h * 0.08)), line);
            return;
        }
        double top = eyeH * 0.95 * o, bottom = eyeH * (0.6 * o + 0.06) * (1 - smile * 0.9);
        var almond = new StreamGeometry();
        using (var g = almond.Open())
        {
            g.BeginFigure(left, true, true);
            g.QuadraticBezierTo(new Point(c.X, c.Y - top), right, true, true);
            g.QuadraticBezierTo(new Point(c.X, c.Y + bottom - smile * eyeH * 0.5), left, true, true);
        }
        almond.Freeze();
        if (glow > 0.01) dc.DrawGeometry(null, new Pen(Brush(lime, 0.7 * glow), h * 0.22), almond);
        var irisC = new Point(c.X + look * eyeW * 0.14, c.Y - eyeH * 0.03);
        dc.PushClip(almond);
        var iris = new RadialGradientBrush(new GradientStopCollection
        {
            new GradientStop(amber, 0), new GradientStop(lime, 0.55), new GradientStop(Color.FromRgb(0x4D, 0x73, 0x1A), 1),
        })
        { MappingMode = BrushMappingMode.Absolute, Center = irisC, GradientOrigin = irisC, RadiusX = eyeW * 0.6, RadiusY = eyeW * 0.6 };
        dc.DrawRectangle(iris, null, new Rect(c.X - eyeW, c.Y - eyeH * 1.5, eyeW * 2, eyeH * 3));
        double slitW = eyeH * (0.16 + 0.34 * wide), slitH = eyeH * 0.95;
        dc.DrawEllipse(Brushes.Black, null, irisC, slitW / 2, slitH / 2);
        dc.DrawEllipse(Brush(Colors.White, 0.8), null, new Point(irisC.X - eyeW * 0.2 + eyeH * 0.12, irisC.Y - eyeH * 0.32 + eyeH * 0.12), eyeH * 0.12, eyeH * 0.12);
        dc.Pop();
        dc.DrawGeometry(null, new Pen(Brush(Color.FromRgb(0x0D, 0x14, 0x05)), Math.Max(1, h * 0.08)), almond);
    }

    // MARK: Cute: big shiny black eyes and a blush

    private static void Cute(DrawingContext dc, Point c, double o, double h, double look, double glow, double smile)
    {
        double r = h * 0.4;
        Soft(dc, Color.FromRgb(0xFF, 0x80, 0x99), 0.34 + 0.25 * smile, new Point(c.X, c.Y + r * 1.1), r * 1.1, r * 0.35);
        var cream = Color.FromRgb(0xFA, 0xF2, 0xE6);
        if (smile > 0.5 || o <= 0.12)
        {
            // Happy: ^ ^; asleep: ‿ ‿
            bool up = smile > 0.5;
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                g.BeginFigure(new Point(c.X - r * 0.8, c.Y + (up ? r * 0.2 : -r * 0.1)), false, false);
                g.QuadraticBezierTo(new Point(c.X, c.Y + (up ? -r * 0.9 : r * 0.6)), new Point(c.X + r * 0.8, c.Y + (up ? r * 0.2 : -r * 0.1)), true, true);
            }
            dc.DrawGeometry(null, RoundPen(Brush(cream), Math.Max(1.1, h * 0.1)), arc);
            return;
        }
        double cx = c.X + look * r * 0.3;
        var center = new Point(cx, c.Y);
        if (glow > 0.01) dc.DrawEllipse(null, new Pen(Brush(Color.FromRgb(0xFF, 0xBF, 0x66), 0.7 * glow), h * 0.22), center, r, r * o);
        dc.DrawEllipse(Brush(Color.FromRgb(0x0F, 0x0D, 0x14)), new Pen(Brush(cream, 0.85), Math.Max(0.7, h * 0.05)), center, r, r * o);
        double big = r * 0.36, small = r * 0.16;
        dc.DrawEllipse(Brushes.White, null, new Point(cx - r * 0.5 + big, c.Y - r * 0.6 * o + big * o), big, big * o);
        dc.DrawEllipse(Brush(Colors.White, 0.85), null, new Point(cx + r * 0.3 + small, c.Y + r * 0.25 * o + small * o), small, small * o);
    }

    // MARK: Robot: two neon bars that collapse to blink and sweep while an agent works

    private static void Robot(DrawingContext dc, Point c, double o, double h, double w, double look, double glow, double smile)
    {
        var neon = glow > 0.5 ? Color.FromRgb(0xFF, 0xB8, 0x40) : Color.FromRgb(0x3D, 0xF2, 0xFF);
        double barW = w * 0.26, barH = Math.Max(h * 0.1, h * 0.62 * o);
        double x = c.X + look * w * 0.035;
        if (smile > 0.5)
        {
            // Happy: ^ ^ chevrons.
            var chevron = new StreamGeometry();
            using (var g = chevron.Open())
            {
                g.BeginFigure(new Point(x - barW / 2, c.Y + h * 0.14), false, false);
                g.LineTo(new Point(x, c.Y - h * 0.16), true, true);
                g.LineTo(new Point(x + barW / 2, c.Y + h * 0.14), true, true);
            }
            dc.DrawGeometry(null, RoundPen(Brush(neon, 0.3), h * 0.3), chevron);
            dc.DrawGeometry(null, RoundPen(Brush(neon), Math.Max(1.2, h * 0.11)), chevron);
            return;
        }
        var rect = new Rect(x - barW / 2, c.Y - barH / 2, barW, barH);
        double radius = Math.Min(barH, barW) / 2.4;
        // The neon halo: two wider, fainter copies behind the bar.
        foreach (var (grow, alpha) in new[] { (h * 0.22, 0.14), (h * 0.11, 0.3) })
        {
            var halo = rect;
            halo.Inflate(grow, grow);
            dc.DrawRoundedRectangle(Brush(neon, alpha), null, halo, radius + grow, radius + grow);
        }
        dc.DrawRoundedRectangle(Brush(neon), null, rect, radius, radius);
        // Scan lines across the LED, and a brighter band that follows the look (it sweeps while an agent works).
        dc.PushClip(new RectangleGeometry(rect, radius, radius));
        double band = rect.Left + rect.Width * (0.5 + look * 0.5) - rect.Width * 0.15;
        dc.DrawRectangle(Brush(Colors.White, 0.55), null, new Rect(band, rect.Top, rect.Width * 0.3, rect.Height));
        for (double y = rect.Top + h * 0.06; y < rect.Bottom; y += h * 0.12)
            dc.DrawRectangle(Brush(Colors.Black, 0.25), null, new Rect(rect.Left, y, rect.Width, Math.Max(0.4, h * 0.03)));
        dc.Pop();
    }
}
