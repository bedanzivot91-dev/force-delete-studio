using System.Diagnostics;
using System.Text.Json;
using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class StabilizationIntegrationTests
{
    [Fact]
    public void StabilizationEdit_IsUndoRedoSafe_Clamped_AndReverseDisablesIt()
    {
        var clip = new TimelineClip
        {
            Id = "stab",
            MediaAssetId = "media",
            SourceTrimInSeconds = 0,
            SourceTrimOutSeconds = 6
        };
        var session = SessionWith(clip);

        Assert.True(session.SetClipStabilization("stab", true, 500, 99, 80));
        var applied = SingleClip(session);
        Assert.True(applied.StabilizationEnabled);
        Assert.Equal(120, applied.StabilizationSmoothingFrames);
        Assert.Equal(15, applied.StabilizationAccuracy);
        Assert.Equal(30, applied.StabilizationZoomPercent, 6);

        session.Undo();
        Assert.False(SingleClip(session).StabilizationEnabled);
        session.Redo();
        Assert.True(SingleClip(session).StabilizationEnabled);
        Assert.Equal(120, SingleClip(session).StabilizationSmoothingFrames);

        session.SetClipTransform("stab", new ClipTransformSettings(
            0, false, false, 0, 0, 0, 0,
            true, false, false, "#00FF00", 0.12, 0.02));
        Assert.True(SingleClip(session).IsReversed);
        Assert.False(SingleClip(session).StabilizationEnabled);
        Assert.False(session.SetClipStabilization("stab", true, 15, 15, 0));
    }

    [Fact]
    public void StabilizationSettings_PersistAndSplitWithTheClip()
    {
        var clip = new TimelineClip
        {
            Id = "stab",
            MediaAssetId = "media",
            SourceTrimInSeconds = 1,
            SourceTrimOutSeconds = 9,
            StabilizationEnabled = true,
            StabilizationSmoothingFrames = 27,
            StabilizationAccuracy = 13,
            StabilizationZoomPercent = 7
        };

        var json = JsonSerializer.Serialize(clip);
        var loaded = JsonSerializer.Deserialize<TimelineClip>(json)!;
        Assert.True(loaded.StabilizationEnabled);
        Assert.Equal(27, loaded.StabilizationSmoothingFrames);
        Assert.Equal(13, loaded.StabilizationAccuracy);
        Assert.Equal(7, loaded.StabilizationZoomPercent, 6);

        var session = SessionWith(loaded);
        session.SplitClip("stab", loaded.TimelineDurationSeconds / 2.0);
        var split = session.Tracks.Single().Clips.OrderBy(c => c.TimelineStartSeconds).ToArray();
        Assert.Equal(2, split.Length);
        Assert.All(split, c =>
        {
            Assert.True(c.StabilizationEnabled);
            Assert.Equal(27, c.StabilizationSmoothingFrames);
            Assert.Equal(13, c.StabilizationAccuracy);
            Assert.Equal(7, c.StabilizationZoomPercent, 6);
        });
    }

    [Fact]
    public void RenderGraph_RefusesSilentFallbackAndConsumesPrepassTransform()
    {
        var clip = new TimelineClip
        {
            Id = "stab",
            MediaAssetId = "media",
            SourceTrimInSeconds = 0,
            SourceTrimOutSeconds = 4,
            StabilizationEnabled = true,
            StabilizationSmoothingFrames = 22,
            StabilizationAccuracy = 11,
            StabilizationZoomPercent = 4
        };
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

        var missing = Assert.Throws<InvalidOperationException>(() => FfmpegFilterGraphBuilder.Build(timeline, assets));
        Assert.Contains("motion", missing.Message, StringComparison.OrdinalIgnoreCase);

        var transform = Path.Combine("C:\\Temp Folder", "clip motion.trf");
        var plan = FfmpegFilterGraphBuilder.Build(
            timeline, assets, stabilizationTransforms: new Dictionary<string, string> { ["stab"] = transform });

        Assert.Contains("vidstabtransform", plan.FilterComplexArgument);
        Assert.Contains("smoothing=22", plan.FilterComplexArgument);
        Assert.Contains("zoom=4", plan.FilterComplexArgument);
        Assert.Contains("C\\:/Temp Folder/clip motion.trf", plan.FilterComplexArgument);
    }

    [Fact]
    public void Studio2026InspectorAndRenderService_AreWiredToRealStabilization()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "Views", "ModernInspectorView.axaml"));
        var renderer = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.Media", "RenderService.cs"));

        Assert.Contains("ModernVideoStabilization", xaml);
        Assert.Contains("StabilizationEnabled", xaml);
        Assert.Contains("StabilizationSmoothingFrames", xaml);
        Assert.Contains("StabilizationAccuracy", xaml);
        Assert.Contains("StabilizationZoomPercent", xaml);
        Assert.Contains("VideoStabilizationPrepass.PrepareAsync", renderer);
        Assert.Contains("stabilization.TransformFiles", renderer);
    }

    [Fact]
    public async Task RealFfmpeg_RunsVidstabDetectThenVidstabTransform()
    {
        // Required Windows CI carries the same Gyan FFmpeg family used for release packaging and has
        // --enable-libvidstab. Other platform/local runs may return; Windows CI must execute this body.
        if (!OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), $"npvs-real-stab-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source.mp4");
        var output = Path.Combine(root, "stabilized.mp4");

        try
        {
            var create = await RunAsync("ffmpeg", new[]
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "testsrc2=size=192x108:rate=30:duration=3",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=3",
                "-shortest", "-c:v", "mpeg4", "-q:v", "4", "-c:a", "aac", source
            });
            Assert.True(create.ExitCode == 0, create.Error);

            var clip = new TimelineClip
            {
                Id = "stab-real",
                MediaAssetId = "media",
                SourceTrimInSeconds = 0,
                SourceTrimOutSeconds = 3,
                StabilizationEnabled = true,
                StabilizationSmoothingFrames = 10,
                StabilizationAccuracy = 15,
                StabilizationZoomPercent = 0
            };
            var project = new Project { Name = "Real stabilization proof" };
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
                HasAudioStream = true
            });
            project.Timeline.Tracks.Add(new TimelineTrack
            {
                Kind = TimelineTrackKind.Video,
                Clips = new List<TimelineClip> { clip }
            });

            using var prepass = await VideoStabilizationPrepass.PrepareAsync(project, "ffmpeg");
            Assert.True(prepass.TransformFiles.TryGetValue("stab-real", out var transform));
            Assert.True(File.Exists(transform) && new FileInfo(transform).Length > 0);

            var filter = FfmpegFilterGraphBuilder.BuildStabilizationFilter(clip, prepass.TransformFiles).TrimStart(',');
            var transformRun = await RunAsync("ffmpeg", new[]
            {
                "-y", "-hide_banner", "-loglevel", "error", "-i", source,
                "-vf", filter, "-an", "-c:v", "mpeg4", "-q:v", "4", output
            });
            Assert.True(transformRun.ExitCode == 0,
                $"Real vidstabtransform failed:\n{transformRun.Error}\nFilter: {filter}");
            Assert.True(File.Exists(output) && new FileInfo(output).Length > 1000);

            var probe = await RunAsync("ffprobe", new[]
            {
                "-v", "error", "-show_entries", "stream=codec_type", "-of", "csv=p=0", output
            });
            Assert.True(probe.ExitCode == 0, probe.Error);
            Assert.Contains("video", probe.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static TimelineEditSession SessionWith(TimelineClip clip) => new(new[]
    {
        new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } }
    });

    private static TimelineClip SingleClip(TimelineEditSession session) => session.Tracks.Single().Clips.Single();

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NPVideoStudio.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string fileName, IEnumerable<string> arguments)
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await outputTask, await errorTask);
    }
}
