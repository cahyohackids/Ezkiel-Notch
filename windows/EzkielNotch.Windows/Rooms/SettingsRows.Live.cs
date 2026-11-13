using System.Windows;
using System.Windows.Controls;
using EzkielNotch.Core;
using EzkielNotch.Island;
using EzkielNotch.Platform;
using static EzkielNotch.Core.Lang;

namespace EzkielNotch.Rooms;

/// <summary>Settings › Features: live activities (downloads in progress in the closed island, the quick timers' sound).
/// The meeting countdown stays with Rally's rows.</summary>
public static class LiveSettingsRows
{
    public static void AddLiveRows(Panel list, NotchSettings s, Func<string, string, bool, Action<bool>, Accent?, Border> toggle,
                                   Func<string, UIElement[], Border> option, Action changed)
    {
        list.Children.Add(toggle(L("Downloads in the island"), L("Progress while a browser downloads to Downloads, then Open · Stash · Show"),
                                 s.LiveDownloads, v => s.LiveDownloads = v, Accent.Stash));
        list.Children.Add(option(L("Timer sound"), [.. TimerSounds.Choices.Select(name =>
            Ui.Chip(name == "none" ? L("None") : name, TimerSounds.Normalize(s.TimerSound) == name, Accent.Den, () =>
            {
                s.TimerSound = name;
                if (name != "none") Sounds.Play(name);
                changed();
            }))]));
    }
}
