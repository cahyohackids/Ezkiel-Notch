using System.Windows;
using System.Windows.Controls;
using EzkielNotch.Core;
using EzkielNotch.Platform;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Island;

/// <summary>
/// Live activities: quick timers ("Masak 10m"), downloads in progress, and the meeting countdown in the closed
/// island, plus their peeks. When several things are live, the closed island shows the first of (Core's LiveKind):
/// Den's focus timer, a meeting within 10 minutes, the soonest quick timer, a download in progress, an AI agent,
/// music, unread messages, charging, the water ring. A meeting, a timer or a download puts its own icon on the left
/// and its count on the right.
/// </summary>
public partial class IslandWindow
{
    public DownloadWatcher Downloads { get; private set; } = null!;
    /// <summary>The pill's changing value (a countdown, a size), refreshed every second without a rebuild.</summary>
    private Action? _liveTick;
    private readonly Dictionary<string, DateTime> _announcedDownloads = new(StringComparer.OrdinalIgnoreCase);

    private const string TimerGlyph = "";

    private void InitLiveActivities()
    {
        Downloads = new DownloadWatcher(Dispatcher);
        Downloads.Changed += _ => UpdateLive();
        Downloads.Finished += path => AnnounceDownload(path, followed: true);
        Model.TimerDone += ShowTimerDone;
    }

    /// <summary>The download the closed island shows (the largest), and how many there are.</summary>
    public (ActiveDownload Download, int Count)? LeadDownload =>
        Model.Settings.LiveDownloads ? DownloadTracker.Lead(Downloads.Active) : null;

    /// <summary>What the closed island shows while live (null: nothing).</summary>
    public LiveKind? ClosedLive
    {
        get
        {
            var live = new List<LiveKind> { LiveKind.Water };
            if (Model.Focus.IsRunning) live.Add(LiveKind.Focus);
            if (MeetingSoon is not null) live.Add(LiveKind.Meeting);
            if (Model.SoonestTimer is not null) live.Add(LiveKind.Timer);
            if (LeadDownload is not null) live.Add(LiveKind.Download);
            if (Model.Settings.ClosedFace != "eyes" && Model.LiveAgents.Count > 0) live.Add(LiveKind.Agent);
            if (Media.Current?.IsPlaying == true) live.Add(LiveKind.Music);
            if (Model.Settings.Roar && Model.UnreadTotal > 0) live.Add(LiveKind.Unread);
            if (Power.Charging) live.Add(LiveKind.Charging);
            return LivePill.Pick(live);
        }
    }

    private static bool OwnsLeftWing(LiveKind? kind) => kind is LiveKind.Meeting or LiveKind.Timer or LiveKind.Download;

    /// <summary>The left of the pill for a meeting, a timer or a download: its icon (a ring round the arrow when the
    /// download's size is known), with a count when there are several.</summary>
    private FrameworkElement? LiveActivityLeft(LiveKind? kind)
    {
        switch (kind)
        {
            case LiveKind.Meeting:
                return Ui.Glyph(PeekLooks.Calendar, 12, Accent.Rally.LightBrush);
            case LiveKind.Timer:
                return WithCount(Ui.Glyph(TimerGlyph, 12, Accent.Den.LightBrush), Model.Data.Timers.Count, Accent.Den);
            case LiveKind.Download when LeadDownload is { } lead:
                FrameworkElement icon;
                if (lead.Download.Fraction is { } f)
                {
                    var ring = new ProgressRing { Width = 16, Height = 16, Thickness = 2, Accent = Accent.Stash, Progress = f };
                    var g = new Grid { Width = 16, Height = 16 };
                    g.Children.Add(ring);
                    g.Children.Add(Ui.Glyph("", 7.5, Accent.Stash.LightBrush));
                    var before = _liveTick;
                    _liveTick = () =>
                    {
                        before?.Invoke();
                        if (LeadDownload?.Download.Fraction is { } now) ring.Progress = now;
                    };
                    icon = g;
                }
                else icon = Ui.Glyph(PeekLooks.Download, 12, Accent.Stash.LightBrush);
                return WithCount(icon, lead.Count, Accent.Stash);
            default:
                return null;
        }
    }

    /// <summary>The right of the pill: "7 min" to a meeting, the soonest timer's mm:ss, a download's percent, or its
    /// size so far with the speed under it.</summary>
    private void LiveActivityRight(LiveKind? kind)
    {
        switch (kind)
        {
            case LiveKind.Meeting when MeetingSoon is { } meeting:
            {
                var t = Ui.Number("", 12, Accent.Rally.Vertical);
                void Fill() => t.Text = L("%d min", MeetingMinutes);
                Fill();
                t.ToolTip = L("Meeting in %d min", MeetingMinutes) + " · " + meeting.Title;
                System.Windows.Automation.AutomationProperties.SetName(t, L("Meeting in %d min", MeetingMinutes) + ", " + meeting.Title);
                LiveRight.Children.Add(t);
                Chain(Fill);
                break;
            }
            case LiveKind.Timer when Model.SoonestTimer is { } timer:
            {
                var t = Ui.Number("", 12, Accent.Den.Vertical);
                void Fill() => t.Text = FocusClock.Format(Model.SoonestTimer?.Remaining(DateTime.UtcNow) ?? TimeSpan.Zero);
                Fill();
                int count = Model.Data.Timers.Count;
                t.ToolTip = NotchModel.TimerName(timer) + (count > 1 ? " · " + L("%d timers", count) : "");
                LiveRight.Children.Add(t);
                Chain(Fill);
                break;
            }
            case LiveKind.Download when LeadDownload is { } lead:
            {
                string tip = lead.Count > 1 ? L("%d downloads", lead.Count) + " · " + lead.Download.FinalName : lead.Download.FinalName;
                if (lead.Download.Fraction is not null)
                {
                    var t = Ui.Number("", 12, Accent.Stash.LightBrush);
                    void Fill() => t.Text = $"{(int)Math.Floor((LeadDownload?.Download.Fraction ?? 0) * 100)}%";
                    Fill();
                    t.ToolTip = tip;
                    LiveRight.Children.Add(t);
                    Chain(Fill);
                }
                else
                {
                    var size = Ui.Number("", 10, Ui.Ink);
                    var speed = Ui.Number("", 8, Ui.Mist);
                    size.HorizontalAlignment = speed.HorizontalAlignment = HorizontalAlignment.Right;
                    void Fill()
                    {
                        var d = LeadDownload?.Download;
                        size.Text = DownloadTracker.Bytes(d?.Bytes ?? 0);
                        speed.Text = d?.BytesPerSecond is { } bps ? DownloadTracker.Speed(bps) : "";
                        speed.Visibility = speed.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                    }
                    Fill();
                    var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, ToolTip = tip };
                    stack.Children.Add(size);
                    stack.Children.Add(speed);
                    LiveRight.Children.Add(stack);
                    Chain(Fill);
                }
                System.Windows.Automation.AutomationProperties.SetName(LiveRight, L("Downloading %@", lead.Download.FinalName));
                break;
            }
        }
    }

    private void Chain(Action fill)
    {
        var before = _liveTick;
        _liveTick = () =>
        {
            before?.Invoke();
            fill();
        };
    }

    private static FrameworkElement WithCount(FrameworkElement icon, int count, Accent accent)
    {
        if (count <= 1) return icon;
        var g = new Grid();
        g.Children.Add(icon);
        g.Children.Add(new Border
        {
            MinWidth = 10,
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = accent.LightBrush,
            Padding = new Thickness(2, 0, 2, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -7, -4),
            Child = new TextBlock
            {
                Text = count.ToString(),
                FontSize = 7.5,
                FontWeight = FontWeights.Bold,
                Foreground = Ui.OnAccent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        return g;
    }

    // MARK: Peeks

    /// <summary>A download landed: the Pounce peek, with Open · Stash · Show. It shows when "Pounce on downloads" is
    /// on, or when live downloads followed it while in progress. Each file peeks once.</summary>
    public void AnnounceDownload(string path, bool followed)
    {
        var s = Model.Settings;
        if (!(s.PounceDownloads || (followed && s.LiveDownloads))) return;
        // Firefox's empty placeholder under the final name: its .part is still downloading.
        if (!followed && Downloads.InProgress(path)) return;
        var now = DateTime.UtcNow;
        if (_announcedDownloads.TryGetValue(path, out var at) && now - at < TimeSpan.FromSeconds(30)) return;
        _announcedDownloads[path] = now;
        if (Presentation == Presentation.Expanded) return;
        ShowPeek(new PeekInfo(L("Downloaded"), System.IO.Path.GetFileName(path))
        {
            Seconds = 6, Room = Room.Stash, File = path, OpensFile = true, Look = PeekLooks.Of(PeekKind.Download),
        });
    }

    /// <summary>A quick timer ended: a peek with "+5 min" and "Stop" and the chosen sound (with Sounds on). In the
    /// open island, a toast with "+5 min" instead.</summary>
    public void ShowTimerDone(QuickTimer t)
    {
        var sound = TimerSounds.Normalize(Model.Settings.TimerSound);
        var name = NotchModel.TimerName(t);
        if (Presentation == Presentation.Expanded)
        {
            if (Model.Settings.Sounds && sound != "none") Sounds.Play(sound);
            ShowToast(new ToastRequest(L("%@ is done", name), L("+5 min"), () => Model.SnoozeTimer(t)));
            return;
        }
        ShowPeek(new PeekInfo(L("%@ is done", name), L("%@ timer", QuickTimers.Short(t.Seconds)))
        {
            Seconds = 12,
            Urgent = true,
            Room = Room.Den,
            Look = new PeekLook(TimerGlyph, PeekTint.Den),
            Sound = sound == "none" ? "" : sound,
            Buttons =
            [
                new PeekButton(L("+5 min"), () => Model.SnoozeTimer(t), Accent.Den),
                new PeekButton(L("Stop"), () => Model.CancelTimer(t.Id), Accent.Den, Prominent: true),
            ],
        });
    }
}
