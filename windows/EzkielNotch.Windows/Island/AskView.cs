using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using EzkielNotch.Core;
using EzkielNotch.Platform;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Island;

/// <summary>
/// Ask: a one-line question box over the page of the open island, and the answer as it streams.
/// Esc or × puts it away; what was asked stays for the session (in memory only).
/// </summary>
public sealed class AskView : Grid
{
    private readonly IslandWindow _w;
    private readonly Border _sendHost = new();
    private readonly Grid _content = new() { Margin = new Thickness(0, 8, 0, 0) };

    // The answer's parts stay while it streams, so the text grows in place and keeps its scroll.
    private Grid? _answer;
    private TextBlock? _question, _text;
    private ScrollViewer? _scroll;
    private Border? _actions, _statusHost;
    private string _shownText = "";
    private bool? _actionsBusy;

    public TextBox Input { get; }
    /// <summary>The error card on screen, if any (the self-test reads it).</summary>
    public AskError? ShownError { get; private set; }
    public bool ShowsSettingsButton { get; private set; }
    /// <summary>The answer as drawn (the self-test reads it).</summary>
    public string AnswerText => _text is null ? "" : string.Concat(_text.Inlines.OfType<Run>().Select(r => r.Text));

    public AskView(IslandWindow w)
    {
        _w = w;
        VerticalAlignment = VerticalAlignment.Top;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var (box, input) = Ui.Field(L("Ask anything…"), _ => w.AskSend(), 30);
        Input = input;
        input.Text = AskHub.Draft;
        input.TextChanged += (_, _) => AskHub.Draft = input.Text;
        var spark = Ui.Glyph("\uE82F", 12, Accent.Den.LightBrush);
        spark.Margin = new Thickness(0, 0, 8, 0);
        var field = new DockPanel();
        DockPanel.SetDock(spark, Dock.Left);
        field.Children.Add(spark);
        var inner = box.Child;
        box.Child = null;
        field.Children.Add(inner);
        box.Child = field;

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        SetColumn(box, 0);
        row.Children.Add(box);
        _sendHost.Margin = new Thickness(6, 0, 0, 0);
        _sendHost.VerticalAlignment = VerticalAlignment.Center;
        SetColumn(_sendHost, 1);
        row.Children.Add(_sendHost);
        var close = Ui.IconButton("\uE711", L("Close Ask (Esc)"), () => w.SetAsk(false), 24, Ui.Mist);
        close.Margin = new Thickness(4, 0, 0, 0);
        SetColumn(close, 2);
        row.Children.Add(close);
        Children.Add(row);

        SetRow(_content, 1);
        Children.Add(_content);
    }

    /// <summary>Shows the session as it is now. While an answer streams only its text and status change.</summary>
    public void Render()
    {
        var s = AskHub.Session;
        if (Input.Text != AskHub.Draft) Input.Text = AskHub.Draft;
        _sendHost.Child = s.Busy
            ? Ui.IconButton("\uE71A", L("Stop the answer"), _w.AskStop, 28, Ui.Ink)
            : Ui.IconButton("\uE74A", L("Ask (Return)"), _w.AskSend, 28, filled: Accent.Den);

        ShownError = null;
        ShowsSettingsButton = false;
        if (s.Error is { } e && s.Answer.Length == 0) Show(ErrorCard(e, s.Question.Length > 0));
        else if (s.Question.Length == 0) Show(AskHub.HasKey ? Intro() : ErrorCard(AskError.NoKey, false));
        else ShowAnswer(s);
    }

    private void Show(UIElement content)
    {
        _answer = null;
        _content.Children.Clear();
        _content.Children.Add(content);
    }

    // MARK: Before the first question

    private UIElement Intro()
    {
        var s = AskHub.Session;
        var stack = new StackPanel();
        var hint = Ui.Label(L("Short answers, right here. Return asks, Esc closes."), Ui.Secondary, Ui.Mist);
        stack.Children.Add(hint);
        var model = Ui.Label($"{_w.Model.Settings.AskModel} · {L("only your question is sent")}", Ui.Caption, Ui.Faint);
        model.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(model);
        if (s.History.Count > 0)
        {
            var earlier = Ui.Label(L("Earlier"), Ui.Caption, Ui.Faint, FontWeights.SemiBold);
            earlier.Margin = new Thickness(0, 10, 0, 4);
            stack.Children.Add(earlier);
            foreach (var turn in s.History.Reverse().Take(3))
            {
                var line = Ui.Row(6, Ui.Glyph("\uE81C", 10, Ui.Mist), Ui.Label(turn.Question, Ui.Secondary, Ui.Ink));
                var card = new Border
                {
                    Child = line,
                    Height = 24,
                    CornerRadius = new CornerRadius(8),
                    Background = Ui.Surface,
                    Padding = new Thickness(8, 0, 8, 0),
                    Margin = new Thickness(0, 0, 0, 3),
                };
                var t = turn;
                stack.Children.Add(Ui.Clickable(card, () => s.Show(t), h => card.Background = h ? Ui.SurfaceHover : Ui.Surface, pressScale: 0.98));
            }
        }
        return stack;
    }

    // MARK: The answer

    private void ShowAnswer(AskSession s)
    {
        if (_answer is null || !_content.Children.Contains(_answer)) BuildAnswer();
        _question!.Text = s.Question;
        if (s.Answer.Length == 0)
        {
            _text!.Inlines.Clear();
            _text.Inlines.Add(new Run(L(s.Phase == AskSession.State.Thinking ? "Thinking…" : "Asking…")) { Foreground = Ui.Mist, FontSize = Ui.Secondary });
            _shownText = "";
        }
        else if (s.Answer != _shownText)
        {
            _text!.Inlines.Clear();
            _text.Inlines.AddRange(Inlines(Ask.Tidy(s.Answer)));
            _shownText = s.Answer;
        }

        // The answer stopped part-way (the balance ran out, the connection dropped), or Stop was pressed.
        var status = new StackPanel { Orientation = Orientation.Horizontal };
        if (s.Error is { } e)
        {
            status.Children.Add(Ui.Glyph("\uE7BA", 10, Accent.Hunt.LightBrush));
            var t = Ui.Label(L(e.Title), Ui.Caption, Accent.Hunt.LightBrush, FontWeights.Medium);
            t.Margin = new Thickness(6, 0, 4, 0);
            status.Children.Add(t);
            status.Children.Add(Ui.TextButton(L("Settings"), _w.OpenAskSettings));
            ShowsSettingsButton = true;
        }
        else if (s.Stopped) status.Children.Add(Ui.Label(L("Stopped"), Ui.Caption, Ui.Faint));
        _statusHost!.Child = status.Children.Count > 0 ? status : null;

        if (_actionsBusy != s.Busy || !s.Busy)
        {
            _actionsBusy = s.Busy;
            _actions!.Child = Actions(s);
        }
        else if (_actions!.Child is DockPanel row && row.Children.Count > 0 && row.Children[0] is TextBlock phase)
            phase.Text = PhaseText(s);

        if (s.Busy) _scroll!.ScrollToEnd();
    }

    private void BuildAnswer()
    {
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _question = Ui.Label("", Ui.Secondary, Ui.Mist, FontWeights.SemiBold);
        _question.Margin = new Thickness(2, 0, 0, 4);
        g.Children.Add(_question);

        _text = new TextBlock
        {
            FontFamily = Ui.Text,
            FontSize = Ui.Body,
            Foreground = Ui.Ink,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18,
            Margin = new Thickness(2, 0, 4, 0),
        };
        _statusHost = new Border { Margin = new Thickness(2, 6, 0, 2) };
        var body = new StackPanel();
        body.Children.Add(_text);
        body.Children.Add(_statusHost);
        _scroll = Ui.Scroll(body);
        SetRow(_scroll, 1);
        g.Children.Add(_scroll);

        _actions = new Border { Height = 26, Margin = new Thickness(0, 6, 0, 0) };
        SetRow(_actions, 2);
        g.Children.Add(_actions);

        _actionsBusy = null;
        _shownText = "\0";
        _answer = g;
        _content.Children.Clear();
        _content.Children.Add(g);
    }

    private static string PhaseText(AskSession s) =>
        L(s.Phase == AskSession.State.Thinking ? "Thinking…" : s.Phase == AskSession.State.Streaming ? "Writing…" : "Asking…");

    private UIElement Actions(AskSession s)
    {
        var row = new DockPanel { LastChildFill = false };
        if (s.Busy)
        {
            var phase = Ui.Label(PhaseText(s), Ui.Caption, Ui.Mist);
            row.Children.Add(phase);
            var stop = Ui.IconButton("\uE71A", L("Stop the answer"), _w.AskStop, 22, Ui.Ink);
            DockPanel.SetDock(stop, Dock.Right);
            row.Children.Add(stop);
            return row;
        }
        bool any = s.Answer.Length > 0;
        foreach (var (glyph, title, act) in new (string, string, Action)[]
                 {
                     ("\uE8C8", "Copy", _w.AskCopy),
                     ("\uE9D5", "Make task", _w.AskMakeTask),
                     ("\uE70B", "Save as note", _w.AskSaveNote),
                 })
        {
            var b = ActionButton(glyph, L(title), act);
            b.IsEnabled = any;
            b.Opacity = any ? 1 : 0.4;
            b.Margin = new Thickness(0, 0, 5, 0);
            row.Children.Add(b);
        }
        var fresh = Ui.TextButton(L("New question"), _w.AskNew, Ui.Ink);
        DockPanel.SetDock(fresh, Dock.Right);
        row.Children.Add(fresh);
        return row;
    }

    /// <summary>A small labelled action under the answer.</summary>
    private static Border ActionButton(string glyph, string title, Action act)
    {
        var label = Ui.Label(title, Ui.Caption + 0.5, Ui.Ink, FontWeights.Medium, trim: false);
        var row = Ui.Row(4, Ui.Glyph(glyph, 10, Ui.Ink), label);
        row.VerticalAlignment = VerticalAlignment.Center;
        var rest = Ui.Frozen(new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)));
        var b = new Border
        {
            Child = row,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 0, 9, 0),
            Background = rest,
        };
        return Ui.Clickable(b, act, h => b.Background = h ? Ui.SurfaceHover : rest, title);
    }

    // MARK: Errors

    /// <summary>No key, a wrong key, no balance, offline, a timeout: what happened, in plain words, and what to do.</summary>
    private UIElement ErrorCard(AskError e, bool canRetry)
    {
        ShownError = e;
        ShowsSettingsButton = true;
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = Ui.Glyph(e.Kind switch
        {
            AskErrorKind.NoKey or AskErrorKind.InvalidKey => "\uE8D7",
            AskErrorKind.NoBalance => "\uE8C7",
            AskErrorKind.Offline => "\uE701",
            AskErrorKind.Timeout => "\uE916",
            _ => "\uE7BA",
        }, 16, e.Kind == AskErrorKind.NoKey ? Accent.Den.LightBrush : Accent.Hunt.LightBrush);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        icon.Margin = new Thickness(2, 2, 0, 0);
        g.Children.Add(icon);

        var text = new StackPanel();
        text.Children.Add(Ui.Label(L(e.Title), Ui.Body, Ui.Ink, FontWeights.SemiBold));
        var detail = Ui.Label(e.Kind == AskErrorKind.Other ? e.Detail : L(e.Detail), Ui.Secondary, Ui.Mist, trim: false);
        detail.TextWrapping = TextWrapping.Wrap;
        detail.MaxHeight = 52;
        detail.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(detail);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(Ui.Pill(L("Settings"), _w.OpenAskSettings, Accent.Den, prominent: e.NeedsSettings, glyph: "\uE713"));
        if (e.Kind is AskErrorKind.NoKey or AskErrorKind.NoBalance or AskErrorKind.InvalidKey)
            buttons.Children.Add(Spaced(Ui.TextButton(L(e.Kind == AskErrorKind.NoKey ? "Get a key" : "Top up"), () => Files.Open(Ask.KeyPage))));
        if (canRetry && e.Kind != AskErrorKind.NoKey)
            buttons.Children.Add(Spaced(Ui.TextButton(L("Try again"), _w.AskRetry, Ui.Ink)));
        text.Children.Add(buttons);
        SetColumn(text, 1);
        g.Children.Add(text);
        return new Border { Child = g, CornerRadius = new CornerRadius(12), Background = Ui.Surface, Padding = new Thickness(10) };
    }

    private static Border Spaced(Border b)
    {
        b.Margin = new Thickness(6, 0, 0, 0);
        b.VerticalAlignment = VerticalAlignment.Center;
        return b;
    }

    // MARK: Minimal markdown

    /// <summary>The tidied answer as runs: **bold** and `code` drawn, everything else as it is.</summary>
    public static List<Inline> Inlines(string text)
    {
        var list = new List<Inline>();
        var run = new StringBuilder();
        bool bold = false, code = false;
        void Flush()
        {
            if (run.Length == 0) return;
            var r = new Run(run.ToString());
            if (bold) r.FontWeight = FontWeights.SemiBold;
            if (code)
            {
                r.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                r.Background = Ui.SurfaceRaised;
            }
            list.Add(r);
            run.Clear();
        }
        for (int i = 0; i < text.Length; i++)
        {
            if (!code && text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                Flush();
                bold = !bold;
                i++;
            }
            else if (text[i] == '`')
            {
                Flush();
                code = !code;
            }
            else run.Append(text[i]);
        }
        Flush();
        return list;
    }
}
