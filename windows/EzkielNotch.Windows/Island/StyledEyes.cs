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

    // MARK: Anime: a big pink-to-plum iris, a twinkling white star, lashes, blush

    public static readonly Color AnimePink = Color.FromRgb(0xFF, 0x9E, 0xDB);
    public static readonly Color AnimeMagenta = Color.FromRgb(0xDB, 0x33, 0xA8);
    public static readonly Color AnimePlum = Color.FromRgb(0x54, 0x12, 0x4D);
    public static readonly Color AnimeViolet = Color.FromRgb(0x8C, 0x4D, 0xF2);
    private static readonly Color Lash = Color.FromRgb(0x29, 0x0D, 0x1F);

    private static void Anime(DrawingContext dc, Point c, double side, double o, double h, double w, double look, double glow, double smile, double twinkle)
    {
        double eyeW = w * 0.36, eyeH = h * 0.8;
        var left = new Point(c.X - eyeW / 2, c.Y + eyeH * 0.1);
        var right = new Point(c.X + eyeW / 2, c.Y + eyeH * 0.1);
        var outer = side < 0 ? left : right;
        double top = eyeH * 0.78 * o;
        double bottom = eyeH * (0.42 * o + 0.04) * (1 - smile * 0.85);

        // Blush under each eye, warmer when happy.
        Soft(dc, AnimePink, 0.32 + 0.25 * smile, new Point(c.X + side * eyeW * 0.12, c.Y + eyeH * 0.58), eyeW * 0.5, eyeH * 0.2);

        if (o <= 0.12)
        {
            // Shut: a soft downward curve with three little lashes.
            var shut = new StreamGeometry();
            using (var g = shut.Open())
            {
                g.BeginFigure(left, false, false);
                g.QuadraticBezierTo(new Point(c.X, c.Y + eyeH * 0.42), right, true, true);
            }
            dc.DrawGeometry(null, RoundPen(Brush(Lash), Math.Max(1, h * 0.09)), shut);
            foreach (var t in new[] { 0.3, 0.55, 0.8 })
            {
                double x = side < 0 ? c.X - eyeW / 2 + eyeW * (1 - t) : c.X - eyeW / 2 + eyeW * t;
                double y = c.Y + eyeH * 0.1 + eyeH * 0.37 * (1 - Math.Pow(2 * t - 1, 2)) * 0.5;
                dc.DrawLine(RoundPen(Brush(Lash), Math.Max(0.7, h * 0.05)), new Point(x, y), new Point(x + side * eyeW * 0.06, y + eyeH * 0.18));
            }
            return;
        }

        var cp1 = new Point(left.X + eyeW * 0.08, c.Y - top);
        var cp2 = new Point(right.X - eyeW * 0.08, c.Y - top);
        var eye = new StreamGeometry();
        using (var g = eye.Open())
        {
            g.BeginFigure(left, true, true);
            g.BezierTo(cp1, cp2, right, true, true);
            g.QuadraticBezierTo(new Point(c.X, c.Y + bottom * 1.35 - smile * eyeH * 0.4), left, true, true);
        }
        eye.Freeze();

        if (glow > 0.01) dc.DrawGeometry(null, new Pen(Brush(AnimePink, 0.7 * glow), h * 0.22), eye);
        double irisR = eyeH * 0.48;
        var irisC = new Point(c.X + look * eyeW * 0.14, c.Y + eyeH * 0.02);
        dc.PushClip(eye);
        dc.DrawRectangle(Brush(Colors.White, 0.92), null, new Rect(c.X - eyeW, c.Y - eyeH * 1.5, eyeW * 2, eyeH * 3));
        // Darker at the top under the lashes, glowing pink at the bottom, as anime irises are.
        var irisBrush = new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(AnimePlum, 0), new GradientStop(AnimeMagenta, 0.5), new GradientStop(AnimePink, 1),
        }, 90);
        dc.DrawEllipse(irisBrush, new Pen(Brush(AnimePlum, 0.9), Math.Max(0.6, h * 0.04)), irisC, irisR * 0.9, irisR);
        dc.DrawEllipse(Brush(AnimePlum), null, irisC, irisR * 0.3, irisR * 0.38);
        // The four-point star, flaring and turning when it twinkles, and a small round catchlight.
        dc.DrawGeometry(Brushes.White, null, Star(new Point(irisC.X - irisR * 0.18, irisC.Y - irisR * 0.22), irisR * (0.5 + 0.22 * twinkle), twinkle * 20));
        dc.DrawEllipse(Brush(Colors.White, 0.85), null, new Point(irisC.X + irisR * 0.32, irisC.Y + irisR * 0.42), irisR * 0.13, irisR * 0.13);
        dc.Pop();

        // The thick upper lash line, with two flicks at the outer corner; a fine line below.
        var upper = new StreamGeometry();
        using (var g = upper.Open())
        {
            g.BeginFigure(left, false, false);
            g.BezierTo(cp1, cp2, right, true, true);
        }
        dc.DrawGeometry(null, RoundPen(Brush(Lash), Math.Max(1.2, h * 0.14)), upper);
        foreach (var (dx, dy) in new[] { (0.16, -0.32), (0.2, -0.08) })
        {
            var flick = new StreamGeometry();
            using (var g = flick.Open())
            {
                g.BeginFigure(new Point(outer.X - side * eyeW * 0.04, outer.Y - eyeH * 0.1), false, false);
                g.QuadraticBezierTo(new Point(outer.X + side * eyeW * dx * 0.4, outer.Y - eyeH * 0.08),
                                    new Point(outer.X + side * eyeW * dx, outer.Y + eyeH * dy * o), true, true);
            }
            dc.DrawGeometry(null, RoundPen(Brush(Lash), Math.Max(0.8, h * 0.07)), flick);
        }
        var lower = new StreamGeometry();
        using (var g = lower.Open())
        {
            g.BeginFigure(new Point(left.X + eyeW * 0.12, left.Y + bottom * 0.5), false, false);
            g.QuadraticBezierTo(new Point(c.X, c.Y + bottom * 1.3 - smile * eyeH * 0.4), new Point(right.X - eyeW * 0.12, right.Y + bottom * 0.5), true, true);
        }
        dc.DrawGeometry(null, RoundPen(Brush(AnimeMagenta, 0.55), Math.Max(0.5, h * 0.035)), lower);

        // A tiny sparkle off the outer corner, brightest as the star twinkles.
        dc.DrawGeometry(Brush(Colors.White, 0.35 + 0.6 * twinkle), null,
                        Star(new Point(outer.X + side * eyeW * 0.2, c.Y - eyeH * 0.55), h * (0.09 + 0.07 * twinkle), 0));
    }

    /// <summary>A four-point star with concave sides.</summary>
    public static Geometry Star(Point center, double r, double degrees)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            for (int k = 0; k < 4; k++)
            {
                double a = degrees * Math.PI / 180 + k * Math.PI / 2 - Math.PI / 2;
                double mid = a + Math.PI / 4, next = a + Math.PI / 2;
                var tip = new Point(center.X + r * Math.Cos(a), center.Y + r * Math.Sin(a));
                var pinch = new Point(center.X + r * 0.16 * Math.Cos(mid), center.Y + r * 0.16 * Math.Sin(mid));
                var nextTip = new Point(center.X + r * Math.Cos(next), center.Y + r * Math.Sin(next));
                if (k == 0) ctx.BeginFigure(tip, true, true);
                ctx.QuadraticBezierTo(pinch, nextTip, true, true);
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
