using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EzkielNotch.Core;
using EzkielNotch.Island;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Rooms;

/// <summary>Hunt: tasks with reminders. Rally (meetings) joins in a later phase.</summary>
public sealed class HuntPane(NotchModel model) : Pane(model, Room.Hunt)
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
        RowDefinitions.Add(new RowDefinition { Height = Px(30) });
        RowDefinitions.Add(new RowDefinition { Height = Px(32) });
        RowDefinitions.Add(new RowDefinition { Height = Star });

        var tasks = Model.Data.Tasks;
        int doneCount = tasks.Count(t => t.Done);

        var add = Columns(Star, Px(6), Px(30));
        var (box, input) = Ui.Field(L("New task"), _ => Submit());
        _input = input;
        input.Text = _draft;
        add.Children.Add(At(box, 0));
        add.Children.Add(At(Ui.IconButton("\uE710", L("Add task (Return)"), Submit, 30, filled: Accent.Hunt), 2));
        Children.Add(At(add, 0, 0));

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
        if (doneCount > 0)
        {
            var clear = Ui.TextButton(L("Clear %d done", doneCount), Model.ClearDoneTasks);
            clear.VerticalAlignment = VerticalAlignment.Center;
            due.Children.Add(At(clear, 2));
        }
        Children.Add(At(due, 0, 1));

        if (tasks.Count == 0)
        {
            Children.Add(At(Ui.EmptyHint(L("Nothing on the hunt"), L("Type a task and press Return. Pick a time for a reminder.")), 0, 2));
        }
        else
        {
            var list = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            var now = DateTime.UtcNow;
            foreach (var t in tasks) list.Children.Add(TaskRow(t, now));
            Children.Add(At(Ui.Scroll(list), 0, 2));
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
