using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EzkielNotch.Core;
using EzkielNotch.Island;
using EzkielNotch.Platform;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Rooms;

/// <summary>Hunt: tasks with reminders, and Rally (the next meeting) on top when it's on.</summary>
public sealed class HuntPane(NotchModel model, RallyClient rally, Action openMirror) : Pane(model, Room.Hunt)
{
    private DueChoice _due = DueChoice.None;
    private TextBox? _input;
    private string _draft = "";
    /// <summary>The field, so the self-test can type into it.</summary>
    public TextBox? Input => _input;

    public override void Refresh()
    {
        bool hadFocus = _input?.IsKeyboardFocused == true;
        if (_input is not null) _draft = _input.Text;
        Children.Clear();
        RowDefinitions.Clear();
        bool showRally = Model.Settings.Rally;
        RowDefinitions.Add(new RowDefinition { Height = showRally ? Px(40) : Px(0) });
        RowDefinitions.Add(new RowDefinition { Height = Px(30) });
        RowDefinitions.Add(new RowDefinition { Height = Px(32) });
        RowDefinitions.Add(new RowDefinition { Height = Star });
        if (showRally) Children.Add(At(RallyRow(), 0, 0));

        var tasks = Model.Data.Tasks;
        int doneCount = tasks.Count(t => t.Done);

        var add = Columns(Star, Px(6), Px(30));
        var (box, input) = Ui.Field(L("New task"), _ => Submit());
        _input = input;
        input.Text = _draft;
        add.Children.Add(At(box, 0));
        add.Children.Add(At(Ui.IconButton("\uE710", L("Add task (Return)"), Submit, 30, filled: Accent.Hunt), 2));
        Children.Add(At(add, 0, 1));

        var due = Columns(Auto, Star, Auto);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        chips.Children.Add(new Border { Child = Ui.Glyph("\uEA8F", 11, Ui.Mist), Margin = new Thickness(2, 0, 6, 0) });
        foreach (var c in Due.All)
        {
            var choice = c;
            var chip = Ui.Chip(L(Due.Label(choice)), _due == choice, Accent.Hunt, () =>
            {
                _due = choice;
                Refresh();
            });
            chip.Margin = new Thickness(0, 0, 4, 0);
            chips.Children.Add(chip);
        }
        due.Children.Add(At(chips, 0));
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (doneCount > 0) right.Children.Add(Ui.TextButton(L("Clear %d done", doneCount), Model.ClearDoneTasks));
        right.Children.Add(Ui.IconButton("\uE8B8", L("Mirror: check your camera"), openMirror, 24, Ui.Mist));
        due.Children.Add(At(right, 2));
        Children.Add(At(due, 0, 2));

        if (tasks.Count == 0)
        {
            Children.Add(At(Ui.EmptyHint(L("Nothing on the hunt"), L("Type a task and press Return. Pick a time for a reminder.")), 0, 3));
        }
        else
        {
            var list = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            var now = DateTime.UtcNow;
            foreach (var t in tasks) list.Children.Add(TaskRow(t, now));
            Children.Add(At(Ui.Scroll(list), 0, 3));
        }
        if (hadFocus) Dispatcher.BeginInvoke(() => _input?.Focus());
    }

    /// <summary>Adds what is typed in the field (Return or the + button).</summary>
    public void Submit()
    {
        if (_input is null) return;
        var title = _input.Text.Trim();
        if (title.Length == 0) return;
        var when = Due.Date(_due, DateTime.UtcNow);
        _draft = "";
        _input.Text = "";
        _due = DueChoice.None;
        Model.AddTask(title, when);   // raises Changed → Refresh
    }

    /// <summary>The next meeting: title, time, and Join when there is a link (the Mac's RallyRow).</summary>
    private Border RallyRow()
    {
        var card = new Border { Height = 34, CornerRadius = new CornerRadius(10), Background = Ui.Surface, Padding = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Top };
        var s = Model.Settings;
        if (RallyClient.Normalize(s.RallyCalendarUrl) is null || rally.State == RallyClient.Status.Failed)
        {
            var g = Columns(Auto, Px(8), Star, Auto);
            g.Children.Add(At(Ui.Glyph("\uE787", 12, Accent.Rally.DeepBrush), 0));
            g.Children.Add(At(Ui.Label(rally.State == RallyClient.Status.Failed ? L("Couldn't read your calendar") : L("Rally needs your calendar's iCal address"), Ui.Secondary, Ui.Mist), 2));
            g.Children.Add(At(Ui.TextButton(L("Open Settings"), () => OpenSettings?.Invoke(), Accent.Rally.LightBrush), 3));
            card.Child = g;
            return card;
        }
        var upcoming = rally.Upcoming;
        if (upcoming.Count == 0)
        {
            card.Child = Ui.Label(rally.State == RallyClient.Status.Loading ? L("Reading your calendar…") : L("No more meetings today"), Ui.Secondary, Ui.Mist);
            return card;
        }
        var m = upcoming[0];
        var now = DateTime.UtcNow;
        bool started = m.Start <= now;
        int minutes = (int)Math.Ceiling((m.Start - now).TotalMinutes);
        var when = $"{Lang.Time(m.Start)}–{Lang.Time(m.End)}";
        if (started) when += " · " + L("now");
        else if (minutes < 60) when += " · " + L("in %d min", minutes);
        if (upcoming.Count > 1) when += " · " + L("+%d more", upcoming.Count - 1);
        var row = Columns(Px(3), Px(8), Star, Auto);
        row.Children.Add(At(new Border { Width = 3, Height = 20, CornerRadius = new CornerRadius(1.5), Background = Accent.Rally.Vertical }, 0));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Label(m.Title, Ui.Body, weight: FontWeights.Medium));
        text.Children.Add(Ui.Label(when, Ui.Caption, started || minutes <= 5 ? Accent.Rally.LightBrush : Ui.Mist));
        row.Children.Add(At(text, 2));
        if (m.Link is { } link)
        {
            var join = Ui.Pill(L("Join"), () => Files.Open(link), Accent.Rally, prominent: started || minutes <= 10, glyph: "\uE714");
            join.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(At(join, 3));
        }
        card.Child = row;
        return card;
    }

    /// <summary>The Rally hint's button: the window opens Settings.</summary>
    public event Action? OpenSettings;

    private Border TaskRow(TaskItem task, DateTime now)
    {
        var g = Columns(Px(28), Star, Auto, Px(26));
        var circle = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(task.Done ? 0 : 1.5),
            BorderBrush = Ui.Mist,
            Background = task.Done ? Accent.Hunt.Vertical : Brushes.Transparent,
            Child = task.Done ? Ui.Glyph("\uE73E", 8.5, Brushes.White) : null,
        };
        var check = new Border { Width = 24, Height = 24, Background = Brushes.Transparent, Child = circle };
        g.Children.Add(At(Ui.Clickable(check, () => Model.ToggleTask(task.Id), tip: task.Done ? L("Mark as not done") : L("Mark as done"), pressScale: 0.8), 0));

        var title = Ui.Label(task.Title, Ui.Body, task.Done ? Ui.Mist : Ui.Ink);
        if (task.Done) title.TextDecorations = TextDecorations.Strikethrough;
        g.Children.Add(At(title, 1));

        if (task.Due is { } due && !task.Done)
        {
            bool overdue = due <= now;
            var when = Ui.Row(4, Ui.Glyph(overdue ? "\uEA8F" : "\uEA8F", 9.5, overdue ? Accent.Hunt.LightBrush : Ui.Mist),
                              Ui.Label(Lang.DayTime(due, now), Ui.Caption, overdue ? Accent.Hunt.LightBrush : Ui.Mist, FontWeights.Medium, trim: false));
            when.Margin = new Thickness(6, 0, 4, 0);
            when.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(At(when, 2));
        }

        var delete = Ui.IconButton("\uE711", L("Delete task"), () => Model.DeleteTask(task.Id), 24, Ui.Mist);
        delete.Opacity = 0;
        g.Children.Add(At(delete, 3));

        var row = new Border { Child = g, Height = 30, CornerRadius = new CornerRadius(10), Background = Ui.Surface, Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(2, 0, 2, 0) };
        row.MouseEnter += (_, _) => { row.Background = Ui.SurfaceRaised; delete.Opacity = 1; };
        row.MouseLeave += (_, _) => { row.Background = Ui.Surface; delete.Opacity = 0; };
        return row;
    }

    public override void SecondTick()
    {
        // Due labels and overdue colours, once a minute.
        if (DateTime.Now.Second == 0 && _input?.IsKeyboardFocused != true) Refresh();
    }
}
