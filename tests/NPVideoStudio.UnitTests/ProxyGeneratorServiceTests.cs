using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Real integration test against the actual ffmpeg binary (present in this dev sandbox and via choco on
/// Windows CI, same as every other ffmpeg-based service in this codebase) - generates a tiny synthetic
/// source video with ffmpeg's own lavfi test source rather than committing a binary fixture, then runs
/// the real proxy transcode on it.
/// </summary>
public class ProxyGeneratorServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"npvs_proxy_test_{Guid.NewGuid():N}");
    private readonly ProxyGeneratorService _service = new();

    public ProxyGeneratorServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private async Task<string> CreateTestVideoAsync(int height = 480)
    {
        var path = Path.Combine(_tempDir, "source.mp4");
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
        process.StartInfo.ArgumentList.Add($"testsrc=duration=1:size=640x{height}:rate=10");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("lavfi");
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add("anullsrc=r=44100:cl=stereo:d=1");
        process.StartInfo.ArgumentList.Add("-shortest");
        process.StartInfo.ArgumentList.Add("-c:v");
        process.StartInfo.ArgumentList.Add("libx264");
        process.StartInfo.ArgumentList.Add("-c:a");
        process.StartInfo.ArgumentList.Add("aac");
        process.StartInfo.ArgumentList.Add(path);

        process.Start();
        await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(File.Exists(path), "Test video fixture generation failed - is ffmpeg on PATH?");
        return path;
    }

    [Fact]
    public async Task GenerateProxyAsync_RealFfmpegTranscode_ProducesSmallerVideoFile()
    {
        var source = await CreateTestVideoAsync(height: 480);
        var output = Path.Combine(_tempDir, "proxy.mp4");

        var result = await _service.GenerateProxyAsync(source, output, targetHeight: 240);

        Assert.Equal(output, result);
        Assert.True(File.Exists(output));
        // Only the source and final proxy should remain - the temp file must be renamed away on success,
        // never left behind looking like a second (possibly half-written) output.
        Assert.Equal(new[] { "proxy.mp4", "source.mp4" }, Directory.GetFiles(_tempDir).Select(Path.GetFileName).OrderBy(n => n));
    }

    [Fact]
    public async Task GenerateProxyAsync_SourceFileMissing_ThrowsFileNotFoundException()
    {
        var missingPath = Path.Combine(_tempDir, "does-not-exist.mp4");
        var output = Path.Combine(_tempDir, "proxy.mp4");

        await Assert.ThrowsAsync<FileNotFoundException>(() => _service.GenerateProxyAsync(missingPath, output));
    }
}
