using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class SpeedCurveIntegrationTests
{
    [Fact]
    public void LinearCurve_UsesExactDurationAndInverseMapping()
    {
        var clip = LinearRampClip();
        var expected = 8.0 * Math.Log(2.0);

        Assert.Equal(expected, clip.TimelineDurationSeconds, 6);

        var halfOutput = expected / 2.0;
        var sourceAtHalf = SpeedCurveMath.SourceTimeAtTimelineOffset(clip, halfOutput);
        var outputToMappedSource = SpeedCurveMath.OutputDuration(
            clip.SourceTrimInSeconds,
            sourceAtHalf,
            clip.SpeedMultiplier,
            clip.SpeedCurvePoints,
            useCurve: true);

        Assert.Equal(halfOutput, outputToMappedSource, 6);
        Assert.InRange(sourceAtHalf, 3.30, 3.32);
    }

    [Fact]
    public void Preset_IsUndoRedoSafe_AndSplitDeepCopiesControlPoints()
    {
        var clip = new TimelineClip
        {
            Id = "curve",
            MediaAssetId = "media",
            SourceTrimInSeconds = 0,
            SourceTrimOutSeconds = 10
        };
        var session = SessionWith(clip);

        session.SetSpeedCurvePreset("curve", SpeedCurvePreset.Hero);
        var applied = SingleClip(session);
        Assert.Equal(SpeedCurvePreset.Hero, applied.SpeedCurvePreset);
        Assert.True(applied.SpeedCurvePoints.Count >= 2);

        session.Undo();
        Assert.Equal(SpeedCurvePreset.None, SingleClip(session).SpeedCurvePreset);
        Assert.Empty(SingleClip(session).SpeedCurvePoints);

        session.Redo();
        applied = SingleClip(session);
        Assert.Equal(SpeedCurvePreset.Hero, applied.SpeedCurvePreset);

        var splitAt = applied.TimelineDurationSeconds * 0.45;
        session.SplitClip("curve", splitAt);
        var split = session.Tracks.Single().Clips.OrderBy(c => c.TimelineStartSeconds).ToArray();
        Assert.Equal(2, split.Length);
        Assert.NotSame(split[0].SpeedCurvePoints, split[1].SpeedCurvePoints);
        Assert.All(split, c => Assert.Equal(SpeedCurvePreset.Hero, c.SpeedCurvePreset));
    }

    [Fact]
    public void Split_MapsTimelineOffsetThroughVariableVelocity()
    {
        var original = LinearRampClip();
        original.Id = "curve";
        original.MediaAssetId = "media";
        var offset = original.TimelineDurationSeconds * 0.5;
        var expectedSource = SpeedCurveMath.SourceTimeAtTimelineOffset(original, offset);
        var session = SessionWith(original);

        session.SplitClip("curve", offset);
        var clips = session.Tracks.Single().Clips.OrderBy(c => c.TimelineStartSeconds).ToArray();

        Assert.Equal(expectedSource, clips[0].SourceTrimOutSeconds, 6);
        Assert.Equal(expectedSource, clips[1].SourceTrimInSeconds, 6);
        Assert.NotEqual(4.0, expectedSource, 3); // proves this was not a constant-speed shortcut.
    }

    [Fact]
    public void TrimInAndOut_UseCurveDurationInsteadOfConstantSpeedShortcut()
    {
        var original = LinearRampClip();
        original.Id = "curve";
        original.MediaAssetId = "media";
        original.TimelineStartSeconds = 5;
        var expectedShift = SpeedCurveMath.OutputDurationBetween(original, 0, 2);
        var session = SessionWith(original);

        session.TrimIn("curve", 2);
        var afterIn = SingleClip(session);
        Assert.Equal(5 + expectedShift, afterIn.TimelineStartSeconds, 6);

        var expectedAfterOut = SpeedCurveMath.OutputDuration(
            2,
            6,
            afterIn.SpeedMultiplier,
            afterIn.SpeedCurvePoints,
            useCurve: true);
        session.TrimOut("curve", 6);
        Assert.Equal(expectedAfterOut, SingleClip(session).TimelineDurationSeconds, 6);
    }

    [Fact]
    public void StaticSpeedChange_DisablesCurve_ButEffectOnlyChangeKeepsIt()
    {
        var clip = new TimelineClip
        {
            Id = "curve",
            MediaAssetId = "media",
            SourceTrimInSeconds = 0,
            SourceTrimOutSeconds = 8
        };
        var session = SessionWith(clip);
        session.SetSpeedCurvePreset("curve", SpeedCurvePreset.Montage);

        session.SetClipEffects("curve", ClipVideoEffect.None, 0.1, 1, 1, 1);
        Assert.Equal(SpeedCurvePreset.Montage, SingleClip(session).SpeedCurvePreset);
        Assert.NotEmpty(SingleClip(session).SpeedCurvePoints);

        session.SetClipEffects("curve", ClipVideoEffect.None, 0.1, 1, 1, 2);
        Assert.Equal(SpeedCurvePreset.None, SingleClip(session).SpeedCurvePreset);
        Assert.Empty(SingleClip(session).SpeedCurvePoints);
        Assert.Equal(2, SingleClip(session).SpeedMultiplier, 6);
    }

    [Fact]
    public void Curve_PersistsThroughJsonAndRangeExtractionUsesInverseTiming()
    {
        var clip = LinearRampClip();
        clip.MediaAssetId = "media";
        var json = JsonSerializer.Serialize(clip);
        var loaded = JsonSerializer.Deserialize<TimelineClip>(json)!;

        Assert.Equal(SpeedCurvePreset.Montage, loaded.SpeedCurvePreset);
        Assert.Equal(2, loaded.SpeedCurvePoints.Count);

        var timeline = new Timeline
        {
            Tracks = new List<TimelineTrack>
            {
                new() { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { loaded } }
            }
        };
        var expectedIn = SpeedCurveMath.SourceTimeAtTimelineOffset(loaded, 1.0);
        var expectedOut = SpeedCurveMath.SourceTimeAtTimelineOffset(loaded, 3.0);
        var ranged = FfmpegFilterGraphBuilder.ExtractRangeTimeline(timeline, 1, 3)
            .Tracks.Single().Clips.Single();

        Assert.Equal(expectedIn, ranged.SourceTrimInSeconds, 6);
        Assert.Equal(expectedOut, ranged.SourceTrimOutSeconds, 6);
        Assert.Equal(SpeedCurvePreset.Montage, ranged.SpeedCurvePreset);
        Assert.NotSame(loaded.SpeedCurvePoints, ranged.SpeedCurvePoints);
    }

    [Fact]
    public void ActiveStudio2026Inspector_ExposesRealVelocityControls()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "Views", "ModernInspectorView.axaml"));

        Assert.Contains("ModernVideoSpeedCurve", xaml);
        Assert.Contains("ModernAudioSpeedCurve", xaml);
        Assert.Contains("AvailableSpeedCurvePresets", xaml);
        Assert.Contains("SelectedItem=\"{Binding SpeedCurvePreset}\"", xaml);
        Assert.Contains("CanUseSpeedCurve", xaml);
    }

    [Fact]
    public void RendererGraph_UsesCurveSetptsAndPitchPreservingRuntimeAudioTempo()
    {
        var clip = LinearRampClip();
        clip.MediaAssetId = "media";
        var asset = new MediaAsset { Id = "media", FilePath = "media.mp4", Kind = MediaKind.Video };
        var timeline = new Timeline
        {
            Tracks = new List<TimelineTrack>
            {
                new() { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } }
            }
        };

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { asset });

        Assert.Contains("setpts='if(", plan.FilterComplexArgument);
        Assert.Contains("asendcmd=c=", plan.FilterComplexArgument);
        Assert.Contains("rubberband@npvsspeed=tempo=", plan.FilterComplexArgument);
        Assert.Equal(clip.TimelineDurationSeconds, plan.TotalDurationSeconds, 6);
    }

    [Fact]
    public async Task RealFfmpeg_RendersVariableVelocityVideoAndAudioWithExpectedDuration()
    {
        // The required Windows CI installs FFmpeg before dotnet test. Other local/platform test runs are
        // allowed to skip this OS-specific executable proof; the Windows gate may not.
        if (!OperatingSystem.IsWindows()) return;

        var clip = new TimelineClip
        {
            MediaAssetId = "media",
            SourceTrimInSeconds = 0,
            SourceTrimOutSeconds = 4,
            SpeedCurvePreset = SpeedCurvePreset.FlashIn,
            SpeedCurvePoints = SpeedCurveMath.CreatePreset(SpeedCurvePreset.FlashIn, 0, 4)
        };
        var videoFilter = "setpts=PTS-STARTPTS" + FfmpegFilterGraphBuilder.BuildSpeedFilter(clip);
        var audioFilter = "asetpts=PTS-STARTPTS" + FfmpegFilterGraphBuilder.BuildAudioSpeedFilter(clip);
        var filterComplex = $"[0:v]{videoFilter}[v];[1:a]{audioFilter}[a]";
        var temp = Path.Combine(Path.GetTempPath(), $"npvs-speed-{Guid.NewGuid():N}.mp4");

        try
        {
            var ffmpeg = await RunAsync("ffmpeg", new[]
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=30:duration=4",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=4",
                "-filter_complex", filterComplex,
                "-map", "[v]", "-map", "[a]",
                "-c:v", "mpeg4", "-q:v", "5", "-c:a", "aac", "-shortest", temp
            });
            Assert.True(ffmpeg.ExitCode == 0, $"Real FFmpeg velocity render failed:\n{ffmpeg.Error}\n{ffmpeg.Output}\nFilter: {filterComplex}");
            Assert.True(File.Exists(temp) && new FileInfo(temp).Length > 1000, "FFmpeg did not produce a usable output file.");

            var streams = await RunAsync("ffprobe", new[]
            {
                "-v", "error", "-show_entries", "stream=codec_type", "-of", "csv=p=0", temp
            });
            Assert.True(streams.ExitCode == 0, streams.Error);
            Assert.Contains("video", streams.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("audio", streams.Output, StringComparison.OrdinalIgnoreCase);

            var durationProbe = await RunAsync("ffprobe", new[]
            {
                "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", temp
            });
            Assert.True(durationProbe.ExitCode == 0, durationProbe.Error);
            Assert.True(double.TryParse(durationProbe.Output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var actualDuration),
                $"Could not parse ffprobe duration: {durationProbe.Output}");
            Assert.InRange(actualDuration, clip.TimelineDurationSeconds - 0.45, clip.TimelineDurationSeconds + 0.45);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static TimelineClip LinearRampClip() => new()
    {
        SourceTrimInSeconds = 0,
        SourceTrimOutSeconds = 8,
        SpeedMultiplier = 1,
        SpeedCurvePreset = SpeedCurvePreset.Montage,
        SpeedCurvePoints = new List<SpeedCurvePoint>
        {
            new() { SourceTimeSeconds = 0, SpeedMultiplier = 1 },
            new() { SourceTimeSeconds = 8, SpeedMultiplier = 2 }
        }
    };

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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await outputTask, await errorTask);
    }
}
