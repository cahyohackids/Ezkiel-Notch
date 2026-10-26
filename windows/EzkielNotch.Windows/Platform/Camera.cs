using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace EzkielNotch.Platform;

/// <summary>Mirror: a quick look at yourself before a call. The camera runs only while Mirror is open;
/// frames go straight to the screen and nothing is recorded or saved.</summary>
public sealed class CameraMirror
{
    public WriteableBitmap? Frame { get; private set; }
    public enum Status { Off, Starting, On, NoCamera, Denied }
    public Status State { get; private set; } = Status.Off;
    public event Action? Changed;

    private readonly Dispatcher _ui;
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private int _busy;

    public CameraMirror(Dispatcher ui) => _ui = ui;

    public async Task Start()
    {
        if (State is Status.Starting or Status.On) return;
        Set(Status.Starting);
        try
        {
            var groups = await MediaFrameSourceGroup.FindAllAsync();
            var pick = groups
                .Select(g => (Group: g, Info: g.SourceInfos.FirstOrDefault(i => i.SourceKind == MediaFrameSourceKind.Color
                    && i.MediaStreamType is Windows.Media.Capture.MediaStreamType.VideoPreview or Windows.Media.Capture.MediaStreamType.VideoRecord)))
                .FirstOrDefault(x => x.Info is not null);
            if (pick.Group is null)
            {
                Set(Status.NoCamera);
                return;
            }
            _capture = new MediaCapture();
            await _capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = pick.Group,
                // Shared: a call that already has the camera keeps it.
                SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video,
            });
            var source = _capture.FrameSources[pick.Info!.Id];
            _reader = await _capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            _reader.FrameArrived += OnFrame;
            await _reader.StartAsync();
            Set(Status.On);
        }
        catch (UnauthorizedAccessException)
        {
            await Stop();
            Set(Status.Denied);
        }
        catch (Exception)
        {
            await Stop();
            Set(Status.NoCamera);
        }
    }

    public async Task Stop()
    {
        try
        {
            if (_reader is not null)
            {
                _reader.FrameArrived -= OnFrame;
                await _reader.StopAsync();
                _reader.Dispose();
            }
        }
        catch (Exception) { }
        _reader = null;
        _capture?.Dispose();
        _capture = null;
        Frame = null;
        if (State is Status.On or Status.Starting) Set(Status.Off);
    }

    private void OnFrame(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        // One frame in flight at a time: a slow screen drops frames instead of queueing them.
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            using var frame = sender.TryAcquireLatestFrame();
            var bmp = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (bmp is null)
            {
                _busy = 0;
                return;
            }
            using var bgra = SoftwareBitmap.Convert(bmp, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var buffer = new byte[w * h * 4];
            bgra.CopyToBuffer(buffer.AsBuffer());
            _ui.BeginInvoke(() =>
            {
                try
                {
                    if (Frame is null || Frame.PixelWidth != w || Frame.PixelHeight != h)
                    {
                        Frame = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
                        Changed?.Invoke();
                    }
                    Frame.WritePixels(new Int32Rect(0, 0, w, h), buffer, w * 4, 0);
                }
                finally { _busy = 0; }
            });
        }
        catch (Exception)
        {
            _busy = 0;
        }
    }

    private void Set(Status s)
    {
        State = s;
        _ui.BeginInvoke(() => Changed?.Invoke());
    }
}
