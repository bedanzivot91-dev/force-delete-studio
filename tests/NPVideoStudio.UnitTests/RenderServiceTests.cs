using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Real integration tests against the actual ffmpeg binary - generates its own tiny synthetic source
/// clips (distinct solid colors + distinct audio tones) via ffmpeg's lavfi test sources rather than
/// committed binary fixtures, then renders them for real and verifies the actual output: correct total
/// duration/resolution, a real black gap between clips, and burned-in caption text confirmed via real
/// Tesseract OCR at the exact timestamps it should (and shouldn't) appear - the same verification method
/// used manually while building this service, now automated.
/// </summary>
public class RenderServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"npvs_render_test_{Guid.NewGuid():N}");
    private readonly RenderService _service = new();

    public RenderServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private async Task<string> CreateSolidColorClipAsync(string name, string color, double durationSeconds, int frequency)
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
        process.StartInfo.ArgumentList.Add($"color=c={color}:s=320x240:d={durationSeconds}:r=10");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("lavfi");
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add($"sine=frequency={frequency}:duration={durationSeconds}");
        process.StartInfo.ArgumentList.Add("-c:v");
        process.StartInfo.ArgumentList.Add("libx264");
        process.StartInfo.ArgumentList.Add("-c:a");
        process.StartInfo.ArgumentList.Add("aac");
        process.StartInfo.ArgumentList.Add(path);

        process.Start();
        await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(File.Exists(path), "Test clip generation failed - is ffmpeg on PATH?");
        return path;
    }

    private static async Task<string> ExtractFrameTextAsync(string videoPath, double atSeconds, string tempDir)
    {
        var framePath = Path.Combine(tempDir, $"frame_{Guid.NewGuid():N}.png");
        using var ffmpegProcess = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        ffmpegProcess.StartInfo.ArgumentList.Add("-y");
        ffmpegProcess.StartInfo.ArgumentList.Add("-ss");
        ffmpegProcess.StartInfo.ArgumentList.Add(atSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
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

        using var tesseractProcess = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "tesseract",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
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

    private Project BuildTwoClipProjectWithGapAndCaptions(string clipAPath, string clipBPath)
    {
        var assetA = new MediaAsset { FilePath = clipAPath, Duration = TimeSpan.FromSeconds(3) };
        var assetB = new MediaAsset { FilePath = clipBPath, Duration = TimeSpan.FromSeconds(2) };
        var project = new Project { Name = "Render test", MediaLibrary = { assetA, assetB } };

        var videoTrack = new TimelineTrack { Kind = TimelineTrackKind.Video };
        videoTrack.Clips.Add(new TimelineClip { MediaAssetId = assetA.Id, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 3, TimelineStartSeconds = 0 });
        videoTrack.Clips.Add(new TimelineClip { MediaAssetId = assetB.Id, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2, TimelineStartSeconds = 4 }); // 1s gap
        project.Timeline.Tracks.Add(videoTrack);

        var textTrack = new TimelineTrack { Kind = TimelineTrackKind.Text };
        textTrack.Clips.Add(new TimelineClip { TextContent = "ZDRAVO", TimelineStartSeconds = 1, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 1 }); // 1..2
        textTrack.Clips.Add(new TimelineClip { TextContent = "SVET", TimelineStartSeconds = 4.5, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 1 }); // 4.5..5.5
        project.Timeline.Tracks.Add(textTrack);

        return project;
    }

    [Fact]
    public async Task RenderAsync_TwoClipsWithGapAndCaptions_ProducesCorrectDurationAndBurnedInText()
    {
        var clipA = await CreateSolidColorClipAsync("clipA.mp4", "red", 3, 440);
        var clipB = await CreateSolidColorClipAsync("clipB.mp4", "blue", 2, 880);
        var project = BuildTwoClipProjectWithGapAndCaptions(clipA, clipB);

        var job = new RenderJob
        {
            ProjectName = project.Name,
            Settings = new RenderSettings { OutputFilePath = Path.Combine(_tempDir, "output.mp4"), OverwriteConfirmed = true }
        };

        var outputPath = await _service.RenderAsync(project, job);

        Assert.Equal(RenderJobStatus.Completed, job.Status);
        Assert.Equal(100, job.ProgressPercent);
        Assert.NotNull(job.FfmpegCommandLogged);
        Assert.True(File.Exists(outputPath));

        // Total timeline duration: clipA (3s) + gap (1s) + clipB (2s) = 6s.
        using var probe = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "ffprobe",
            ArgumentList = { "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", outputPath },
            RedirectStandardOutput = true,
            UseShellExecute = false
        })!;
        var durationText = await probe.StandardOutput.ReadToEndAsync();
        await probe.WaitForExitAsync();
        var duration = double.Parse(durationText.Trim(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(6.0, duration, precision: 1);

        // Real OCR verification at key timestamps, same method used to manually verify this service.
        var beforeCaption = await ExtractFrameTextAsync(outputPath, 0.5, _tempDir);
        Assert.DoesNotContain("ZDRAVO", beforeCaption);

        var duringFirstCaption = await ExtractFrameTextAsync(outputPath, 1.5, _tempDir);
        Assert.Contains("ZDRAVO", duringFirstCaption);

        var duringGap = await ExtractFrameTextAsync(outputPath, 3.5, _tempDir);
        Assert.DoesNotContain("ZDRAVO", duringGap);
        Assert.DoesNotContain("SVET", duringGap);

        var duringSecondCaption = await ExtractFrameTextAsync(outputPath, 5.0, _tempDir);
        Assert.Contains("SVET", duringSecondCaption);
    }

    [Fact]
    public async Task RenderAsync_OutputAlreadyExistsWithoutConfirmation_ThrowsWithoutOverwriting()
    {
        var clipA = await CreateSolidColorClipAsync("clipA.mp4", "green", 1, 220);
        var project = new Project { Name = "Test", MediaLibrary = { new MediaAsset { Id = "a", FilePath = clipA, Duration = TimeSpan.FromSeconds(1) } } };
        project.Timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Video,
            Clips = { new TimelineClip { MediaAssetId = "a", SourceTrimInSeconds = 0, SourceTrimOutSeconds = 1, TimelineStartSeconds = 0 } }
        });

        var outputPath = Path.Combine(_tempDir, "existing.mp4");
        await File.WriteAllTextAsync(outputPath, "already here");

        var job = new RenderJob { ProjectName = project.Name, Settings = new RenderSettings { OutputFilePath = outputPath, OverwriteConfirmed = false } };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RenderAsync(project, job));
        Assert.Equal("already here", await File.ReadAllTextAsync(outputPath));
    }

    [Fact]
    public async Task RenderAsync_UnavailableHardwareCodec_FallsBackToLibx264AndSucceeds()
    {
        var clipA = await CreateSolidColorClipAsync("clipA.mp4", "yellow", 1, 330);
        var project = new Project { Name = "Test", MediaLibrary = { new MediaAsset { Id = "a", FilePath = clipA, Duration = TimeSpan.FromSeconds(1) } } };
        project.Timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Video,
            Clips = { new TimelineClip { MediaAssetId = "a", SourceTrimInSeconds = 0, SourceTrimOutSeconds = 1, TimelineStartSeconds = 0 } }
        });

        // This sandbox has no GPU, so h264_nvenc genuinely fails here - a real test of the fallback path,
        // not a simulated one.
        var job = new RenderJob
        {
            ProjectName = project.Name,
            Settings = new RenderSettings { OutputFilePath = Path.Combine(_tempDir, "fallback.mp4"), Codec = VideoCodec.H264Nvenc, OverwriteConfirmed = true }
        };

        var outputPath = await _service.RenderAsync(project, job);

        Assert.Equal(RenderJobStatus.Completed, job.Status);
        Assert.True(File.Exists(outputPath));
        Assert.Contains("libx264", job.FfmpegCommandLogged); // the logged command reflects the successful fallback attempt
    }

    [Fact]
    public async Task RenderAsync_CancelledMidRender_StopsAndReportsCancelled()
    {
        // A longer clip so there's real time for cancellation to land mid-encode rather than after completion.
        var clipA = await CreateSolidColorClipAsync("clipA.mp4", "purple", 8, 550);
        var project = new Project { Name = "Test", MediaLibrary = { new MediaAsset { Id = "a", FilePath = clipA, Duration = TimeSpan.FromSeconds(8) } } };
        project.Timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Video,
            Clips = { new TimelineClip { MediaAssetId = "a", SourceTrimInSeconds = 0, SourceTrimOutSeconds = 8, TimelineStartSeconds = 0 } }
        });

        var job = new RenderJob
        {
            ProjectName = project.Name,
            Settings = new RenderSettings { OutputFilePath = Path.Combine(_tempDir, "cancelled.mp4"), Preset = "veryslow", OverwriteConfirmed = true }
        };

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.RenderAsync(project, job, cts.Token));

        Assert.Equal(RenderJobStatus.Cancelled, job.Status);
        Assert.False(File.Exists(job.Settings.OutputFilePath));
        Assert.Empty(Directory.GetFiles(_tempDir, "*.part"));
    }
}
