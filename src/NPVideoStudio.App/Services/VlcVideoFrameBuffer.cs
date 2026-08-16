using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace NPVideoStudio.App.Services;

/// <summary>
/// Receives libvlc's decoded video frames straight into memory we own, so the picture can be drawn by
/// Avalonia itself instead of by a native child window.
///
/// Why this exists at all: LibVLCSharp's own Avalonia VideoView is, in VideoLAN's own words, "a detached
/// window over your video control", which is what the airspace limitation in their README is about - you
/// "cannot easily draw things over the video", it does not behave inside a UserControl
/// (AvaloniaUI/Avalonia#6237, VideoLAN/LibVLCSharp#525), and when that separate native window fails to
/// attach there is no managed exception to catch, the process simply dies. Every one of the user's
/// complaints - small fixed picture, cannot resize, no picture at all, app vanishes on play - traces back
/// to that design. Decoding into a buffer removes the native window from the picture entirely.
///
/// VideoLAN documents the trade-off honestly and so should this comment: memory output is less efficient
/// than a native window. For a preview/editing player at capped resolution that cost is worth paying to
/// get a picture that reliably appears, scales with the window, and cannot take the process down.
/// </summary>
public sealed class VlcVideoFrameBuffer : IDisposable
{
    /// <summary>Decode resolution is capped because every frame is memcpy'd once per display refresh -
    /// a 4K buffer is 33 MB per copy, which would burn far more CPU than a preview justifies. libvlc
    /// rescales to whatever size is requested, and the surface scales that up to the window.</summary>
    public const int MaxDecodeWidth = 1920;
    public const int MaxDecodeHeight = 1080;

    private readonly object _sync = new();

    // Two buffers so libvlc never decodes into the same memory the UI is reading. The alternative -
    // holding a lock from the lock callback until the unlock callback - relies on both firing on the
    // same thread and would stall the decoder behind the UI.
    private IntPtr _decodeBuffer;
    private IntPtr _readyBuffer;

    private long _framesDisplayed;
    private bool _hasNewFrame;
    private bool _isDisposed;

    // These delegates MUST be held in fields. Passing lambdas straight to SetVideoCallbacks leaves
    // nothing referencing them, so the GC is free to collect the thunks while libvlc still holds the
    // function pointers - and libvlc then calls into freed memory from a native thread, which is an
    // instant process kill with no managed exception. Keeping them alive here is not tidiness.
    private readonly MediaPlayer.LibVLCVideoLockCb _lockCb;
    private readonly MediaPlayer.LibVLCVideoUnlockCb _unlockCb;
    private readonly MediaPlayer.LibVLCVideoDisplayCb _displayCb;

    public VlcVideoFrameBuffer(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Frame size must be positive.");
        }

        Width = width;
        Height = height;

        _decodeBuffer = Marshal.AllocHGlobal(ByteCount);
        _readyBuffer = Marshal.AllocHGlobal(ByteCount);

        // Start both buffers opaque-black rather than whatever the allocator handed back, so the first
        // paint before any frame arrives is black instead of garbage pixels.
        FillOpaqueBlack(_decodeBuffer);
        FillOpaqueBlack(_readyBuffer);

        _lockCb = (IntPtr _, IntPtr planes) =>
        {
            Marshal.WriteIntPtr(planes, _decodeBuffer);
            return IntPtr.Zero;
        };

        _unlockCb = (IntPtr _, IntPtr _, IntPtr _) => { };

        _displayCb = (IntPtr _, IntPtr _) =>
        {
            lock (_sync)
            {
                if (_isDisposed)
                {
                    return;
                }

                (_decodeBuffer, _readyBuffer) = (_readyBuffer, _decodeBuffer);
                _hasNewFrame = true;
                _framesDisplayed++;
            }
        };
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Bytes per row. RV32 is 4 bytes per pixel.</summary>
    public int Stride => Width * 4;

    public int ByteCount => Stride * Height;

    /// <summary>How many frames libvlc has handed over. The single most useful signal for "is the
    /// picture actually playing", and the thing tests assert on.</summary>
    public long FramesDisplayed
    {
        get
        {
            lock (_sync)
            {
                return _framesDisplayed;
            }
        }
    }

    public bool HasNewFrame
    {
        get
        {
            lock (_sync)
            {
                return _hasNewFrame;
            }
        }
    }

    /// <summary>
    /// Picks the decode size for a video of the given native size: never upscaled, never above the cap,
    /// and always aspect-correct so nothing is stretched. Both dimensions stay even because some
    /// rescalers dislike odd sizes.
    /// </summary>
    public static (int Width, int Height) ChooseDecodeSize(int nativeWidth, int nativeHeight)
    {
        if (nativeWidth <= 0 || nativeHeight <= 0)
        {
            return (1280, 720);
        }

        var scale = Math.Min(
            Math.Min(1.0, (double)MaxDecodeWidth / nativeWidth),
            (double)MaxDecodeHeight / nativeHeight);

        var width = Math.Max(2, (int)Math.Round(nativeWidth * scale));
        var height = Math.Max(2, (int)Math.Round(nativeHeight * scale));

        return (width - (width % 2), height - (height % 2));
    }

    /// <summary>
    /// Points a media player at this buffer. Must be called before playback starts - libvlc reads the
    /// video output configuration when it builds the output chain.
    /// </summary>
    public void AttachTo(MediaPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);

        // RV32 is BGRA byte order in memory on little-endian, which is exactly Avalonia's Bgra8888 -
        // so the surface can hand the bytes to the GPU with no per-pixel conversion.
        player.SetVideoFormat("RV32", (uint)Width, (uint)Height, (uint)Stride);
        player.SetVideoCallbacks(_lockCb, _unlockCb, _displayCb);
    }

    /// <summary>
    /// Copies the most recently displayed frame out. Returns false when nothing new has arrived, so the
    /// caller can skip repainting an unchanged picture.
    /// </summary>
    public unsafe bool CopyLatestFrame(IntPtr destination, int destinationStride, int destinationHeight)
    {
        if (destination == IntPtr.Zero)
        {
            return false;
        }

        lock (_sync)
        {
            if (_isDisposed || !_hasNewFrame)
            {
                return false;
            }

            var rows = Math.Min(Height, destinationHeight);
            var rowBytes = Math.Min(Stride, destinationStride);

            for (var y = 0; y < rows; y++)
            {
                Buffer.MemoryCopy(
                    (void*)(_readyBuffer + y * Stride),
                    (void*)(destination + y * destinationStride),
                    destinationStride,
                    rowBytes);
            }

            _hasNewFrame = false;
            return true;
        }
    }

    /// <summary>Reads one pixel of the last displayed frame as (R, G, B). Exists so tests can assert the
    /// picture is the RIGHT picture, not merely that bytes moved.</summary>
    public (byte R, byte G, byte B) ReadPixel(int x, int y)
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return (0, 0, 0);
            }

            var clampedX = Math.Clamp(x, 0, Width - 1);
            var clampedY = Math.Clamp(y, 0, Height - 1);
            var offset = clampedY * Stride + clampedX * 4;

            return (
                Marshal.ReadByte(_readyBuffer, offset + 2),
                Marshal.ReadByte(_readyBuffer, offset + 1),
                Marshal.ReadByte(_readyBuffer, offset + 0));
        }
    }

    private void FillOpaqueBlack(IntPtr buffer)
    {
        for (var i = 0; i < ByteCount; i += 4)
        {
            Marshal.WriteByte(buffer, i + 0, 0);
            Marshal.WriteByte(buffer, i + 1, 0);
            Marshal.WriteByte(buffer, i + 2, 0);
            Marshal.WriteByte(buffer, i + 3, 255);
        }
    }

    /// <summary>
    /// Frees the buffers. The caller must have stopped the player first: libvlc calls the lock callback
    /// from its own decoder thread, and freeing memory it is about to write to is a native access
    /// violation that kills the process silently.
    /// </summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _hasNewFrame = false;

            if (_decodeBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_decodeBuffer);
                _decodeBuffer = IntPtr.Zero;
            }

            if (_readyBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_readyBuffer);
                _readyBuffer = IntPtr.Zero;
            }
        }
    }
}
