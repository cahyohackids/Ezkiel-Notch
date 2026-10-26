using System.Windows;
using System.Windows.Controls;
using EzkielNotch.Core;
using EzkielNotch.Island;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Rooms;

/// <summary>Oasis: water through the day, the last week, the goal and gentle reminders.</summary>
public sealed class OasisPane(NotchModel model) : Pane(model, Room.Oasis)
{
    private WaterDrop? _drop;
    private double _shownLevel = -1;

    public override void Refresh()
    {
        Children.Clear();
        var s = Model.Settings;
        int today = Model.WaterToday, goal = s.WaterGoal;
        double progress = Model.WaterProgress;
        var week = WaterLog.LastDays(7, Model.Data.Water, DateTime.Now).Select(d => (d.Day, d.Ml)).ToList();
        int streak = WaterLog.Streak(Model.Data.Water, goal, DateTime.Now);

        var g = Columns(Px(70), Px(16), Star);
        // The level springs from where it was, so a drink visibly fills the drop.
        _drop = new WaterDrop { Width = 70, Height = 92, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0) };
        _drop.SetLevel(_shownLevel < 0 ? progress : _shownLevel, animate: false);
        _drop.SetLevel(progress, animate: _shownLevel >= 0 && Math.Abs(_shownLevel - progress) > 0.001);
        _shownLevel = progress;
        g.Children.Add(At(_drop, 0));

        var right = new StackPanel();

        var head = Columns(Auto, Auto, Star, Auto);
        head.Children.Add(At(Ui.Number($"{today} ml", Ui.Title), 0));
        var of = Ui.Label(progress >= 1 ? L("goal reached") : L("of %d", goal), Ui.Secondary, progress >= 1 ? Accent.Oasis.LightBrush : Ui.Mist);
        of.Margin = new Thickness(6, 2, 0, 0);
        head.Children.Add(At(of, 1));
        if (streak > 0) head.Children.Add(At(Ui.Label(L("%d-day streak", streak), Ui.Caption, Ui.Mist, FontWeights.Medium), 3));
        right.Children.Add(head);

        var drinks = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        foreach (var ml in new[] { 200, 350, 500 })
        {
            var pill = Ui.Pill($"+{ml}", () => Model.AddWater(ml), Accent.Oasis, prominent: ml == 350);
            pill.Margin = new Thickness(0, 0, 5, 0);
            drinks.Children.Add(pill);
        }
        drinks.Children.Add(Ui.IconButton("\uE7A7", L("Undo last drink"), Model.UndoWater, 28, Ui.Mist));
        right.Children.Add(drinks);

        right.Children.Add(new WeekBars(week, Accent.Oasis, 20, 7, goal, "ml") { Height = 34 });

        var foot = Columns(Auto, Star, Auto);
        foot.Margin = new Thickness(0, 8, 0, 0);
        var goals = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var gl in new[] { 1500, 2000, 2500, 3000 })
        {
            var chip = Ui.Chip($"{gl / 1000}.{gl % 1000 / 100}L", goal == gl, Accent.Oasis, () => Model.SetWaterGoal(gl));
            chip.Margin = new Thickness(0, 0, 4, 0);
            goals.Children.Add(chip);
        }
        foot.Children.Add(At(goals, 0));
        bool on = s.WaterReminders;
        var bell = Ui.Row(5, Ui.Glyph(on ? "\uEA8F" : "\uE7ED", 10, on ? Accent.Oasis.LightBrush : Ui.Mist),
                          Ui.Label(on ? $"{s.WaterIntervalMinutes}m" : L("Off"), Ui.Caption, on ? Accent.Oasis.LightBrush : Ui.Mist, FontWeights.Medium, trim: false));
        var reminder = new Border { Child = bell, Height = 24, Padding = new Thickness(8, 0, 8, 0), CornerRadius = new CornerRadius(12), Background = Ui.SurfaceRaised };
        bell.VerticalAlignment = VerticalAlignment.Center;
        foot.Children.Add(At(Ui.Clickable(reminder, () =>
        {
            s.WaterReminders = !s.WaterReminders;
            Model.SettingsChanged();
            Refresh();
        }, tip: L("Reminders between %d:00 and %d:00", s.ActiveHourStart, s.ActiveHourEnd)), 2));
        right.Children.Add(foot);

        g.Children.Add(At(right, 2));
        Children.Add(g);
    }

    public override void Shown(bool visible)
    {
        if (_drop is not null) _drop.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
    }
}
