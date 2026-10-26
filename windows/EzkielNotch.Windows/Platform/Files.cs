using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace EzkielNotch.Platform;

/// <summary>File helpers for Stash and Pounce: thumbnails, opening, revealing, copying, sharing.</summary>
public static class Files
{
    private static readonly Dictionary<string, BitmapSource?> Thumbs = new(StringComparer.OrdinalIgnoreCase);

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>"2.4 MB", "Folder", "Missing" (the Mac's StashItem.detail).</summary>
    public static string Detail(string path)
    {
        if (Directory.Exists(path)) return "Folder";
        if (!File.Exists(path)) return "Missing";
        double size = new FileInfo(path).Length;
        string[] units = ["bytes", "KB", "MB", "GB"];
        int u = 0;
        while (size >= 1000 && u < units.Length - 1) { size /= 1000; u++; }
        return u == 0 ? $"{size:0} {units[u]}" : $"{size:0.#} {units[u]}";
    }

    /// <summary>The Explorer thumbnail (pictures, PDFs, videos) or the file's icon.</summary>
    public static BitmapSource? Thumbnail(string path, int size = 96)
    {
        var key = $"{path}|{size}|{(File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0)}";
        if (Thumbs.TryGetValue(key, out var cached)) return cached;
        BitmapSource? result = null;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(path, 0, ref iid, out var factory) == 0)
            {
                factory.GetImage(new SIZE { cx = size, cy = size }, 0, out var hbitmap);
                result = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                result.Freeze();
                DeleteObject(hbitmap);
                Marshal.ReleaseComObject(factory);
            }
        }
        catch (Exception) { }
        if (Thumbs.Count > 300) Thumbs.Clear();
        Thumbs[key] = result;
        return result;
    }

    public static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception) { }
    }

    public static void Reveal(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); } catch (Exception) { }
    }

    /// <summary>Copies the file (paste it in Explorer, chats, mail); a picture also goes as an image.</summary>
    public static void Copy(IEnumerable<string> paths)
    {
        var list = paths.Where(Exists).ToList();
        if (list.Count == 0) return;
        var data = new DataObject();
        var drop = new StringCollection();
        drop.AddRange(list.ToArray());
        data.SetFileDropList(drop);
        if (list.Count == 1 && IsImage(list[0]))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(list[0]);
                bmp.EndInit();
                data.SetImage(bmp);
            }
            catch (Exception) { }
        }
        try { System.Windows.Clipboard.SetDataObject(data, true); } catch (Exception) { }
    }

    public static bool IsImage(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".heic";

    // MARK: Share (Windows' share sheet: Nearby sharing, Mail, Teams, …)

    private static List<string> _sharing = [];
    private static bool _hooked;

    /// <summary>Opens Windows' share sheet for these files, like the Mac's Share… and AirDrop.</summary>
    public static void Share(nint hwnd, IEnumerable<string> paths)
    {
        _sharing = paths.Where(File.Exists).ToList();
        if (_sharing.Count == 0) return;
        try
        {
            var dtm = DataTransferManagerInterop.GetForWindow(hwnd);
            if (!_hooked)
            {
                dtm.DataRequested += OnDataRequested;
                _hooked = true;
            }
            DataTransferManagerInterop.ShowShareUIForWindow(hwnd);
        }
        catch (Exception) { }
    }

    private static async void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        var deferral = args.Request.GetDeferral();
        try
        {
            var files = new List<IStorageItem>();
            foreach (var p in _sharing) files.Add(await StorageFile.GetFileFromPathAsync(p));
            args.Request.Data.Properties.Title = _sharing.Count == 1 ? Path.GetFileName(_sharing[0]) : $"{_sharing.Count} files";
            args.Request.Data.SetStorageItems(files);
        }
        catch (Exception)
        {
            args.Request.FailWithDisplayText("Couldn't share these files");
        }
        finally
        {
            deferral.Complete();
        }
    }

    // MARK: Known folders

    public static string? KnownFolder(Guid id)
    {
        try
        {
            if (SHGetKnownFolderPath(ref id, 0, 0, out var p) != 0) return null;
            var s = Marshal.PtrToStringUni(p);
            Marshal.FreeCoTaskMem(p);
            return s;
        }
        catch (Exception) { return null; }
    }

    public static string Downloads => KnownFolder(new Guid("374DE290-123F-4565-9164-39C4925E467B"))
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    /// <summary>Where Win+PrtScn and the Snipping Tool save screenshots.</summary>
    public static string Screenshots => KnownFolder(new Guid("B7BEDE81-DF94-4682-A7D8-57A52620B86F"))
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, nint token, out nint path);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindCtx, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint obj);

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, int flags, out nint phbm);
    }
}

/// <summary>Pounce: a new screenshot or a finished download, as soon as it lands. Partial downloads
/// (.crdownload, .part, …) are ignored until they get their final name and stop growing.</summary>
public sealed class PounceWatcher
{
    public event Action<string, bool>? Pounced;   // path, isScreenshot
    private readonly Dispatcher _ui;
    private FileSystemWatcher? _shots, _downloads;
    private readonly Dictionary<string, DateTime> _seen = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] Partial = [".crdownload", ".part", ".partial", ".download", ".opdownload", ".tmp", ".!ut", ".aria2"];

    public PounceWatcher(Dispatcher ui) => _ui = ui;

    public void Configure(bool screenshots, bool downloads)
    {
        _shots = Set(_shots, screenshots, Files.Screenshots, true);
        _downloads = Set(_downloads, downloads, Files.Downloads, false);
    }

    private FileSystemWatcher? Set(FileSystemWatcher? current, bool on, string folder, bool screenshot)
    {
        if (!on || !Directory.Exists(folder))
        {
            current?.Dispose();
            return null;
        }
        if (current is not null) return current;
        var w = new FileSystemWatcher(folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size };
        w.Created += (_, e) => Candidate(e.FullPath, screenshot);
        w.Renamed += (_, e) => Candidate(e.FullPath, screenshot);
        w.EnableRaisingEvents = true;
        return w;
    }

    private void Candidate(string path, bool screenshot)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var name = Path.GetFileName(path);
        if (Partial.Contains(ext) || name.StartsWith('.') || name.StartsWith('~') || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) return;
        if (screenshot && !Files.IsImage(path)) return;
        _ = Task.Run(async () =>
        {
            // Wait until it stops growing and can be read.
            long last = -1;
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(500);
                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists) return;
                    if ((info.Attributes & System.IO.FileAttributes.Hidden) != 0) return;
                    if (info.Length == last && info.Length > 0)
                    {
                        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) { }
                        break;
                    }
                    last = info.Length;
                }
                catch (IOException) { }
            }
            _ = _ui.BeginInvoke(() =>
            {
                var now = DateTime.UtcNow;
                if (_seen.TryGetValue(path, out var at) && now - at < TimeSpan.FromSeconds(15)) return;
                _seen[path] = now;
                Pounced?.Invoke(path, screenshot);
            });
        });
    }
}
