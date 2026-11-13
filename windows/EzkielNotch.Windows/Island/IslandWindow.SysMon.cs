using System.Windows.Controls;
using System.Windows.Media;
using EzkielNotch.Core;
using EzkielNotch.Platform;
using EzkielNotch.Rooms;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Island;

/// <summary>
/// System monitor (the Mac's AppModel+SysMon): when to read, the closed pill (CPU % or network speed) and the peeks.
/// Lost and regained connections are Whiskers' peek (Wi-Fi &amp; headphones) and a low battery is the battery peek;
/// neither is repeated here.
/// </summary>
public partial class IslandWindow
{
    public SysMonSampler SysMon { get; } = new();
    private readonly SysAlerts _sysAlerts = new();
    private bool _sysmonRunning;
    private TextBlock? _sysPillText, _sysPillGlyph;

    // Segoe Fluent Icons.
    internal const string CpuGlyph = "";
    internal const string MemoryGlyph = "";
    internal const string NetworkGlyph = "";
    internal const string DiskGlyph = "";
    private const string DownGlyph = "";
    private const string UpGlyph = "";
    private const string OfflineGlyph = "";

    /// <summary>The user wants the CPU or network pill in the closed island.</summary>
    public bool SysPillOn => Model.Settings.Sysmon && Model.Settings.SysmonPill != SysPill.Off;

    /// <summary>The Den card is on screen.</summary>
    public bool SysmonCardShowing => Model.Settings.Sysmon && Presentation == Presentation.Expanded && Selected == Room.Den;

    /// <summary>The pill is on screen.</summary>
    public bool SysmonPillShowing => SysPillOn && Presentation == Presentation.Lip && ShowsWings;

    private bool SysmonAlertsOn => Model.Settings.Sysmon
        && (Model.Settings.SysmonCpuAlert || Model.Settings.SysmonMemoryAlert || Model.Settings.SysmonDiskAlert);

    /// <summary>From the 1 s tick: read every 2 s while the card or the pill shows, every 10 s while only alerts listen,
    /// never while the monitor is off.</summary>
    private void SysMonTick()
    {
        var s = Model.Settings;
        if (s.Sysmon != _sysmonRunning)
        {
            _sysmonRunning = s.Sysmon;
            if (!s.Sysmon)
            {
                SysMon.Stop();
                _sysAlerts.Reset();
            }
        }
        bool fast = SysmonCardShowing || SysmonPillShowing;
        var now = DateTime.UtcNow;
        if (!s.Sysmon || !(fast || SysmonAlertsOn) || !SysMon.IsDue(now, fast)) return;
        var r = SysMon.Sample(now, Power);
        CheckSysAlerts(r, now);
        if (SysmonCardShowing) (CurrentPane as DenPane)?.SysMonUpdated();
    }

    private void CheckSysAlerts(SysMonSampler.Reading r, DateTime now)
    {
        var s = Model.Settings;
        double t = (now - DateTime.UnixEpoch).TotalSeconds;
        if (s.SysmonCpuAlert && r.Cpu is { } cpu && _sysAlerts.Feed("cpu", cpu, t))
        {
            // The busiest app takes a second to find (two passes over the process list), off the UI thread.
            Task.Run(() => SysMonSampler.TopProcess()).ContinueWith(top => Dispatcher.BeginInvoke(() => ShowCpuPeek(cpu, top.Result)));
        }
        if (s.SysmonMemoryAlert && r.MemTotal > 0 && _sysAlerts.Feed("memory", SysMemory.AlertLoad(r.MemPercent, null), t))
            ShowPeek(new PeekInfo(L("Memory almost full"), L("Quit apps you aren't using"))
            {
                Room = Room.Den, Look = new PeekLook(MemoryGlyph, PeekTint.Berry), Value = SysFormat.Percent(r.MemPercent),
            });
        if (s.SysmonDiskAlert && r.DiskFree is { } free && _sysAlerts.Feed("disk", free / 1e9, t))
            ShowPeek(new PeekInfo(L("System drive almost full"), L("%@ free on %@", SysFormat.Disk(free), r.DiskName))
            {
                Room = Room.Den, Look = new PeekLook(DiskGlyph, PeekTint.Berry, Sound: "Funk"),
            });
    }

    /// <summary>CPU above 90 % for 30 s, with the app using most of it when that could be read.</summary>
    public void ShowCpuPeek(double cpu, string? top) =>
        ShowPeek(new PeekInfo(L("CPU is busy"), top is null ? L("Above 90% for 30 seconds") : L("Mostly %@", top))
        {
            Room = Room.Den, Look = new PeekLook(CpuGlyph, PeekTint.Den), Value = SysFormat.Percent(cpu),
        });

    /// <summary>Part of LiveKey: the pill is rebuilt when its kind or the connection changes; its number updates in place.</summary>
    private string SysPillKey => SysPillOn ? $"{Model.Settings.SysmonPill}:{SysMon.Current.Online}" : "";

    /// <summary>The pill on the right of the closed island: a glyph and CPU % or the busier direction's speed.</summary>
    private void AddSysPill(Panel right)
    {
        _sysPillGlyph = Ui.Glyph("", 9);
        _sysPillText = Ui.Number("", 11);
        _sysPillText.Margin = new System.Windows.Thickness(2, 0, 0, 0);
        right.Children.Add(_sysPillGlyph);
        right.Children.Add(_sysPillText);
        UpdateSysPill();
    }

    /// <summary>Every second while the closed island shows the pill (the reading itself changes every 2 s).</summary>
    private void UpdateSysPill()
    {
        if (_sysPillText is null || _sysPillGlyph is null || !SysPillOn) return;
        var r = SysMon.Current;
        Brush brush;
        if (Model.Settings.SysmonPill == SysPill.Network)
        {
            double down = r.Down ?? 0, up = r.Up ?? 0;
            _sysPillGlyph.Text = !r.Online ? OfflineGlyph : up > down ? UpGlyph : DownGlyph;
            _sysPillText.Text = r.Online ? SysFormat.CompactRate(Math.Max(down, up)) : "";
            brush = r.Online ? Accent.Oasis.LightBrush : Ui.Frozen(new SolidColorBrush(Accent.Hex(0xFF5A7A)));
            _sysPillText.ToolTip = r.Online ? $"↓ {(r.Down is { } d ? SysFormat.Rate(d) : "–")}  ↑ {(r.Up is { } u ? SysFormat.Rate(u) : "–")}" : L("Offline");
        }
        else
        {
            _sysPillGlyph.Text = CpuGlyph;
            _sysPillText.Text = r.Cpu is { } c ? SysFormat.Percent(c) : "–";
            brush = (r.Cpu ?? 0) >= 90 ? Ui.Frozen(new SolidColorBrush(Accent.Hex(0xFF5A7A))) : Accent.Den.LightBrush;
            _sysPillText.ToolTip = $"CPU {_sysPillText.Text}";
        }
        _sysPillGlyph.Foreground = brush;
        _sysPillText.Foreground = brush;
    }
}
