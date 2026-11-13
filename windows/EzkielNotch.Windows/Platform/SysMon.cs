using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using EzkielNotch.Core;

namespace EzkielNotch.Platform;

/// <summary>
/// System monitor: reads CPU, memory, network, the system drive and the battery when asked (<see cref="Sample"/>),
/// never on its own. The island asks every 2 s while the Den card or the closed pill shows, every 10 s while only
/// alerts listen, and not at all while it's off. Each reading is a few cheap system calls.
/// </summary>
public sealed class SysMonSampler
{
    public sealed record Reading
    {
        public double? Cpu { get; init; }
        public double MemUsed { get; init; }
        public double MemTotal { get; init; }
        public double? Down { get; init; }
        public double? Up { get; init; }
        public bool Online { get; init; } = true;
        public double? DiskFree { get; init; }
        public double? DiskTotal { get; init; }
        /// <summary>"C:" — the drive Windows runs from.</summary>
        public string DiskName { get; init; } = "C:";
        public int? Battery { get; init; }
        public bool Charging { get; init; }
        public bool OnAC { get; init; }
        /// <summary>Minutes to empty on battery (Windows doesn't estimate time to full); null while unknown.</summary>
        public int? BatteryMinutes { get; init; }

        public double MemPercent => MemTotal > 0 ? MemUsed / MemTotal * 100 : 0;
    }

    public Reading Current { get; private set; } = new();
    public SysSeries Cpu { get; } = new(60);
    public SysSeries Memory { get; } = new(60);
    public SysSeries Down { get; } = new(60);
    public SysSeries Up { get; } = new(60);
    /// <summary>Goes up with every reading, so the card redraws only when something new arrived.</summary>
    public int Version { get; private set; }

    private DateTime? _last;
    private (ulong Busy, ulong Idle)? _cpu;
    private (Dictionary<string, ulong> Down, Dictionary<string, ulong> Up, DateTime At)? _net;
    private DateTime? _diskAt;
    private bool _simulated;

    /// <summary>Due for a reading? 2 s while something shows it, 10 s for alerts only.</summary>
    public bool IsDue(DateTime now, bool fast) => _last is not { } last || (now - last).TotalSeconds >= (fast ? 2 : 10) - 0.4;

    /// <summary>The monitor was switched off: forget the counters and the history.</summary>
    public void Stop()
    {
        _last = null;
        _cpu = null;
        _net = null;
        _diskAt = null;
        if (_simulated) return;
        Current = new();
        Cpu.Clear();
        Memory.Clear();
        Down.Clear();
        Up.Clear();
        Version++;
    }

    /// <summary>One reading of everything; the battery comes from the island's PowerWatcher.</summary>
    public Reading Sample(DateTime now, PowerWatcher power)
    {
        if (_simulated) return Current;
        _last = now;
        var r = Current;

        if (GetSystemTimes(out ulong idle, out ulong kernel, out ulong user))
        {
            // Kernel time includes idle time.
            ulong busy = kernel - Math.Min(kernel, idle) + user;
            if (_cpu is { } prev && SysCpu.Percent(prev.Busy, prev.Idle, busy, idle) is { } pct) r = r with { Cpu = pct };
            _cpu = (busy, idle);
        }

        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref mem))
            r = r with { MemTotal = mem.ullTotalPhys, MemUsed = mem.ullTotalPhys - Math.Min(mem.ullTotalPhys, mem.ullAvailPhys) };

        var (down, up) = NetCounters();
        if (_net is { } net)
        {
            double seconds = (now - net.At).TotalSeconds;
            r = r with
            {
                Down = SysCounters.Rate(net.Down, down, seconds, 64) ?? r.Down,
                Up = SysCounters.Rate(net.Up, up, seconds, 64) ?? r.Up,
            };
        }
        _net = (down, up, now);
        r = r with { Online = SafeIsNetworkAvailable() };

        // The drive changes slowly: every 30 s.
        if (_diskAt is not { } diskAt || (now - diskAt).TotalSeconds >= 30)
        {
            _diskAt = now;
            try
            {
                var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                var drive = new DriveInfo(root);
                r = r with { DiskFree = drive.AvailableFreeSpace, DiskTotal = drive.TotalSize, DiskName = root.TrimEnd('\\') };
            }
            catch (Exception) { }
        }

        bool gotPower = GetSystemPowerStatus(out var ps);
        bool onAC = gotPower && ps.ACLineStatus == 1;
        int? minutes = power.HasBattery && gotPower && !onAC && ps.BatteryLifeTime > 0 ? ps.BatteryLifeTime / 60 : null;
        r = r with { Battery = power.HasBattery ? power.Percent : null, Charging = power.Charging, OnAC = onAC, BatteryMinutes = minutes };

        Current = r;
        if (r.Cpu is { } c) Cpu.Append(c);
        if (r.MemTotal > 0) Memory.Append(r.MemPercent);
        if (r.Down is { } d) Down.Append(d);
        if (r.Up is { } u) Up.Append(u);
        Version++;
        return r;
    }

    /// <summary>Fake readings and history for the self-test (real ones would make every run look different). Null: real again.</summary>
    public void Simulate(Reading? reading, IEnumerable<double>? cpu = null, IEnumerable<double>? memory = null,
                         IEnumerable<double>? down = null, IEnumerable<double>? up = null)
    {
        _simulated = false;
        Stop();
        if (reading is null) return;
        _simulated = true;
        Current = reading;
        foreach (var v in cpu ?? Enumerable.Empty<double>()) Cpu.Append(v);
        foreach (var v in memory ?? Enumerable.Empty<double>()) Memory.Append(v);
        foreach (var v in down ?? Enumerable.Empty<double>()) Down.Append(v);
        foreach (var v in up ?? Enumerable.Empty<double>()) Up.Append(v);
        Version++;
    }

    private static bool SafeIsNetworkAvailable()
    {
        try { return NetworkInterface.GetIsNetworkAvailable(); }
        catch (Exception) { return true; }
    }

    /// <summary>Bytes in and out per adapter that's up (not loopback, not tunnels: their traffic passes through one of these).</summary>
    private static (Dictionary<string, ulong> Down, Dictionary<string, ulong> Up) NetCounters()
    {
        var down = new Dictionary<string, ulong>();
        var up = new Dictionary<string, ulong>();
        try
        {
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up
                    || n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var stats = n.GetIPStatistics();
                down[n.Id] = (ulong)Math.Max(0, stats.BytesReceived);
                up[n.Id] = (ulong)Math.Max(0, stats.BytesSent);
            }
        }
        catch (Exception) { }
        return (down, up);
    }

    /// <summary>The process that used the most CPU over one second (for the CPU alert only, off the UI thread).
    /// Processes that can't be read (other users', protected ones) are skipped.</summary>
    public static string? TopProcess()
    {
        static Dictionary<int, (string Name, TimeSpan Cpu)> Snapshot()
        {
            var result = new Dictionary<int, (string, TimeSpan)>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id != 0) result[p.Id] = (p.ProcessName, p.TotalProcessorTime);
                }
                catch (Exception) { }
                finally { p.Dispose(); }
            }
            return result;
        }
        try
        {
            var before = Snapshot();
            Thread.Sleep(1000);
            var top = Snapshot()
                .Where(p => before.TryGetValue(p.Key, out var b) && b.Name == p.Value.Name && p.Value.Cpu > b.Cpu)
                .Select(p => (p.Value.Name, Delta: p.Value.Cpu - before[p.Key].Cpu))
                .OrderByDescending(p => p.Delta)
                .FirstOrDefault();
            return string.IsNullOrEmpty(top.Name) ? null : top.Name;
        }
        catch (Exception) { return null; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}
