namespace EzkielNotch.Native;

/// <summary>Which monitor the island lives on, in physical pixels and DPI.</summary>
internal readonly record struct ScreenSpot(nint Monitor, Win32.RECT Bounds, Win32.RECT Work, double Scale, bool IsPrimary)
{
    /// <summary>The primary monitor (Phase 0; a picker comes with Settings).</summary>
    public static ScreenSpot Primary()
    {
        var monitor = Win32.MonitorFromPoint(new Win32.POINT { X = 0, Y = 0 }, Win32.MONITOR_DEFAULTTOPRIMARY);
        return From(monitor);
    }

    public static ScreenSpot From(nint monitor)
    {
        var info = Win32.Info(monitor) ?? default;
        double scale = 1;
        if (Win32.GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0) scale = dpiX / 96.0;
        return new ScreenSpot(monitor, info.rcMonitor, info.rcWork, scale, (info.dwFlags & Win32.MONITORINFOF_PRIMARY) != 0);
    }
}

/// <summary>Is something fullscreen right now (a game, a video, slides)? Windows' own answer first,
/// then a covering foreground window as a second opinion (browsers' fullscreen video).</summary>
internal static class Fullscreen
{
    public static bool Active(ScreenSpot screen, nint ownWindow)
    {
        if (Win32.SHQueryUserNotificationState(out var state) == 0 &&
            state is Win32.NotificationState.Busy or Win32.NotificationState.RunningD3DFullScreen or Win32.NotificationState.PresentationMode)
        {
            return true;
        }
        var fg = Win32.GetForegroundWindow();
        if (fg == 0 || fg == ownWindow || fg == Win32.GetShellWindow() || fg == Win32.GetDesktopWindow()) return false;
        // The desktop and the taskbar cover the screen too; they aren't "fullscreen".
        var cls = Win32.ClassName(fg);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (!Win32.GetWindowRect(fg, out var r)) return false;
        var b = screen.Bounds;
        return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
    }
}
