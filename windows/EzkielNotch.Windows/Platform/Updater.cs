using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using EzkielNotch.Core;

namespace EzkielNotch.Platform;

/// <summary>
/// One-click updates, like the Mac's: once a day (and on "Check now") GitHub's releases are asked for a newer
/// Windows build for this machine. "Update now" downloads the zip, checks its SHA-256 against the release's
/// SHA256SUMS.txt, checks the app inside (name and version), then swaps it in: a running exe can be renamed but
/// not overwritten, so this copy steps aside as EzkielNotch.old.exe, the new one takes its name and starts, and
/// the old file is deleted on the next launch. If any step fails, nothing is changed.
/// </summary>
public sealed class Updater
{
    public enum Phase { Idle, Checking, UpToDate, Available, Downloading, Verifying, Installing, Failed }
    public Phase State { get; private set; } = Phase.Idle;
    public WindowsRelease? Release { get; private set; }
    public string? Problem { get; private set; }
    public DateTime? LastCheck { get; private set; }
    public event Action? Changed;

    public static string Current => typeof(Updater).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    private static string Rid => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
    private static readonly HttpClient Http = MakeHttp();

    private static HttpClient MakeHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd($"EzkielNotch-Windows/{Current}");
        return h;
    }

    public async Task Check(bool force = false)
    {
        if (State is Phase.Checking or Phase.Downloading or Phase.Verifying or Phase.Installing) return;
        if (!force && LastCheck is { } last && DateTime.UtcNow - last < TimeSpan.FromHours(20)) return;
        Set(Phase.Checking);
        try
        {
            var json = await Http.GetStringAsync("https://api.github.com/repos/cahyohackids/Ezkiel-Notch/releases?per_page=20");
            LastCheck = DateTime.UtcNow;
            Release = UpdateVerify.Pick(json, Rid);
            Set(Release is not null && AppVersion.IsNewer(Release.Version, Current) ? Phase.Available : Phase.UpToDate);
        }
        catch (Exception)
        {
            Problem = Lang.L("Couldn't reach GitHub");
            Set(Phase.Failed);
        }
    }

    public async Task Install()
    {
        if (Release is not { } r || State is Phase.Downloading or Phase.Verifying or Phase.Installing) return;
        var exe = Environment.ProcessPath;
        if (exe is null) return;
        var work = Path.Combine(Path.GetTempPath(), "EzkielNotch-update");
        try
        {
            Set(Phase.Downloading);
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(work);
            var zip = Path.Combine(work, r.ZipName);
            await using (var s = await Http.GetStreamAsync(r.ZipUrl))
            await using (var f = File.Create(zip))
                await s.CopyToAsync(f);
            var sums = await Http.GetStringAsync(r.SumsUrl);

            Set(Phase.Verifying);
            var expected = UpdateVerify.Checksum(r.ZipName, sums);
            if (expected is null || expected != await Task.Run(() => UpdateVerify.Sha256(zip))) throw new UpdateException(Lang.L("The download didn't match its checksum"));
            var unpacked = Path.Combine(work, "app");
            ZipFile.ExtractToDirectory(zip, unpacked);
            var fresh = Directory.GetFiles(unpacked, "EzkielNotch.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new UpdateException(Lang.L("The download has no app inside"));
            var info = FileVersionInfo.GetVersionInfo(fresh);
            if (info.ProductName != "Ezkiel Notch" || !AppVersion.IsNewer(info.ProductVersion ?? "0", Current))
                throw new UpdateException(Lang.L("The download isn't a newer Ezkiel Notch"));

            Set(Phase.Installing);
            var old = Path.Combine(Path.GetDirectoryName(exe)!, "EzkielNotch.old.exe");
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try
            {
                File.Copy(fresh, exe);
            }
            catch (Exception)
            {
                File.Move(old, exe);   // put this copy back
                throw;
            }
            Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = false });
            System.Windows.Application.Current.Shutdown();
        }
        catch (UpdateException e)
        {
            Problem = e.Message;
            Set(Phase.Failed);
        }
        catch (Exception)
        {
            Problem = Lang.L("Couldn't install the update");
            Set(Phase.Failed);
        }
    }

    /// <summary>At launch: the copy that stepped aside during an update goes away.</summary>
    public static void CleanUp()
    {
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath);
            var old = dir is null ? null : Path.Combine(dir, "EzkielNotch.old.exe");
            if (old is not null && File.Exists(old)) File.Delete(old);
        }
        catch (Exception) { }
    }

    private void Set(Phase p)
    {
        State = p;
        Changed?.Invoke();
    }

    private sealed class UpdateException(string message) : Exception(message);
}

/// <summary>The tray lion (the Mac's menu-bar lion): click to open or close, right-click for the menu.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly System.Windows.Forms.ToolStripMenuItem _open, _settings, _quit;

    public TrayIcon(Action toggle, Action settings, Action quit)
    {
        var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))?.Stream;
        _open = new System.Windows.Forms.ToolStripMenuItem("", null, (_, _) => toggle());
        _settings = new System.Windows.Forms.ToolStripMenuItem("", null, (_, _) => settings());
        _quit = new System.Windows.Forms.ToolStripMenuItem("", null, (_, _) => quit());
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.AddRange([_open, _settings, new System.Windows.Forms.ToolStripSeparator(), _quit]);
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = stream is null ? System.Drawing.SystemIcons.Application : new System.Drawing.Icon(stream, 16, 16),
            Text = "Ezkiel Notch",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) toggle(); };
        Relabel("Ctrl+Alt+N");
    }

    /// <summary>Menu text follows the language and the chosen shortcut.</summary>
    public void Relabel(string hotkey)
    {
        _open.Text = Lang.L("Open") + $"  ({hotkey})";
        _settings.Text = Lang.L("Settings…");
        _quit.Text = Lang.L("Quit Ezkiel Notch");
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
