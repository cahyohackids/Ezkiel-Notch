using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using EzkielNotch.Core;

namespace EzkielNotch.Island;

/// <summary>A room's tab icon, matched to the Mac's SF Symbols (NotchTab.icon / selectedIcon): timer, checklist,
/// bell, note.text, waveform, tray.full, drop, gearshape. Segoe has the timer, checklist and gear; the rest are
/// drawn on a 16×16 grid so they read like the Mac's. Selected tabs take the room's gradient, and the bell,
/// tray and drop fill in like bell.fill / tray.full.fill / drop.fill. A small dot rides on the glyph's corner
/// when the room has something going on (the Mac's tab badge).</summary>
public sealed class TabGlyph : Grid
{
    private readonly Room _room;
    private readonly Accent _accent;
    private readonly Ellipse _badge;
    private TextBlock? _font;
    private readonly List<Shape> _outline = [];
    private readonly List<Shape> _fillable = [];
    private readonly List<Shape> _cutouts = [];

    public TabGlyph(Room room)
    {
        _room = room;
        _accent = RoomInfo.Of(room).Accent;
        Width = 16;
        Height = 16;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Build();
        _badge = new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = _accent.DeepBrush,
            Stroke = Brushes.Black,
            StrokeThickness = 1.5,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -3, -4, 0),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        Children.Add(_badge);
    }

    /// <summary>The Segoe glyph shown for this room, or null when it's drawn (for tests and tooltips).</summary>
    public string? FontGlyph => _font?.Text;

    public bool Badge
    {
        get => _badge.Visibility == Visibility.Visible;
        set => _badge.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetState(bool selected, bool hovering)
    {
        Brush ink = selected ? _accent.Vertical : hovering ? Ui.Ink : Ui.Mist;
        if (_font is not null)
        {
            _font.Foreground = ink;
            // Selected glyphs are a touch heavier, like the Mac's .semibold symbol weight.
            _font.FontSize = selected ? 15 : 14.5;
        }
        foreach (var s in _outline)
        {
            s.Stroke = ink;
            s.StrokeThickness = selected ? 1.45 : 1.25;
        }
        // bell.fill, tray.full.fill, drop.fill: the shape fills and its inner lines become cut-outs.
        foreach (var s in _fillable) s.Fill = selected ? ink : Brushes.Transparent;
        foreach (var s in _cutouts) s.Stroke = selected ? Brushes.Black : ink;
    }

    private void Build()
    {
        switch (_room)
        {
            case Room.Den: Font(""); break;      // Stopwatch: SF timer
            case Room.Hunt: Font(""); break;     // CheckList: SF checklist
            case Room.Settings: Font(""); break; // Setting: SF gearshape
            case Room.Roar:
                // bell / bell.fill
                Fillable("M8,2.3 C5.7,2.3 4.3,4.1 4.3,6.4 L4.3,9.2 L3.1,11.1 C2.9,11.5 3.1,11.9 3.6,11.9 L12.4,11.9 C12.9,11.9 13.1,11.5 12.9,11.1 L11.7,9.2 L11.7,6.4 C11.7,4.1 10.3,2.3 8,2.3 Z");
                Fillable("M6.4,13.2 L9.6,13.2 C9.4,14 8.8,14.5 8,14.5 C7.2,14.5 6.6,14 6.4,13.2 Z");
                Outline("M8,1.2 L8,2.3");
                break;
            case Room.Scratch:
                // note.text: a card with three lines
                Outline("M4.2,2.6 L11.8,2.6 C13,2.6 13.8,3.4 13.8,4.6 L13.8,11.4 C13.8,12.6 13,13.4 11.8,13.4 L4.2,13.4 C3,13.4 2.2,12.6 2.2,11.4 L2.2,4.6 C2.2,3.4 3,2.6 4.2,2.6 Z");
                Outline("M5,6 L11,6 M5,8.4 L11,8.4 M5,10.8 L8.6,10.8");
                break;
            case Room.Purr:
            {
                // waveform: nine bars, tallest near the middle
                double[] half = [1.0, 2.4, 4.2, 6.2, 4.4, 6.6, 3.8, 2.2, 1.0];
                var d = string.Join(" ", half.Select((h, i) =>
                    FormattableString.Invariant($"M{1.6 + i * 1.6:0.##},{8 - h:0.##} L{1.6 + i * 1.6:0.##},{8 + h:0.##}")));
                Outline(d);
                break;
            }
            case Room.Stash:
                // tray.full / tray.full.fill: an in-tray with two sheets in it
                Fillable("M2.2,8.6 L3.7,4.2 C3.9,3.6 4.4,3.2 5,3.2 L11,3.2 C11.6,3.2 12.1,3.6 12.3,4.2 L13.8,8.6 L13.8,11.8 C13.8,12.7 13.2,13.3 12.3,13.3 L3.7,13.3 C2.8,13.3 2.2,12.7 2.2,11.8 Z");
                Cutout("M2.2,8.6 L5.5,8.6 C5.7,9.7 6.7,10.5 8,10.5 C9.3,10.5 10.3,9.7 10.5,8.6 L13.8,8.6");
                Cutout("M5.3,5.2 L10.7,5.2 M4.8,6.9 L11.2,6.9");
                break;
            case Room.Oasis:
                // drop / drop.fill
                Fillable("M8,1.8 C8,1.8 3.5,6.9 3.5,9.9 C3.5,12.5 5.5,14.3 8,14.3 C10.5,14.3 12.5,12.5 12.5,9.9 C12.5,6.9 8,1.8 8,1.8 Z");
                break;
        }
        SetState(false, false);
    }

    private void Font(string glyph)
    {
        _font = new TextBlock
        {
            Text = glyph,
            FontFamily = Ui.Icons,
            FontSize = 14.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            // The Segoe glyphs are drawn larger than their box; let them overhang the 16×16 cell.
            Margin = new Thickness(-4),
            TextAlignment = TextAlignment.Center,
        };
        Children.Add(_font);
    }

    private Path Shape(string data)
    {
        var p = new Path
        {
            Data = Geometry.Parse(data),
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Stretch = Stretch.None,
            SnapsToDevicePixels = false,
        };
        Children.Add(p);
        return p;
    }

    private Path Outline(string data)
    {
        var p = Shape(data);
        _outline.Add(p);
        return p;
    }

    private void Fillable(string data)
    {
        var p = Outline(data);
        _fillable.Add(p);
    }

    private void Cutout(string data)
    {
        var p = Shape(data);
        _cutouts.Add(p);
        p.StrokeThickness = 1.25;
    }
}
