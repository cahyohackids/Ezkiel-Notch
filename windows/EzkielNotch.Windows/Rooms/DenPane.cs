using System.Windows;
using System.Windows.Controls;
using EzkielNotch.Core;
using EzkielNotch.Island;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Rooms;

/// <summary>Den: the focus timer, with today's minutes, the streak and the last seven days.</summary>
public sealed class DenPane(NotchModel model) : Pane(model, Room.Den)
{
    private ProgressRing? _ring;
    private TextBlock? _time, _subtitle;

    public override void Refresh()
    {
        Children.Clear();
        RowDefinitions.Clear();
        RowDefinitions.Add(new RowDefinition { Height = Px(92) });
        RowDefinitions.Add(new RowDefinition { Height = Px(12) });
        RowDefinitions.Add(new RowDefinition { Height = Px(40) });

        var focus = Model.Focus;
        bool rest = focus.Mode == FocusMode.Rest;
        var accent = rest ? Accent.Rest : Accent.Den;

        var top = Columns(Px(92), Px(16), Star);
        _ring = new ProgressRing { Accent = accent, Width = 92, Height = 92 };
        var inside = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _time = Ui.Number("", 20);
        _time.HorizontalAlignment = HorizontalAlignment.Center;
        var mode = Ui.Label(rest ? L("break") : L("focus"), Ui.Caption, Ui.Mist, FontWeights.Medium);
        mode.HorizontalAlignment = HorizontalAlignment.Center;
        inside.Children.Add(_time);
        inside.Children.Add(mode);
        var ringCell = new Grid();
        ringCell.Children.Add(_ring);
        ringCell.Children.Add(inside);
        top.Children.Add(At(ringCell, 0));

        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        right.Children.Add(Ui.Label(Title(), Ui.Title, weight: FontWeights.SemiBold));
        _subtitle = Ui.Label("", Ui.Secondary, Ui.Mist);
        right.Children.Add(_subtitle);

        var buttons = Ui.Row(8,
            Ui.Pill(focus.IsRunning ? L("Pause") : focus.IsPaused ? L("Resume") : L("Start"), Model.FocusStartPause, accent,
                    prominent: true, glyph: focus.IsRunning ? "\uE769" : "\uE768", tip: L("Space")),
            Ui.IconButton("\uE72C", L("Reset timer"), Model.FocusReset, tint: Ui.Mist));
        buttons.Margin = new Thickness(0, 8, 0, 8);
        right.Children.Add(buttons);

        var chips = Columns(Auto, Star, Auto);
        var presets = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var minutes in new[] { 15, 25, 45, 60 })
        {
            var chip = Ui.Chip($"{minutes}m", !rest && Model.Settings.FocusMinutes == minutes, Accent.Den, () => Model.SetFocusMinutes(minutes));
            chip.Margin = new Thickness(0, 0, 5, 0);
            presets.Children.Add(chip);
        }
        chips.Children.Add(At(presets, 0));
        chips.Children.Add(At(Ui.Chip(rest ? L("Focus") : L("%dm break", Model.Settings.BreakMinutes), rest, Accent.Rest, Model.ToggleBreak,
                                      enabled: !focus.IsBusy,
                                      tip: focus.IsBusy ? L("Available when the timer isn't running") : L("Switch between focus and break")), 2));
        right.Children.Add(chips);
        top.Children.Add(At(right, 2));
        Children.Add(At(top, 0, 0));

        Children.Add(At(Stats(), 0, 2));
        SecondTick();
    }

    private Border Stats()
    {
        var week = FocusStats.LastDays(7, Model.Data.FocusLog, DateTime.Now).Select(d => (d.Day, d.Minutes)).ToList();
        var g = Columns(Auto, Px(18), Auto, Star, Px(150));
        g.Children.Add(At(Stat(L("%d min", Model.FocusToday), L("today")), 0));
        int streak = Model.FocusStreak;
        g.Children.Add(At(Stat(streak == 1 ? L("1 day") : L("%d days", streak), L("streak")), 2));
        var bars = new WeekBars(week, Accent.Den, 18, 6, unit: "min") { Height = 32, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(At(bars, 4));
        return Ui.Card(g, 12, new Thickness(12, 0, 12, 0));
    }

    private static StackPanel Stat(string value, string label)
    {
        var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        s.Children.Add(Ui.Number(value, Ui.Body));
        s.Children.Add(Ui.Label(label, Ui.Caption, Ui.Mist));
        return s;
    }

    private string Title()
    {
        var f = Model.Focus;
        int minutes = (int)Math.Round(f.Duration.TotalMinutes);
        if (f.IsPaused) return L("Paused");
        if (f.IsRunning) return f.Mode == FocusMode.Focus ? L("In the Den") : L("On a break");
        return f.Mode == FocusMode.Focus ? L("%d min focus", minutes) : L("%d min break", minutes);
    }

    public override void SecondTick()
    {
        if (_ring is null || _time is null || _subtitle is null) return;
        var now = DateTime.UtcNow;
        var f = Model.Focus;
        var remaining = f.Remaining(now);
        _ring.Progress = 1 - f.Progress(now);
        _time.Text = FocusClock.Format(remaining);
        _time.FontSize = remaining.TotalSeconds >= 3600 ? 17 : 20;
        _subtitle.Text = f.IsRunning ? L("Ends at %@", Lang.Time(now + remaining)) : L("Click the island, then Space to start");
    }
}
