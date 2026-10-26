using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using EzkielNotch.Core;
using Microsoft.Win32;

namespace EzkielNotch.Platform;

/// <summary>
/// Scout's transport. The app's own exe doubles as the sender: Claude Code runs it as a hook (--claude-hook,
/// JSON on stdin), Codex as its notify program (--codex-notify &lt;json&gt;), scripts call --notify "title" "body",
/// and ezkielnotch:// links arrive as --url. The sender hands the event to the running app over a named pipe
/// (same user, same session) and exits without printing anything, so it never adds to an agent's context.
/// </summary>
public static class ScoutPipe
{
    private static string Name => $"EzkielNotch.Scout.{Process.GetCurrentProcess().SessionId}.{Environment.UserName}";

    /// <summary>Handles the sender flags. Returns false for a normal launch (and for --url when no app is running,
    /// so the launch goes on and handles the link itself).</summary>
    public static bool RunSender(string[] args, out string? pendingUrl)
    {
        pendingUrl = null;
        var app = AppIdentity.CallerApp();
        ScoutEvent? e;
        if (args.Contains("--claude-hook")) e = ScoutParser.ClaudeHook(ReadStdin(), app);
        else if (Array.IndexOf(args, "--codex-notify") is var c and >= 0) e = c + 1 < args.Length ? ScoutParser.CodexNotify(args[c + 1], app) : null;
        else if (Array.IndexOf(args, "--notify") is var n and >= 0)
        {
            var rest = args[(n + 1)..];
            var positional = new List<string>();
            string source = "script";
            var kind = ScoutKind.Info;
            for (int j = 0; j < rest.Length; j++)
            {
                if (rest[j] == "--source" && j + 1 < rest.Length) source = rest[++j];
                else if (rest[j] == "--kind" && j + 1 < rest.Length) kind = ScoutKindFrom(rest[++j]);
                else positional.Add(rest[j]);
            }
            if (positional.Count == 0 || positional[0].Length == 0) return true;
            e = ScoutEvent.New(source, kind, positional[0], positional.Count > 1 ? positional[1] : "", Environment.CurrentDirectory, app: app);
        }
        else if (Array.IndexOf(args, "--url") is var u and >= 0 && u + 1 < args.Length)
        {
            e = ScoutParser.Url(args[u + 1]);
            if (e is not null && !Send(e)) pendingUrl = args[u + 1];
            return pendingUrl is null;
        }
        else return false;
        if (e is not null) Send(e);
        return true;
    }

    private static ScoutKind ScoutKindFrom(string s) => s switch
    {
        "done" => ScoutKind.Done,
        "needsYou" => ScoutKind.NeedsYou,
        "working" => ScoutKind.Working,
        _ => ScoutKind.Info,
    };

    private static string ReadStdin()
    {
        try
        {
            var read = Task.Run(() => new StreamReader(Console.OpenStandardInput(), Encoding.UTF8).ReadToEnd());
            return read.Wait(TimeSpan.FromSeconds(3)) ? read.Result : "";
        }
        catch (Exception) { return ""; }
    }

    public static bool Send(ScoutEvent e)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.Out);
            pipe.Connect(400);
            var bytes = Encoding.UTF8.GetBytes(ScoutWire.Encode(e) + "\n");
            pipe.Write(bytes);
            pipe.Flush();
            return true;
        }
        catch (Exception) { return false; }   // the app isn't running: nothing to tell
    }

    /// <summary>The running app's side: one line per connection, handed to the UI thread. A runaway script
    /// can't turn the island into a strobe (12 events per 10 s).</summary>
    public static void Listen(Dispatcher ui, Action<ScoutEvent> received)
    {
        var recent = new Queue<DateTime>();
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var buffer = new char[8193];
                    int len = reader.ReadBlock(buffer, 0, buffer.Length);
                    if (ScoutWire.Decode(new string(buffer, 0, len).Trim()) is not { } e) continue;
                    var now = DateTime.UtcNow;
                    while (recent.Count > 0 && now - recent.Peek() > TimeSpan.FromSeconds(10)) recent.Dequeue();
                    if (recent.Count >= 12) continue;
                    recent.Enqueue(now);
                    ui.BeginInvoke(() => received(e));
                }
                catch (Exception)
                {
                    Thread.Sleep(500);
                }
            }
        }) { IsBackground = true, Name = "Scout pipe" };
        thread.Start();
    }

    /// <summary>ezkielnotch:// opens this app (per user, no admin), like the Mac's URL scheme.</summary>
    public static void RegisterUrlScheme()
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\ezkielnotch");
            k.SetValue("", "URL:Ezkiel Notch");
            k.SetValue("URL Protocol", "");
            using var cmd = k.CreateSubKey(@"shell\open\command");
            cmd.SetValue("", $"\"{Environment.ProcessPath}\" --url \"%1\"");
        }
        catch (Exception) { }
    }
}

/// <summary>Connecting Claude Code and Codex: edits their config files (a backup is kept first).</summary>
public static class AgentLinks
{
    public enum Agent { ClaudeCode, Codex }

    public static string Title(Agent a) => a == Agent.ClaudeCode ? "Claude Code" : "Codex";

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string FileOf(Agent a) => a == Agent.ClaudeCode ? Path.Combine(Home, ".claude", "settings.json") : Path.Combine(Home, ".codex", "config.toml");
    public static bool Installed(Agent a) => Directory.Exists(Path.GetDirectoryName(FileOf(a)));
    private static string Exe => Environment.ProcessPath ?? "EzkielNotch.exe";

    public static LinkState State(Agent a)
    {
        var text = File.Exists(FileOf(a)) ? File.ReadAllText(FileOf(a)) : "";
        return a == Agent.ClaudeCode ? ScoutSetup.ClaudeState(text, ScoutSetup.ClaudeCommand(Exe)) : ScoutSetup.CodexState(text, Exe);
    }

    /// <summary>Returns a problem to show, or null when it worked.</summary>
    public static string? Set(Agent a, bool connected)
    {
        var file = FileOf(a);
        try
        {
            var existing = File.Exists(file) ? File.ReadAllText(file) : "";
            string next = a == Agent.ClaudeCode
                ? connected ? ScoutSetup.ConnectClaude(existing, ScoutSetup.ClaudeCommand(Exe)) : ScoutSetup.DisconnectClaude(existing)
                : connected ? ScoutSetup.ConnectCodex(existing, Exe) : ScoutSetup.DisconnectCodex(existing);
            if (next == existing) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if (File.Exists(file)) File.Copy(file, file + ".ezkiel-backup", overwrite: true);
            File.WriteAllText(file, next, new UTF8Encoding(false));
            return null;
        }
        catch (ScoutSetup.SetupException ex) when (ex.Message == "unreadable")
        {
            return Lang.L("%@ isn't valid JSON; left it as it is", Path.GetFileName(file));
        }
        catch (ScoutSetup.SetupException)
        {
            return Lang.L("Codex already runs another notify program");
        }
        catch (Exception)
        {
            return Lang.L("Couldn't change %@", Path.GetFileName(file));
        }
    }
}

/// <summary>Which app a window or process belongs to: its app id when packaged (Store apps, Windows Terminal),
/// else its exe path. Roar, Scout and "is it in front?" all use the same form.</summary>
public static partial class AppIdentity
{
    public static string? Foreground()
    {
        var hwnd = Native.Win32.GetForegroundWindow();
        if (hwnd == 0) return null;
        Native.Win32.GetWindowThreadProcessId(hwnd, out var pid);
        return OfProcess((int)pid);
    }

    public static string? OfProcess(int pid)
    {
        var h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, (uint)pid);
        if (h == 0) return null;
        try
        {
            var buffer = new char[1024];
            uint len = (uint)buffer.Length;
            if (GetApplicationUserModelId(h, ref len, buffer) == 0) return new string(buffer, 0, (int)len - 1);
            len = (uint)buffer.Length;
            return QueryFullProcessImageName(h, 0, buffer, ref len) ? new string(buffer, 0, (int)len) : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    /// <summary>The app that ran an agent: the first ancestor process with a window (the terminal or editor).</summary>
    public static string? CallerApp()
    {
        try
        {
            var parents = ParentMap();
            int pid = Environment.ProcessId;
            for (int i = 0; i < 12 && parents.TryGetValue(pid, out var parent) && parent > 4; i++)
            {
                pid = parent;
                try
                {
                    using var p = Process.GetProcessById(pid);
                    if (p.MainWindowHandle != 0) return OfProcess(pid);
                }
                catch (Exception) { }
            }
        }
        catch (Exception) { }
        return null;
    }

    private static Dictionary<int, int> ParentMap()
    {
        var map = new Dictionary<int, int>();
        var snap = CreateToolhelp32Snapshot(0x2, 0);
        if (snap == -1) return map;
        try
        {
            var e = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (!Process32FirstW(snap, ref e)) return map;
            do map[(int)e.th32ProcessID] = (int)e.th32ParentProcessID;
            while (Process32NextW(snap, ref e));
        }
        finally
        {
            CloseHandle(snap);
        }
        return map;
    }

    /// <summary>Bring an app forward: packaged ones through shell:AppsFolder, others by starting the exe
    /// (which, for a running single-instance app, focuses it).</summary>
    public static void Open(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        try
        {
            if (id.Contains('!') || !id.Contains('\\') && !id.Contains('/'))
                Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{id}") { UseShellExecute = true });
            else
            {
                var running = Process.GetProcesses().FirstOrDefault(p =>
                {
                    try { return p.MainWindowHandle != 0 && string.Equals(OfProcess(p.Id), id, StringComparison.OrdinalIgnoreCase); }
                    catch (Exception) { return false; }
                });
                if (running is not null) Native.Win32.SetForegroundWindow(running.MainWindowHandle);
                else if (File.Exists(id)) Process.Start(new ProcessStartInfo(id) { UseShellExecute = true });
            }
        }
        catch (Exception) { }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public nint th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Process32FirstW(nint snap, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Process32NextW(nint snap, ref PROCESSENTRY32W entry);

    [LibraryImport("kernel32.dll")]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint h);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(nint h, uint flags, [Out] char[] name, ref uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(nint h, ref uint length, [Out] char[] id);
}
