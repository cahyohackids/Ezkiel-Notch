using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EzkielNotch.Core;
using EzkielNotch.Island;
using EzkielNotch.Platform;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Rooms;

/// <summary>Settings › Features: sync tasks, notes, water and focus with other PCs and Macs through a cloud folder
/// (the Mac's SyncCard). The card updates itself in place, so a sync finishing never takes the name field away.</summary>
public static class SyncSettingsRows
{
    private static Action? _listening;

    /// <summary>The Sync card, or nothing when the app runs without sync.</summary>
    public static void Add(Panel list)
    {
        if (SyncService.Current is not { } sync) return;
        var body = new StackPanel();
        var card = new Border
        {
            Child = body,
            MinHeight = 38,
            Padding = new Thickness(10, 6, 8, 6),
            CornerRadius = new CornerRadius(10),
            Background = Ui.Surface,
            Margin = new Thickness(0, 0, 0, 3),
        };
        list.Children.Add(card);

        TextBlock? status = null;
        StackPanel? devices = null;
        (bool, bool)? shape = null;

        void Render()
        {
            body.Children.Clear();
            shape = (sync.Config.Enabled, sync.Preview is not null);
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = Ui.Glyph("", 13, sync.Config.Enabled ? Accent.Oasis.LightBrush : Ui.Mist);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            head.Children.Add(icon);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            text.Children.Add(Ui.Label(L("Sync"), Ui.Body, weight: FontWeights.Medium));
            status = Ui.Label("", Ui.Caption, Ui.Mist, trim: false);
            status.TextWrapping = TextWrapping.Wrap;
            text.Children.Add(status);
            Grid.SetColumn(text, 1);
            head.Children.Add(text);
            UIElement? action = sync.Config.Enabled
                ? Ui.Pill(sync.Syncing ? L("Syncing…") : L("Sync now"), sync.SyncNow, Accent.Oasis, glyph: "")
                : sync.Preview is null ? Ui.Pill(L("Pick folder…"), sync.ChooseFolder, Accent.Oasis, prominent: true, glyph: "") : null;
            if (action is FrameworkElement fe)
            {
                fe.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(fe, 2);
                head.Children.Add(fe);
            }
            body.Children.Add(head);

            var detail = new StackPanel { Margin = new Thickness(26, 6, 0, 0) };
            if (sync.Preview is { } p)
            {
                detail.Children.Add(Ui.Label(p.Folder, Ui.Caption, weight: FontWeights.Medium));
                foreach (var line in p.Lines)
                {
                    var l = Ui.Label(line, Ui.Caption, Ui.Mist, trim: false);
                    l.TextWrapping = TextWrapping.Wrap;
                    detail.Children.Add(l);
                }
                var buttons = Ui.Row(6, Ui.Pill(L("Start syncing"), sync.ConfirmPreview, Accent.Oasis, prominent: true, glyph: ""),
                                     Ui.TextButton(L("Cancel"), sync.CancelPreview));
                buttons.Margin = new Thickness(0, 6, 0, 0);
                detail.Children.Add(buttons);
                body.Children.Add(detail);
            }
            else if (sync.Config.Enabled)
            {
                var folder = new Grid();
                folder.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                folder.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                folder.Children.Add(Ui.Label(sync.Config.Folder, Ui.Caption));
                var change = Ui.TextButton(L("Change"), sync.ChooseFolder);
                Grid.SetColumn(change, 1);
                folder.Children.Add(change);
                detail.Children.Add(folder);

                var (box, input) = Ui.Field(L("Device name"), i => sync.SetDeviceName(i.Text), 26);
                input.Text = sync.Config.DeviceName;
                input.LostKeyboardFocus += (_, _) => sync.SetDeviceName(input.Text);
                box.Width = 170;
                var nameRow = Ui.Row(8, Ui.Label(L("This PC"), Ui.Caption, Ui.Mist), box);
                nameRow.Margin = new Thickness(0, 4, 0, 4);
                detail.Children.Add(nameRow);

                devices = new StackPanel();
                detail.Children.Add(devices);
                var stop = Ui.TextButton(L("Stop syncing on this device"), sync.Stop, Accent.Hunt.LightBrush);
                stop.HorizontalAlignment = HorizontalAlignment.Left;
                stop.Margin = new Thickness(-6, 2, 0, 0);
                detail.Children.Add(stop);
                body.Children.Add(detail);
            }
            else devices = null;
            Update();
        }

        void Update()
        {
            if (status is not null)
            {
                status.Text = sync.Status;
                status.Foreground = sync.IsError ? Accent.Hunt.LightBrush : Ui.Mist;
            }
            if (devices is null) return;
            devices.Children.Clear();
            if (sync.Config.Devices.Count == 0)
            {
                var none = Ui.Label(L("No other devices yet · pick the same folder on your other Mac or PC"), Ui.Caption, Ui.Mist, trim: false);
                none.TextWrapping = TextWrapping.Wrap;
                devices.Children.Add(none);
            }
            foreach (var d in sync.Config.Devices)
                devices.Children.Add(Ui.Row(6,
                    Ui.Glyph(d.Platform == "windows" ? "" : "", 11, Ui.Mist),
                    Ui.Label(d.Name.Length > 0 ? d.Name : L("another device"), Ui.Caption, weight: FontWeights.Medium),
                    Ui.Label(Seen(d), Ui.Caption, Ui.Mist)));
        }

        Render();
        if (_listening is not null) sync.Changed -= _listening;
        _listening = () =>
        {
            // The pane was rebuilt and this card is gone: nothing to do (the next card listens instead).
            if (PresentationSource.FromVisual(card) is null && !card.IsLoaded) return;
            if (shape != (sync.Config.Enabled, sync.Preview is not null) || (sync.Config.Enabled && devices is null)) Render();
            else Update();
        };
        sync.Changed += _listening;
    }

    private static string Seen(SyncDevice d)
    {
        if (d.Ignored) return L("not synced · update it or open it again");
        if (d.Written is not { } at) return "";
        var ago = DateTime.UtcNow - at;
        string when = ago.TotalMinutes < 1 ? L("just now")
            : ago.TotalHours < 1 ? L("%d min", (int)ago.TotalMinutes)
            : ago.TotalDays < 1 ? Lang.Time(at.ToLocalTime())
            : at.ToLocalTime().ToString("d", Lang.Culture);
        return L("seen %@", when);
    }
}
