using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using EzkielNotch.Core;

namespace EzkielNotch.Island;

/// <summary>Apple-style two-stop gradient for one feature (light → deep, same hue). Same values as the Mac.</summary>
public readonly record struct Accent(Color Light, Color Deep)
{
    public static readonly Accent Den = new(Hex(0xFFD27A), Hex(0xFF9500));
    public static readonly Accent Rest = new(Hex(0x9EF3EE), Hex(0x00C7BE));
    public static readonly Accent Hunt = new(Hex(0xFF9A8A), Hex(0xFF453A));
    public static readonly Accent Roar = new(Hex(0x9BF0B4), Hex(0x30D158));
    public static readonly Accent Scout = new(Hex(0x9EF3EE), Hex(0x00C7BE));
    public static readonly Accent Rally = new(Hex(0xD9A2FF), Hex(0xBF5AF2));
    public static readonly Accent Scratch = new(Hex(0xFFE680), Hex(0xFFC400));
    public static readonly Accent Purr = new(Hex(0xFF8FB0), Hex(0xFF375F));
    public static readonly Accent Stash = new(Hex(0x9C9AFF), Hex(0x5E5CE6));
    public static readonly Accent Oasis = new(Hex(0x8AE3FF), Hex(0x0A84FF));
    public static readonly Accent Neutral = new(Hex(0xC7C7CC), Hex(0x8E8E93));

    public Brush Vertical => Ui.Frozen(new LinearGradientBrush(Light, Deep, 90));
    public Brush LightBrush => Ui.Frozen(new SolidColorBrush(Light));
    public Brush DeepBrush => Ui.Frozen(new SolidColorBrush(Deep));
    public Brush Tint(byte alpha) => Ui.Frozen(new SolidColorBrush(Color.FromArgb(alpha, Deep.R, Deep.G, Deep.B)));

    public static Color Hex(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}

/// <summary>One room: its name (a feature name, never translated), accent, pane height and the subtitle the tab's
/// tooltip shows (the Mac's NotchTab.subtitle). The tab glyph is drawn by TabGlyph.</summary>
public sealed record RoomInfo(Room Room, Accent Accent, double PaneHeight, string Subtitle)
{
    // Pane heights follow the Mac's NotchMetrics.paneHeight, so each tab is only as tall as its content.
    public static readonly RoomInfo[] All =
    [
        new(Room.Den, Accent.Den, 160, "Focus timer"),
        new(Room.Hunt, Accent.Hunt, 188, "Tasks and meetings"),
        new(Room.Roar, Accent.Roar, 176, "Unread and agents"),
        new(Room.Scratch, Accent.Scratch, 180, "Notes and clips"),
        new(Room.Purr, Accent.Purr, 122, "Now playing"),
        new(Room.Stash, Accent.Stash, 128, "File shelf"),
        new(Room.Oasis, Accent.Oasis, 136, "Water"),
        new(Room.Settings, Accent.Neutral, 196, "Settings"),
    ];

    public string Name => Room.ToString();
    public static RoomInfo Of(Room r) => All[(int)r];
}

/// <summary>Design tokens and small builders shared by the rooms (the Mac's Theme and Components).</summary>
public static class Ui
{
    // Ink on black, as on the Mac: ink, mist (secondary), faint (tertiary).
    public static readonly Brush Ink = Frozen(new SolidColorBrush(Color.FromRgb(0xF5, 0xF1, 0xEA)));
    public static readonly Brush Mist = Frozen(new SolidColorBrush(Color.FromArgb(0x8F, 0xFF, 0xFF, 0xFF)));
    public static readonly Brush Faint = Frozen(new SolidColorBrush(Color.FromArgb(0x57, 0xFF, 0xFF, 0xFF)));
    public static readonly Brush Surface = Frozen(new SolidColorBrush(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF)));
    public static readonly Brush SurfaceRaised = Frozen(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)));
    public static readonly Brush SurfaceHover = Frozen(new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)));
    public static readonly Brush Track = Frozen(new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)));
    /// <summary>Text on a light accent fill.</summary>
    public static readonly Brush OnAccent = Frozen(new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E)));

    public static readonly FontFamily Text = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Display = new("Segoe UI Variable Display, Segoe UI");
    /// <summary>Semibold and bold text. WPF can't move Segoe UI Variable's weight axis: asking it for SemiBold still
    /// draws the regular outlines, which is why titles looked lighter than the Mac's. Segoe UI ships real
    /// Semibold and Bold faces, so weighted text uses it (same design, same metrics).</summary>
    public static readonly FontFamily Strong = new("Segoe UI");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>The face for a weight: Segoe UI for SemiBold and up, else the Variable face.</summary>
    public static FontFamily Face(FontWeight weight, bool display = false) =>
        weight.ToOpenTypeWeight() >= 600 ? Strong : display ? Display : Text;

    /// <summary>Sets a weight and the face that can draw it.</summary>
    public static T Weight<T>(T t, FontWeight weight) where T : Control
    {
        t.FontWeight = weight;
        t.FontFamily = Face(weight);
        return t;
    }

    /// <summary>Sets a weight and the face that can draw it.</summary>
    public static TextBlock Weight(TextBlock t, FontWeight weight)
    {
        t.FontWeight = weight;
        t.FontFamily = Face(weight, t.FontFamily == Display);
        return t;
    }

    // Type scale (the Mac's, a touch larger: Segoe UI sets smaller than SF Pro at the same size).
    public const double Caption = 10.5, Secondary = 12, Body = 13, Title = 15;

    public static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    public static TextBlock Label(string text, double size = Body, Brush? brush = null, FontWeight? weight = null, bool trim = true) => new()
    {
        Text = text,
        FontFamily = Face(weight ?? FontWeights.Normal),
        FontSize = size,
        Foreground = brush ?? Ink,
        FontWeight = weight ?? FontWeights.Normal,
        TextTrimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Numbers (the Mac's Theme.number): semibold by default, tabular digits, so counting down never jitters.</summary>
    public static TextBlock Number(string text, double size, Brush? brush = null, FontWeight? weight = null)
    {
        var w = weight ?? FontWeights.SemiBold;
        var t = Label(text, size, brush, w, trim: false);
        t.FontFamily = Face(w, display: true);
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    public static TextBlock Glyph(string glyph, double size = 12, Brush? brush = null) => new()
    {
        Text = glyph,
        FontFamily = Icons,
        FontSize = size,
        Foreground = brush ?? Ink,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static Border Card(UIElement? child = null, double radius = 12, Thickness? padding = null) => new()
    {
        Child = child,
        CornerRadius = new CornerRadius(radius),
        Background = Surface,
        Padding = padding ?? new Thickness(0),
    };

    /// <summary>Makes any element a button: hand cursor, hover callback, a little press, click on release inside.</summary>
    public static T Clickable<T>(T e, Action onClick, Action<bool>? hover = null, string? tip = null, double pressScale = 0.95)
        where T : FrameworkElement
    {
        e.Cursor = Cursors.Hand;
        if (tip is not null) e.ToolTip = tip;
        e.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(1, 1);
        e.RenderTransform = scale;
        void Press(double to)
        {
            if (SpringEase.ReduceMotion) return;
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(120)) { EasingFunction = new QuadraticEase() };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        e.MouseEnter += (_, _) => hover?.Invoke(true);
        e.MouseLeave += (_, _) => { hover?.Invoke(false); Press(1); };
        e.MouseLeftButtonDown += (_, ev) =>
        {
            if (!e.IsEnabled) return;
            e.CaptureMouse();
            Press(pressScale);
            ev.Handled = true;
        };
        e.MouseLeftButtonUp += (_, ev) =>
        {
            bool inside = e.IsMouseCaptured && e.IsMouseOver;
            e.ReleaseMouseCapture();
            Press(1);
            ev.Handled = true;
            if (inside && e.IsEnabled) onClick();
        };
        return e;
    }

    /// <summary>A capsule button (the Mac's PillButton, 28 high). Prominent ones carry the room's gradient and the
    /// Mac's hairline highlight along the top.</summary>
    public static Border Pill(string title, Action onClick, Accent accent, bool prominent = false, string? glyph = null, string? tip = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var fg = prominent ? OnAccent : Ink;
        if (glyph is not null) row.Children.Add(new Border { Child = Glyph(glyph, 10.5, fg), Margin = new Thickness(0, 0, 5, 0) });
        // 12 pt Segoe UI Semibold matches the Mac's 12.5 pt medium SF in width and weight.
        row.Children.Add(Label(title, Secondary, fg, FontWeights.SemiBold, trim: false));
        var b = new Border
        {
            Child = row,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 0, 12, 0),
            Background = prominent ? accent.Vertical : SurfaceRaised,
            BorderBrush = prominent ? Highlight : null,
            BorderThickness = new Thickness(prominent ? 0.5 : 0),
        };
        var rest = b.Background;
        return Clickable(b, onClick, h =>
        {
            if (prominent) b.Opacity = h ? 0.9 : 1;
            else b.Background = h ? SurfaceHover : rest;
        }, tip);
    }

    /// <summary>The white hairline on top of an accent fill (the Mac's AccentFill stroke).</summary>
    private static readonly Brush Highlight = Frozen(new LinearGradientBrush(
        new GradientStopCollection { new(Color.FromArgb(0x73, 0xFF, 0xFF, 0xFF), 0), new(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0.5) }, 90));

    private static readonly Brush ChipRest = Frozen(new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush ChipHover = Frozen(new SolidColorBrush(Color.FromArgb(0x19, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush ChipInk = Frozen(new SolidColorBrush(Color.FromArgb(0xD9, 0xF5, 0xF1, 0xEA)));

    /// <summary>A small selectable capsule (presets, filters), the Mac's Chip: selected ones are filled with the
    /// accent's gradient and dark text, the rest a faint capsule with ink text.</summary>
    public static Border Chip(string title, bool selected, Accent accent, Action onClick, bool enabled = true, string? tip = null)
    {
        // Same width as before the restyle (Hunt's reminder row is tight at 440): 11 pt, 8 px sides.
        var label = Label(title, Caption + 0.5, selected ? OnAccent : ChipInk, selected ? FontWeights.SemiBold : FontWeights.Medium, trim: false);
        var b = new Border
        {
            Child = label,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 0, 8, 0),
            Background = selected ? accent.Vertical : ChipRest,
            BorderBrush = selected ? Highlight : null,
            BorderThickness = new Thickness(selected ? 0.5 : 0),
            IsEnabled = enabled,
            Opacity = enabled ? 1 : 0.4,
        };
        return Clickable(b, onClick, h => { if (!selected && enabled) b.Background = h ? ChipHover : ChipRest; }, tip);
    }

    private static readonly Brush IconRest = Frozen(new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush IconHover = Frozen(new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)));

    /// <summary>A round icon button with a fixed glyph size, the Mac's IconButton: a faint disc behind the glyph
    /// (brighter on hover), or the accent's gradient when filled. Plain ones (transport, steppers) show the disc only on hover.</summary>
    public static Border IconButton(string glyph, string tip, Action onClick, double size = 26, Brush? tint = null, Accent? filled = null, bool plain = false)
    {
        var b = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = filled is { } f ? f.Vertical : plain ? Brushes.Transparent : IconRest,
            Child = Glyph(glyph, size >= 28 ? 12 : 11, filled is not null ? OnAccent : tint ?? Ink),
        };
        var rest = b.Background;
        return Clickable(b, onClick, h => b.Background = filled is not null ? rest : h ? IconHover : rest, tip, 0.88);
    }

    /// <summary>A text-only button with a padded hit area.</summary>
    public static Border TextButton(string title, Action onClick, Brush? tint = null)
    {
        var b = new Border
        {
            Child = Label(title, Caption + 0.5, tint ?? Mist, FontWeights.Medium, trim: false),
            Padding = new Thickness(6, 3, 6, 3),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
        };
        return Clickable(b, onClick, h => b.Background = h ? SurfaceRaised : Brushes.Transparent);
    }

    /// <summary>A one-line text field on a card, with a placeholder.</summary>
    public static (Border Box, TextBox Input) Field(string placeholder, Action<TextBox>? onSubmit = null, double height = 30)
    {
        var input = StyledTextBox(Body);
        input.VerticalContentAlignment = VerticalAlignment.Center;
        var hint = Label(placeholder, Body, Faint);
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(2, 0, 0, 0);
        input.TextChanged += (_, _) => hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (onSubmit is not null)
            input.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                onSubmit(input);
                e.Handled = true;
            };
        var grid = new Grid();
        grid.Children.Add(hint);
        grid.Children.Add(input);
        var box = new Border
        {
            Child = grid,
            Height = height,
            CornerRadius = new CornerRadius(10),
            Background = SurfaceRaised,
            Padding = new Thickness(10, 0, 10, 0),
            Cursor = Cursors.IBeam,
        };
        box.MouseLeftButtonDown += (_, _) => input.Focus();
        return (box, input);
    }

    /// <summary>A borderless text box in the island's colours.</summary>
    public static TextBox StyledTextBox(double size)
    {
        var t = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Ink,
            CaretBrush = Ink,
            SelectionBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF))),
            SelectionOpacity = 0.45,
            FontFamily = Text,
            FontSize = size,
            Padding = new Thickness(0),
            FocusVisualStyle = null,
        };
        // The default template draws a light border on hover and focus; a bare one keeps the island black.
        t.Template = BareTextBox;
        return t;
    }

    private static readonly ControlTemplate BareTextBox = MakeBareTemplate();

    private static ControlTemplate MakeBareTemplate()
    {
        var template = new ControlTemplate(typeof(TextBox));
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(ScrollViewer.FocusableProperty, false);
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        template.VisualTree = host;
        template.Seal();
        return template;
    }

    /// <summary>An on/off switch (Apple style), 34×20.</summary>
    public static Border Switch(bool on, Accent accent, Action<bool> changed)
    {
        var knob = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            Background = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(on ? 16 : 2, 0, 0, 0),
        };
        var track = new Border
        {
            Width = 34,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = on ? accent.Vertical : Track,
            Child = knob,
        };
        bool state = on;
        return Clickable(track, () =>
        {
            state = !state;
            track.Background = state ? accent.Vertical : Track;
            var to = new Thickness(state ? 16 : 2, 0, 0, 0);
            if (SpringEase.ReduceMotion) knob.Margin = to;
            else knob.BeginAnimation(FrameworkElement.MarginProperty,
                new ThicknessAnimation(to, TimeSpan.FromMilliseconds(220)) { EasingFunction = SpringEase.Smooth });
            changed(state);
        }, pressScale: 0.97);
    }

    /// <summary>A scroll area without visible bars (wheel and touch still scroll).</summary>
    public static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        PanningMode = PanningMode.VerticalOnly,
        Focusable = false,
    };

    /// <summary>The lion, as in the header and the empty states.</summary>
    public static readonly System.Windows.Media.Imaging.BitmapImage Lion = MakeLion();

    private static System.Windows.Media.Imaging.BitmapImage MakeLion()
    {
        var b = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/lion.png"));
        b.Freeze();
        return b;
    }

    /// <summary>The Mac's EmptyHint: a still lion on the left, the title and the subtitle left-aligned beside it,
    /// the pair centred in the room. A sleeping lion (dimmed) when the feature is off.</summary>
    public static FrameworkElement EmptyHint(string title, string subtitle, bool sleeping = false)
    {
        var lion = new Image { Source = Lion, Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Center, Opacity = sleeping ? 0.6 : 1 };
        var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var t = Label(title, Body, Ink, FontWeights.SemiBold);
        var sub = Label(subtitle, Secondary, Mist, trim: false);
        sub.TextWrapping = TextWrapping.Wrap;
        sub.Margin = new Thickness(0, 2, 0, 0);
        s.Children.Add(t);
        s.Children.Add(sub);
        var pair = new DockPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(lion, Dock.Left);
        pair.Children.Add(lion);
        pair.Children.Add(s);
        // Wrap the text at the room's width (less the lion), like the Mac's HStack, and keep the pair centred.
        var host = new Border { Child = pair, Background = Brushes.Transparent };
        host.SizeChanged += (_, e) => sub.MaxWidth = t.MaxWidth = Math.Max(120, e.NewSize.Width - 40 - 12 - 8);
        return host;
    }

    public static StackPanel Row(double spacing, params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in children)
        {
            if (p.Children.Count > 0 && c is FrameworkElement fe) fe.Margin = new Thickness(spacing, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
            p.Children.Add(c);
        }
        return p;
    }
}
