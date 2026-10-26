using System.Windows;
using System.Windows.Controls;
using EzkielNotch.Core;
using EzkielNotch.Island;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Rooms;

/// <summary>One room's page. It rebuilds itself from the model in Refresh(), and is laid out at the room's
/// pane height (RoomInfo.PaneHeight) inside the open island.</summary>
public abstract class Pane : Grid
{
    protected NotchModel Model { get; }
    public RoomInfo Info { get; }

    protected Pane(NotchModel model, Room room)
    {
        Model = model;
        Info = RoomInfo.Of(room);
        Height = Info.PaneHeight;
        VerticalAlignment = VerticalAlignment.Top;
    }

    public abstract void Refresh();

    /// <summary>How tall the room is right now (Purr grows when Chorus is on).</summary>
    public virtual double DesiredHeight => Info.PaneHeight;

    /// <summary>Once a second while the room is on screen.</summary>
    public virtual void SecondTick() { }

    /// <summary>The room was just shown or hidden (to start or stop animations, flush edits).</summary>
    public virtual void Shown(bool visible) { }

    protected static Grid Columns(params GridLength[] widths)
    {
        var g = new Grid();
        foreach (var w in widths) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        return g;
    }

    protected static T At<T>(T e, int column, int row = 0) where T : UIElement
    {
        SetColumn(e, column);
        SetRow(e, row);
        return e;
    }

    protected static readonly GridLength Auto = GridLength.Auto;
    protected static readonly GridLength Star = new(1, GridUnitType.Star);
    protected static GridLength Px(double v) => new(v);
}

/// <summary>Roar, Purr and Stash arrive in later phases; until then the tab says so.</summary>
public sealed class ComingPane(NotchModel model, Room room) : Pane(model, room)
{
    public override void Refresh()
    {
        Children.Clear();
        var card = Ui.Card(null, 14);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new System.Windows.Controls.Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/lion.png")),
            Width = 44,
            Height = 44,
            Margin = new Thickness(0, 0, 12, 0),
        });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 280 };
        text.Children.Add(Ui.Label(L("%@ is coming to Windows", Info.Name), Ui.Title, weight: FontWeights.SemiBold));
        var body = Ui.Label(L(Info.Subtitle) + " · " + L("arrives in a later update."), Ui.Secondary, Ui.Mist, trim: false);
        body.TextWrapping = TextWrapping.Wrap;
        body.Margin = new Thickness(0, 3, 0, 0);
        text.Children.Add(body);
        row.Children.Add(text);
        card.Child = row;
        Children.Add(card);
    }
}
