using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EzkielNotch.Core;
using EzkielNotch.Island;
using EzkielNotch.Platform;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Rooms;

/// <summary>Roar: unread counts from other apps. Scout: what AI agents and scripts reported.</summary>
public sealed class RoarPane(NotchModel model) : Pane(model, Room.Roar)
{
    /// <summary>The settings button: the window opens Settings at Roar.</summary>
    public event Action? OpenSettings;

    public override void Refresh()
    {
        Children.Clear();
        RowDefinitions.Clear();
        RowDefinitions.Add(new RowDefinition { Height = Px(28) });
        RowDefinitions.Add(new RowDefinition { Height = Px(8) });
        RowDefinitions.Add(new RowDefinition { Height = Star });

        bool scout = Model.ShowingScout;
        int unread = Model.UnreadTotal, live = Model.LiveAgents.Count;
        var head = Columns(Auto, Star, Auto);
        var modes = Ui.Row(6,
            Ui.Chip(unread > 0 ? $"Roar · {Short(unread)}" : "Roar", !scout, Accent.Roar, () => { Model.ShowingScout = false; Refresh(); }),
            Ui.Chip(live > 0 ? $"Scout · {live}" : "Scout", scout, Accent.Scout, () => { Model.ShowingScout = true; Refresh(); }));
        modes.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(At(modes, 0));
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        if (scout && Model.Data.ScoutLog.Count > 0) tools.Children.Add(Ui.IconButton("", L("Clear Scout"), Model.ClearScoutLog, 26, Ui.Mist));
        tools.Children.Add(Ui.IconButton("", L("Roar & Scout settings"), () => OpenSettings?.Invoke(), 26, Ui.Mist));
        head.Children.Add(At(tools, 2));
        Children.Add(At(head, 0, 0));
        Children.Add(At(scout ? ScoutList() : RoarList(), 0, 2));
    }

    internal static string Short(int n) => n > 999 ? "999+" : n.ToString();

    // MARK: Roar

    private FrameworkElement RoarList()
    {
        if (!Model.Settings.Roar) return Ui.EmptyHint(L("Roar is off"), L("Turn it on in Settings › Roar."));
        var badges = Model.RoarBadges.ToList();
        if (badges.Count == 0)
            return Ui.EmptyHint(L("All quiet"), L("Unread counts from WhatsApp, Teams, Outlook and any app that notifies you show here. Only the number, never the message."));
        var list = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        var now = DateTime.UtcNow;
        foreach (var b in badges) list.Children.Add(BadgeRow(b, now));
        return Ui.Scroll(list);
    }

    private Border BadgeRow(Badge b, DateTime now)
    {
        var g = Columns(Px(26), Px(10), Star, Auto, Auto);
        g.Children.Add(At(AppIcon(b.Id, 26), 0));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Label(b.Name, Ui.Body, weight: FontWeights.SemiBold));
        text.Children.Add(Ui.Label(Lang.Relative(b.Since, now), Ui.Caption, Ui.Faint));
        g.Children.Add(At(text, 2));
        var mute = Ui.IconButton("", L("Mute %@", b.Name), () => Model.Mute(b), 24, Ui.Mist);
        mute.Opacity = 0;
        mute.Margin = new Thickness(0, 0, 4, 0);
        g.Children.Add(At(mute, 3));
        g.Children.Add(At(CountPill(b.Label, Accent.Roar), 4));
        var row = new Border { Child = g, CornerRadius = new CornerRadius(10), Background = Ui.Surface, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 4) };
        row.MouseEnter += (_, _) => { row.Background = Ui.SurfaceRaised; mute.Opacity = 1; };
        row.MouseLeave += (_, _) => { row.Background = Ui.Surface; mute.Opacity = 0; };
        return Ui.Clickable(row, () => AppIdentity.Open(b.Id), tip: L("Open %@", b.Name), pressScale: 0.98);
    }

    /// <summary>The unread count, like a taskbar badge but in the feature's colour.</summary>
    internal static Border CountPill(string label, Accent accent) => new()
    {
        Child = new TextBlock
        {
            Text = label,
            FontFamily = Ui.Display,
            FontSize = Ui.Secondary,
            FontWeight = FontWeights.Bold,
            Foreground = Ui.OnAccent,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
        MinWidth = 22,
        Height = 20,
        Padding = new Thickness(7, 0, 7, 0),
        CornerRadius = new CornerRadius(10),
        Background = accent.Vertical,
        VerticalAlignment = VerticalAlignment.Center,
    };

    internal static FrameworkElement AppIcon(string? id, double size)
    {
        if (AppIcons.Icon(id) is { } icon) return new Image { Source = icon, Width = size, Height = size };
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.25),
            Background = Accent.Roar.Tint(0x33),
            Child = Ui.Glyph("", size * 0.45, Accent.Roar.LightBrush),
        };
    }

    // MARK: Scout

    private FrameworkElement ScoutList()
    {
        if (!Model.Settings.Scout) return Ui.EmptyHint(L("Scout is off"), L("Turn it on in Settings › Roar."));
        var live = Model.LiveAgents;
        var history = Model.ScoutHistory;
        var ai = Model.AiBadges.ToList();
        if (live.Count == 0 && history.Count == 0 && ai.Count == 0) return SetupCard();
        var list = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        if (ai.Count > 0)
        {
            var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var b in ai) chips.Children.Add(AiChip(b));
            list.Children.Add(chips);
        }
        var now = DateTime.UtcNow;
        foreach (var e in live) list.Children.Add(ScoutRow(e, true, now));
        foreach (var e in history) list.Children.Add(ScoutRow(e, false, now));
        return Ui.Scroll(list);
    }

    private Border AiChip(Badge b)
    {
        var row = Ui.Row(6, AppIcon(b.Id, 18), Ui.Label(b.Name, Ui.Secondary, weight: FontWeights.Medium), CountPill(b.Label, Accent.Scout));
        var chip = new Border { Child = row, Height = 28, Padding = new Thickness(6, 0, 3, 0), CornerRadius = new CornerRadius(14), Background = Ui.SurfaceRaised, Margin = new Thickness(0, 0, 6, 0) };
        return Ui.Clickable(chip, () => AppIdentity.Open(b.Id), tip: L("Open %@", b.Name));
    }

    internal static string Glyph(ScoutEvent e) => e.Kind switch
    {
        ScoutKind.NeedsYou => "",
        ScoutKind.Working => "",
        ScoutKind.Done => e.Source == "codex" ? "" : "",
        _ => e.Source == "claude" ? "" : "",
    };

    private Border ScoutRow(ScoutEvent e, bool live, DateTime now)
    {
        var accent = e.Kind == ScoutKind.NeedsYou ? Accent.Den : Accent.Scout;
        var g = Columns(Px(26), Px(10), Star, Auto, Auto);
        g.Children.Add(At(new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = accent.Tint(live ? (byte)0x33 : (byte)0x1F),
            Child = Ui.Glyph(Glyph(e), 12, accent.LightBrush),
        }, 0));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(Ui.Label(L(e.Title), Ui.Body, weight: FontWeights.SemiBold));
        if (e.Project is { } project)
        {
            var p = Ui.Label(project, Ui.Caption, accent.LightBrush, FontWeights.Medium);
            p.Margin = new Thickness(5, 1, 0, 0);
            title.Children.Add(p);
        }
        text.Children.Add(title);
        if (e.Body.Length > 0) text.Children.Add(Ui.Label(L(e.Body), Ui.Secondary, Ui.Mist));
        g.Children.Add(At(text, 2));
        Border? clear = null;
        if (live)
        {
            clear = Ui.IconButton("", L("Clear"), () => Model.DismissAgent(e), 22, Ui.Mist);
            clear.Opacity = 0;
            g.Children.Add(At(clear, 3));
        }
        var age = Ui.Number(Age(e.Date, now), Ui.Caption, live ? accent.LightBrush : Ui.Faint);
        age.FontWeight = FontWeights.Medium;
        age.Margin = new Thickness(6, 0, 0, 0);
        g.Children.Add(At(age, 4));
        var row = new Border
        {
            Child = g,
            CornerRadius = new CornerRadius(10),
            Background = Ui.Surface,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 4),
            BorderThickness = new Thickness(live ? 0.75 : 0),
            BorderBrush = accent.Tint(0x59),
        };
        row.MouseEnter += (_, _) => { row.Background = Ui.SurfaceRaised; if (clear is not null) clear.Opacity = 1; };
        row.MouseLeave += (_, _) => { row.Background = Ui.Surface; if (clear is not null) clear.Opacity = 0; };
        return e.AppBundleId is null ? row : Ui.Clickable(row, () => AppIdentity.Open(e.AppBundleId), tip: L("Open where it ran"), pressScale: 0.98);
    }

    /// <summary>"now", "4m", "2h", "3d".</summary>
    internal static string Age(DateTime date, DateTime now)
    {
        var s = Math.Max(0, (now - date).TotalSeconds);
        if (s < 60) return L("now");
        if (s < 3600) return $"{(int)(s / 60)}m";
        if (s < 86400) return $"{(int)(s / 3600)}h";
        return $"{(int)(s / 86400)}d";
    }

    /// <summary>First run: connect an agent, or send from any script.</summary>
    private Border SetupCard()
    {
        var s = new StackPanel();
        var top = Columns(Px(34), Px(10), Star);
        top.Children.Add(At(new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = Accent.Scout.Tint(0x24), Child = Ui.Glyph("", 15, Accent.Scout.LightBrush) }, 0));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Label(L("Hear from your agents"), Ui.Body, weight: FontWeights.SemiBold));
        var sub = Ui.Label(L("A peek when Claude Code or Codex finishes or needs you, while you're in another app."), Ui.Secondary, Ui.Mist, trim: false);
        sub.TextWrapping = TextWrapping.Wrap;
        text.Children.Add(sub);
        top.Children.Add(At(text, 2));
        s.Children.Add(top);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var agent in new[] { AgentLinks.Agent.ClaudeCode, AgentLinks.Agent.Codex })
        {
            var state = AgentLinks.State(agent);
            bool installed = AgentLinks.Installed(agent);
            var pill = Ui.Pill(state == LinkState.On ? $"{AgentLinks.Title(agent)} ✓" : AgentLinks.Title(agent), () =>
            {
                if (state != LinkState.On) Connect(agent, true);
            }, Accent.Scout, prominent: state != LinkState.On && installed, glyph: state == LinkState.On ? null : "",
               tip: installed ? L("Adds a small hook to %@ (a backup is kept)", AgentLinks.FileOf(agent)) : L("%@ isn't set up on this PC", AgentLinks.Title(agent)));
            pill.Opacity = installed || state == LinkState.On ? 1 : 0.5;
            pill.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(pill);
        }
        row.Children.Add(Ui.Pill(L("Script"), CopyScriptCommand, Accent.Neutral, glyph: "", tip: "EzkielNotch.exe --notify \"title\" \"body\""));
        s.Children.Add(row);
        return Ui.Card(s, 14, new Thickness(12));
    }

    internal void Connect(AgentLinks.Agent agent, bool on)
    {
        var problem = AgentLinks.Set(agent, on);
        Model.RaiseToast(problem ?? (on ? L("%@ connected · new sessions report to Scout", AgentLinks.Title(agent)) : L("%@ disconnected", AgentLinks.Title(agent))));
        Refresh();
    }

    private void CopyScriptCommand()
    {
        try
        {
            Clipboard.SetText($"\"{Environment.ProcessPath}\" --notify \"Build done\" \"All green\"");
            Model.RaiseToast(L("Copied · run it from any script"));
        }
        catch (Exception) { }
    }
}
