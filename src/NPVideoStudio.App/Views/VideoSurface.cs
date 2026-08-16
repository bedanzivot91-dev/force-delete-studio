using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using NPVideoStudio.App.Services;

namespace NPVideoStudio.App.Views;

/// <summary>
/// Draws the video. An ordinary Avalonia control that paints a bitmap - no NativeControlHost, no child
/// window, nothing native at all.
///
/// That is the whole point. LibVLCSharp's VideoView is "a detached window over your video control"
/// (VideoLAN's own README), which is why the old player was stuck at a fixed little size, could not be
/// laid out or overlaid normally, and took the whole process down when the native window failed to
/// attach. This control is laid out, clipped and scaled by Avalonia like any other control, so the
/// picture fills whatever space it is given and follows the window when it is resized or maximised.
/// </summary>
public sealed class VideoSurface : Control
{
    private readonly DispatcherTimer _timer;
    private WriteableBitmap? _bitmap;
    private VlcVideoFrameBuffer? _frames;

    public VideoSurface()
    {
        // 60 Hz is the paint rate, not the decode rate: libvlc pushes frames whenever the media says so
        // and CopyLatestFrame reports when nothing changed, so a still picture costs one cheap check.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => PullFrame();
    }

    /// <summary>Frames painted by this control. Distinct from the buffer's decoded-frame count, and
    /// useful when diagnosing "the file plays but I see nothing".</summary>
    public long PaintedFrames { get; private set; }

    public void Attach(VlcVideoFrameBuffer frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        Detach();

        _frames = frames;
        _bitmap = new WriteableBitmap(
            new PixelSize(frames.Width, frames.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        _timer.Start();
    }

    public void Detach()
    {
        _timer.Stop();
        _frames = null;

        var bitmap = _bitmap;
        _bitmap = null;
        bitmap?.Dispose();

        InvalidateVisual();
    }

    private void PullFrame()
    {
        if (_frames is null || _bitmap is null)
        {
            return;
        }

        using (var locked = _bitmap.Lock())
        {
            if (!_frames.CopyLatestFrame(locked.Address, locked.RowBytes, locked.Size.Height))
            {
                return;   // nothing new decoded; leave the last picture on screen
            }
        }

        PaintedFrames++;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        // Letterbox on black so nothing is ever stretched out of shape, whatever the window's shape is.
        context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));

        if (_bitmap is null)
        {
            return;
        }

        var target = FitUniform(Bounds.Size, _bitmap.PixelSize);
        if (target.Width <= 0 || target.Height <= 0)
        {
            return;
        }

        context.DrawImage(_bitmap, new Rect(0, 0, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height), target);
    }

    /// <summary>Largest aspect-correct rectangle of <paramref name="source"/> that fits inside
    /// <paramref name="available"/>, centred. Pulled out as a static so the scaling can be unit tested
    /// without a window.</summary>
    public static Rect FitUniform(Size available, PixelSize source)
    {
        if (available.Width <= 0 || available.Height <= 0 || source.Width <= 0 || source.Height <= 0)
        {
            return default;
        }

        var scale = Math.Min(available.Width / source.Width, available.Height / source.Height);
        var width = source.Width * scale;
        var height = source.Height * scale;

        return new Rect(
            (available.Width - width) / 2,
            (available.Height - height) / 2,
            width,
            height);
    }
}
