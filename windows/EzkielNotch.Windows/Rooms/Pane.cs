using System.Windows;
using System.Windows.Controls;
using EzkielNotch.Core;
using EzkielNotch.Island;

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

    /// <summary>Opened with the shortcut or the tray: put the caret where you'd type first (Hunt's "New task").</summary>
    public virtual void FocusFirstField() { }

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
