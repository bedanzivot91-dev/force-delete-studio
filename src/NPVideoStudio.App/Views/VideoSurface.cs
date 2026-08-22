using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using NPVideoStudio.App.Services;

namespace NPVideoStudio.App.Views;

/// <summary>
/// The one place video is shown. An ordinary Avalonia control that paints a bitmap - no
/// NativeControlHost, no child window, nothing native at all.
///
/// It draws whichever of two sources is active, so the app needs only ONE player on screen instead of a
/// separate panel per playback technology: a live libvlc stream (via <see cref="Attach"/>) when playing
/// with sound, or a single decoded still (via <see cref="StaticImage"/>) when scrubbing the timeline.
///
/// Being a normal control is what makes the picture interactive at all. LibVLCSharp's VideoView is "a
/// detached window over your video control" (VideoLAN's own README): a separate OS window that Avalonia
/// cannot lay out, scale, clip or hit-test, which is why the old player was stuck at a fixed small size
/// with nothing to click. Here the picture is painted by Avalonia, so it can be zoomed, dragged and
/// resized like any other content.
/// </summary>
public sealed class VideoSurface : Control
{
    /// <summary>Zoom range. 1 = fit the whole picture in the panel; below that would only add empty
    /// space, above 8x a preview stops being useful and starts being pixels.</summary>
    public const double MinZoom = 1.0;
    public const double MaxZoom = 8.0;

    private readonly DispatcherTimer _timer;
    private WriteableBitmap? _liveBitmap;
    private VlcVideoFrameBuffer? _frames;

    private double _zoom = 1.0;
    private Point _pan;
    private Point? _dragStart;
    private Point _dragStartPan;

    public VideoSurface()
    {
        // 60 Hz is the paint rate, not the decode rate: libvlc pushes frames whenever the media says so
        // and CopyLatestFrame reports when nothing changed, so a still picture costs one cheap check.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => PullFrame();

        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    /// <summary>Frames painted by this control. Distinct from the buffer's decoded-frame count, and
    /// useful when diagnosing "the file plays but I see nothing".</summary>
    public long PaintedFrames { get; private set; }

    /// <summary>A single decoded still to show when nothing is streaming - the timeline scrub preview.
    /// Live frames win whenever they are attached.</summary>
    public static readonly StyledProperty<Bitmap?> StaticImageProperty =
        AvaloniaProperty.Register<VideoSurface, Bitmap?>(nameof(StaticImage));

    public Bitmap? StaticImage
    {
        get => GetValue(StaticImageProperty);
        set => SetValue(StaticImageProperty, value);
    }

    /// <summary>1 = fit to the panel. Larger crops in; the visible part is chosen by <see cref="Pan"/>.</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            var clamped = Math.Clamp(value, MinZoom, MaxZoom);
            if (Math.Abs(clamped - _zoom) < 0.0001)
            {
                return;
            }

            _zoom = clamped;
            _pan = ClampPan(_pan, Bounds.Size, SourcePixelSize, _zoom);
            UpdateCursor();
            InvalidateVisual();
        }
    }

    /// <summary>How far the zoomed picture has been dragged, in panel pixels.</summary>
    public Point Pan => _pan;

    public bool CanPan => _zoom > 1.0001;

    static VideoSurface()
    {
        AffectsRender<VideoSurface>(StaticImageProperty);
    }

    public void Attach(VlcVideoFrameBuffer frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        Detach();

        _frames = frames;
        _liveBitmap = new WriteableBitmap(
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

        var bitmap = _liveBitmap;
        _liveBitmap = null;
        bitmap?.Dispose();

        InvalidateVisual();
    }

    /// <summary>Back to "whole picture visible", which is also what the Fit button does.</summary>
    public void ResetView()
    {
        _zoom = 1.0;
        _pan = default;
        UpdateCursor();
        InvalidateVisual();
    }

    /// <summary>Zooms about a point on the panel, so the pixel under the cursor stays under the cursor -
    /// the behaviour every map and image viewer has, and the reason wheel-zoom feels wrong without it.</summary>
    public void ZoomAt(Point origin, double factor)
    {
        var source = SourcePixelSize;
        if (source.Width <= 0 || source.Height <= 0)
        {
            return;
        }

        var newZoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - _zoom) < 0.0001)
        {
            return;
        }

        _pan = ZoomPanAbout(origin, Bounds.Size, source, _zoom, newZoom, _pan);
        _zoom = newZoom;
        UpdateCursor();
        InvalidateVisual();
    }

    /// <summary>
    /// Where the pan must move so that the content under <paramref name="origin"/> does not shift while
    /// the zoom changes. Static and pure so the behaviour is unit tested rather than eyeballed.
    /// </summary>
    public static Point ZoomPanAbout(
        Point origin, Size available, PixelSize source, double oldZoom, double newZoom, Point pan)
    {
        var oldRect = ComputeDestination(available, source, oldZoom, pan);
        if (oldRect.Width <= 0 || oldRect.Height <= 0)
        {
            return pan;
        }

        // Fraction of the picture sitting under the origin right now.
        var fractionX = (origin.X - oldRect.X) / oldRect.Width;
        var fractionY = (origin.Y - oldRect.Y) / oldRect.Height;

        var fitted = FitUniform(available, source);
        var newWidth = fitted.Width * newZoom;
        var newHeight = fitted.Height * newZoom;

        // Solve for the pan that keeps that same fraction under the same panel point.
        var centredX = (available.Width - newWidth) / 2;
        var centredY = (available.Height - newHeight) / 2;

        var wanted = new Point(
            origin.X - fractionX * newWidth - centredX,
            origin.Y - fractionY * newHeight - centredY);

        return ClampPan(wanted, available, source, newZoom);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (SourcePixelSize.Width <= 0)
        {
            return;
        }

        ZoomAt(e.GetPosition(this), e.Delta.Y > 0 ? 1.2 : 1 / 1.2);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!CanPan)
        {
            return;
        }

        _dragStart = e.GetPosition(this);
        _dragStartPan = _pan;
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_dragStart is not { } start)
        {
            return;
        }

        var delta = e.GetPosition(this) - start;
        _pan = ClampPan(
            new Point(_dragStartPan.X + delta.X, _dragStartPan.Y + delta.Y),
            Bounds.Size,
            SourcePixelSize,
            _zoom);

        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        _dragStart = null;
        e.Pointer.Capture(null);
        UpdateCursor();
    }

    private void UpdateCursor() =>
        Cursor = new Cursor(CanPan ? StandardCursorType.Hand : StandardCursorType.Arrow);

    /// <summary>The pixel size of whatever is currently being shown, live stream or still.</summary>
    public PixelSize SourcePixelSize =>
        _liveBitmap?.PixelSize ?? StaticImage?.PixelSize ?? default;

    private void PullFrame()
    {
        if (_frames is null || _liveBitmap is null)
        {
            return;
        }

        using (var locked = _liveBitmap.Lock())
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
        // Letterbox on black so nothing is ever stretched out of shape, whatever the panel's shape is.
        context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));

        IImage? image = _liveBitmap;
        image ??= StaticImage;

        if (image is null)
        {
            return;
        }

        var source = SourcePixelSize;
        var target = ComputeDestination(Bounds.Size, source, _zoom, _pan);
        if (target.Width <= 0 || target.Height <= 0)
        {
            return;
        }

        context.DrawImage(image, new Rect(0, 0, source.Width, source.Height), target);
    }

    /// <summary>
    /// Where the picture lands: fitted to the panel, scaled by the zoom, offset by the pan, centred when
    /// it is smaller than the panel. Pure and static so every zoom/pan rule below is unit tested without
    /// needing a window.
    /// </summary>
    public static Rect ComputeDestination(Size available, PixelSize source, double zoom, Point pan)
    {
        var fitted = FitUniform(available, source);
        if (fitted.Width <= 0 || fitted.Height <= 0)
        {
            return default;
        }

        var width = fitted.Width * zoom;
        var height = fitted.Height * zoom;

        var clamped = ClampPan(pan, available, source, zoom);

        return new Rect(
            (available.Width - width) / 2 + clamped.X,
            (available.Height - height) / 2 + clamped.Y,
            width,
            height);
    }

    /// <summary>
    /// Keeps the picture from being dragged away into empty space: at fit there is nothing to pan, and
    /// when zoomed in the edge of the picture can never come inside the edge of the panel.
    /// </summary>
    public static Point ClampPan(Point pan, Size available, PixelSize source, double zoom)
    {
        var fitted = FitUniform(available, source);
        if (fitted.Width <= 0 || fitted.Height <= 0)
        {
            return default;
        }

        var overflowX = Math.Max(0, fitted.Width * zoom - available.Width) / 2;
        var overflowY = Math.Max(0, fitted.Height * zoom - available.Height) / 2;

        return new Point(
            Math.Clamp(pan.X, -overflowX, overflowX),
            Math.Clamp(pan.Y, -overflowY, overflowY));
    }

    /// <summary>Largest aspect-correct rectangle of <paramref name="source"/> that fits inside
    /// <paramref name="available"/>, centred.</summary>
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
