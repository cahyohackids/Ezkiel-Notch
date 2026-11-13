using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using EzkielNotch.Core;
using EzkielNotch.Native;
using EzkielNotch.Platform;
using EzkielNotch.Rooms;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Island;

/// <summary>Ask: a quick question to api.ezkielyna.store with the user's own key, answered in the open island
/// (the header button, Ctrl+Alt+A, the right-click menu and the tray).</summary>
public partial class IslandWindow
{
    private const int AskHotKeyId = 0xE22;
    /// <summary>Ctrl+Alt+N, Ctrl+Alt+Space and Ctrl+Alt+I are the open/close choices; Ask has its own.</summary>
    public const string AskHotkeyLabel = "Ctrl+Alt+A";
    /// <summary>The page height the ask box needs (a short room grows while it's open).</summary>
    public const double AskPaneHeight = 196;

    public bool AskHotKeyRegistered { get; private set; }
    /// <summary>Another app has Ctrl+Alt+A (the header button and the menus still open Ask).</summary>
    public static bool AskShortcutTaken { get; private set; }
    public bool AskOpen { get; private set; }
    public AskView? AskBox { get; private set; }
    private bool AskBusy => AskOpen && AskHub.Session.Busy;

    private readonly Border _askButtonHost = new() { Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
    private string _askSettingsShown = "";

    private void InitAsk()
    {
        DockPanel.SetDock(_askButtonHost, Dock.Right);
        if (PinHost.Parent is Panel header) header.Children.Insert(header.Children.IndexOf(PinHost) + 1, _askButtonHost);
        AskHub.Changed += () => Dispatcher.BeginInvoke(OnAskChanged);
        Model.Changed += room =>
        {
            if (room is not (null or Room.Settings)) return;
            if (room is null && AskBox is not null) RebuildAskBox();
            ConfigureAsk();
        };
        Loaded += (_, _) =>
        {
            HwndSource.FromHwnd(_hwnd)?.AddHook(AskHook);
            ConfigureAsk();
        };
        Closed += (_, _) =>
        {
            if (AskHotKeyRegistered) Win32.UnregisterHotKey(_hwnd, AskHotKeyId);
        };
    }

    private nint AskHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && wParam == AskHotKeyId)
        {
            AskFromKeyboard();
            handled = true;
        }
        return 0;
    }

    /// <summary>The setting decides the shortcut and the header button; turning it off puts the box away.</summary>
    private void ConfigureAsk()
    {
        bool on = Model.Settings.Ask;
        if (_hwnd != 0)
        {
            if (on && !AskHotKeyRegistered)
            {
                AskHotKeyRegistered = Win32.RegisterHotKey(_hwnd, AskHotKeyId, Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, 0x41);
                AskShortcutTaken = !AskHotKeyRegistered;
            }
            else if (!on && AskHotKeyRegistered)
            {
                Win32.UnregisterHotKey(_hwnd, AskHotKeyId);
                AskHotKeyRegistered = false;
                AskShortcutTaken = false;
            }
        }
        RenderAskButton();
        if (!on && AskOpen) SetAsk(false);
    }

    private void RenderAskButton() =>
        _askButtonHost.Child = Model.Settings.Ask
            ? Ui.IconButton("\uE82F", L("Ask · %@", AskHotkeyLabel), () => SetAsk(!AskOpen), 24, AskOpen ? Accent.Den.LightBrush : Ui.Mist)
            : null;

    /// <summary>Opens or puts away the ask box (over the page; the room stays as it was).</summary>
    public void SetAsk(bool on)
    {
        if (!on)
        {
            if (!AskOpen) return;
            bool typing = AskBox?.Input.IsKeyboardFocusWithin == true;
            HideAsk();
            if (typing) Focus();
            if (_stale.Remove(Selected)) CurrentPane?.Refresh();
            if (Presentation == Presentation.Expanded) Apply(ExpandedShape(Selected), animate: true);
            return;
        }
        if (!Model.Settings.Ask) return;
        if (MirrorOpen) SetMirror(false);
        bool changed = !AskOpen;
        AskOpen = true;
        if (AskBox is null) RebuildAskBox();
        AskBox!.Visibility = Visibility.Visible;
        PageHost.Visibility = Visibility.Hidden;
        AskBox.Render();
        RenderAskButton();
        if (Presentation != Presentation.Expanded) Expand();
        else if (changed) Apply(ExpandedShape(Selected), animate: true);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!AskOpen || Presentation != Presentation.Expanded || AskBox is null) return;
            EngageKeyboard("ask");
            AskBox.Input.Focus();
            AskBox.Input.CaretIndex = AskBox.Input.Text.Length;
        });
    }

    /// <summary>Closing the island or picking a room puts the box away (they reshape the island themselves).</summary>
    private void HideAsk()
    {
        if (!AskOpen) return;
        AskOpen = false;
        if (AskBox is not null) AskBox.Visibility = Visibility.Collapsed;
        PageHost.Visibility = Visibility.Visible;
        RenderAskButton();
    }

    /// <summary>The shortcut, the tray and the menu: open the box with the caret in it (the island waits for you,
    /// like opening it with its own shortcut), or close the island if the box is already up.</summary>
    public void AskFromKeyboard()
    {
        if (!Model.Settings.Ask) return;
        if (AskOpen && Presentation == Presentation.Expanded) Collapse();
        else OpenAsk();
    }

    public void OpenAsk()
    {
        _openedByKeyboard = true;
        SetAsk(true);
    }

    private void RebuildAskBox()
    {
        var parent = (Grid)MirrorView.Parent;
        if (AskBox is not null) parent.Children.Remove(AskBox);
        AskBox = new AskView(this) { Height = AskPaneHeight, Visibility = AskOpen ? Visibility.Visible : Visibility.Collapsed };
        // Over the page and the Mirror, under the toasts.
        parent.Children.Insert(parent.Children.IndexOf(ToastView), AskBox);
        if (AskOpen) AskBox.Render();
    }

    private void OnAskChanged()
    {
        if (AskOpen) AskBox?.Render();
        // Settings shows the key check; it redraws only when that changes, not on every word of an answer.
        var s = AskHub.Session;
        var shown = $"{AskHub.HasKey}|{s.KeyCheck}|{s.KeyError?.Code}|{s.Models.Count}";
        if (shown == _askSettingsShown) return;
        _askSettingsShown = shown;
        if (Presentation == Presentation.Expanded && Selected == Room.Settings && !AskOpen) CurrentPane?.Refresh();
        else if (_panes.ContainsKey(Room.Settings)) _stale.Add(Room.Settings);
    }

    // MARK: Actions

    public void AskSend()
    {
        if (AskBox is null) return;
        var question = AskBox.Input.Text;
        if (question.Trim().Length == 0) return;
        AskHub.Draft = "";
        AskBox.Input.Text = "";
        _ = AskHub.Session.Send(question, AskHub.Key, Model.Settings.AskModel, IsIndonesian);
    }

    public void AskRetry()
    {
        var s = AskHub.Session;
        if (s.Question.Length == 0) return;
        _ = s.Send(s.Question, AskHub.Key, Model.Settings.AskModel, IsIndonesian);
    }

    public void AskStop() => AskHub.Session.Stop();

    public void AskNew()
    {
        AskHub.Session.Clear();
        AskBox?.Input.Focus();
    }

    public void AskCopy()
    {
        var answer = AskHub.Session.Answer;
        if (answer.Length == 0) return;
        try
        {
            Clipboard.SetText(Ask.PlainText(answer));
            ShowToast(new ToastRequest(L("Copied")));
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // The clipboard is busy: nothing copied, nothing claimed.
        }
    }

    /// <summary>A Hunt task: the question (a time in it becomes the reminder), or the answer's first line.</summary>
    public void AskMakeTask()
    {
        var s = AskHub.Session;
        if (s.Question.Length == 0) return;
        var (title, due) = Ask.MakeTask(s.Question, s.Answer, DateTime.Now);
        Model.AddTask(title, due?.ToUniversalTime());
        var text = due is { } d ? L("Added to Hunt · %@", Lang.Time(d)) : L("Added to Hunt");
        ShowToast(new ToastRequest(text, L("Open Hunt"), () => Select(Room.Hunt)));
    }

    /// <summary>A Scratch note: the question as its title, the answer as its text.</summary>
    public void AskSaveNote()
    {
        var s = AskHub.Session;
        if (s.Answer.Length == 0) return;
        var (title, body) = Ask.Note(s.Question, s.Answer);
        Model.AddNote(title, body);
        ShowToast(new ToastRequest(L("Saved to Scratch"), L("Open Scratch"), () => Select(Room.Scratch)));
    }

    /// <summary>The key, the model and "Test key" live in Settings › Features.</summary>
    public void OpenAskSettings()
    {
        AskSettingsRows.Reveal = true;
        if (_panes.ContainsKey(Room.Settings)) _stale.Add(Room.Settings);
        HideAsk();
        Select(Room.Settings);
        if (Presentation != Presentation.Expanded) Expand();
    }
}
