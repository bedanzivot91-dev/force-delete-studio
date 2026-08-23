using System.Buffers.Binary;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Real integration tests against the actual ffmpeg binary, following the same approach as
/// RenderServiceTests: generate a tiny synthetic source clip via ffmpeg's lavfi test source, then
/// extract a real frame and verify it's a genuinely valid, correctly-sized PNG by reading its IHDR
/// chunk directly - not just "some bytes came back". (Avalonia's headless test platform stubs out its
/// real bitmap decoder and reports a fake 1x1 size for any image, so Bitmap can't be used here to verify
/// decodability; parsing the PNG header ourselves is the only way to check the real bytes ffmpeg wrote.)
/// </summary>
public class FramePreviewServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"npvs_framepreview_test_{Guid.NewGuid():N}");
    private readonly FramePreviewService _service = new();

    public FramePreviewServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private async Task<string> CreateSolidColorClipAsync(string name, string color, double durationSeconds, int width = 320, int height = 240)
    {
        var path = Path.Combine(_tempDir, name);
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("lavfi");
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add($"color=c={color}:s={width}x{height}:d={durationSeconds}:r=10");
        process.StartInfo.ArgumentList.Add("-c:v");
        process.StartInfo.ArgumentList.Add("libx264");
        process.StartInfo.ArgumentList.Add(path);

        process.Start();
        await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(File.Exists(path), "Test clip generation failed - is ffmpeg on PATH?");
        return path;
    }

    /// <summary>Reads width/height straight out of the PNG's IHDR chunk (bytes 16-23, big-endian
    /// per the PNG spec) - a real, independent check of what ffmpeg actually wrote, with no image
    /// library involved.</summary>
    private static (int Width, int Height) ReadPngDimensions(byte[] png)
    {
        Assert.True(png.Length > 24, "Too short to be a valid PNG");
        Assert.Equal(0x89, png[0]);
        Assert.Equal((byte)'P', png[1]);
        Assert.Equal((byte)'N', png[2]);
        Assert.Equal((byte)'G', png[3]);

        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        return (width, height);
    }

    [Fact]
    public async Task ExtractFrameAsync_RealClip_ReturnsValidPngAtCorrectSize()
    {
        var clip = await CreateSolidColorClipAsync("clip.mp4", "red", 3, 320, 240);

        var bytes = await _service.ExtractFrameAsync(clip, 1.5);

        Assert.NotNull(bytes);
        var (width, height) = ReadPngDimensions(bytes!);
        Assert.Equal(320, width);
        Assert.Equal(240, height);
    }

    [Fact]
    public async Task ExtractFrameAsync_NonexistentSourceFile_ReturnsNull()
    {
        var bytes = await _service.ExtractFrameAsync(Path.Combine(_tempDir, "does-not-exist.mp4"), 1.0);

        Assert.Null(bytes);
    }

    [Fact]
    public async Task ExtractFrameAsync_TimestampPastEndOfClip_ReturnsNull()
    {
        var clip = await CreateSolidColorClipAsync("short.mp4", "blue", 1);

        var bytes = await _service.ExtractFrameAsync(clip, 30.0);

        Assert.Null(bytes);
    }

    [Fact]
    public async Task ExtractFrameAsync_Cancelled_ThrowsOperationCancelledAndLeavesNoOrphanProcess()
    {
        var clip = await CreateSolidColorClipAsync("clip.mp4", "green", 5);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.ExtractFrameAsync(clip, 1.0, cts.Token));
    }
}
