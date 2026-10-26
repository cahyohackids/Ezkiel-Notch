using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace EzkielNotch.Platform;

/// <summary>
/// Whiskers on Windows: Caps Lock, the keyboard language, the network and the sound output, as short peeks.
/// Nothing here needs a permission or sees what you type (Caps Lock and the layout are read, not keys).
/// Windows draws its own volume flyout, so there is no second volume overlay.
/// </summary>
public sealed partial class WhiskersWatcher
{
    public event Action<bool>? CapsLock;
    public event Action<string>? Keyboard;
    public event Action<bool, bool>? Network;              // online, wifi
    public event Action<string, bool, string?>? Output;    // name, isHeadphones, previous headphones name

    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _fast = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _slow = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
    private bool? _caps;
    private nint _layout;
    private nint _layoutWindow;
    private (string Name, bool Headphones)? _output;
    private bool _overlays, _connections;

    public WhiskersWatcher(Dispatcher ui)
    {
        _ui = ui;
        _fast.Tick += (_, _) => PollKeys();
        _slow.Tick += (_, _) => PollOutput();
        NetworkChange.NetworkAvailabilityChanged += (_, e) => _ui.BeginInvoke(() =>
        {
            if (_connections) Network?.Invoke(e.IsAvailable, e.IsAvailable && WifiUp());
        });
    }

    public void Configure(bool overlays, bool connections)
    {
        _overlays = overlays;
        _connections = connections;
        if (overlays) _fast.Start(); else _fast.Stop();
        if (connections) { _output ??= ReadOutput(); _slow.Start(); } else _slow.Stop();
    }

    private void PollKeys()
    {
        bool caps = (GetKeyState(0x14) & 1) != 0;
        if (_caps is { } was && was != caps) CapsLock?.Invoke(caps);
        _caps = caps;

        // The layout is per window: only a change within the same window is the user switching languages.
        var fg = Native.Win32.GetForegroundWindow();
        uint thread = Native.Win32.GetWindowThreadProcessId(fg, out _);
        var hkl = GetKeyboardLayout(thread);
        if (hkl == 0) return;
        if (fg == _layoutWindow && _layout != 0 && hkl != _layout) Keyboard?.Invoke(LayoutName(hkl));
        _layout = hkl;
        _layoutWindow = fg;
    }

    private static string LayoutName(nint hkl)
    {
        try { return CultureInfo.GetCultureInfo((int)(hkl & 0xFFFF)).DisplayName; }
        catch (Exception) { return "Keyboard"; }
    }

    private static bool WifiUp()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().Any(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
        }
        catch (Exception) { return false; }
    }

    private void PollOutput()
    {
        var now = ReadOutput();
        if (now is null) return;
        var before = _output;
        _output = now;
        if (before is null || before.Value.Name == now.Value.Name) return;
        if (now.Value.Headphones) Output?.Invoke(now.Value.Name, true, null);
        else if (before.Value.Headphones) Output?.Invoke(now.Value.Name, false, before.Value.Name);
    }

    // MARK: Default sound output (Core Audio)

    private static (string Name, bool Headphones)? ReadOutput()
    {
        IMMDeviceEnumerator? e = null;
        IMMDevice? d = null;
        IPropertyStore? p = null;
        try
        {
            e = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (e.GetDefaultAudioEndpoint(0 /* eRender */, 0 /* eConsole */, out d) != 0 || d is null) return null;
            if (d.OpenPropertyStore(0, out p) != 0 || p is null) return null;
            var nameKey = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
            var formKey = new PROPERTYKEY { fmtid = new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), pid = 0 };
            string name = p.GetValue(ref nameKey, out var nv) == 0 ? Marshal.PtrToStringUni(nv.p) ?? "" : "";
            PropVariantClear(ref nv);
            uint form = p.GetValue(ref formKey, out var fv) == 0 ? fv.ui : 0;
            PropVariantClear(ref fv);
            // Headphones (3) or a headset (5); Bluetooth buds report one of these.
            bool headphones = form is 3 or 5 || name.Contains("AirPods", StringComparison.OrdinalIgnoreCase) || name.Contains("Buds", StringComparison.OrdinalIgnoreCase);
            return name.Length == 0 ? null : (name, headphones);
        }
        catch (Exception) { return null; }
        finally
        {
            if (p is not null) Marshal.ReleaseComObject(p);
            if (d is not null) Marshal.ReleaseComObject(d);
            if (e is not null) Marshal.ReleaseComObject(e);
        }
    }

    [LibraryImport("user32.dll")]
    private static partial short GetKeyState(int key);

    [LibraryImport("user32.dll")]
    private static partial nint GetKeyboardLayout(uint thread);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT v);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    // 24 bytes on 64-bit Windows: the union follows an 8-byte header.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public nint p;
        [FieldOffset(8)] public uint ui;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out nint devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, nint activationParams, out nint iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }
}
