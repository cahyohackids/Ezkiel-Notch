using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;

namespace EzkielNotch.Platform;

/// <summary>
/// Prowl: is a microphone or camera on right now? Windows records every app's use of them in the
/// capability consent store (the same data as the mic/camera icons in the taskbar): an entry whose
/// LastUsedTimeStop is 0 is in use. Read from the registry, no permission needed.
/// </summary>
public sealed class ProwlWatcher
{
    public bool MicInUse { get; private set; }
    public bool CameraInUse { get; private set; }
    public event Action? Changed;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };

    private const string Store = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

    public ProwlWatcher() => _timer.Tick += (_, _) => Poll();

    public void SetEnabled(bool on)
    {
        if (on) { _timer.Start(); Poll(); }
        else
        {
            _timer.Stop();
            Set(false, false);
        }
    }

    private void Poll() => Set(InUse("microphone"), InUse("webcam"));

    private void Set(bool mic, bool cam)
    {
        if (mic == MicInUse && cam == CameraInUse) return;
        MicInUse = mic;
        CameraInUse = cam;
        Changed?.Invoke();
    }

    private static bool InUse(string capability)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(Store + capability);
            if (root is null) return false;
            foreach (var name in root.GetSubKeyNames())
            {
                using var app = root.OpenSubKey(name);
                if (app is null) continue;
                if (name == "NonPackaged")
                {
                    foreach (var exe in app.GetSubKeyNames())
                    {
                        using var k = app.OpenSubKey(exe);
                        if (Active(k)) return true;
                    }
                }
                else if (Active(app)) return true;
            }
        }
        catch (Exception) { }
        return false;
    }

    private static bool Active(RegistryKey? k) =>
        k?.GetValue("LastUsedTimeStart") is long start && start > 0 && k.GetValue("LastUsedTimeStop") is long stop && stop == 0;
}

/// <summary>Battery for Whiskers and the closed pill: percent, charging, and a peek when it runs low.</summary>
public sealed class PowerWatcher
{
    public int? Percent { get; private set; }
    public bool Charging { get; private set; }
    public bool HasBattery { get; private set; }
    public event Action? Changed;
    /// <summary>Raised when the battery crosses 20 % or 10 % going down, or the charger is plugged in.</summary>
    public event Action<string, int>? Notice;   // kind ("low", "critical", "charging"), percent

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(20) };

    public PowerWatcher()
    {
        var ui = Dispatcher.CurrentDispatcher;
        _timer.Tick += (_, _) => Poll();
        SystemEvents.PowerModeChanged += (_, e) => { if (e.Mode == PowerModes.StatusChange) ui.BeginInvoke(Poll); };
    }

    public void Start()
    {
        Poll(initial: true);
        _timer.Start();
    }

    private void Poll() => Poll(false);

    private void Poll(bool initial)
    {
        if (!GetSystemPowerStatus(out var s)) return;
        bool has = (s.BatteryFlag & 128) == 0 && s.BatteryLifePercent != 255;
        int? pct = has ? s.BatteryLifePercent : null;
        bool charging = has && s.ACLineStatus == 1 && pct < 100;
        var before = (Percent, Charging);
        HasBattery = has;
        if (!initial && has && pct is { } p)
        {
            if (charging && !before.Charging && s.ACLineStatus == 1) Notice?.Invoke("charging", p);
            if (!charging && before.Percent is { } was)
            {
                if (was > 10 && p <= 10) Notice?.Invoke("critical", p);
                else if (was > 20 && p <= 20) Notice?.Invoke("low", p);
            }
        }
        Percent = pct;
        Charging = charging;
        if (before != (Percent, Charging)) Changed?.Invoke();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}
