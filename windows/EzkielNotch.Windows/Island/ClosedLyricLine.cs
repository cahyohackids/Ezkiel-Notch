using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using EzkielNotch.Core;
using EzkielNotch.Platform;

namespace EzkielNotch.Island;

/// <summary>
/// Chorus under the closed island: the line being sung (the Mac's ClosedLyricLine). Each new line rises in and the
/// old one drifts up and fades. There's no frame loop: a one-shot timer wakes up when the next line starts, and the
/// window's own once-a-second update and media changes (a seek, a pause) set it again. Reduce Motion: lines swap.
/// </summary>
internal sealed class ClosedLyricLine : Grid
{
    private readonly DispatcherTimer _next = new(DispatcherPriority.Background);
    private NowPlaying? _np;
    private IReadOnlyList<LyricLine>? _lines;
    private TextBlock? _current;

    /// <summary>The line shown now ("" when stopped), for the self-test.</summary>
    public string Text => _current?.Text ?? "";

    public ClosedLyricLine()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
        _next.Tick += (_, _) =>
        {
            _next.Stop();
            Update();
        };
    }

    /// <summary>Follow this song's position (a new report after a seek or a pause comes through here too).</summary>
    public void Follow(NowPlaying np, IReadOnlyList<LyricLine> lines)
    {
        _np = np;
        _lines = lines;
        Update();
    }

    public void Stop()
    {
        _next.Stop();
        _np = null;
        _lines = null;
        _current = null;
        Children.Clear();
    }

    private void Update()
    {
        if (_np is not { } np || _lines is not { } lines) return;
        double position = np.Position(DateTime.UtcNow);
        var text = ClosedChorus.Line(position, lines);
        if (text != _current?.Text) Show(text, animate: _current is not null && !SpringEase.ReduceMotion);
        _next.Stop();
        if (np.IsPlaying && ClosedChorus.UntilNextLine(position, lines) is double wait)
        {
            // A long instrumental gap wakes up at the latest after 30 s; the window's updates re-arm it anyway.
            _next.Interval = TimeSpan.FromSeconds(Math.Clamp(wait + 0.02, 0.05, 30));
            _next.Start();
        }
    }

    private void Show(string text, bool animate)
    {
        var line = new TextBlock
        {
            Text = text,
            FontFamily = Ui.Text,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = Ui.Ink,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 16, 0),
            RenderTransform = new TranslateTransform(),
        };
        // The Mac lets a long line shrink to 75 % before it's cut short.
        double room = ActualWidth - 32;
        if (room > 0)
        {
            line.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (line.DesiredSize.Width - 32 > room) line.FontSize = Math.Max(9, 12 * room / (line.DesiredSize.Width - 32));
        }
        var old = _current;
        _current = line;
        Children.Add(line);
        if (old is null) return;
        if (!animate)
        {
            Children.Remove(old);
            return;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        line.Opacity = 0;
        line.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
        line.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
        fade.Completed += (_, _) => Children.Remove(old);
        old.BeginAnimation(OpacityProperty, fade);
        old.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
    }
}
