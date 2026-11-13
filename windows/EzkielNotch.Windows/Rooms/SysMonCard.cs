using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EzkielNotch.Core;
using EzkielNotch.Island;
using EzkielNotch.Platform;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Rooms;

/// <summary>The system monitor in Den (the Mac's SysMonCard): CPU, memory, network, battery (when there is one) and
/// the system drive, each with its number and, for the ones that move, a sparkline of the last two minutes.</summary>
public sealed class SysMonCard : Grid
{
    public const double CardHeight = 56;
    private static readonly Brush Berry = Ui.Frozen(new SolidColorBrush(Accent.Hex(0xFF5A7A)));
    private int _version = -1;

    public SysMonCard() => Height = CardHeight;

    /// <summary>Redraws when the sampler has a new reading.</summary>
    public void Update(SysMonSampler s)
    {
        if (s.Version == _version) return;
        _version = s.Version;
        Children.Clear();
        ColumnDefinitions.Clear();
        var r = s.Current;
        var tiles = new List<UIElement>
        {
            Tile(IslandWindow.CpuGlyph, "CPU", r.Cpu is { } c ? SysFormat.Percent(c) : "–", L("%d cores", Environment.ProcessorCount),
                 (r.Cpu ?? 0) >= 90 ? Berry : Ui.Ink, s.Cpu.Normalized(100), Accent.Den),
            Tile(IslandWindow.MemoryGlyph, L("Memory"), SysFormat.Memory(r.MemUsed), L("of %@", SysFormat.Memory(r.MemTotal)),
                 r.MemPercent >= 90 ? Berry : Ui.Ink, s.Memory.Normalized(100), Accent.Stash),
            r.Online
                ? Tile(IslandWindow.NetworkGlyph, L("Network"), r.Down is { } d ? SysFormat.Rate(d) : "–",
                       "↑ " + (r.Up is { } u ? SysFormat.Rate(u) : "–"), Ui.Ink, s.Down.Normalized(50_000), Accent.Oasis)
                : Tile(IslandWindow.NetworkGlyph, L("Network"), L("Offline"), L("No connection"), Berry, [], Accent.Oasis),
        };
        if (r.Battery is { } pct)
        {
            string detail = r.Charging ? L("Charging") : r.OnAC ? L("On power")
                : r.BatteryMinutes is { } m ? L("%@ left", SysFormat.Minutes(m)) : L("On battery");
            tiles.Add(Tile(PeekLooks.Battery(pct), L("Battery"), $"{pct}%", detail, pct <= 20 && !r.OnAC ? Berry : Ui.Ink, [], Accent.Roar));
        }
        if (r.DiskFree is { } free)
            tiles.Add(Tile(IslandWindow.DiskGlyph, L("Disk"), SysFormat.Disk(free), $"{L("free")} · {r.DiskName}", free < 5e9 ? Berry : Ui.Ink, [], Accent.Neutral));

        for (int i = 0; i < tiles.Count; i++)
        {
            if (i > 0) ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            SetColumn(tiles[i], i * 2);
            Children.Add(tiles[i]);
        }
    }

    private static Border Tile(string glyph, string label, string value, string detail, Brush valueBrush, IReadOnlyList<double> spark, Accent accent)
    {
        var g = new Grid();
        var text = new StackPanel { Margin = new Thickness(7, 4, 7, 4) };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(Ui.Glyph(glyph, 8.5, Ui.Mist));
        var name = Ui.Label(label, 9, Ui.Mist, FontWeights.Medium);
        name.Margin = new Thickness(3, 0, 0, 0);
        head.Children.Add(name);
        text.Children.Add(head);
        var number = Ui.Number(value, 12.5, valueBrush);
        number.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(number);
        text.Children.Add(Ui.Label(detail, 9, Ui.Faint));
        // The last two minutes in its own thin row, so it never covers the words or numbers.
        if (spark.Count > 1)
            text.Children.Add(new Sparkline(spark, accent.Deep) { Height = 7, Margin = new Thickness(0, 2, 0, 0), IsHitTestVisible = false });
        g.Children.Add(text);
        return new Border
        {
            Child = g,
            CornerRadius = new CornerRadius(10),
            Background = Ui.Surface,
            ClipToBounds = true,
            ToolTip = $"{label}: {value}",
        };
    }
}

/// <summary>A sparkline through 0…1 values, left to right, with a soft fill under it. Drawn only when it changes.</summary>
public sealed class Sparkline(IReadOnlyList<double> values, Color color) : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        if (values.Count < 2 || ActualWidth <= 0 || ActualHeight <= 0) return;
        double w = ActualWidth, h = ActualHeight, step = w / (values.Count - 1);
        Point At(int i) => new(i * step, h - Math.Clamp(values[i], 0, 1) * h * 0.92);
        var line = new StreamGeometry();
        using (var c = line.Open())
        {
            c.BeginFigure(At(0), false, false);
            for (int i = 1; i < values.Count; i++) c.LineTo(At(i), true, false);
        }
        line.Freeze();
        var area = new StreamGeometry();
        using (var c = area.Open())
        {
            c.BeginFigure(new Point(0, h), true, true);
            for (int i = 0; i < values.Count; i++) c.LineTo(At(i), true, false);
            c.LineTo(new Point(w, h), true, false);
        }
        area.Freeze();
        var fill = new LinearGradientBrush(Color.FromArgb(0x47, color.R, color.G, color.B), Color.FromArgb(0x05, color.R, color.G, color.B), 90);
        fill.Freeze();
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0xBF, color.R, color.G, color.B)), 1);
        pen.Freeze();
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, pen, line);
    }
}
