using System.Diagnostics;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class MotionTrackingStabilizationIntegrationTests
{
    [Fact]
    public void RenderGraph_AutoReframeRunsBeforeVidstabTransform()
    {
        var clip = BuildClip();
        var timeline = new Timeline
        {
            Tracks = new List<TimelineTrack>
            {
                new() { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } }
            }
        };
        var assets = new[]
        {
            new MediaAsset { Id = "media", FilePath = "source.mp4", Kind = MediaKind.Video }
        };

        var plan = FfmpegFilterGraphBuilder.Build(
            timeline,
            assets,
            targetWidth: 108,
            targetHeight: 192,
            stabilizationTransforms: new Dictionary<string, string> { [clip.Id] = @"C:\Temp\tracking.trf" });

        var cropIndex = plan.FilterComplexArgument.IndexOf("crop=w=", StringComparison.Ordinal);
        var stabilizationIndex = plan.FilterComplexArgument.IndexOf("vidstabtransform", StringComparison.Ordinal);
        Assert.True(cropIndex >= 0, plan.FilterComplexArgument);
        Assert.True(stabilizationIndex > cropIndex, plan.FilterComplexArgument);
    }

    [Fact]
    public async Task RealWindowsFfmpeg_AutoReframeAndStabilizationUseSameGeometry()
    {
        if (!OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), $"npvs-track-stab-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source.mp4");
        var output = Path.Combine(root, "output.mp4");

        try
        {
            var create = await RunAsync("ffmpeg", new[]
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "testsrc2=size=192x108:rate=30:duration=3",
                "-c:v", "mpeg4", "-q:v", "4", source
            });
            Assert.True(create.ExitCode == 0, create.Error);

            var clip = BuildClip();
            var project = new Project
            {
                Name = "Tracking + stabilization proof",
                Format = new ProjectFormat
                {
                    AspectRatio = AspectRatioPreset.Vertical9x16,
                    Width = 108,
                    Height = 192,
                    FrameRate = FrameRatePreset.Fps30,
                    Fps = 30
                }
            };
            project.MediaLibrary.Add(new MediaAsset
            {
                Id = "media",
                FilePath = source,
                Kind = MediaKind.Video,
                Duration = TimeSpan.FromSeconds(3),
                Width = 192,
                Height = 108,
                Fps = 30,
                HasVideoStream = true,
                HasAudioStream = false
            });
            project.Timeline.Tracks.Add(new TimelineTrack
            {
                Kind = TimelineTrackKind.Video,
                Clips = new List<TimelineClip> { clip }
            });

            using var prepass = await VideoStabilizationPrepass.PrepareAsync(project, "ffmpeg");
            Assert.True(prepass.TransformFiles.TryGetValue(clip.Id, out var transform));
            Assert.True(File.Exists(transform) && new FileInfo(transform).Length > 0);

            var filter = FfmpegFilterGraphBuilder.BuildAutoReframeFilter(clip, 108, 192).TrimStart(',') +
                         FfmpegFilterGraphBuilder.BuildStabilizationFilter(clip, prepass.TransformFiles) +
                         ",scale=108:192,setsar=1";
            Assert.True(filter.IndexOf("crop=w=", StringComparison.Ordinal) <
                        filter.IndexOf("vidstabtransform", StringComparison.Ordinal));

            var render = await RunAsync("ffmpeg", new[]
            {
                "-y", "-hide_banner", "-loglevel", "error", "-i", source,
                "-vf", filter,
                "-an", "-c:v", "mpeg4", "-q:v", "4", output
            });
            Assert.True(render.ExitCode == 0,
                $"Combined Auto Reframe + stabilization render failed:\n{render.Error}\nFilter: {filter}");
            Assert.True(File.Exists(output) && new FileInfo(output).Length > 1000);

            var probe = await RunAsync("ffprobe", new[]
            {
                "-v", "error", "-show_entries", "stream=width,height", "-of", "csv=p=0:s=x", output
            });
            Assert.True(probe.ExitCode == 0, probe.Error);
            Assert.Contains("108x192", probe.Output, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static TimelineClip BuildClip() => new()
    {
        Id = "track-stab",
        MediaAssetId = "media",
        SourceTrimInSeconds = 0,
        SourceTrimOutSeconds = 3,
        StabilizationEnabled = true,
        StabilizationSmoothing = 10,
        StabilizationAccuracy = 15,
        StabilizationZoomPercent = 0,
        AutoReframeEnabled = true,
        MotionTrackingPoints = new List<MotionTrackingPoint>
        {
            new() { SourceTimeSeconds = 0, CenterX = 0.35, CenterY = 0.50, Width = 0.20, Height = 0.30 },
            new() { SourceTimeSeconds = 1.5, CenterX = 0.50, CenterY = 0.50, Width = 0.20, Height = 0.30 },
            new() { SourceTimeSeconds = 3, CenterX = 0.65, CenterY = 0.50, Width = 0.20, Height = 0.30 }
        }
    };

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string fileName,
        IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await outputTask, await errorTask);
    }
}
