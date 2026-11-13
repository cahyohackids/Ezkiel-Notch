using System.Windows;
using System.Windows.Controls;
using EzkielNotch.Core;
using EzkielNotch.Island;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Rooms;

/// <summary>The system monitor's Settings rows, kept out of SettingsPane so features merge cleanly.</summary>
internal static class SysMonSettingsRows
{
    /// <summary>System monitor (Features): on/off, the closed pill, and its peeks. Lost and regained connections share
    /// Whiskers' "Wi-Fi &amp; headphones" switch; low battery is the battery peek, always on.</summary>
    public static void AddSysMonRows(Panel list, NotchSettings s, Func<string, string, string, bool, Action<bool>, Accent?, Border> toggle,
                                     Func<string, string, UIElement[], Border> option, Action changed, Action refresh)
    {
        list.Children.Add(toggle("\uE9D9", L("System monitor"), L("CPU, memory, network, battery and disk in Den. Nothing is read while it's off."),
                                 s.Sysmon, v => { s.Sysmon = v; list.Dispatcher.BeginInvoke(refresh); }, Accent.Den));
        if (!s.Sysmon) return;
        list.Children.Add(option("\uEC4A", L("Closed island"), [.. new[] { (SysPill.Off, "Off"), (SysPill.Cpu, "CPU"), (SysPill.Network, "Network") }.Select(o =>
            (UIElement)Ui.Chip(L(o.Item2), s.SysmonPill == o.Item1, Accent.Den, () => { s.SysmonPill = o.Item1; changed(); }))]));
        list.Children.Add(toggle("\uEC4A", L("CPU busy"), L("A peek after 30 s above 90%, with the busiest app"), s.SysmonCpuAlert, v => s.SysmonCpuAlert = v, Accent.Den));
        list.Children.Add(toggle("\uE964", L("Memory almost full"), L("A peek when 90% of memory is in use"), s.SysmonMemoryAlert, v => s.SysmonMemoryAlert = v, Accent.Den));
        list.Children.Add(toggle("\uEDA2", L("Disk almost full"), L("A peek under 5 GB free on the system drive"), s.SysmonDiskAlert, v => s.SysmonDiskAlert = v, Accent.Den));
        list.Children.Add(toggle("\uE968", L("Internet lost and back"), L("Same switch as Whiskers › Wi-Fi & headphones"), s.ConnectionPeeks,
                                 v => { s.ConnectionPeeks = v; list.Dispatcher.BeginInvoke(refresh); }, Accent.Neutral));
    }
}
