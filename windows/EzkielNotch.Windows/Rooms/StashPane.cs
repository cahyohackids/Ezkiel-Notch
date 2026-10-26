using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using EzkielNotch.Core;
using EzkielNotch.Island;
using EzkielNotch.Platform;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Rooms;

/// <summary>Stash: files you drop on the island, ready to drag out, share (Nearby sharing, mail, chats) or open.
/// Pounce adds new screenshots and downloads here with one click.</summary>
public sealed class StashPane(NotchModel model) : Pane(model, Room.Stash)
{
    /// <summary>Our own drag-out is running: it must not light up the island or close it.</summary>
    public static bool DraggingOut { get; private set; }

    public event Action? ChooseFiles;
    public event Action<IReadOnlyList<string>>? ShareFiles;

    private bool _targeted;
    private Border? _zone;
    private FrameworkElement? _hint;

    public void SetTargeted(bool on)
    {
        if (on == _targeted) return;
        _targeted = on;
        Refresh();
    }

    public override void Refresh()
    {
        Children.Clear();
        RowDefinitions.Clear();
        RowDefinitions.Add(new RowDefinition { Height = Px(26) });
        RowDefinitions.Add(new RowDefinition { Height = Px(8) });
        RowDefinitions.Add(new RowDefinition { Height = Star });
        var items = Model.Data.Shelf;

        var head = Columns(Auto, Star, Auto);
        var title = Ui.Row(6, Ui.Label("Stash", Ui.Title, weight: FontWeights.SemiBold));
        if (items.Count > 0) title.Children.Add(new Border { Child = Ui.Label(items.Count.ToString(), Ui.Secondary, Ui.Mist), Margin = new Thickness(6, 2, 0, 0) });
        title.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(At(title, 0));
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        if (items.Count > 0)
        {
            tools.Children.Add(Ui.IconButton("", L("Share…"), () => ShareFiles?.Invoke(items.Select(r => r.Path).Where(Files.Exists).ToList()), 26, Ui.Mist));
            tools.Children.Add(Ui.IconButton("", L("Clear Stash (you can undo)"), Model.ClearShelf, 26, Ui.Mist));
        }
        var add = Ui.IconButton("", L("Choose files…"), () => ChooseFiles?.Invoke(), 26, filled: Accent.Stash);
        add.Margin = new Thickness(4, 0, 0, 0);
        tools.Children.Add(add);
        head.Children.Add(At(tools, 2));
        Children.Add(At(head, 0, 0));

        var zone = new Grid();
        var accent = Accent.Stash;
        zone.Children.Add(new Rectangle
        {
            RadiusX = 14,
            RadiusY = 14,
            Fill = _targeted ? accent.Tint(0x1F) : Ui.Frozen(new SolidColorBrush(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF))),
            Stroke = _targeted ? accent.LightBrush : Ui.Frozen(new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF))),
            StrokeThickness = 1,
            StrokeDashArray = [5, 4],
        });
        if (items.Count > 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            foreach (var item in items) row.Children.Add(Card(item));
            zone.Children.Add(new ScrollViewer
            {
                Content = row,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                PanningMode = PanningMode.HorizontalOnly,
                Focusable = false,
            });
            // The wheel scrolls the shelf sideways.
            zone.PreviewMouseWheel += (_, e) =>
            {
                if (zone.Children[^1] is ScrollViewer sv) sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta / 2.0);
                e.Handled = true;
            };
        }
        if (items.Count == 0 || _targeted)
        {
            var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            s.Children.Add(Ui.Glyph("", 16, accent.LightBrush));
            var t = Ui.Label(_targeted ? L("Release to stash") : L("Drop files here"), Ui.Body, weight: FontWeights.SemiBold);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.Margin = new Thickness(0, 4, 0, 0);
            s.Children.Add(t);
            if (!_targeted)
            {
                var sub = Ui.Label(L("Originals stay where they are"), Ui.Caption, Ui.Mist);
                sub.HorizontalAlignment = HorizontalAlignment.Center;
                s.Children.Add(sub);
            }
            _hint = new Border
            {
                Child = s,
                CornerRadius = new CornerRadius(14),
                Background = items.Count == 0 ? Brushes.Transparent : Ui.Frozen(new SolidColorBrush(Color.FromArgb(0xB8, 0, 0, 0))),
                IsHitTestVisible = false,
            };
            zone.Children.Add(_hint);
        }
        _zone = new Border { Child = zone };
        Children.Add(At(_zone, 0, 2));
    }

    private Border Card(ShelfRecord item)
    {
        bool exists = Files.Exists(item.Path);
        var s = new StackPanel { Width = 74 };
        var thumbCell = new Grid { Width = 44, Height = 44, HorizontalAlignment = HorizontalAlignment.Center };
        if (Files.Thumbnail(item.Path) is { } thumb)
            thumbCell.Children.Add(new Image { Source = thumb, Width = 40, Height = 40, Opacity = exists ? 1 : 0.4 });
        else
            thumbCell.Children.Add(Ui.Glyph("", 24, Ui.Mist));
        var remove = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            Background = Ui.Ink,
            Child = Ui.Glyph("", 7, Ui.OnAccent),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -4, -8, 0),
            Visibility = Visibility.Hidden,
        };
        thumbCell.Children.Add(Ui.Clickable(remove, () => Model.RemoveFromShelf(item.Id), tip: L("Remove from Stash"), pressScale: 0.85));
        s.Children.Add(thumbCell);
        var name = Ui.Label(MiddleTrim(item.Name, 5), Ui.Caption, weight: FontWeights.Medium);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.Margin = new Thickness(0, 4, 0, 0);
        s.Children.Add(name);
        var detail = Ui.Label(L(Files.Detail(item.Path)), 9.5, exists ? Ui.Mist : Accent.Hunt.LightBrush);
        detail.HorizontalAlignment = HorizontalAlignment.Center;
        s.Children.Add(detail);

        var card = new Border
        {
            Child = s,
            Padding = new Thickness(0, 6, 0, 6),
            CornerRadius = new CornerRadius(10),
            Background = Brushes.Transparent,
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = exists ? Cursors.Hand : Cursors.Arrow,
            ToolTip = exists ? item.Path + "\n" + L("Double-click to open · drag to share") : L("File missing: %@", item.Path),
        };
        card.MouseEnter += (_, _) => { card.Background = Ui.SurfaceRaised; remove.Visibility = Visibility.Visible; };
        card.MouseLeave += (_, _) => { card.Background = Brushes.Transparent; remove.Visibility = Visibility.Hidden; };

        // Double-click opens; dragging starts a real file drag to anywhere (Explorer, mail, chats).
        Point? down = null;
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2 && exists) { Files.Open(item.Path); return; }
            down = e.GetPosition(card);
        };
        card.MouseLeftButtonUp += (_, _) => down = null;
        card.MouseMove += (_, e) =>
        {
            if (down is not { } d || e.LeftButton != MouseButtonState.Pressed || !exists) return;
            var p = e.GetPosition(card);
            if (Math.Abs(p.X - d.X) < 4 && Math.Abs(p.Y - d.Y) < 4) return;
            down = null;
            DraggingOut = true;
            try { DragDrop.DoDragDrop(card, new DataObject(DataFormats.FileDrop, new[] { item.Path }), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); }
            catch (Exception) { }
            finally { DraggingOut = false; }
        };

        var menu = new ContextMenu();
        void Item(string title, Action act, bool enabled = true)
        {
            var mi = new MenuItem { Header = title, IsEnabled = enabled };
            mi.Click += (_, _) => act();
            menu.Items.Add(mi);
        }
        Item(L("Open"), () => Files.Open(item.Path), exists);
        Item(L("Show in Explorer"), () => Files.Reveal(item.Path), exists);
        Item(L("Share…"), () => ShareFiles?.Invoke([item.Path]), exists);
        Item(L("Copy"), () => Files.Copy([item.Path]), exists);
        Item(L("Copy Path"), () => { try { Clipboard.SetText(item.Path); } catch (Exception) { } });
        menu.Items.Add(new Separator());
        Item(L("Remove from Stash"), () => Model.RemoveFromShelf(item.Id));
        card.ContextMenu = menu;
        return card;
    }

    /// <summary>"Screenshot 2026…png" keeps both ends of a long name.</summary>
    private static string MiddleTrim(string s, int keep) =>
        s.Length <= keep * 2 + 1 ? s : s[..keep] + "…" + s[^keep..];
}
