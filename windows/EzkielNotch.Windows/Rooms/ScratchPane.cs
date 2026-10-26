using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EzkielNotch.Core;
using EzkielNotch.Island;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Rooms;

/// <summary>Scratch: notes, and Catch (text captured from the clipboard when you press Capture).</summary>
public sealed class ScratchPane : Pane
{
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private Guid? _editing;
    private TextBox? _title, _body;
    private readonly Dictionary<Guid, TextBlock> _rowTitles = [];

    /// <summary>The editor, so the self-test can type into it.</summary>
    public TextBox? BodyEditor => _body;

    public ScratchPane(NotchModel model) : base(model, Room.Scratch)
    {
        _save.Tick += (_, _) => Flush();
    }

    public override void Refresh()
    {
        Flush();
        Children.Clear();
        RowDefinitions.Clear();
        RowDefinitions.Add(new RowDefinition { Height = Px(28) });
        RowDefinitions.Add(new RowDefinition { Height = Px(8) });
        RowDefinitions.Add(new RowDefinition { Height = Star });

        bool catching = Model.ShowingCatch;
        var head = Columns(Auto, Star, Auto);
        var modes = Ui.Row(6,
            Ui.Chip(L("Notes"), !catching, Accent.Scratch, () => { Model.ShowingCatch = false; Refresh(); }),
            Ui.Chip("Catch", catching, Accent.Scratch, () => { Model.ShowingCatch = true; Refresh(); }));
        modes.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(At(modes, 0));
        head.Children.Add(At(catching
            ? Ui.Pill(L("Capture"), Capture, Accent.Scratch, prominent: true, glyph: "\uE77F", tip: L("Reads the clipboard only when you press it"))
            : Ui.IconButton("\uE70F", L("New note"), Model.AddNote, 26), 2));
        Children.Add(At(head, 0, 0));

        Children.Add(At(catching ? CatchView() : NotesView(), 0, 2));
    }

    private void Capture()
    {
        string? text = null;
        try
        {
            if (Clipboard.ContainsText()) text = Clipboard.GetText();
        }
        catch (Exception)
        {
            // Another app can hold the clipboard open for a moment.
        }
        Model.Capture(text);
    }

    // MARK: Notes

    private FrameworkElement NotesView()
    {
        _editing = null;
        _title = _body = null;
        _rowTitles.Clear();
        var notes = Model.Data.Notes;
        if (notes.Count == 0) return Ui.EmptyHint(L("No notes"), L("Notes stay on this PC."));

        var g = Columns(Px(124), Px(8), Star);
        var list = new StackPanel();
        var selected = notes.FirstOrDefault(n => n.Id == Model.SelectedNoteId) ?? notes[0];
        Model.SelectedNoteId = selected.Id;
        var now = DateTime.UtcNow;
        foreach (var note in notes) list.Children.Add(NoteRow(note, note.Id == selected.Id, now));
        g.Children.Add(At(Ui.Scroll(list), 0));
        g.Children.Add(At(Editor(selected), 2));
        return g;
    }

    private Border NoteRow(NoteItem note, bool selected, DateTime now)
    {
        var s = new StackPanel();
        var title = Ui.Label(note.DisplayTitle(L("Untitled note")), Ui.Secondary, selected ? Ui.Ink : Ui.Ink, FontWeights.Medium);
        title.Opacity = selected ? 1 : 0.85;
        _rowTitles[note.Id] = title;
        s.Children.Add(title);
        s.Children.Add(Ui.Label(Lang.Relative(note.Updated, now), Ui.Caption, Ui.Faint));
        var rest = selected ? Accent.Scratch.Tint(0x29) : Brushes.Transparent;
        var b = new Border { Child = s, Padding = new Thickness(8, 5, 8, 5), CornerRadius = new CornerRadius(8), Background = rest, Margin = new Thickness(0, 0, 0, 2) };
        return Ui.Clickable(b, () =>
        {
            Flush();
            Model.SelectedNoteId = note.Id;
            Refresh();
        }, h => b.Background = selected ? rest : h ? Ui.Surface : rest, pressScale: 0.97);
    }

    private Border Editor(NoteItem note)
    {
        _editing = note.Id;
        var head = Columns(Star, Auto);
        _title = Ui.StyledTextBox(Ui.Title);
        _title.FontWeight = FontWeights.SemiBold;
        _title.Text = note.Title;
        var hint = Ui.Label(L("Title"), Ui.Title, Ui.Faint, FontWeights.SemiBold);
        hint.IsHitTestVisible = false;
        hint.Visibility = note.Title.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var titleCell = new Grid();
        titleCell.Children.Add(hint);
        titleCell.Children.Add(_title);
        head.Children.Add(At(titleCell, 0));
        head.Children.Add(At(Ui.IconButton("\uE74D", L("Delete note"), () =>
        {
            Flush();
            Model.DeleteNote(note.Id);
        }, 24, Ui.Mist), 1));

        _body = Ui.StyledTextBox(Ui.Body);
        _body.AcceptsReturn = true;
        _body.AcceptsTab = false;
        _body.TextWrapping = TextWrapping.Wrap;
        _body.VerticalAlignment = VerticalAlignment.Stretch;
        _body.Text = note.Body;
        _body.Margin = new Thickness(0, 4, 0, 0);

        _title.TextChanged += (_, _) =>
        {
            hint.Visibility = _title.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Schedule();
        };
        _body.TextChanged += (_, _) => Schedule();
        // Return in the title moves on to the body, like a notes app.
        _title.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            _body.Focus();
            _body.CaretIndex = 0;
            e.Handled = true;
        };

        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = Star });
        g.Children.Add(At(head, 0, 0));
        g.Children.Add(At(_body, 0, 1));
        var card = Ui.Card(g, 12, new Thickness(10, 6, 6, 6));
        card.Cursor = Cursors.IBeam;
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource == card) _body.Focus();
        };

        if (Model.NoteToFocus == note.Id)
        {
            Model.NoteToFocus = null;
            Dispatcher.BeginInvoke(() => _title?.Focus(), DispatcherPriority.Input);
        }
        return card;
    }

    private void Schedule()
    {
        _save.Stop();
        _save.Start();
    }

    /// <summary>Writes the editor back (after a pause in typing, on leaving the note, and on close).</summary>
    public void Flush()
    {
        _save.Stop();
        if (_editing is not { } id || _title is null || _body is null) return;
        Model.UpdateNote(id, _title.Text, _body.Text);
        if (_rowTitles.TryGetValue(id, out var row) && Model.Data.Notes.FirstOrDefault(n => n.Id == id) is { } note)
            row.Text = note.DisplayTitle(L("Untitled note"));
    }

    public override void Shown(bool visible)
    {
        if (!visible) Flush();
    }

    // MARK: Catch

    private FrameworkElement CatchView()
    {
        _editing = null;
        _title = _body = null;
        var clips = Model.Data.Clips;
        if (clips.Count == 0) return Ui.EmptyHint(L("Nothing caught yet"), L("Copy some text, then press Capture. Keeps 30 clips."));
        var list = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        var now = DateTime.UtcNow;
        foreach (var clip in clips) list.Children.Add(ClipRow(clip, now));
        return Ui.Scroll(list);
    }

    private Border ClipRow(ClipItem clip, DateTime now)
    {
        var g = Columns(Star, Auto, Auto);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var body = Ui.Label(clip.Text, Ui.Secondary);
        body.TextWrapping = TextWrapping.Wrap;
        body.MaxHeight = Ui.Secondary * 2.8;   // two lines
        text.Children.Add(body);
        text.Children.Add(Ui.Label(Lang.Relative(clip.Captured, now), Ui.Caption, Ui.Faint));
        g.Children.Add(At(text, 0));
        g.Children.Add(At(Ui.IconButton("\uE8C8", L("Copy"), () => CopyClip(clip), 24), 1));
        var remove = Ui.IconButton("\uE711", L("Remove clip"), () => Model.RemoveClip(clip.Id), 24, Ui.Mist);
        remove.Opacity = 0;
        g.Children.Add(At(remove, 2));
        var row = new Border { Child = g, CornerRadius = new CornerRadius(10), Background = Ui.Surface, Padding = new Thickness(10, 6, 4, 6), Margin = new Thickness(0, 0, 0, 4) };
        row.MouseEnter += (_, _) => { row.Background = Ui.SurfaceRaised; remove.Opacity = 1; };
        row.MouseLeave += (_, _) => { row.Background = Ui.Surface; remove.Opacity = 0; };
        return row;
    }

    private void CopyClip(ClipItem clip)
    {
        try
        {
            Clipboard.SetText(clip.Text);
            Copied?.Invoke();
        }
        catch (Exception)
        {
            // Clipboard busy: nothing copied, nothing claimed.
        }
    }

    /// <summary>Raised after a clip is copied (the window shows "Copied").</summary>
    public event Action? Copied;
}
