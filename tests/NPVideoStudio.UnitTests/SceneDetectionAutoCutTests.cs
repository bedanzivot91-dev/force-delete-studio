using System.Collections.ObjectModel;
using System.Diagnostics;
using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class SceneDetectionAutoCutTests
{
    [Fact]
    public void AutoCutSelectedClip_CreatesRealSourceBoundariesAndPersistsThem()
    {
        var asset = new MediaAsset
        {
            Id = "media",
            FilePath = "scene-source.mp4",
            Kind = MediaKind.Video,
            Duration = TimeSpan.FromSeconds(10),
            HasVideoStream = true
        };
        var clip = new TimelineClip
        {
            Id = "clip",
            MediaAssetId = asset.Id,
            SourceTrimInSeconds = 0,
            SourceTrimOutSeconds = 10,
            TimelineStartSeconds = 3
        };
        var project = new Project
        {
            Name = "Auto Cut",
            MediaLibrary = new List<MediaAsset> { asset },
            Timeline = new Timeline
            {
                Tracks = new List<TimelineTrack>
                {
                    new() { Id = "video", Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } }
                }
            }
        };
        var media = new ObservableCollection<MediaAssetViewModel> { new(asset) };
        var timeline = new TimelineViewModel(project, media, () => 0);
        timeline.SelectedClipId = clip.Id;

        var cuts = timeline.AutoCutSelectedAtSourceTimes(new[] { 2.0, 5.0, 5.001, 9.98 });

        Assert.Equal(2, cuts); // near-duplicate and edge cut are rejected.
        var split = timeline.CurrentTracks.Single().Clips.OrderBy(c => c.SourceTrimInSeconds).ToArray();
        Assert.Equal(3, split.Length);
        Assert.Equal((0.0, 2.0), (split[0].SourceTrimInSeconds, split[0].SourceTrimOutSeconds));
        Assert.Equal((2.0, 5.0), (split[1].SourceTrimInSeconds, split[1].SourceTrimOutSeconds));
        Assert.Equal((5.0, 10.0), (split[2].SourceTrimInSeconds, split[2].SourceTrimOutSeconds));

        timeline.SaveToProject();
        Assert.Equal(3, project.Timeline.Tracks.Single().Clips.Count);
    }

    [Fact]
    public void AutoCutSelectedClip_MapsSceneBoundariesThroughVelocityCurve()
    {
        var asset = new MediaAsset
        {
            Id = "media",
            FilePath = "curve-source.mp4",
            Kind = MediaKind.Video,
            Duration = TimeSpan.FromSeconds(8),
            HasVideoStream = true
        };
        var clip = new TimelineClip
        {
            Id = "curve",
            MediaAssetId = asset.Id,
            SourceTrimInSeconds = 0,
            SourceTrimOutSeconds = 8,
            TimelineStartSeconds = 1,
            SpeedCurvePreset = SpeedCurvePreset.Montage,
            SpeedCurvePoints = new List<SpeedCurvePoint>
            {
                new() { SourceTimeSeconds = 0, SpeedMultiplier = 1 },
                new() { SourceTimeSeconds = 8, SpeedMultiplier = 2 }
            }
        };
        var expectedTimelineCut = clip.TimelineStartSeconds + SpeedCurveMath.OutputDurationBetween(clip, 0, 4);
        var project = new Project
        {
            Name = "Velocity Auto Cut",
            MediaLibrary = new List<MediaAsset> { asset },
            Timeline = new Timeline
            {
                Tracks = new List<TimelineTrack>
                {
                    new() { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } }
                }
            }
        };
        var timeline = new TimelineViewModel(project, new ObservableCollection<MediaAssetViewModel> { new(asset) }, () => 0)
        {
            SelectedClipId = clip.Id
        };

        Assert.Equal(1, timeline.AutoCutSelectedAtSourceTimes(new[] { 4.0 }));
        var pieces = timeline.CurrentTracks.Single().Clips.OrderBy(c => c.TimelineStartSeconds).ToArray();
        Assert.Equal(2, pieces.Length);
        Assert.Equal(4, pieces[0].SourceTrimOutSeconds, 6);
        Assert.Equal(4, pieces[1].SourceTrimInSeconds, 6);
        Assert.Equal(expectedTimelineCut, pieces[1].TimelineStartSeconds, 6);
    }

    [Fact]
    public void Studio2026CommandBar_ExposesSceneDetectionControls()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "Views", "ModernWorkspaceCommandBarView.axaml"));
        Assert.Contains("Scene / Auto Cut", xaml);
        Assert.Contains("SceneDetectionThresholdPercent", xaml);
        Assert.Contains("SceneMinimumSpacingSeconds", xaml);
        Assert.Contains("AutoCutSelectedClipCommand", xaml);
        Assert.Contains("CancelSceneDetectionCommand", xaml);
    }

    [Fact]
    public async Task RealFfmpeg_ScdetFindsSyntheticHardSceneChanges()
    {
        // Required Windows CI has FFmpeg on PATH. Other platforms can run the pure timeline/UI tests.
        if (!OperatingSystem.IsWindows()) return;

        var source = Path.Combine(Path.GetTempPath(), $"npvs-scenes-{Guid.NewGuid():N}.mp4");
        try
        {
            var create = await RunAsync("ffmpeg", new[]
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "color=c=black:s=160x90:r=30:d=1",
                "-f", "lavfi", "-i", "color=c=white:s=160x90:r=30:d=1",
                "-f", "lavfi", "-i", "color=c=red:s=160x90:r=30:d=1",
                "-filter_complex", "[0:v][1:v][2:v]concat=n=3:v=1:a=0,format=yuv420p[v]",
                "-map", "[v]", "-c:v", "mpeg4", "-q:v", "2", source
            });
            Assert.True(create.ExitCode == 0, create.Error);
            Assert.True(File.Exists(source) && new FileInfo(source).Length > 1000);

            var service = new SceneDetectionService();
            var scenes = await service.DetectAsync(source, 0, 3, thresholdPercent: 5, minimumSpacingSeconds: 0.40);

            Assert.True(scenes.Count >= 2, $"Expected at least two hard scene changes, got {string.Join(", ", scenes.Select(s => $"{s.SourceTimeSeconds:0.000}/{s.Score:0.0}"))}");
            Assert.Contains(scenes, s => s.SourceTimeSeconds >= 0.80 && s.SourceTimeSeconds <= 1.20);
            Assert.Contains(scenes, s => s.SourceTimeSeconds >= 1.80 && s.SourceTimeSeconds <= 2.20);
        }
        finally
        {
            if (File.Exists(source)) File.Delete(source);
        }
    }

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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await outputTask, await errorTask);
    }
}
