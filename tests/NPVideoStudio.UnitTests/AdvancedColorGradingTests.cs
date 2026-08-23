using System.Diagnostics;
using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using NPVideoStudio.Infrastructure.Persistence;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class AdvancedColorGradingTests
{
    [Fact]
    public void ColorGrading_IsUndoRedoSafeAndClamped()
    {
        var clip = new TimelineClip { MediaAssetId = "m", SourceTrimOutSeconds = 2 };
        var session = new TimelineEditSession(new[]
        {
            new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } }
        });

        session.SetColorGrading(clip.Id, new ClipColorGradingSettings(9, .4, -.3, .5, -.6, 240, 2));
        var edited = session.Tracks.Single().Clips.Single();
        Assert.Equal(3, edited.ExposureStops);
        Assert.Equal(.4, edited.Highlights, 6);
        Assert.Equal(-.3, edited.Shadows, 6);
        Assert.Equal(.5, edited.Temperature, 6);
        Assert.Equal(-.6, edited.Tint, 6);
        Assert.Equal(180, edited.HueDegrees, 6);
        Assert.Equal(1, edited.Vibrance, 6);

        session.Undo();
        Assert.Equal(0, session.Tracks.Single().Clips.Single().ExposureStops);
        session.Redo();
        Assert.Equal(3, session.Tracks.Single().Clips.Single().ExposureStops);
    }

    [Fact]
    public async Task ColorGrading_RoundTripsThroughRealProjectRepository()
    {
        var dir = Path.Combine(Path.GetTempPath(), "npvs-color-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "grade.npvsproject");
            var project = new Project { Name = "Grade" };
            project.Timeline.Tracks.Add(new TimelineTrack
            {
                Kind = TimelineTrackKind.Video,
                Clips = new List<TimelineClip>
                {
                    new() { MediaAssetId = "m", SourceTrimOutSeconds = 1, ExposureStops = .75, Highlights = .2, Shadows = -.15, Temperature = .3, Tint = -.25, HueDegrees = 35, Vibrance = .4 }
                }
            });
            var repo = new ProjectRepository();
            await repo.SaveAsync(project, path);
            var loaded = await repo.LoadAsync(path);
            var clip = loaded.Timeline.Tracks.Single().Clips.Single();
            Assert.Equal(.75, clip.ExposureStops, 6);
            Assert.Equal(.2, clip.Highlights, 6);
            Assert.Equal(-.15, clip.Shadows, 6);
            Assert.Equal(.3, clip.Temperature, 6);
            Assert.Equal(-.25, clip.Tint, 6);
            Assert.Equal(35, clip.HueDegrees, 6);
            Assert.Equal(.4, clip.Vibrance, 6);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ColorGrading_EmitsExposureCurvesAndWhiteBalanceFilters()
    {
        var clip = new TimelineClip
        {
            ExposureStops = 1,
            Highlights = .25,
            Shadows = -.2,
            Temperature = .4,
            Tint = -.3,
            HueDegrees = 24,
            Vibrance = .35
        };
        var filters = FfmpegFilterGraphBuilder.BuildEffectFilters(clip);
        Assert.Contains("lutrgb=", filters);
        Assert.Contains("curves=all=", filters);
        Assert.Contains("colorbalance=", filters);
        Assert.Contains("hue=h=24", filters);
        Assert.Contains("vibrance=intensity=0.35", filters);
    }

    [Fact]
    public void Studio2026Inspector_ExposesRealColorBindings()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "Views", "ModernInspectorView.axaml"));
        Assert.Contains("Header=\"Boja\"", xaml);
        foreach (var binding in new[] { "ExposureStops", "Highlights", "Shadows", "Temperature", "Tint", "HueDegrees", "Vibrance" })
            Assert.Contains($"Binding {binding}", xaml);
    }

    [Fact]
    public void ThreeWayColorWheels_ArePersistedClampedAndRendered()
    {
        var clip = new TimelineClip { MediaAssetId = "m", SourceTrimOutSeconds = 2 };
        var session = new TimelineEditSession(new[] { new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } } });
        session.SetColorGrading(clip.Id, new ClipColorGradingSettings(0, 0, 0, 0, 0, 0, 0,
            2, -.2, .3, .4, -.5, .6, -.7, .8, -2));
        var edited = session.Tracks.Single().Clips.Single();
        Assert.Equal(1, edited.ShadowRed, 6);
        Assert.Equal(-1, edited.HighlightBlue, 6);
        var filters = FfmpegFilterGraphBuilder.BuildEffectFilters(edited);
        Assert.Contains("colorbalance=rs=1", filters);
        Assert.Contains(":rm=0.4", filters);
        Assert.Contains(":rh=-0.7", filters);

        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "Views", "ModernInspectorView.axaml"));
        foreach (var binding in new[] { "ShadowRed", "ShadowGreen", "ShadowBlue", "MidtoneRed", "MidtoneGreen", "MidtoneBlue", "HighlightRed", "HighlightGreen", "HighlightBlue" })
            Assert.Contains($"Binding {binding}", xaml);
    }

    [Fact]
    public void CubeLut_IsUndoableValidatedAndRendered()
    {
        var cube = Path.Combine(_tempLutDirectory.Value, "look.cube");
        File.WriteAllText(cube, "TITLE \"Identity\"\nLUT_3D_SIZE 2\n0 0 0\n0 0 1\n0 1 0\n0 1 1\n1 0 0\n1 0 1\n1 1 0\n1 1 1\n");
        var clip = new TimelineClip { Id = "lut", MediaAssetId = "m", SourceTrimOutSeconds = 1 };
        var session = new TimelineEditSession(new[] { new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } } });
        session.SetClipLut("lut", cube);
        var edited = session.Tracks.Single().Clips.Single();
        Assert.Equal(Path.GetFullPath(cube), edited.LutFilePath);
        Assert.Contains("lut3d=file=", FfmpegFilterGraphBuilder.BuildEffectFilters(edited));
        session.Undo();
        Assert.Null(session.Tracks.Single().Clips.Single().LutFilePath);
    }

    private static readonly Lazy<string> _tempLutDirectory = new(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), "npvs-lut-tests");
        Directory.CreateDirectory(path);
        return path;
    });

    [Fact]
    public async Task RealFfmpeg_ExecutesAdvancedColorFilters()
    {
        var ffmpeg = FfmpegLocator.ResolveFfmpegPath(null);
        var dir = Path.Combine(Path.GetTempPath(), "npvs-grade-ffmpeg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var output = Path.Combine(dir, "graded.mp4");
            var clip = new TimelineClip { ExposureStops = .5, Highlights = .2, Shadows = -.1, Temperature = .25, Tint = -.15, HueDegrees = 18, Vibrance = .2 };
            var filter = FfmpegFilterGraphBuilder.BuildEffectFilters(clip).TrimStart(',');
            using var process = new Process { StartInfo = new ProcessStartInfo { FileName = ffmpeg, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true } };
            foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=320x180:r=30:d=0.5", "-vf", filter, "-c:v", "libx264", "-pix_fmt", "yuv420p", output })
                process.StartInfo.ArgumentList.Add(arg);
            Assert.True(process.Start());
            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stderr = await stderrTask;
            _ = await stdoutTask;
            Assert.True(process.ExitCode == 0, stderr);
            Assert.True(File.Exists(output) && new FileInfo(output).Length > 1000);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NPVideoStudio.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("NPVideoStudio.sln nije pronađen iz test output foldera.");
    }
}
