using System.Runtime.InteropServices;
using Avalonia;
using NPVideoStudio.App.Services;
using NPVideoStudio.App.Views;
using Xunit;
using Xunit.Abstractions;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// The replacement for LibVLCSharp's VideoView, measured the same way the session is: play a real file
/// and check the pixels that actually arrive.
///
/// This matters more than a normal unit test because the thing being replaced could not be tested at all.
/// VideoView is "a detached window over your video control" (VideoLAN's README) - a native window, which
/// means nothing about it can be asserted from a test, and a failure to attach it kills the process with
/// no exception. Decoding into a buffer makes the picture an ordinary value that can be inspected.
/// </summary>
public class VlcVideoFrameBufferTests
{
    private readonly ITestOutputHelper _output;

    public VlcVideoFrameBufferTests(ITestOutputHelper output) => _output = output;

    private static string ProbeVideo =>
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "player_probe.mp4");

    [Theory]
    // Never upscaled: a small clip decodes at its own size.
    [InlineData(320, 240, 320, 240)]
    [InlineData(1280, 720, 1280, 720)]
    // 4K is capped, and stays 16:9 rather than being squashed to the cap's shape.
    [InlineData(3840, 2160, 1920, 1080)]
    // A vertical Shorts/TikTok clip is capped by HEIGHT, which is the case a width-only cap gets wrong.
    [InlineData(2160, 3840, 608, 1080)]
    // Unknown size falls back rather than throwing, so a failed probe never blocks playback.
    [InlineData(0, 0, 1280, 720)]
    public void ChooseDecodeSize_CapsWithoutDistortingOrUpscaling(int nativeW, int nativeH, int expectedW, int expectedH)
    {
        var (width, height) = VlcVideoFrameBuffer.ChooseDecodeSize(nativeW, nativeH);

        Assert.Equal(expectedW, width);
        Assert.Equal(expectedH, height);
        Assert.True(width <= VlcVideoFrameBuffer.MaxDecodeWidth);
        Assert.True(height <= VlcVideoFrameBuffer.MaxDecodeHeight);
        Assert.Equal(0, width % 2);
        Assert.Equal(0, height % 2);

        if (nativeW > 0 && nativeH > 0)
        {
            // Aspect ratio preserved - this is what stops the picture looking stretched.
            var nativeAspect = (double)nativeW / nativeH;
            var decodedAspect = (double)width / height;
            Assert.True(
                Math.Abs(nativeAspect - decodedAspect) < 0.02,
                $"aspect changed: {nativeAspect:F3} -> {decodedAspect:F3}");
        }
    }

    [Fact]
    public void NewBuffer_StartsOpaqueBlack_NotGarbage()
    {
        using var buffer = new VlcVideoFrameBuffer(64, 48);

        var (r, g, b) = buffer.ReadPixel(32, 24);

        Assert.Equal(0, r);
        Assert.Equal(0, g);
        Assert.Equal(0, b);
    }

    [Fact]
    public void CopyLatestFrame_ReturnsFalse_WhenNothingHasBeenDecoded()
    {
        using var buffer = new VlcVideoFrameBuffer(32, 32);
        var destination = Marshal.AllocHGlobal(buffer.ByteCount);

        try
        {
            Assert.False(buffer.CopyLatestFrame(destination, buffer.Stride, buffer.Height));
        }
        finally
        {
            Marshal.FreeHGlobal(destination);
        }
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var buffer = new VlcVideoFrameBuffer(16, 16);

        buffer.Dispose();
        buffer.Dispose();

        Assert.False(buffer.HasNewFrame);
    }

    /// <summary>
    /// The one that replaces "I hope the VideoView shows something": play the real clip through the real
    /// session with the memory video output attached, then read the pixels back out.
    /// </summary>
    [Fact]
    public void PlayingThroughTheMemoryOutput_DeliversTheRightPicture()
    {
        Assert.True(File.Exists(ProbeVideo), $"Test asset missing: {ProbeVideo}");

        using var session = VideoPlaybackSession.Create();

        if (!session.IsReady)
        {
            _output.WriteLine($"libvlc unavailable here - {session.FailureReason}");
            Assert.NotNull(session.FailureReason);
            return;
        }

        // The probe clip is 320x240, so this is also a check that a small clip is not upscaled.
        var frames = session.UseMemoryVideoOutput(320, 240);
        Assert.NotNull(frames);
        Assert.Equal(320, frames!.Width);
        Assert.Equal(240, frames.Height);

        Assert.True(session.Open(ProbeVideo, out var message), message);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline && frames.FramesDisplayed < 40)
        {
            Thread.Sleep(50);
        }

        var decoded = frames.FramesDisplayed;
        var (r, g, b) = frames.ReadPixel(frames.Width / 2, frames.Height / 2);

        _output.WriteLine($"frames delivered to the buffer : {decoded}");
        _output.WriteLine($"centre pixel R,G,B             : {r}, {g}, {b}");

        // Picture is really arriving...
        Assert.True(decoded >= 40, $"only {decoded} frames reached the buffer - there is no picture.");

        // ...and it is the picture the file actually contains (authored R=30 G=144 B=255, allowing for
        // yuv420p round-tripping), not noise or an uninitialised buffer.
        Assert.InRange(r, 20, 40);
        Assert.InRange(g, 134, 154);
        Assert.InRange(b, 245, 255);

        // And a copy-out hands over the same picture, which is exactly what the on-screen bitmap does.
        var destination = Marshal.AllocHGlobal(frames.ByteCount);
        try
        {
            var copiedSomething = false;
            var copyDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < copyDeadline && !copiedSomething)
            {
                copiedSomething = frames.CopyLatestFrame(destination, frames.Stride, frames.Height);
                if (!copiedSomething)
                {
                    Thread.Sleep(20);
                }
            }

            Assert.True(copiedSomething, "no frame could be copied out to the on-screen bitmap");

            var offset = (frames.Height / 2) * frames.Stride + (frames.Width / 2) * 4;
            Assert.InRange(Marshal.ReadByte(destination, offset + 2), 20, 40);    // R
            Assert.InRange(Marshal.ReadByte(destination, offset + 1), 134, 154);  // G
            Assert.InRange(Marshal.ReadByte(destination, offset + 0), 245, 255);  // B
        }
        finally
        {
            session.Stop();
            Marshal.FreeHGlobal(destination);
        }
    }

    [Theory]
    // Wider window than video: bars left and right, picture vertically full.
    [InlineData(1000, 500, 320, 240, 166.667, 0, 666.667, 500)]
    // Taller window than video: bars top and bottom.
    [InlineData(640, 1000, 320, 240, 0, 260, 640, 480)]
    // Exact aspect match: fills completely, no bars.
    [InlineData(640, 480, 320, 240, 0, 0, 640, 480)]
    public void FitUniform_ScalesToFillWithoutDistorting(
        double availableW, double availableH, int sourceW, int sourceH,
        double expectedX, double expectedY, double expectedW, double expectedH)
    {
        var rect = VideoSurface.FitUniform(new Size(availableW, availableH), new PixelSize(sourceW, sourceH));

        Assert.Equal(expectedX, rect.X, 1);
        Assert.Equal(expectedY, rect.Y, 1);
        Assert.Equal(expectedW, rect.Width, 1);
        Assert.Equal(expectedH, rect.Height, 1);

        // Never larger than the space it was given - that is what stops the picture spilling out of the
        // window when it is made smaller.
        Assert.True(rect.Width <= availableW + 0.01);
        Assert.True(rect.Height <= availableH + 0.01);
    }

    [Fact]
    public void FitUniform_GrowsWithTheWindow()
    {
        var small = VideoSurface.FitUniform(new Size(320, 240), new PixelSize(320, 240));
        var large = VideoSurface.FitUniform(new Size(1920, 1440), new PixelSize(320, 240));

        // The complaint was "ne može se povećati video" - so assert enlarging the window really does
        // enlarge the picture, rather than leaving it pinned at its decoded size.
        Assert.Equal(320, small.Width, 1);
        Assert.Equal(1920, large.Width, 1);
        Assert.True(large.Width > small.Width * 5);
    }

    [Fact]
    public void FitUniform_HandlesZeroSizedWindowWithoutThrowing()
    {
        Assert.Equal(default, VideoSurface.FitUniform(new Size(0, 0), new PixelSize(320, 240)));
        Assert.Equal(default, VideoSurface.FitUniform(new Size(100, 100), new PixelSize(0, 0)));
    }
}
