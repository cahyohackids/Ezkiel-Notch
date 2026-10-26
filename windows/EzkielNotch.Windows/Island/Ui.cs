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

/// <summary>One room: its name (a feature name, never translated), tab glyph, accent and pane height.</summary>
public sealed record RoomInfo(Room Room, string Glyph, Accent Accent, double PaneHeight, string Subtitle)
{
    // Pane heights follow the Mac's NotchMetrics.paneHeight, so each tab is only as tall as its content.
    public static readonly RoomInfo[] All =
    [
        new(Room.Den, "\uE916", Accent.Den, 160, "Focus timer"),
        new(Room.Hunt, "\uE9D5", Accent.Hunt, 188, "Tasks"),
        new(Room.Roar, "\uEA8F", Accent.Roar, 176, "Unread and agents"),
        new(Room.Scratch, "\uE70B", Accent.Scratch, 180, "Notes and clips"),
        new(Room.Purr, "\uE8D6", Accent.Purr, 122, "Now playing"),
        new(Room.Stash, "\uE8B7", Accent.Stash, 122, "File shelf"),
        new(Room.Oasis, "\uEC32", Accent.Oasis, 136, "Water"),
        new(Room.Settings, "\uE713", Accent.Neutral, 196, "Settings"),
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
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

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
        FontFamily = Text,
        FontSize = size,
        Foreground = brush ?? Ink,
        FontWeight = weight ?? FontWeights.Normal,
        TextTrimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Numbers: Display face with tabular digits, so counting down never jitters.</summary>
    public static TextBlock Number(string text, double size, Brush? brush = null)
    {
        var t = Label(text, size, brush, FontWeights.SemiBold, trim: false);
        t.FontFamily = Display;
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

    /// <summary>A capsule button. Prominent ones carry the room's gradient.</summary>
    public static Border Pill(string title, Action onClick, Accent accent, bool prominent = false, string? glyph = null, string? tip = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var fg = prominent ? OnAccent : Ink;
        if (glyph is not null) row.Children.Add(new Border { Child = Glyph(glyph, 10.5, fg), Margin = new Thickness(0, 0, 6, 0) });
        row.Children.Add(Label(title, Secondary, fg, FontWeights.SemiBold, trim: false));
        var b = new Border
        {
            Child = row,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(12, 0, 12, 0),
            Background = prominent ? accent.Vertical : SurfaceRaised,
        };
        return Clickable(b, onClick, h => b.Opacity = h ? 0.88 : 1, tip);
    }

    /// <summary>A small selectable capsule (presets, filters).</summary>
    public static Border Chip(string title, bool selected, Accent accent, Action onClick, bool enabled = true, string? tip = null)
    {
        var label = Label(title, Caption + 0.5, selected ? accent.LightBrush : Mist, selected ? FontWeights.SemiBold : FontWeights.Medium, trim: false);
        var b = new Border
        {
            Child = label,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(8, 0, 8, 0),
            Background = selected ? accent.Tint(0x38) : Frozen(new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF))),
            IsEnabled = enabled,
            Opacity = enabled ? 1 : 0.4,
        };
        return Clickable(b, onClick, h => { if (!selected && enabled) b.Background = h ? SurfaceHover : Frozen(new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF))); }, tip);
    }

    /// <summary>A round icon button with a fixed glyph size.</summary>
    public static Border IconButton(string glyph, string tip, Action onClick, double size = 26, Brush? tint = null, Accent? filled = null)
    {
        var b = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = filled is { } f ? f.Vertical : Brushes.Transparent,
            Child = Glyph(glyph, size >= 28 ? 12 : 11, filled is not null ? OnAccent : tint ?? Ink),
        };
        var rest = b.Background;
        return Clickable(b, onClick, h => b.Background = filled is not null ? rest : h ? SurfaceHover : rest, tip, 0.88);
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

    /// <summary>Centred title + subtitle for an empty list.</summary>
    public static FrameworkElement EmptyHint(string title, string subtitle)
    {
        var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var t = Label(title, Body, Mist, FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        var sub = Label(subtitle, Caption + 0.5, Faint, trim: false);
        sub.TextWrapping = TextWrapping.Wrap;
        sub.TextAlignment = TextAlignment.Center;
        sub.MaxWidth = 300;
        sub.Margin = new Thickness(0, 3, 0, 0);
        s.Children.Add(t);
        s.Children.Add(sub);
        return s;
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
