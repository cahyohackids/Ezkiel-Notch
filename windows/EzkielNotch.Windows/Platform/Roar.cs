using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EzkielNotch.Core;
using Microsoft.Data.Sqlite;

namespace EzkielNotch.Platform;

/// <summary>
/// Roar on Windows: unread counts from the notification database Windows keeps for each user
/// (%LOCALAPPDATA%\Microsoft\Windows\Notifications\wpndatabase.db). An app's taskbar badge number when it has one,
/// otherwise how many of its notifications are still waiting in Notification Center. Only the numbers are read,
/// never the messages, and no permission is needed. The live file is copied first and the copy is read, so the
/// notification service is never blocked.
/// </summary>
public sealed partial class RoarWatcher
{
    public event Action<IReadOnlyList<Badge>>? Updated;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(4) };
    private readonly Dictionary<string, DateTime> _since = [];
    private DateTime _lastWrite;
    private bool _busy;

    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Notifications");

    public RoarWatcher(Dispatcher ui)
    {
        _ui = ui;
        _timer.Tick += (_, _) => Poll();
    }

    public void SetEnabled(bool on)
    {
        if (on) { _timer.Start(); Poll(force: true); }
        else { _timer.Stop(); Updated?.Invoke([]); }
    }

    private void Poll(bool force = false)
    {
        if (_busy) return;
        var db = Path.Combine(Folder, "wpndatabase.db");
        DateTime write;
        try
        {
            write = new[] { db, db + "-wal" }.Where(File.Exists).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max();
        }
        catch (Exception) { return; }
        if (!force && write == _lastWrite) return;
        _lastWrite = write;
        _busy = true;
        Task.Run(() => Read(db)).ContinueWith(t =>
        {
            _busy = false;
            if (t.Result is { } list) _ui.BeginInvoke(() => Updated?.Invoke(list));
        });
    }

    private List<Badge>? Read(string db)
    {
        var dir = Path.Combine(Path.GetTempPath(), "EzkielNotch", "roar");
        try
        {
            Directory.CreateDirectory(dir);
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var from = db + suffix;
                var to = Path.Combine(dir, "wpn.db" + suffix);
                if (File.Exists(from)) Copy(from, to); else if (File.Exists(to)) File.Delete(to);
            }
            var rows = new List<(string Id, string Type, string Payload, long Arrival)>();
            using (var c = new SqliteConnection($"Data Source={Path.Combine(dir, "wpn.db")};Mode=ReadOnly;Pooling=False"))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = """
                    SELECT h.PrimaryId, n.Type, n.Payload, n.ArrivalTime
                    FROM Notification n JOIN NotificationHandler h ON h.RecordId = n.HandlerId
                    WHERE n.Type IN ('badge', 'toast')
                    """;
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var payload = r.IsDBNull(2) ? "" : r.GetValue(2) is byte[] bytes ? Encoding.UTF8.GetString(bytes) : r.GetString(2);
                    rows.Add((r.GetString(0), r.GetString(1), payload, r.IsDBNull(3) ? 0 : r.GetInt64(3)));
                }
            }
            return Count(rows);
        }
        catch (Exception)
        {
            return null;   // the database moved or changed shape; try again next time
        }
    }

    /// <summary>Copies a file another process holds open (the notification service keeps it open).</summary>
    private static void Copy(string from, string to)
    {
        using var src = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var dst = new FileStream(to, FileMode.Create, FileAccess.Write, FileShare.None);
        src.CopyTo(dst);
    }

    private List<Badge> Count(List<(string Id, string Type, string Payload, long Arrival)> rows)
    {
        var now = DateTime.UtcNow;
        var list = new List<Badge>();
        foreach (var app in rows.GroupBy(r => r.Id))
        {
            if (Ignored(app.Key)) continue;
            int? badge = app.Where(r => r.Type == "badge").Select(r => BadgeValue(r.Payload)).LastOrDefault(v => v is not null);
            int toasts = app.Count(r => r.Type == "toast");
            int count = badge is > 0 ? badge.Value : toasts;
            if (count <= 0)
            {
                _since.Remove(app.Key);
                continue;
            }
            long latest = app.Max(r => r.Arrival);
            var since = latest > 0 ? DateTime.FromFileTimeUtc(latest) : _since.GetValueOrDefault(app.Key, now);
            _since[app.Key] = since;
            list.Add(new Badge(app.Key, AppIcons.Name(app.Key), count, count > 999 ? "999+" : count.ToString(), since));
        }
        return list.OrderByDescending(b => b.Since).ToList();
    }

    /// <summary>Windows' own notices (Security, updates, the Store) aren't messages.</summary>
    private static bool Ignored(string id) =>
        id.StartsWith("Windows.SystemToast", StringComparison.OrdinalIgnoreCase)
        || id.StartsWith("Microsoft.Windows.", StringComparison.OrdinalIgnoreCase)
        || id.Contains("WindowsStore", StringComparison.OrdinalIgnoreCase)
        || id.Contains("EzkielNotch", StringComparison.OrdinalIgnoreCase);

    /// <summary>&lt;badge value="5"/&gt; → 5; a glyph ("newMessage", "attention"…) → 1; "none" or 0 → nothing.</summary>
    internal static int? BadgeValue(string payload)
    {
        var m = BadgeRegex().Match(payload);
        if (!m.Success) return null;
        var v = m.Groups[1].Value;
        if (v is "none" or "") return 0;
        return int.TryParse(v, out var n) ? Math.Max(0, n) : 1;
    }

    [GeneratedRegex("""<badge[^>]*\bvalue\s*=\s*["']([^"']*)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex BadgeRegex();
}

/// <summary>Another app's name and icon, by app id or exe path (looked up once per app).</summary>
public static class AppIcons
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, BitmapSource?> Icons = new(StringComparer.OrdinalIgnoreCase);

    public static string Name(string id)
    {
        lock (Names)
        {
            if (Names.TryGetValue(id, out var cached)) return cached;
        }
        string name = Fallback(id);
        try
        {
            if (ShellItem(id) is { } item)
            {
                item.GetDisplayName(0 /* SIGDN_NORMALDISPLAY */, out var p);
                var s = Marshal.PtrToStringUni(p);
                Marshal.FreeCoTaskMem(p);
                Marshal.ReleaseComObject(item);
                if (!string.IsNullOrWhiteSpace(s) && !s.Contains('!')) name = s;
            }
        }
        catch (Exception) { }
        lock (Names) Names[id] = name;
        return name;
    }

    public static BitmapSource? Icon(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (Icons.TryGetValue(id, out var cached)) return cached;
        BitmapSource? icon = null;
        try
        {
            if (ShellItem(id) is { } item && item is IShellItemImageFactory factory)
            {
                factory.GetImage(new SIZE { cx = 64, cy = 64 }, 0x4 /* SIIGBF_ICONONLY */, out var hbitmap);
                icon = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                icon.Freeze();
                DeleteObject(hbitmap);
                Marshal.ReleaseComObject(item);
            }
        }
        catch (Exception) { }
        Icons[id] = icon;
        return icon;
    }

    /// <summary>"5319275A.WhatsAppDesktop_cv1g…!App" → "WhatsAppDesktop"; "C:\…\Telegram.exe" → "Telegram".</summary>
    private static string Fallback(string id)
    {
        if (id.Contains('\\') || id.Contains('/')) return Path.GetFileNameWithoutExtension(id);
        var s = id.Split('!')[0];
        int us = s.IndexOf('_');
        if (us > 0) s = s[..us];
        int dot = s.LastIndexOf('.');
        return dot >= 0 && dot < s.Length - 1 ? s[(dot + 1)..] : s;
    }

    private static IShellItem? ShellItem(string id)
    {
        var path = id.Contains('\\') || id.Contains('/') ? id : $"shell:AppsFolder\\{id}";
        var iid = typeof(IShellItem).GUID;
        return SHCreateItemFromParsingName(path, 0, ref iid, out var item) == 0 ? item : null;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindCtx, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint obj);

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint pbc, ref Guid bhid, ref Guid riid, out nint ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out nint ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, int flags, out nint phbm);
    }
}
