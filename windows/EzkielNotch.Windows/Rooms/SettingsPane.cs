using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using EzkielNotch.Core;
using EzkielNotch.Island;
using EzkielNotch.Platform;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Rooms;

/// <summary>Settings: the same rows and choices as the Mac where Windows has them, grouped in one scroll.</summary>
public sealed class SettingsPane(NotchModel model) : Pane(model, Room.Settings)
{
    private ScrollViewer? _scroll;
    private bool _confirmQuit;

    public override void Refresh()
    {
        double offset = _scroll?.VerticalOffset ?? 0;
        Children.Clear();
        var s = Model.Settings;
        var list = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };

        list.Children.Add(Section(L("General")));
        list.Children.Add(Option(L("Language"), [.. new[] { AppLanguage.Auto, AppLanguage.En, AppLanguage.Id }.Select(lang =>
            Ui.Chip(lang switch { AppLanguage.En => "English", AppLanguage.Id => "Indonesia", _ => L("Auto") }, s.Language == lang, Accent.Neutral, () =>
            {
                s.Language = lang;
                Model.SettingsChanged(everything: true);
            }))]));
        list.Children.Add(Toggle(L("Open on hover"), L("Click always works too"), s.ExpandOnHover, v => s.ExpandOnHover = v));
        list.Children.Add(Option(L("Hover delay"), [.. new[] { 0.05, 0.12, 0.25, 0.45 }.Select(d =>
            Ui.Chip(d < 0.1 ? L("Instant") : $"{(int)(d * 1000)} ms", Math.Abs(s.HoverDelay - d) < 0.01, Accent.Neutral, () => { s.HoverDelay = d; Changed(); }))]));
        list.Children.Add(Toggle(L("Launch at login"), L("Start with Windows"), Startup.IsEnabled, v =>
        {
            s.LaunchAtLogin = Startup.Set(v);
        }));
        list.Children.Add(Toggle(L("Calm in fullscreen"), L("Videos, games and slides: the island steps away, only urgent peeks"), s.HideInFullscreen, v => s.HideInFullscreen = v));
        list.Children.Add(Toggle(L("Sounds"), L("Chimes for timers and reminders"), s.Sounds, v => s.Sounds = v));

        list.Children.Add(Section(L("Features")));
        list.Children.Add(Option(L("Den break"), [.. new[] { 5, 10, 15 }.Select(m =>
            Ui.Chip(L("%d min", m), s.BreakMinutes == m, Accent.Rest, () => Model.SetBreakMinutes(m)))]));
        list.Children.Add(Toggle(L("Oasis reminders"), L("Every %d min in your active hours", s.WaterIntervalMinutes), s.WaterReminders,
                                 v => s.WaterReminders = v, Accent.Oasis));
        list.Children.Add(Option(L("Remind every"), [.. new[] { 30, 45, 60, 90 }.Select(m =>
            Ui.Chip($"{m}m", s.WaterIntervalMinutes == m, Accent.Oasis, () => { s.WaterIntervalMinutes = m; Changed(); }))]));
        list.Children.Add(Option(L("Active hours"), [.. new[] { (7, 21), (8, 22), (9, 23) }.Select(h =>
            Ui.Chip($"{h.Item1}–{h.Item2}", s.ActiveHourStart == h.Item1 && s.ActiveHourEnd == h.Item2, Accent.Oasis, () =>
            {
                s.ActiveHourStart = h.Item1;
                s.ActiveHourEnd = h.Item2;
                Changed();
            }))]));

        list.Children.Add(Toggle(L("Pounce on screenshots"), L("New screenshots appear with Copy · Stash · Show"), s.PounceScreenshots, v => s.PounceScreenshots = v, Accent.Stash));
        list.Children.Add(Toggle(L("Pounce on downloads"), L("Finished downloads appear the same way"), s.PounceDownloads, v => s.PounceDownloads = v, Accent.Stash));

        list.Children.Add(Section("Roar & Scout"));
        list.Children.Add(Toggle("Roar", L("Unread counts from WhatsApp, Teams, Outlook and any app that notifies you. Only the number, no permission."),
                                 s.Roar, v => s.Roar = v, Accent.Roar));
        if (s.Roar) list.Children.Add(Toggle(L("Peek on new messages"), L("Not while that app is in front"), s.RoarPeeks, v => s.RoarPeeks = v, Accent.Roar));
        if (s.RoarMuted.Count > 0)
            list.Children.Add(Option(L("Muted"), [.. s.RoarMuted.ToList().Select(id =>
                Ui.Chip($"{AppIcons.Name(id)}  ✕", false, Accent.Roar, () => { Model.Unmute(id); Refresh(); }))]));
        list.Children.Add(Toggle("Scout", L("AI agents and scripts: a peek when they finish or need you"), s.Scout, v => s.Scout = v, Accent.Scout));
        if (s.Scout)
            foreach (var agent in new[] { AgentLinks.Agent.ClaudeCode, AgentLinks.Agent.Codex }) list.Children.Add(AgentRow(agent));

        list.Children.Add(Section(L("About")));
        list.Children.Add(About());

        _scroll = Ui.Scroll(list);
        Children.Add(_scroll);
        _scroll.ScrollToVerticalOffset(offset);
    }

    private void Changed()
    {
        Model.SettingsChanged();
        Refresh();
    }

    private static TextBlock Section(string title)
    {
        var t = Ui.Label(title.ToUpper(Lang.Culture), Ui.Caption - 0.5, Ui.Faint, FontWeights.SemiBold);
        t.Margin = new Thickness(4, 10, 0, 4);
        return t;
    }

    private Border Toggle(string title, string subtitle, bool on, Action<bool> set, Accent? accent = null)
    {
        var g = Columns(Star, Auto);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        text.Children.Add(Ui.Label(title, Ui.Body, weight: FontWeights.Medium));
        var sub = Ui.Label(subtitle, Ui.Caption, Ui.Mist);
        text.Children.Add(sub);
        g.Children.Add(At(text, 0));
        var sw = Ui.Switch(on, accent ?? Accent.Oasis, v =>
        {
            set(v);
            Model.SettingsChanged();
        });
        sw.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(At(sw, 1));
        return Row(g);
    }

    private Border Option(string title, UIElement[] chips)
    {
        var g = Columns(Star, Auto);
        g.Children.Add(At(Ui.Label(title, Ui.Body, weight: FontWeights.Medium), 0));
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in chips)
        {
            if (c is FrameworkElement fe) fe.Margin = new Thickness(4, 0, 0, 0);
            row.Children.Add(c);
        }
        g.Children.Add(At(row, 1));
        return Row(g);
    }

    private static Border Row(UIElement content) => new()
    {
        Child = content,
        MinHeight = 38,
        Padding = new Thickness(10, 5, 8, 5),
        CornerRadius = new CornerRadius(10),
        Background = Ui.Surface,
        Margin = new Thickness(0, 0, 0, 3),
    };

    private Border AgentRow(AgentLinks.Agent agent)
    {
        var state = AgentLinks.State(agent);
        bool installed = AgentLinks.Installed(agent);
        var g = Columns(Star, Auto);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        text.Children.Add(Ui.Label(AgentLinks.Title(agent), Ui.Body, weight: FontWeights.Medium));
        text.Children.Add(Ui.Label(state switch
        {
            LinkState.On => L("Connected · reports to Scout"),
            LinkState.Stale => L("Connected to an older copy · reconnect"),
            _ => installed ? L("Adds a small hook to %@ (a backup is kept)", AgentLinks.FileOf(agent)) : L("%@ isn't set up on this PC", AgentLinks.Title(agent)),
        }, Ui.Caption, Ui.Mist));
        g.Children.Add(At(text, 0));
        void Set(bool on)
        {
            var problem = AgentLinks.Set(agent, on);
            Model.RaiseToast(problem ?? (on ? L("%@ connected · new sessions report to Scout", AgentLinks.Title(agent)) : L("%@ disconnected", AgentLinks.Title(agent))));
            Refresh();
        }
        var button = state switch
        {
            LinkState.On => Ui.TextButton(L("Disconnect"), () => Set(false)),
            LinkState.Stale => Ui.Pill(L("Reconnect"), () => Set(true), Accent.Scout, prominent: true),
            _ => Ui.Pill(L("Connect"), () => Set(true), Accent.Scout, prominent: installed),
        };
        button.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(At(button, 1));
        return Row(g);
    }

    private Border About()
    {
        var g = new StackPanel();
        var version = typeof(SettingsPane).Assembly.GetName().Version;
        g.Children.Add(Ui.Label($"Ezkiel Notch for Windows {version?.ToString(3)} · " + L("Preview"), Ui.Body, weight: FontWeights.SemiBold));
        var path = Model.Store?.Path ?? "";
        var where = Ui.Label(path, Ui.Caption, Ui.Faint);
        where.TextTrimming = TextTrimming.CharacterEllipsis;
        where.ToolTip = path;
        g.Children.Add(where);
        var buttons = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = Auto });
        var left = Ui.Row(6,
            Ui.Pill("ezkielyna.store/notch", () => Open("https://ezkielyna.store/notch"), Accent.Neutral, glyph: "\uE774"),
            Ui.Pill(L("Data folder"), () => Open(Path.GetDirectoryName(path) ?? ""), Accent.Neutral, glyph: "\uE8B7"));
        buttons.Children.Add(At(left, 0));
        buttons.Children.Add(At(Ui.Pill(_confirmQuit ? L("Click to quit") : L("Quit"), Quit, Accent.Hunt, prominent: _confirmQuit, glyph: "\uE7E8"), 2));
        g.Children.Add(buttons);
        return Row(g);
    }

    private void Quit()
    {
        if (_confirmQuit)
        {
            Application.Current.Shutdown();
            return;
        }
        _confirmQuit = true;
        Refresh();
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            _confirmQuit = false;
            Refresh();
        };
        t.Start();
    }

    private static void Open(string target)
    {
        if (target.Length == 0) return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception) { }
    }
}
