using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EzkielNotch.Core;
using EzkielNotch.Island;
using EzkielNotch.Platform;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Rooms;

/// <summary>The Mac's settings sections, in the Mac's order (its SettingsSection).</summary>
public enum SettingsSection { General, Whiskers, Roar, Features, About }

/// <summary>Settings, laid out like the Mac's: section chips on top (General · Whiskers · Roar · Features · About),
/// then that section's rows, each with an icon, a title, a subtitle and its control on the right.</summary>
public sealed class SettingsPane(NotchModel model) : Pane(model, Room.Settings)
{
    /// <summary>The section on show. Remembered while the app runs, like the Mac's model.settingsSection.</summary>
    public static SettingsSection Section { get; set; } = SettingsSection.General;

    public static readonly SettingsSection[] Sections =
        [SettingsSection.General, SettingsSection.Whiskers, SettingsSection.Roar, SettingsSection.Features, SettingsSection.About];

    public static string SectionTitle(SettingsSection s) => s switch
    {
        SettingsSection.General => "General",
        SettingsSection.Whiskers => "Whiskers",
        SettingsSection.Roar => "Roar",
        SettingsSection.Features => "Features",
        _ => "About",
    };

    // The Mac's gold (Accent.gold, and Theme.gold for a selected card's border) and its Whiskers accent.
    private static readonly Accent Gold = new(Accent.Hex(0xFFE3B5), Accent.Hex(0xFFC46B));
    private static readonly Accent Whisker = new(Accent.Hex(0xF5F1EA), Accent.Hex(0xC7C2BA));
    private static readonly Brush GoldBorder = Ui.Frozen(new SolidColorBrush(Accent.Hex(0xFFCF87)));
    private static readonly Brush ChipRest = Ui.Frozen(new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush CardHover = Ui.Frozen(new SolidColorBrush(Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF)));
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    private static readonly BitmapImage LionPicture = new(new Uri("pack://application:,,,/Assets/lion.png"));

    private ScrollViewer? _scroll;
    private SettingsSection _built = Section;
    private bool _confirmQuit;
    /// <summary>This build's text fields by name, and what's typed in them but not saved yet.</summary>
    private readonly Dictionary<string, TextBox> _fields = [];
    private readonly Dictionary<string, string> _drafts = [];
    private bool _rebuilding;

    /// <summary>Rebuilds every row (a city search result, an update, a chip). A field you're typing in comes back
    /// with its text, caret and keyboard, so a refresh never eats what you typed.</summary>
    public override void Refresh()
    {
        if (_rebuilding) return;
        var focused = _fields.FirstOrDefault(f => f.Value.IsKeyboardFocused);
        int caret = focused.Value?.CaretIndex ?? 0;
        _rebuilding = true;
        try { Build(); }
        finally { _rebuilding = false; }
        if (focused.Key is { } key && _fields.TryGetValue(key, out var again))
            Dispatcher.BeginInvoke(() =>
            {
                again.Focus();
                again.CaretIndex = Math.Min(caret, again.Text.Length);
            });
    }

    /// <summary>Another part of the app picked a section (Roar's gear, the update peek, the tray): show it.</summary>
    public override void Shown(bool visible)
    {
        if (visible && _built != Section) Refresh();
    }

    /// <summary>Shows one section (the chips, the self-test).</summary>
    public void ShowSection(SettingsSection section)
    {
        Section = section;
        Refresh();
    }

    private void Build()
    {
        // Same section: keep the scroll position. Another section starts at the top.
        double offset = _built == Section ? _scroll?.VerticalOffset ?? 0 : 0;
        _built = Section;
        _fields.Clear();
        Children.Clear();
        RowDefinitions.Clear();
        RowDefinitions.Add(new RowDefinition { Height = Auto });
        RowDefinitions.Add(new RowDefinition { Height = Star });

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var section in Sections)
        {
            var chip = Chip(L(SectionTitle(section)), Section == section, Gold, () => ShowSection(section));
            if (chips.Children.Count > 0) chip.Margin = new Thickness(5, 0, 0, 0);
            chips.Children.Add(chip);
        }
        Children.Add(At(chips, 0, 0));

        var list = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        switch (Section)
        {
            case SettingsSection.General: General(list); break;
            case SettingsSection.Whiskers: Whiskers(list); break;
            case SettingsSection.Roar: RoarAndScout(list); break;
            case SettingsSection.Features: Features(list); break;
            default: About(list); break;
        }
        // The last row needs no gap under it.
        if (list.Children.Count > 0 && list.Children[^1] is FrameworkElement last)
            last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);

        _scroll = Ui.Scroll(list);
        // The Mac's scrollEdgeFade(8): rows cut off at the top or bottom fade out, so they read as "more".
        _scroll.OpacityMask = Ui.Frozen(new LinearGradientBrush(new GradientStopCollection
        {
            new(Colors.Transparent, 0),
            new(Colors.Black, 0.05),
            new(Colors.Black, 0.95),
            new(Colors.Transparent, 1),
        }, new Point(0, 0), new Point(0, 1)));
        Children.Add(At(_scroll, 0, 1));
        _scroll.ScrollToVerticalOffset(offset);
    }

    // MARK: General

    private void General(StackPanel list)
    {
        var s = Model.Settings;
        list.Children.Add(StyleCard());
        if (s.Style == "island")
        {
            list.Children.Add(Option("", L("Size"), [.. new[] { ("small", "Small"), ("medium", "Medium"), ("large", "Large") }.Select(o =>
                Chip(L(o.Item2), s.IslandSize == o.Item1, Gold, () => { s.IslandSize = o.Item1; Changed(); }))]));
            list.Children.Add(Option("", L("Gap"), [.. new[] { ("tight", "Tight"), ("medium", "Medium"), ("roomy", "Roomy") }.Select(o =>
                Chip(L(o.Item2), s.IslandGap == o.Item1, Gold, () => { s.IslandGap = o.Item1; Changed(); }))]));
            list.Children.Add(Toggle("", "Liquid Glass", L("See-through glass, tinted dark so text stays readable"), s.IslandGlass, v => s.IslandGlass = v));
            list.Children.Add(Toggle("", L("Shadow"), L("A soft shadow under the floating island"), s.IslandShadow, v => s.IslandShadow = v));
        }
        list.Children.Add(Option("", L("Language"), [.. new[] { AppLanguage.Auto, AppLanguage.En, AppLanguage.Id }.Select(lang =>
            Chip(lang switch { AppLanguage.En => "English", AppLanguage.Id => "Indonesia", _ => L("Auto") }, s.Language == lang, Gold, () =>
            {
                s.Language = lang;
                Model.SettingsChanged(everything: true);
            }))]));
        list.Children.Add(Toggle("", L("Open on hover"), L("Click always works too"), s.ExpandOnHover, v => s.ExpandOnHover = v));
        list.Children.Add(Option("", L("Hover delay"), [.. new[] { 0.05, 0.12, 0.25, 0.45 }.Select(d =>
            Chip(d < 0.1 ? L("Instant") : $"{(int)(d * 1000)} ms", Math.Abs(s.HoverDelay - d) < 0.01, Gold, () => { s.HoverDelay = d; Changed(); }))]));
        list.Children.Add(Option("", L("Shortcut"), [.. new[] { "controlOptionN", "controlOptionSpace", "optionCommandI" }.Select(h =>
            Chip(IslandWindow.HotkeyLabel(h), s.Hotkey == h, Gold, () => { s.Hotkey = h; Changed(); }))]));
        list.Children.Add(Toggle("", L("Launch at login"), L("Start with Windows"), Startup.IsEnabled, v => s.LaunchAtLogin = Startup.Set(v)));
        list.Children.Add(Option("", L("Closed face"), [.. new[] { ("eyes", "Eyes"), ("lion", "Lion"), ("off", "Off") }.Select(o =>
            Chip(L(o.Item2), s.ClosedFace == o.Item1, Gold, () => { s.ClosedFace = o.Item1; Changed(); }))]));
        if (s.ClosedFace == "eyes") list.Children.Add(EyeStyleCard());
        list.Children.Add(Toggle("", L("Lion naps when idle"), L("Wakes up when you're back"), s.MascotSleeps, v => s.MascotSleeps = v));
        list.Children.Add(Toggle("", L("Minimal closed island"), L("Hide the wings until something is live"), s.MinimalClosed, v => s.MinimalClosed = v));
        list.Children.Add(Toggle("", L("Calm in fullscreen"), L("Videos, games and slides: the island steps away, only urgent peeks"), s.HideInFullscreen, v => s.HideInFullscreen = v));
        list.Children.Add(Toggle("", L("Quiet during focus"), L("Roar and finished agents wait while Den runs"), s.QuietDuringFocus, v => s.QuietDuringFocus = v, Accent.Den));
        list.Children.Add(Toggle("", L("Hide from screen sharing"), L("Teams, Zoom, Meet and recordings don't see the island"), s.HideFromScreenShare, v => s.HideFromScreenShare = v));
        list.Children.Add(Toggle("", L("Sounds"), L("Chimes for timers and reminders"), s.Sounds, v => s.Sounds = v));
    }

    // MARK: Whiskers (small notifications)

    private void Whiskers(StackPanel list)
    {
        var s = Model.Settings;
        list.Children.Add(Toggle("", L("Caps Lock & keyboard"), L("Caps Lock and keyboard language"), s.SystemOverlays, v => s.SystemOverlays = v, Whisker));
        list.Children.Add(Toggle("", L("Wi-Fi & headphones"), L("When they connect or drop"), s.ConnectionPeeks, v => s.ConnectionPeeks = v, Whisker));
        list.Children.Add(Toggle("", L("Pounce on screenshots"), L("New screenshots appear with Copy · Stash · Show"), s.PounceScreenshots, v => s.PounceScreenshots = v, Accent.Stash));
        list.Children.Add(Toggle("", L("Pounce on downloads"), L("Finished downloads appear the same way"), s.PounceDownloads, v => s.PounceDownloads = v, Accent.Stash));
    }

    // MARK: Roar & Scout

    private void RoarAndScout(StackPanel list)
    {
        var s = Model.Settings;
        list.Children.Add(Toggle("", "Roar", L("Unread counts from WhatsApp, Teams, Outlook and any app that notifies you. Only the number, no permission."),
                                 s.Roar, v => s.Roar = v, Accent.Roar, rebuild: true));
        if (s.Roar)
        {
            list.Children.Add(Toggle("", L("Peek on new messages"), L("Not while that app is in front"), s.RoarPeeks, v => s.RoarPeeks = v, Accent.Roar));
            if (s.RoarMuted.Count > 0)
                list.Children.Add(Option("", L("Muted"), [.. s.RoarMuted.ToList().Select(id =>
                    Chip($"{AppIcons.Name(id)}  ✕", false, Accent.Roar, () => { Model.Unmute(id); Refresh(); }, L("Unmute")))]));
        }
        list.Children.Add(Toggle("", "Scout", L("AI agents and scripts: a peek when they finish or need you"), s.Scout, v => s.Scout = v, Accent.Scout, rebuild: true));
        if (s.Scout)
        {
            foreach (var agent in new[] { AgentLinks.Agent.ClaudeCode, AgentLinks.Agent.Codex }) list.Children.Add(AgentRow(agent));
            list.Children.Add(ScriptRow());
        }
    }

    /// <summary>Any script can tell Scout it's done, through the ezkielnotch:// link (the Mac's "Any script or app").</summary>
    private Border ScriptRow()
    {
        const string example = "Start-Process \"ezkielnotch://notify?title=Done&body=…\"";
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Label(L("Any script or app"), Ui.Body, weight: FontWeights.Medium));
        var command = Ui.Label(example, Ui.Caption, Ui.Mist);
        command.FontFamily = Mono;
        command.ToolTip = example;
        text.Children.Add(command);
        var copy = Ui.Pill(L("Copy"), () =>
        {
            try
            {
                Clipboard.SetText("Start-Process \"ezkielnotch://notify?title=Build%20done&body=All%20green\"");
                Model.RaiseToast(L("Copied · run it from any script"));
            }
            catch (Exception)
            {
                // Clipboard busy: nothing copied, nothing claimed.
            }
        }, Accent.Neutral, glyph: "");
        return Card(Lead("", Ui.Mist, text, copy));
    }

    private Border AgentRow(AgentLinks.Agent agent)
    {
        var state = AgentLinks.State(agent);
        bool installed = AgentLinks.Installed(agent);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Label(AgentLinks.Title(agent), Ui.Body, weight: FontWeights.Medium));
        text.Children.Add(Ui.Label(state switch
        {
            LinkState.On => L("Connected · reports to Scout"),
            LinkState.Stale => L("Connected to an older copy · reconnect"),
            _ => installed ? L("Adds a small hook to %@ (a backup is kept)", AgentLinks.FileOf(agent)) : L("%@ isn't set up on this PC", AgentLinks.Title(agent)),
        }, Ui.Caption, state == LinkState.Stale ? Accent.Den.LightBrush : Ui.Mist));
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
        var tint = state == LinkState.On ? Accent.Scout.DeepBrush : Ui.Mist;
        return Card(Lead(agent == AgentLinks.Agent.ClaudeCode ? "" : "", tint, text, button));
    }

    // MARK: Features

    private void Features(StackPanel list)
    {
        var s = Model.Settings;
        AskSettingsRows.AddAskRows(list, Model, Refresh);
        SyncSettingsRows.Add(list);
        list.Children.Add(WeatherRow());
        list.Children.Add(Toggle("", "Prowl", L("Green and orange dots while a camera or microphone is on"), s.Prowl, v => s.Prowl = v, Accent.Roar));
        list.Children.Add(Toggle("", "Rally", L("Next meeting in Hunt, a peek with Join 5 min before"), s.Rally, v => s.Rally = v, Accent.Rally, rebuild: true));
        if (s.Rally)
        {
            // Windows reads the calendar from its secret iCal address (no calendar app to ask).
            list.Children.Add(TextRow("", L("Calendar address (iCal)"), L("Google: Settings › your calendar › Secret address in iCal format. Outlook: Shared calendars › Publish › ICS."),
                                      s.RallyCalendarUrl, "https://…/basic.ics", v =>
                                      {
                                          s.RallyCalendarUrl = v.Trim();
                                          Model.SettingsChanged();
                                          Refresh();
                                      }));
            list.Children.Add(Toggle("", L("Meeting countdown"), L("\"Meeting in 7 min\" in the closed island, from 10 min before"), s.RallyCountdown, v => s.RallyCountdown = v, Accent.Rally));
        }
        LiveSettingsRows.AddLiveRows(list, s, (g, t, sub, on, set, a) => Toggle(g, t, sub, on, set, a), Option, Changed);
        SysMonSettingsRows.AddSysMonRows(list, s, (g, t, sub, on, set, a) => Toggle(g, t, sub, on, set, a), Option, Changed, Refresh);
        // Auto follows any player in Windows' media controls.
        list.Children.Add(Option("", L("Purr source"), [.. MusicSource.All.Select(src =>
            Chip(PurrPane.SourceName(src), s.MusicSource == src, Accent.Purr, () => { s.MusicSource = src; Changed(); }))]));
        list.Children.Add(Toggle("", "Chorus", L("Synced lyrics from LRCLIB, only asked while on"), s.ShowLyrics, v => s.ShowLyrics = v, Accent.Purr));
        list.Children.Add(Toggle("", L("Chorus under the notch"), L("Sing along while it's closed"), s.ClosedLyrics, v => s.ClosedLyrics = v, Accent.Purr));
        list.Children.Add(Option("", L("Den break"), [.. new[] { 5, 10, 15 }.Select(m =>
            Chip(L("%d min", m), s.BreakMinutes == m, Accent.Rest, () => { Model.SetBreakMinutes(m); Refresh(); }))]));
        list.Children.Add(Toggle("", L("Oasis reminders"), L("Every %d min in your active hours", s.WaterIntervalMinutes), s.WaterReminders,
                                 v => s.WaterReminders = v, Accent.Oasis));
        list.Children.Add(Option("", L("Remind every"), [.. new[] { 30, 45, 60, 90 }.Select(m =>
            Chip($"{m}m", s.WaterIntervalMinutes == m, Accent.Oasis, () => { s.WaterIntervalMinutes = m; Changed(); }))]));
        list.Children.Add(Option("", L("Active hours"), [.. new[] { (7, 21), (8, 22), (9, 23) }.Select(h =>
            Chip($"{h.Item1}–{h.Item2}", s.ActiveHourStart == h.Item1 && s.ActiveHourEnd == h.Item2, Accent.Oasis, () =>
            {
                s.ActiveHourStart = h.Item1;
                s.ActiveHourEnd = h.Item2;
                Changed();
            }))]));
    }

    private string _weatherQuery = "";
    /// <summary>The City field (shown while no city is set), so the self-test can type into it.</summary>
    public TextBox? CityField => _fields.GetValueOrDefault("city");

    /// <summary>Weather: type a city, pick it from the results (no location permission). With a city set,
    /// °C / °F and Remove, like the Mac's WeatherRow.</summary>
    private Border WeatherRow()
    {
        var s = Model.Settings;
        var weather = WeatherSource?.Invoke();
        var stack = new StackPanel();
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Label(L("Weather"), Ui.Body, weight: FontWeights.Medium));
        text.Children.Add(Ui.Label(WeatherSubtitle(weather), Ui.Caption, Ui.Mist));
        UIElement trailing;
        if (s.WeatherCity.Length == 0)
        {
            void Search(string q)
            {
                _weatherQuery = q;
                if (weather is not null && q.Trim().Length > 0) _ = weather.Search(q.Trim(), Lang.IsIndonesian);
            }
            var (box, input) = Ui.Field(L("City"), i => Search(i.Text), 26);
            input.Text = _weatherQuery;
            input.TextChanged += (_, _) => { if (!_rebuilding) _weatherQuery = input.Text; };
            _fields["city"] = input;
            box.Width = 130;
            // Return searches, and so does the button (for anyone who doesn't think to press Return).
            trailing = Ui.Row(4, box, Ui.IconButton("", L("Search"), () => Search(input.Text), 26, Accent.Oasis.LightBrush));
        }
        else
        {
            trailing = Ui.Row(4,
                Chip("°C", !s.Fahrenheit, Gold, () => { s.Fahrenheit = false; Changed(); }),
                Chip("°F", s.Fahrenheit, Gold, () => { s.Fahrenheit = true; Changed(); }),
                Ui.TextButton(L("Remove"), () =>
                {
                    s.WeatherCity = "";
                    _weatherQuery = "";
                    Changed();
                }));
        }
        stack.Children.Add(Lead("", Accent.Den.LightBrush, text, trailing));
        if (s.WeatherCity.Length == 0 && weather is { Results.Count: > 0 })
        {
            var results = new WrapPanel { Margin = new Thickness(28, 6, 0, 0) };
            foreach (var p in weather.Results)
            {
                var chip = Chip(p.Detail.Length > 0 ? $"{p.Name}, {p.Detail}" : p.Name, false, Accent.Oasis, () =>
                {
                    s.WeatherCity = p.Name;
                    s.WeatherLatitude = p.Latitude;
                    s.WeatherLongitude = p.Longitude;
                    _weatherQuery = "";
                    weather.ClearResults();
                    Changed();
                });
                chip.Margin = new Thickness(0, 0, 5, 5);
                results.Children.Add(chip);
            }
            stack.Children.Add(results);
        }
        return Card(stack);
    }

    private string WeatherSubtitle(WeatherClient? weather)
    {
        var s = Model.Settings;
        if (s.WeatherCity.Length == 0)
            return weather is { Searching: true } ? L("Searching…") : L("Type a city and press Return · no location access");
        if (weather?.Current is not { } now) return s.WeatherCity;
        string T(double c) => $"{Math.Round(s.Fahrenheit ? c * 9 / 5 + 32 : c):0}°";
        return $"{s.WeatherCity} · {L(WeatherClient.Word(now.Code))} {T(now.Temperature)} · {T(now.Low)}–{T(now.High)}";
    }

    /// <summary>Set by the window: the weather client (for city search).</summary>
    public Func<WeatherClient>? WeatherSource { get; set; }

    /// <summary>Set by the window: the updater.</summary>
    public Func<Updater>? UpdaterSource { get; set; }

    // MARK: About

    private void About(StackPanel list)
    {
        var u = UpdaterSource?.Invoke();
        bool waiting = UpdateWaiting(u);
        // A waiting update goes first, where it's seen without scrolling.
        if (waiting) list.Children.Add(UpdateRow(u));
        list.Children.Add(AboutCard());
        list.Children.Add(LaunchTiles());
        if (!waiting) list.Children.Add(UpdateRow(u));
        list.Children.Add(AboutButtons());
    }

    private static bool UpdateWaiting(Updater? u) =>
        u is { State: not (Updater.Phase.Idle or Updater.Phase.Checking or Updater.Phase.UpToDate), Release: { } r } && AppVersion.IsNewer(r.Version, Updater.Current);

    /// <summary>The lion, the name and version, and what leaves this PC (the Mac's About card).</summary>
    private Border AboutCard()
    {
        var g = Columns(Auto, Px(12), Star);
        g.Children.Add(At(new Image { Source = LionPicture, Width = 56, Height = 56, VerticalAlignment = VerticalAlignment.Center }, 0));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Label(L("Ezkiel Notch"), Ui.Title, weight: FontWeights.SemiBold));
        text.Children.Add(Ui.Label(L("Version %@ · open source", Updater.Current), Ui.Secondary, Ui.Mist));
        var privacy = Ui.Label(L("Everything stays on this PC. Chorus asks LRCLIB for lyrics only while it's on."), Ui.Caption, Ui.Mist, trim: false);
        privacy.TextWrapping = TextWrapping.Wrap;
        privacy.Margin = new Thickness(0, 1, 0, 0);
        text.Children.Add(privacy);
        g.Children.Add(At(text, 2));
        var card = Card(g);
        card.Padding = new Thickness(10);
        return card;
    }

    /// <summary>Six shortcuts, as on the Mac (Finder and Terminal become File Explorer and Windows Terminal).</summary>
    private Grid LaunchTiles()
    {
        (string Glyph, string Title, Accent Accent, Action Open)[] items =
        [
            ("", "Ezkielyna", Gold, () => Open("https://ezkielyna.store")),
            ("", "Claude", Accent.Den, () =>
            {
                try { Clipboard.SetText("Help me plan my day. Ask what matters most, then suggest a realistic schedule with breaks."); }
                catch (Exception) { }
                Open("https://claude.ai/new");
                Model.RaiseToast(L("Prompt copied · paste it in Claude"));
            }),
            ("", L("Explorer"), Accent.Oasis, () => Open(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))),
            ("", "Terminal", new Accent(Accent.Hex(0x9BF0C9), Accent.Hex(0x30D158)), () => { if (!Open("wt.exe")) Open("powershell.exe"); }),
            ("", L("Downloads"), Accent.Stash, () => Open(Files.Downloads)),
            ("", L("Settings"), Accent.Neutral, () => Open("ms-settings:")),
        ];
        var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        for (int i = 0; i < items.Length; i++)
        {
            if (i > 0) g.ColumnDefinitions.Add(new ColumnDefinition { Width = Px(6) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
            var (glyph, title, accent, open) = items[i];
            var icon = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(8),
                Background = accent.Tint(0x29),
                Child = Ui.Glyph(glyph, 13, accent.Vertical),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
            };
            var label = Ui.Label(title, Ui.Caption, weight: FontWeights.Medium);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            var s = new StackPanel();
            s.Children.Add(icon);
            s.Children.Add(label);
            var tile = new Border { Child = s, Padding = new Thickness(2, 6, 2, 6), CornerRadius = new CornerRadius(10), Background = Ui.Surface };
            Ui.Clickable(tile, open, h => tile.Background = h ? CardHover : Ui.Surface, title);
            g.Children.Add(At(tile, i * 2));
        }
        return g;
    }

    /// <summary>The website, the data folder, and Quit (asks once more).</summary>
    private Grid AboutButtons()
    {
        var path = Model.Store?.Path ?? "";
        var g = Columns(Auto, Star, Auto);
        var data = Ui.Pill(L("Data folder"), () => Open(Path.GetDirectoryName(path) ?? ""), Accent.Neutral, glyph: "", tip: path);
        g.Children.Add(At(Ui.Row(6, Ui.Pill("ezkielyna.store/notch", () => Open("https://ezkielyna.store/notch"), Accent.Neutral, glyph: ""), data), 0));
        g.Children.Add(At(QuitButton(), 2));
        return g;
    }

    /// <summary>The Mac's Quit: red text on a faint red capsule, then "Click to quit" on solid red for 3 s.</summary>
    private Border QuitButton()
    {
        var fg = _confirmQuit ? Brushes.White : Accent.Hunt.LightBrush;
        var power = Ui.Glyph("", 10.5, fg);
        power.Margin = new Thickness(0, 0, 6, 0);
        var b = new Border
        {
            Child = Ui.Row(0, power, Ui.Label(_confirmQuit ? L("Click to quit") : L("Quit"), Ui.Secondary, fg, FontWeights.SemiBold, trim: false)),
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 0, 10, 0),
            Background = _confirmQuit ? Accent.Hunt.Vertical : Accent.Hunt.Tint(0x24),
        };
        return Ui.Clickable(b, Quit, h => b.Opacity = h ? 0.88 : 1, L("Quit Ezkiel Notch"));
    }

    /// <summary>"Version 0.11.0 is out · Update now", or the daily-check switch with "Check now" (the Mac's UpdateRow).</summary>
    private Border UpdateRow(Updater? u)
    {
        var s = Model.Settings;
        string title, sub;
        UIElement action;
        bool waiting = UpdateWaiting(u);
        bool failed = u?.State == Updater.Phase.Failed;
        if (waiting && u!.Release is { } r)
        {
            title = L("Version %@ is out", string.Join('.', AppVersion.Parts(r.Version)));
            switch (u.State)
            {
                case Updater.Phase.Downloading or Updater.Phase.Verifying or Updater.Phase.Installing:
                    sub = u.State switch { Updater.Phase.Downloading => L("Downloading…"), Updater.Phase.Verifying => L("Checking the download…"), _ => L("Installing…") };
                    action = new Border();
                    break;
                default:
                    sub = failed ? u.Problem ?? "" : L("You have %@ · updates in place, your data stays", Updater.Current);
                    action = Ui.Row(4,
                        Ui.TextButton(L("Release notes"), () => Open(r.Page)),
                        Ui.Pill(failed ? L("Try again") : L("Update now"), () => _ = u.Install(), Gold, prominent: true, glyph: ""));
                    break;
            }
        }
        else
        {
            title = L("Updates");
            sub = u?.State switch
            {
                Updater.Phase.Checking => L("Checking…"),
                Updater.Phase.UpToDate => u.LastCheck is { } at ? L("Up to date · checked %@", Lang.Time(at)) : L("You're up to date (%@)", Updater.Current),
                Updater.Phase.Failed => u.Problem ?? "",
                _ => s.CheckUpdates ? L("Checks GitHub once a day") : L("Daily check is off"),
            };
            var sw = Ui.Switch(s.CheckUpdates, Gold, v => { s.CheckUpdates = v; Model.SettingsChanged(); Refresh(); });
            sw.ToolTip = L("Ask GitHub once a day");
            sw.VerticalAlignment = VerticalAlignment.Center;
            var check = Ui.TextButton(u?.State == Updater.Phase.Checking ? L("Checking…") : L("Check now"), () => { if (u is not null) _ = u.Check(force: true); });
            check.VerticalAlignment = VerticalAlignment.Center;
            action = Ui.Row(6, check, sw);
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Label(title, Ui.Body, weight: FontWeights.Medium));
        text.Children.Add(Ui.Label(sub, Ui.Caption, failed ? Accent.Hunt.LightBrush : Ui.Mist));
        return Card(Lead(waiting ? "" : "", waiting ? Gold.Vertical : Ui.Mist, text, action));
    }

    // MARK: Building blocks

    private void Changed()
    {
        Model.SettingsChanged();
        Refresh();
    }

    /// <summary>A row on the Mac's card surface: 10 across, 7 down, at least 36 tall, corner radius 10, 6 apart.</summary>
    private static Border Card(UIElement content) => new()
    {
        Child = content,
        MinHeight = 36,
        Padding = new Thickness(10, 7, 10, 7),
        CornerRadius = new CornerRadius(10),
        Background = Ui.Surface,
        Margin = new Thickness(0, 0, 0, 6),
    };

    /// <summary>Icon (18 wide), then title and subtitle, then the control on the right.</summary>
    private static Grid Lead(string glyph, Brush tint, UIElement text, UIElement? trailing)
    {
        var g = Columns(Px(18), Px(10), Star, Auto);
        var icon = Ui.Glyph(glyph, 12, tint);
        icon.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(At(icon, 0));
        if (text is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 6, 0);
        g.Children.Add(At(text, 2));
        if (trailing is FrameworkElement t)
        {
            t.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(At(t, 3));
        }
        return g;
    }

    /// <summary>The Mac's Chip: a capsule, filled with the accent's gradient when selected.</summary>
    private static Border Chip(string title, bool selected, Accent accent, Action onClick, string? tip = null)
    {
        var label = Ui.Label(title, Ui.Secondary - 0.5, selected ? Ui.OnAccent : Ui.Ink, FontWeights.Medium, trim: false);
        if (!selected) label.Opacity = 0.85;
        var b = new Border
        {
            Child = label,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 0, 10, 0),
            Background = selected ? accent.Vertical : ChipRest,
        };
        return Ui.Clickable(b, onClick, h => { if (!selected) b.Background = h ? Ui.SurfaceHover : ChipRest; }, tip);
    }

    /// <summary>The Mac's ToggleRow: the whole row flips the switch; the icon takes the accent while on.</summary>
    private Border Toggle(string glyph, string title, string subtitle, bool on, Action<bool> set, Accent? accent = null, bool rebuild = false)
    {
        var a = accent ?? Gold;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Label(title, Ui.Body, weight: FontWeights.Medium));
        var sub = Ui.Label(subtitle, Ui.Caption, Ui.Mist, trim: false);
        sub.TextWrapping = TextWrapping.Wrap;
        text.Children.Add(sub);
        TextBlock? icon = null;
        bool state = on;
        var sw = Ui.Switch(on, a, v =>
        {
            state = v;
            set(v);
            Model.SettingsChanged();
            if (icon is not null) icon.Foreground = v ? a.DeepBrush : Ui.Mist;
            // Rows that only show while this is on come and go once the switch has slid over.
            if (rebuild) Later(0.24, Refresh);
        });
        var lead = Lead(glyph, on ? a.DeepBrush : Ui.Mist, text, sw);
        icon = lead.Children.OfType<TextBlock>().First();
        var row = Card(lead);
        AutomationLabel(row, title);
        return Ui.Clickable(row, () =>
        {
            set(!state);
            Model.SettingsChanged();
            Refresh();
        }, h => row.Background = h ? CardHover : Ui.Surface, pressScale: 0.98);
    }

    /// <summary>The Mac's OptionRow: icon, a title column, then the choices.</summary>
    private static Border Option(string glyph, string title, UIElement[] chips)
    {
        var g = Columns(Px(18), Px(8), Auto, Star);
        var icon = Ui.Glyph(glyph, 12, Ui.Mist);
        g.Children.Add(At(icon, 0));
        var label = Ui.Label(title, Ui.Body, weight: FontWeights.Medium);
        label.MinWidth = 92;
        label.Margin = new Thickness(0, 0, 8, 0);
        g.Children.Add(At(label, 2));
        // Choices that don't fit on one line wrap to the next, never off the edge.
        var row = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in chips)
        {
            if (c is FrameworkElement fe) fe.Margin = new Thickness(0, 1, 5, 1);
            row.Children.Add(c);
        }
        g.Children.Add(At(row, 3));
        var card = Card(g);
        card.Padding = new Thickness(10, 5, 10, 5);
        return card;
    }

    /// <summary>A labelled text field that saves on Return or when it loses focus.</summary>
    private Border TextRow(string glyph, string title, string help, string value, string placeholder, Action<string> save)
    {
        var s = new StackPanel();
        var text = new StackPanel();
        text.Children.Add(Ui.Label(title, Ui.Body, weight: FontWeights.Medium));
        var helpText = Ui.Label(help, Ui.Caption, Ui.Mist, trim: false);
        helpText.TextWrapping = TextWrapping.Wrap;
        text.Children.Add(helpText);
        s.Children.Add(Lead(glyph, Ui.Mist, text, null));
        var (box, input) = Ui.Field(placeholder, i => Save(i.Text), 28);
        input.Text = _drafts.TryGetValue(title, out var draft) ? draft : value;
        input.TextChanged += (_, _) => { if (!_rebuilding) _drafts[title] = input.Text; };
        // Leaving the field saves it, but a rebuild taking the field away isn't leaving (the draft carries over).
        input.LostKeyboardFocus += (_, _) => { if (!_rebuilding && input.Text.Trim() != value) Save(input.Text); };
        _fields[title] = input;
        void Save(string typed)
        {
            _drafts.Remove(title);
            save(typed);
        }
        box.Margin = new Thickness(28, 6, 0, 0);
        s.Children.Add(box);
        return Card(s);
    }

    /// <summary>The Mac's StyleCard: Notch or Island, as two little pictures of the top of a screen.</summary>
    private Grid StyleCard()
    {
        var g = Columns(Star, Px(8), Star);
        g.Margin = new Thickness(0, 0, 0, 6);
        g.Children.Add(At(StyleTile("notch", L("Notch"), L("Hangs from the top edge")), 0));
        g.Children.Add(At(StyleTile("island", L("Island"), L("Floats like the Dynamic Island")), 2));
        return g;
    }

    private static readonly Brush ScreenTop = Ui.Frozen(new LinearGradientBrush(Accent.Hex(0x3A2F4F), Accent.Hex(0x1B2236), 90));
    private static readonly Brush IslandRim = Ui.Frozen(new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)));

    private Border StyleTile(string style, string title, string subtitle)
    {
        bool on = Model.Settings.Style == style;
        var preview = new Grid { Height = 34, Background = ScreenTop };
        preview.Children.Add(style == "island"
            ? new Border
            {
                Width = 44,
                Height = 12,
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Black,
                BorderBrush = IslandRim,
                BorderThickness = new Thickness(0.5),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 3, 0, 0),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Direction = 270, Opacity = 0.4 },
            }
            : new Border
            {
                Width = 52,
                Height = 11,
                CornerRadius = new CornerRadius(0, 0, 5, 5),
                Background = Brushes.Black,
                VerticalAlignment = VerticalAlignment.Top,
            });
        var s = new StackPanel();
        s.Children.Add(new Border { Child = preview, CornerRadius = new CornerRadius(7), ClipToBounds = true, Margin = new Thickness(0, 0, 0, 6) });
        s.Children.Add(Ui.Label(title, Ui.Body, weight: FontWeights.SemiBold));
        s.Children.Add(Ui.Label(subtitle, Ui.Caption, Ui.Mist));
        var tile = new Border
        {
            Child = s,
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(10),
            Background = Ui.Surface,
            BorderBrush = on ? GoldBorder : Brushes.Transparent,
            BorderThickness = new Thickness(1.5),
        };
        AutomationLabel(tile, title);
        return Ui.Clickable(tile, () =>
        {
            Model.Settings.Style = style;
            Changed();
        }, h => tile.Background = h ? CardHover : Ui.Surface, pressScale: 0.97);
    }

    /// <summary>The five eye styles, each a small live preview that blinks like the real thing.</summary>
    private Grid EyeStyleCard()
    {
        var styles = new[] { ("lion", "Lion"), ("anime", "Sparkly"), ("cat", "Cat"), ("cute", "Cute"), ("robot", "Robot") };
        var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        for (int i = 0; i < styles.Length; i++)
        {
            if (i > 0) g.ColumnDefinitions.Add(new ColumnDefinition { Width = Px(6) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
            g.Children.Add(At(EyeTile(styles[i].Item1, L(styles[i].Item2)), i * 2));
        }
        return g;
    }

    private Border EyeTile(string style, string title)
    {
        bool on = Model.Settings.EyeStyle == style;
        var capsule = new Border
        {
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = Brushes.Black,
            Margin = new Thickness(4, 0, 4, 5),
            Child = new EzkielEyes(8, style) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        // A hint of the sparkly eyes' ambient glow.
        if (style == "anime")
            capsule.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = 0.8, Color = StyledEyes.AnimeMagenta };
        var s = new StackPanel();
        s.Children.Add(capsule);
        var label = Ui.Label(title, Ui.Caption, on ? Ui.Ink : Ui.Mist, FontWeights.Medium);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        s.Children.Add(label);
        var tile = new Border
        {
            Child = s,
            Padding = new Thickness(0, 6, 0, 6),
            CornerRadius = new CornerRadius(10),
            Background = Ui.Surface,
            BorderBrush = on ? GoldBorder : Brushes.Transparent,
            BorderThickness = new Thickness(1.5),
        };
        return Ui.Clickable(tile, () =>
        {
            Model.Settings.EyeStyle = style;
            Changed();
        }, h => tile.Background = h ? CardHover : Ui.Surface, tip: title, pressScale: 0.95);
    }

    private static void AutomationLabel(FrameworkElement e, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(e, name);

    private void Quit()
    {
        if (_confirmQuit)
        {
            Application.Current.Shutdown();
            return;
        }
        _confirmQuit = true;
        Refresh();
        Later(3, () =>
        {
            _confirmQuit = false;
            Refresh();
        });
    }

    private static void Later(double seconds, Action action)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            action();
        };
        t.Start();
    }

    private static bool Open(string target)
    {
        if (target.Length == 0) return false;
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
