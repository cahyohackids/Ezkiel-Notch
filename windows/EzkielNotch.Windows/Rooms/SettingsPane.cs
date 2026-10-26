using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        list.Children.Add(StyleCard());
        if (s.Style == "island")
        {
            list.Children.Add(Option(L("Size"), [.. new[] { ("small", "Small"), ("medium", "Medium"), ("large", "Large") }.Select(o =>
                Ui.Chip(L(o.Item2), s.IslandSize == o.Item1, Accent.Neutral, () => { s.IslandSize = o.Item1; Changed(); }))]));
            list.Children.Add(Option(L("Gap"), [.. new[] { ("tight", "Tight"), ("medium", "Medium"), ("roomy", "Roomy") }.Select(o =>
                Ui.Chip(L(o.Item2), s.IslandGap == o.Item1, Accent.Neutral, () => { s.IslandGap = o.Item1; Changed(); }))]));
            list.Children.Add(Toggle(L("Shadow"), L("A soft shadow under the floating island"), s.IslandShadow, v => s.IslandShadow = v, Accent.Neutral));
        }
        list.Children.Add(Option(L("Closed face"), [.. new[] { ("eyes", "Eyes"), ("lion", "Lion"), ("off", "Off") }.Select(o =>
            Ui.Chip(L(o.Item2), s.ClosedFace == o.Item1, Accent.Den, () => { s.ClosedFace = o.Item1; Changed(); }))]));
        list.Children.Add(Toggle(L("Lion naps when idle"), L("Wakes up when you're back"), s.MascotSleeps, v => s.MascotSleeps = v, Accent.Den));
        list.Children.Add(Toggle(L("Open on hover"), L("Click always works too"), s.ExpandOnHover, v => s.ExpandOnHover = v));
        list.Children.Add(Option(L("Hover delay"), [.. new[] { 0.05, 0.12, 0.25, 0.45 }.Select(d =>
            Ui.Chip(d < 0.1 ? L("Instant") : $"{(int)(d * 1000)} ms", Math.Abs(s.HoverDelay - d) < 0.01, Accent.Neutral, () => { s.HoverDelay = d; Changed(); }))]));
        list.Children.Add(Option(L("Shortcut"), [.. new[] { "controlOptionN", "controlOptionSpace", "optionCommandI" }.Select(h =>
            Ui.Chip(IslandWindow.HotkeyLabel(h), s.Hotkey == h, Accent.Neutral, () => { s.Hotkey = h; Changed(); }))]));
        list.Children.Add(Toggle(L("Launch at login"), L("Start with Windows"), Startup.IsEnabled, v =>
        {
            s.LaunchAtLogin = Startup.Set(v);
        }));
        list.Children.Add(Toggle(L("Calm in fullscreen"), L("Videos, games and slides: the island steps away, only urgent peeks"), s.HideInFullscreen, v => s.HideInFullscreen = v));
        list.Children.Add(Toggle(L("Sounds"), L("Chimes for timers and reminders"), s.Sounds, v => s.Sounds = v));

        list.Children.Add(Section("Whiskers"));
        list.Children.Add(Toggle(L("Caps Lock & keyboard"), L("Caps Lock and keyboard language"), s.SystemOverlays, v => s.SystemOverlays = v, Accent.Neutral));
        list.Children.Add(Toggle(L("Wi-Fi & headphones"), L("When they connect or drop"), s.ConnectionPeeks, v => s.ConnectionPeeks = v, Accent.Neutral));

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

        list.Children.Add(Toggle("Prowl", L("Green and orange dots while a camera or microphone is on"), s.Prowl, v => s.Prowl = v, Accent.Roar));
        list.Children.Add(Toggle("Rally", L("Next meeting in Hunt, a peek with Join 5 min before"), s.Rally, v => { s.Rally = v; Dispatcher.BeginInvoke(Refresh); }, Accent.Rally));
        if (s.Rally)
        {
            list.Children.Add(TextRow(L("Calendar address (iCal)"), L("Google: Settings › your calendar › Secret address in iCal format. Outlook: Shared calendars › Publish › ICS."),
                                      s.RallyCalendarUrl, "https://…/basic.ics", v =>
                                      {
                                          s.RallyCalendarUrl = v.Trim();
                                          Model.SettingsChanged();
                                          Refresh();
                                      }));
            list.Children.Add(Toggle(L("Meeting countdown"), L("\"in 5m\" in the closed island"), s.RallyCountdown, v => s.RallyCountdown = v, Accent.Rally));
        }
        list.Children.Add(WeatherRow());
        if (s.WeatherCity.Length > 0)
            list.Children.Add(Option(L("Temperature"), [
                Ui.Chip("°C", !s.Fahrenheit, Accent.Neutral, () => { s.Fahrenheit = false; Changed(); }),
                Ui.Chip("°F", s.Fahrenheit, Accent.Neutral, () => { s.Fahrenheit = true; Changed(); })]));

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
        list.Children.Add(UpdateRow());
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

    /// <summary>Notch or Island, as two tiles with a tiny drawing of each (the Mac's StyleCard).</summary>
    private Grid StyleCard()
    {
        var g = Columns(Star, Px(6), Star);
        g.Margin = new Thickness(0, 0, 0, 3);
        g.Children.Add(At(StyleTile("notch", L("Notch"), L("Hangs from the top edge")), 0));
        g.Children.Add(At(StyleTile("island", L("Island"), L("Floats like the Dynamic Island")), 2));
        return g;
    }

    private Border StyleTile(string style, string title, string subtitle)
    {
        bool on = Model.Settings.Style == style;
        var preview = new Grid { Height = 26, Background = Ui.Frozen(new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF))) };
        preview.Children.Add(new Border
        {
            Width = 54,
            Height = style == "island" ? 12 : 11,
            CornerRadius = style == "island" ? new CornerRadius(6) : new CornerRadius(0, 0, 5, 5),
            Background = Brushes.Black,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, style == "island" ? 4 : 0, 0, 0),
        });
        var s = new StackPanel();
        s.Children.Add(new Border { Child = preview, CornerRadius = new CornerRadius(6), ClipToBounds = true, Margin = new Thickness(0, 0, 0, 6) });
        s.Children.Add(Ui.Label(title, Ui.Body, weight: FontWeights.SemiBold));
        s.Children.Add(Ui.Label(subtitle, Ui.Caption, Ui.Mist));
        var tile = new Border
        {
            Child = s,
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(12),
            Background = on ? Accent.Den.Tint(0x22) : Ui.Surface,
            BorderBrush = on ? Accent.Den.DeepBrush : Brushes.Transparent,
            BorderThickness = new Thickness(1),
        };
        return Ui.Clickable(tile, () =>
        {
            Model.Settings.Style = style;
            Changed();
        }, pressScale: 0.97);
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

    /// <summary>A labelled text field that saves on Return or when it loses focus.</summary>
    private Border TextRow(string title, string help, string value, string placeholder, Action<string> save)
    {
        var s = new StackPanel();
        s.Children.Add(Ui.Label(title, Ui.Body, weight: FontWeights.Medium));
        var helpText = Ui.Label(help, Ui.Caption, Ui.Mist, trim: false);
        helpText.TextWrapping = TextWrapping.Wrap;
        s.Children.Add(helpText);
        var (box, input) = Ui.Field(placeholder, i => save(i.Text), 28);
        input.Text = value;
        input.LostKeyboardFocus += (_, _) => { if (input.Text.Trim() != value) save(input.Text); };
        box.Margin = new Thickness(0, 6, 0, 0);
        s.Children.Add(box);
        return Row(s);
    }

    private string _weatherQuery = "";

    /// <summary>Weather: type a city, pick it from the results (no location permission).</summary>
    private Border WeatherRow()
    {
        var s = Model.Settings;
        var weather = WeatherSource?.Invoke();
        var stack = new StackPanel();
        var head = Columns(Star, Auto);
        var text = new StackPanel();
        text.Children.Add(Ui.Label(L("Weather"), Ui.Body, weight: FontWeights.Medium));
        text.Children.Add(Ui.Label(s.WeatherCity.Length > 0 ? s.WeatherCity : L("Type your city · Open-Meteo, no location permission"), Ui.Caption, Ui.Mist));
        head.Children.Add(At(text, 0));
        if (s.WeatherCity.Length > 0)
            head.Children.Add(At(Ui.TextButton(L("Turn off"), () =>
            {
                s.WeatherCity = "";
                Changed();
            }), 1));
        stack.Children.Add(head);
        var (box, input) = Ui.Field(L("City"), i =>
        {
            _weatherQuery = i.Text;
            if (weather is not null) _ = weather.Search(i.Text, Lang.IsIndonesian);
        }, 28);
        input.Text = _weatherQuery;
        box.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(box);
        if (weather is { Results.Count: > 0 })
        {
            var results = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var p in weather.Results)
            {
                var chip = Ui.Chip(p.Detail.Length > 0 ? $"{p.Name} · {p.Detail}" : p.Name, false, Accent.Oasis, () =>
                {
                    s.WeatherCity = p.Name;
                    s.WeatherLatitude = p.Latitude;
                    s.WeatherLongitude = p.Longitude;
                    _weatherQuery = "";
                    weather.ClearResults();
                    Changed();
                });
                chip.Margin = new Thickness(0, 0, 4, 4);
                results.Children.Add(chip);
            }
            stack.Children.Add(results);
        }
        else if (weather is { Searching: true })
            stack.Children.Add(Ui.Label(L("Searching…"), Ui.Caption, Ui.Faint));
        return Row(stack);
    }

    /// <summary>Set by the window: the weather client (for city search).</summary>
    public Func<WeatherClient>? WeatherSource { get; set; }

    /// <summary>Set by the window: the updater.</summary>
    public Func<Updater>? UpdaterSource { get; set; }

    /// <summary>"Ezkiel Notch 0.3.0 is out · Update now", or the daily-check switch with "Check now".</summary>
    private Border UpdateRow()
    {
        var s = Model.Settings;
        var u = UpdaterSource?.Invoke();
        var g = Columns(Star, Auto);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        string title, sub;
        UIElement action;
        switch (u?.State)
        {
            case Updater.Phase.Available or Updater.Phase.Failed when u.Release is { } r && AppVersion.IsNewer(r.Version, Updater.Current):
                title = L("Ezkiel Notch %@ is out", string.Join('.', AppVersion.Parts(r.Version)));
                sub = u.State == Updater.Phase.Failed ? u.Problem ?? "" : L("Downloads, checks and restarts by itself · your data stays");
                action = Ui.Pill(u.State == Updater.Phase.Failed ? L("Try again") : L("Update now"), () => _ = u.Install(), Accent.Den, prominent: true, glyph: "\uE896");
                break;
            case Updater.Phase.Downloading or Updater.Phase.Verifying or Updater.Phase.Installing:
                title = L("Updating…");
                sub = u!.State switch { Updater.Phase.Downloading => L("Downloading…"), Updater.Phase.Verifying => L("Checking the download…"), _ => L("Installing…") };
                action = new Border();
                break;
            default:
                title = L("Check for updates");
                sub = u?.State switch
                {
                    Updater.Phase.Checking => L("Checking…"),
                    Updater.Phase.UpToDate => L("You're up to date (%@)", Updater.Current),
                    Updater.Phase.Failed => u.Problem ?? "",
                    _ => L("Once a day, from GitHub"),
                };
                var row = Ui.Row(8, Ui.TextButton(L("Check now"), () => { if (u is not null) _ = u.Check(force: true); }),
                                 Ui.Switch(s.CheckUpdates, Accent.Oasis, v => { s.CheckUpdates = v; Model.SettingsChanged(); }));
                row.VerticalAlignment = VerticalAlignment.Center;
                action = row;
                break;
        }
        text.Children.Add(Ui.Label(title, Ui.Body, weight: FontWeights.Medium));
        text.Children.Add(Ui.Label(sub, Ui.Caption, Ui.Mist));
        g.Children.Add(At(text, 0));
        if (action is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(At(action, 1));
        return Row(g);
    }

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
