using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EzkielNotch.Core;
using EzkielNotch.Island;
using EzkielNotch.Platform;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Rooms;

/// <summary>Settings › Features: Ask's switch, the user's own API key (DPAPI), the model and "Test key".</summary>
public static class AskSettingsRows
{
    /// <summary>What's pasted but not saved yet (memory only), and whether it shows in clear.</summary>
    private static string _draft = "";
    private static bool _reveal;
    /// <summary>Set by an error's Settings button: the next build scrolls to these rows.</summary>
    public static bool Reveal { get; set; }
    /// <summary>The key field while there is no key (the self-test reads it).</summary>
    public static Control? KeyField { get; private set; }

    public static void AddAskRows(Panel list, NotchModel model, Action refresh)
    {
        var s = model.Settings;
        KeyField = null;
        var toggle = ToggleRow(L("Ask"), IslandWindow.AskShortcutTaken && s.Ask
                                   ? L("%@ is taken by another app", IslandWindow.AskHotkeyLabel)
                                   : L("Quick AI answers in the notch · %@", IslandWindow.AskHotkeyLabel),
                               s.Ask, v =>
                               {
                                   s.Ask = v;
                                   model.SettingsChanged();
                                   list.Dispatcher.BeginInvoke(refresh);
                               });
        list.Children.Add(toggle);
        if (!s.Ask) return;
        var key = KeyRow(refresh);
        list.Children.Add(key);
        list.Children.Add(ModelRow(model, refresh));
        if (Reveal)
        {
            Reveal = false;
            key.Loaded += (_, _) => key.BringIntoView();
        }
    }

    private static Border ToggleRow(string title, string subtitle, bool on, Action<bool> set)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        text.Children.Add(Ui.Label(title, Ui.Body, weight: FontWeights.Medium));
        text.Children.Add(Ui.Label(subtitle, Ui.Caption, Ui.Mist));
        g.Children.Add(text);
        var sw = Ui.Switch(on, Accent.Den, set);
        sw.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(sw, 1);
        g.Children.Add(sw);
        return Row(g);
    }

    private static Border KeyRow(Action refresh)
    {
        var session = AskHub.Session;
        var stack = new StackPanel();

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(Ui.Label(L("Ezkielyna API key"), Ui.Body, weight: FontWeights.Medium));
        var (status, tint) = session.KeyCheck switch
        {
            AskSession.KeyState.Checking => (L("Testing…"), Ui.Mist),
            AskSession.KeyState.Ok => (session.Models.Count == 0 ? L("Key works") : L("Key works · %d models", session.Models.Count), Accent.Roar.LightBrush),
            AskSession.KeyState.Failed => (L(session.KeyError?.Title ?? "Couldn't get an answer"), Accent.Hunt.LightBrush),
            _ => (AskHub.HasKey ? L("Saved with Windows encryption · only sent to api.ezkielyna.store") : L("Your own key from api.ezkielyna.store"), Ui.Mist),
        };
        title.Children.Add(Ui.Label(status, Ui.Caption, tint));
        head.Children.Add(title);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (AskHub.HasKey)
        {
            buttons.Children.Add(Ui.TextButton(L(_reveal ? "Hide key" : "Show key"), () =>
            {
                _reveal = !_reveal;
                refresh();
            }));
            buttons.Children.Add(Ui.TextButton(L("Remove"), () =>
            {
                AskHub.RemoveKey();
                _reveal = false;
                refresh();
            }, Accent.Hunt.LightBrush));
            var test = Ui.Pill(L(session.KeyCheck == AskSession.KeyState.Checking ? "Testing…" : "Test key"), () => _ = AskHub.TestKey(), Accent.Den);
            test.Margin = new Thickness(4, 0, 0, 0);
            buttons.Children.Add(test);
        }
        else buttons.Children.Add(Ui.TextButton(L("Get a key"), () => Files.Open(Ask.KeyPage), Accent.Den.LightBrush));
        Grid.SetColumn(buttons, 1);
        head.Children.Add(buttons);
        stack.Children.Add(head);

        if (AskHub.HasKey)
        {
            var shown = Ui.Label(_reveal ? AskHub.Key ?? "" : AskHub.Masked, 11, Ui.Ink);
            shown.FontFamily = Mono;
            shown.Opacity = 0.8;
            shown.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(shown);
        }
        else
        {
            var g = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(PasteField(refresh));
            var eye = Ui.IconButton(_reveal ? "\uED1A" : "\uE7B3", L(_reveal ? "Hide key" : "Show key"), () =>
            {
                _reveal = !_reveal;
                refresh();
            }, 24, Ui.Mist);
            eye.Margin = new Thickness(4, 0, 0, 0);
            Grid.SetColumn(eye, 1);
            g.Children.Add(eye);
            var save = Ui.Pill(L("Save key"), () => Save(refresh), Accent.Den, prominent: _draft.Length > 0);
            save.Margin = new Thickness(4, 0, 0, 0);
            Grid.SetColumn(save, 2);
            g.Children.Add(save);
            stack.Children.Add(g);
        }
        return Row(stack);
    }

    /// <summary>The paste field: hidden characters unless Show key is on.</summary>
    private static Border PasteField(Action refresh)
    {
        Control field;
        if (_reveal)
        {
            var t = Ui.StyledTextBox(11.5);
            t.FontFamily = Mono;
            t.Text = _draft;
            t.TextChanged += (_, _) => _draft = t.Text;
            field = t;
        }
        else
        {
            var p = new PasswordBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Ui.Ink,
                CaretBrush = Ui.Ink,
                FontFamily = Mono,
                FontSize = 11.5,
                Padding = new Thickness(0),
                FocusVisualStyle = null,
                Password = _draft,
                Template = BarePassword,
            };
            p.PasswordChanged += (_, _) => _draft = p.Password;
            field = p;
        }
        field.VerticalContentAlignment = VerticalAlignment.Center;
        field.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Save(refresh);
            e.Handled = true;
        };
        KeyField = field;
        var hint = Ui.Label(L("Paste your key"), 11.5, Ui.Faint);
        hint.IsHitTestVisible = false;
        hint.Visibility = _draft.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (field is TextBox tb) tb.TextChanged += (_, _) => hint.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (field is PasswordBox pb) pb.PasswordChanged += (_, _) => hint.Visibility = pb.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var grid = new Grid();
        grid.Children.Add(hint);
        grid.Children.Add(field);
        var box = new Border
        {
            Child = grid,
            Height = 26,
            CornerRadius = new CornerRadius(9),
            Background = Ui.SurfaceRaised,
            Padding = new Thickness(10, 0, 10, 0),
            Cursor = Cursors.IBeam,
        };
        box.MouseLeftButtonDown += (_, _) => field.Focus();
        return box;
    }

    private static void Save(Action refresh)
    {
        if (!AskHub.SaveKey(_draft)) return;
        _draft = "";
        _reveal = false;
        refresh();
        _ = AskHub.TestKey();
    }

    private static Border ModelRow(NotchModel model, Action refresh)
    {
        var s = model.Settings;
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = Ui.Label(L("AI model"), Ui.Body, weight: FontWeights.Medium);
        label.VerticalAlignment = VerticalAlignment.Top;
        label.Margin = new Thickness(0, 2, 0, 0);
        g.Children.Add(label);
        var chips = new WrapPanel();
        foreach (var m in AskHub.Session.ModelChoices(s.AskModel))
        {
            var chip = Ui.Chip(m, s.AskModel == m, Accent.Den, () =>
            {
                s.AskModel = m;
                model.SettingsChanged();
                refresh();
            });
            chip.Margin = new Thickness(0, 0, 4, 3);
            chips.Children.Add(chip);
        }
        Grid.SetColumn(chips, 1);
        g.Children.Add(chips);
        return Row(g);
    }

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    /// <summary>The same row card as the rest of Settings.</summary>
    private static Border Row(UIElement content) => new()
    {
        Child = content,
        MinHeight = 38,
        Padding = new Thickness(10, 5, 8, 5),
        CornerRadius = new CornerRadius(10),
        Background = Ui.Surface,
        Margin = new Thickness(0, 0, 0, 3),
    };

    /// <summary>A borderless password box in the island's colours (the default one draws a light frame).</summary>
    private static readonly ControlTemplate BarePassword = MakeBarePassword();

    private static ControlTemplate MakeBarePassword()
    {
        var template = new ControlTemplate(typeof(PasswordBox));
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(UIElement.FocusableProperty, false);
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        template.VisualTree = host;
        template.Seal();
        return template;
    }
}
