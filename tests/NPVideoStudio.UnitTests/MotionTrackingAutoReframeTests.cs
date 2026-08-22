using System.Diagnostics;
using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class MotionTrackingAutoReframeTests
{
    private static TimelineClip NewTrackedClip() => new()
    {
        Id = "clip",
        MediaAssetId = "media",
        SourceTrimInSeconds = 1,
        SourceTrimOutSeconds = 5,
        MotionTrackingPoints = new List<MotionTrackingPoint>
        {
            new() { SourceTimeSeconds = 1, CenterX = 0.25, CenterY = 0.45, Width = 0.2, Height = 0.3 },
            new() { SourceTimeSeconds = 3, CenterX = 0.50, CenterY = 0.50, Width = 0.2, Height = 0.3 },
            new() { SourceTimeSeconds = 5, CenterX = 0.75, CenterY = 0.55, Width = 0.2, Height = 0.3 }
        },
        AutoReframeEnabled = true
    };

    [Fact]
    public void ApplyTrackingResult_IsUndoSafeAndDeepCopiesPoints()
    {
        var clip = new TimelineClip
        {
            Id = "clip",
            MediaAssetId = "media",
            SourceTrimInSeconds = 1,
            SourceTrimOutSeconds = 5
        };
        var session = new TimelineEditSession(new[]
        {
            new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } }
        });
        var points = new List<MotionTrackingPoint>
        {
            new() { SourceTimeSeconds = 1, CenterX = 0.2, CenterY = 0.5 },
            new() { SourceTimeSeconds = 5, CenterX = 0.8, CenterY = 0.5 }
        };

        Assert.True(session.ApplyMotionTrackingResult(
            "clip", new MotionTrackingRegion(0.2, 0.5, 0.2, 0.3), points));

        var applied = session.Tracks.Single().Clips.Single();
        Assert.True(applied.AutoReframeEnabled);
        Assert.Equal(2, applied.MotionTrackingPoints.Count);
        points[0].CenterX = 0.99;
        Assert.Equal(0.2, applied.MotionTrackingPoints[0].CenterX, 6);

        session.Undo();
        var undone = session.Tracks.Single().Clips.Single();
        Assert.False(undone.AutoReframeEnabled);
        Assert.Empty(undone.MotionTrackingPoints);
    }

    [Fact]
    public void ApplyTrackingResult_RejectsPartialPathInsteadOfFreezingLastKnownPosition()
    {
        var clip = new TimelineClip
        {
            Id = "clip",
            MediaAssetId = "media",
            SourceTrimInSeconds = 1,
            SourceTrimOutSeconds = 5
        };
        var session = new TimelineEditSession(new[]
        {
            new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } }
        });

        var missingStart = new List<MotionTrackingPoint>
        {
            new() { SourceTimeSeconds = 2, CenterX = 0.3, CenterY = 0.5 },
            new() { SourceTimeSeconds = 5, CenterX = 0.7, CenterY = 0.5 }
        };
        var missingEnd = new List<MotionTrackingPoint>
        {
            new() { SourceTimeSeconds = 1, CenterX = 0.3, CenterY = 0.5 },
            new() { SourceTimeSeconds = 4, CenterX = 0.7, CenterY = 0.5 }
        };

        Assert.False(session.ApplyMotionTrackingResult(
            "clip", new MotionTrackingRegion(0.3, 0.5, 0.2, 0.3), missingStart));
        Assert.False(session.ApplyMotionTrackingResult(
            "clip", new MotionTrackingRegion(0.3, 0.5, 0.2, 0.3), missingEnd));
        Assert.False(session.Tracks.Single().Clips.Single().AutoReframeEnabled);
        Assert.Empty(session.Tracks.Single().Clips.Single().MotionTrackingPoints);
    }

    [Fact]
    public void ChangingTrackingRegion_InvalidatesStalePathAndDisablesAutoReframe()
    {
        var clip = NewTrackedClip();
        var session = new TimelineEditSession(new[]
        {
            new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } }
        });

        session.SetMotionTrackingRegion("clip", new MotionTrackingRegion(0.4, 0.4, 0.3, 0.3));

        var edited = session.Tracks.Single().Clips.Single();
        Assert.Empty(edited.MotionTrackingPoints);
        Assert.False(edited.AutoReframeEnabled);
        Assert.Equal(0.4, edited.TrackingRegionCenterX, 6);
    }

    [Fact]
    public void AutoReframeFilter_UsesPiecewiseTrackingCoordinates()
    {
        var filter = FfmpegFilterGraphBuilder.BuildAutoReframeFilter(NewTrackedClip(), 1080, 1920);

        Assert.Contains("crop=w=", filter, StringComparison.Ordinal);
        Assert.Contains("0.5625", filter, StringComparison.Ordinal);
        Assert.Contains("iw*(", filter, StringComparison.Ordinal);
        Assert.Contains("t-", filter, StringComparison.Ordinal);
        Assert.Contains("min(iw-ow", filter, StringComparison.Ordinal);
        Assert.Contains("min(ih-oh", filter, StringComparison.Ordinal);
    }

    [Fact]
    public void RangePreview_PreservesTrackingPathAndAutoReframe()
    {
        var clip = NewTrackedClip();
        clip.TimelineStartSeconds = 0;
        var timeline = new Timeline
        {
            Tracks = new List<TimelineTrack>
            {
                new() { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } }
            }
        };

        var sliced = FfmpegFilterGraphBuilder.ExtractRangeTimeline(timeline, 1, 3);
        var ranged = sliced.Tracks.Single().Clips.Single();

        Assert.True(ranged.AutoReframeEnabled);
        Assert.Equal(3, ranged.MotionTrackingPoints.Count);
        Assert.NotSame(clip.MotionTrackingPoints, ranged.MotionTrackingPoints);
        Assert.NotSame(clip.MotionTrackingPoints[0], ranged.MotionTrackingPoints[0]);
    }

    [Fact]
    public void AppAndReleaseGate_RequireBundledMotionTrackerScript()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "NPVideoStudio.App.csproj"));
        var release = File.ReadAllText(Path.Combine(root, "scripts", "build-release.ps1"));

        Assert.Contains("ai-worker\\motion_tracker.py", project, StringComparison.Ordinal);
        Assert.Contains("Tools\\ai-worker\\motion_tracker.py", project, StringComparison.Ordinal);
        Assert.Contains("'Tools\\ai-worker\\motion_tracker.py'", release, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoReframeCrop_RunsInRealWindowsFfmpeg()
    {
        if (!OperatingSystem.IsWindows()) return;

        var clip = new TimelineClip
        {
            SourceTrimInSeconds = 0,
            SourceTrimOutSeconds = 1,
            AutoReframeEnabled = true,
            MotionTrackingPoints = new List<MotionTrackingPoint>
            {
                new() { SourceTimeSeconds = 0, CenterX = 0.3, CenterY = 0.5 },
                new() { SourceTimeSeconds = 1, CenterX = 0.7, CenterY = 0.5 }
            }
        };
        var filter = FfmpegFilterGraphBuilder.BuildAutoReframeFilter(clip, 90, 160).TrimStart(',');
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in new[]
        {
            "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=10:duration=1",
            "-vf", filter,
            "-frames:v", "8", "-f", "null", "-"
        })
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi);
        Assert.NotNull(process);
        var stderr = process!.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, stderr + Environment.NewLine + filter);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NPVideoStudio.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
