using System.Diagnostics;
using System.Globalization;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Real integration tests against the actual ffmpeg binary (and Tesseract for the caption-burn-in case) -
/// same "generate a tiny synthetic fixture with lavfi, verify the real output" methodology as
/// RenderServiceTests.cs, first verified manually before writing this: a 5s synthetic song + a still
/// image produced exactly a 5.0s 1280x720 output, and a burned-in .srt caption showed up via real OCR at
/// exactly its timestamp window and nowhere else.
/// </summary>
public class QuickVideoServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"npvs_quickvideo_test_{Guid.NewGuid():N}");
    private readonly QuickVideoService _service = new();

    public QuickVideoServiceTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private async Task<string> CreateImageAsync(string name, string color, int width = 640, int height = 360)
    {
        var path = Path.Combine(_tempDir, name);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo { FileName = "ffmpeg", RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true }
        };
        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("lavfi");
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add($"color=c={color}:s={width}x{height}");
        process.StartInfo.ArgumentList.Add("-frames:v");
        process.StartInfo.ArgumentList.Add("1");
        process.StartInfo.ArgumentList.Add(path);
        process.Start();
        await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(File.Exists(path), "Test image generation failed - is ffmpeg on PATH?");
        return path;
    }

    private async Task<string> CreateSongAsync(string name, double durationSeconds, int frequency = 440)
    {
        var path = Path.Combine(_tempDir, name);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo { FileName = "ffmpeg", RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true }
        };
        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("lavfi");
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add($"sine=frequency={frequency}:duration={durationSeconds}");
        process.StartInfo.ArgumentList.Add("-c:a");
        process.StartInfo.ArgumentList.Add("libmp3lame");
        process.StartInfo.ArgumentList.Add(path);
        process.Start();
        await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(File.Exists(path), "Test song generation failed - is ffmpeg on PATH?");
        return path;
    }

    private static async Task<double> ProbeDurationAsync(string videoPath)
    {
        using var probe = Process.Start(new ProcessStartInfo
        {
            FileName = "ffprobe",
            ArgumentList = { "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", videoPath },
            RedirectStandardOutput = true,
            UseShellExecute = false
        })!;
        var durationText = await probe.StandardOutput.ReadToEndAsync();
        await probe.WaitForExitAsync();
        return double.Parse(durationText.Trim(), CultureInfo.InvariantCulture);
    }

    private static async Task<(int Width, int Height)> ProbeResolutionAsync(string videoPath)
    {
        using var probe = Process.Start(new ProcessStartInfo
        {
            FileName = "ffprobe",
            ArgumentList = { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=p=0", videoPath },
            RedirectStandardOutput = true,
            UseShellExecute = false
        })!;
        var text = (await probe.StandardOutput.ReadToEndAsync()).Trim();
        await probe.WaitForExitAsync();
        var parts = text.Split(',');
        return (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private static async Task<string> ExtractFrameTextAsync(string videoPath, double atSeconds, string tempDir)
    {
        var framePath = Path.Combine(tempDir, $"frame_{Guid.NewGuid():N}.png");
        using var ffmpegProcess = new Process
        {
            StartInfo = new ProcessStartInfo { FileName = "ffmpeg", RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true }
        };
        ffmpegProcess.StartInfo.ArgumentList.Add("-y");
        ffmpegProcess.StartInfo.ArgumentList.Add("-ss");
        ffmpegProcess.StartInfo.ArgumentList.Add(atSeconds.ToString(CultureInfo.InvariantCulture));
        ffmpegProcess.StartInfo.ArgumentList.Add("-i");
        ffmpegProcess.StartInfo.ArgumentList.Add(videoPath);
        ffmpegProcess.StartInfo.ArgumentList.Add("-frames:v");
        ffmpegProcess.StartInfo.ArgumentList.Add("1");
        ffmpegProcess.StartInfo.ArgumentList.Add("-update");
        ffmpegProcess.StartInfo.ArgumentList.Add("1");
        ffmpegProcess.StartInfo.ArgumentList.Add(framePath);
        ffmpegProcess.Start();
        await ffmpegProcess.StandardError.ReadToEndAsync();
        await ffmpegProcess.WaitForExitAsync();

        using var tesseractProcess = new Process
        {
            StartInfo = new ProcessStartInfo { FileName = "tesseract", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true }
        };
        tesseractProcess.StartInfo.ArgumentList.Add(framePath);
        tesseractProcess.StartInfo.ArgumentList.Add("stdout");
        tesseractProcess.StartInfo.ArgumentList.Add("--psm");
        tesseractProcess.StartInfo.ArgumentList.Add("6");
        tesseractProcess.Start();
        var text = await tesseractProcess.StandardOutput.ReadToEndAsync();
        await tesseractProcess.StandardError.ReadToEndAsync();
        await tesseractProcess.WaitForExitAsync();
        return text;
    }

    [Fact]
    public async Task CreateAsync_ImageAndSong_ProducesVideoMatchingSongDurationAndTargetResolution()
    {
        var image = await CreateImageAsync("image.jpg", "blue");
        var song = await CreateSongAsync("song.mp3", 5);
        var outputPath = Path.Combine(_tempDir, "output.mp4");

        var reportedPercents = new List<double>();
        var progress = new Progress<double>(reportedPercents.Add);

        var result = await _service.CreateAsync(image, song, songDurationSeconds: 5, outputPath, overwriteConfirmed: false, width: 1280, height: 720, progress: progress);

        Assert.Equal(outputPath, result);
        Assert.True(File.Exists(outputPath));
        Assert.Equal(5.0, await ProbeDurationAsync(outputPath), precision: 1);
        Assert.Equal((1280, 720), await ProbeResolutionAsync(outputPath));
    }

    [Fact]
    public async Task CreateAsync_WithSubtitleFile_BurnsInTextAtExactWindowAndNowhereElse()
    {
        var image = await CreateImageAsync("image.jpg", "darkgreen");
        var song = await CreateSongAsync("song.mp3", 5);
        var srtPath = Path.Combine(_tempDir, "captions.srt");
        await File.WriteAllTextAsync(srtPath,
            "1\n00:00:01,000 --> 00:00:02,500\nZDRAVO SVETE\n\n2\n00:00:03,500 --> 00:00:04,500\nDRUGI TITL\n\n");
        var outputPath = Path.Combine(_tempDir, "captioned.mp4");

        await _service.CreateAsync(image, song, songDurationSeconds: 5, outputPath, overwriteConfirmed: false, subtitleSrtPath: srtPath, width: 640, height: 360);

        Assert.True(File.Exists(outputPath));

        var before = await ExtractFrameTextAsync(outputPath, 0.4, _tempDir);
        Assert.DoesNotContain("ZDRAVO", before);

        var duringFirst = await ExtractFrameTextAsync(outputPath, 1.5, _tempDir);
        Assert.Contains("ZDRAVO", duringFirst);

        var between = await ExtractFrameTextAsync(outputPath, 3.0, _tempDir);
        Assert.DoesNotContain("ZDRAVO", between);
        Assert.DoesNotContain("DRUGI", between);

        var duringSecond = await ExtractFrameTextAsync(outputPath, 4.0, _tempDir);
        Assert.Contains("DRUGI", duringSecond);
    }

    [Fact]
    public async Task CreateAsync_OutputAlreadyExistsWithoutConfirmation_ThrowsWithoutOverwriting()
    {
        var image = await CreateImageAsync("image.jpg", "red");
        var song = await CreateSongAsync("song.mp3", 1);
        var outputPath = Path.Combine(_tempDir, "existing.mp4");
        await File.WriteAllTextAsync(outputPath, "already here");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateAsync(image, song, songDurationSeconds: 1, outputPath, overwriteConfirmed: false));

        Assert.Equal("already here", await File.ReadAllTextAsync(outputPath));
    }

    [Fact]
    public async Task CreateAsync_CancelledMidRender_StopsAndReportsCancelled()
    {
        // Large resolution + a longer song so there's real wall-clock time to cancel mid-encode.
        var image = await CreateImageAsync("bigimage.jpg", "green", 3840, 2160);
        var song = await CreateSongAsync("bigsong.mp3", 10);
        var outputPath = Path.Combine(_tempDir, "cancelled.mp4");

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.CreateAsync(image, song, songDurationSeconds: 10, outputPath, overwriteConfirmed: true, width: 3840, height: 2160, cancellationToken: cts.Token));

        Assert.False(File.Exists(outputPath));
        Assert.Empty(Directory.GetFiles(_tempDir, "*.part"));
    }

    [Theory]
    [InlineData(@"C:\Users\me\captions.srt", "C\\:/Users/me/captions.srt")]
    [InlineData("/home/user/captions.srt", "/home/user/captions.srt")]
    public void EscapeSubtitlesFilterPath_EscapesColonAndNormalizesBackslashes(string input, string expected)
    {
        Assert.Equal(expected, QuickVideoService.EscapeSubtitlesFilterPath(input));
    }
}
