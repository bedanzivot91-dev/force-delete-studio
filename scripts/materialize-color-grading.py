from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    if old not in text:
        raise SystemExit(f"Anchor not found in {path}: {old[:120]!r}")
    text = text.replace(old, new, 1)
    p.write_text(text, encoding="utf-8")


# Domain: persist a grouped professional color state on every picture clip.
replace_once(
    "src/NPVideoStudio.Domain/Timeline.cs",
    "public readonly record struct ClipStabilizationSettings(\n    bool Enabled,\n    int Shakiness,\n    int Accuracy,\n    int Smoothing,\n    double ZoomPercent,\n    int OptimalZoom);\n",
    "public readonly record struct ClipStabilizationSettings(\n    bool Enabled,\n    int Shakiness,\n    int Accuracy,\n    int Smoothing,\n    double ZoomPercent,\n    int OptimalZoom);\n\n/// <summary>Professional per-clip tone/white-balance controls. Neutral values are all zero.\n/// The renderer maps them to real FFmpeg LUT/curves/colorbalance filters; this is persisted project data,\n/// not preview-only UI state.</summary>\npublic readonly record struct ClipColorGradingSettings(\n    double ExposureStops,\n    double Highlights,\n    double Shadows,\n    double Temperature,\n    double Tint);\n"
)
replace_once(
    "src/NPVideoStudio.Domain/Timeline.cs",
    "    /// <summary>Manual colour saturation, 0..3, 1 = unchanged. 0 is fully grey.</summary>\n    public double Saturation { get; set; } = 1.0;\n\n    /// <summary>Playback speed, 0.25..4. Used when no velocity curve is active.</summary>",
    "    /// <summary>Manual colour saturation, 0..3, 1 = unchanged. 0 is fully grey.</summary>\n    public double Saturation { get; set; } = 1.0;\n\n    // --- Advanced color grading -----------------------------------------------------------------\n    /// <summary>Exposure compensation in stops, -3..+3. Zero is neutral.</summary>\n    public double ExposureStops { get; set; }\n    /// <summary>Upper-tone curve adjustment, -1..+1. Zero is neutral.</summary>\n    public double Highlights { get; set; }\n    /// <summary>Lower-tone curve adjustment, -1..+1. Zero is neutral.</summary>\n    public double Shadows { get; set; }\n    /// <summary>White-balance temperature, -1 (cool) .. +1 (warm). Zero is neutral.</summary>\n    public double Temperature { get; set; }\n    /// <summary>White-balance tint, -1 (green) .. +1 (magenta). Zero is neutral.</summary>\n    public double Tint { get; set; }\n\n    /// <summary>Playback speed, 0.25..4. Used when no velocity curve is active.</summary>"
)

# Undo-safe session setter and deep clone.
replace_once(
    "src/NPVideoStudio.AI/TimelineEditSession.cs",
    "    public void SetSpeedCurvePreset(string clipId, SpeedCurvePreset preset)\n",
    "    public void SetColorGrading(string clipId, ClipColorGradingSettings settings)\n    {\n        var (_, clip) = FindClipWithTrack(clipId);\n        if (clip is null) return;\n\n        var exposure = Math.Clamp(settings.ExposureStops, -3, 3);\n        var highlights = Math.Clamp(settings.Highlights, -1, 1);\n        var shadows = Math.Clamp(settings.Shadows, -1, 1);\n        var temperature = Math.Clamp(settings.Temperature, -1, 1);\n        var tint = Math.Clamp(settings.Tint, -1, 1);\n        if (Math.Abs(clip.ExposureStops - exposure) < 1e-9 &&\n            Math.Abs(clip.Highlights - highlights) < 1e-9 &&\n            Math.Abs(clip.Shadows - shadows) < 1e-9 &&\n            Math.Abs(clip.Temperature - temperature) < 1e-9 &&\n            Math.Abs(clip.Tint - tint) < 1e-9) return;\n\n        SaveSnapshot();\n        var live = FindClipWithTrack(clipId).Clip!;\n        live.ExposureStops = exposure;\n        live.Highlights = highlights;\n        live.Shadows = shadows;\n        live.Temperature = temperature;\n        live.Tint = tint;\n    }\n\n    public void SetSpeedCurvePreset(string clipId, SpeedCurvePreset preset)\n"
)
replace_once(
    "src/NPVideoStudio.AI/TimelineEditSession.cs",
    "        Saturation = clip.Saturation,\n        SpeedMultiplier = clip.SpeedMultiplier,",
    "        Saturation = clip.Saturation,\n        ExposureStops = clip.ExposureStops,\n        Highlights = clip.Highlights,\n        Shadows = clip.Shadows,\n        Temperature = clip.Temperature,\n        Tint = clip.Tint,\n        SpeedMultiplier = clip.SpeedMultiplier,"
)

# Renderer: copy fields into range preview clones and append real grading filters after legacy look controls.
replace_once(
    "src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs",
    "        Saturation = clip.Saturation,\n        SpeedMultiplier = clip.SpeedMultiplier,",
    "        Saturation = clip.Saturation,\n        ExposureStops = clip.ExposureStops,\n        Highlights = clip.Highlights,\n        Shadows = clip.Shadows,\n        Temperature = clip.Temperature,\n        Tint = clip.Tint,\n        SpeedMultiplier = clip.SpeedMultiplier,"
)
replace_once(
    "src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs",
    "        if (Math.Abs(brightness) > 1e-6 || Math.Abs(contrast - 1) > 1e-6 || Math.Abs(saturation - 1) > 1e-6)\n        {\n            parts.Add(FormattableString.Invariant(\n                $\"eq=brightness={brightness}:contrast={contrast}:saturation={saturation}\"));\n        }\n\n        return parts.Count == 0 ? string.Empty : \",\" + string.Join(\",\", parts);",
    "        if (Math.Abs(brightness) > 1e-6 || Math.Abs(contrast - 1) > 1e-6 || Math.Abs(saturation - 1) > 1e-6)\n        {\n            parts.Add(FormattableString.Invariant(\n                $\"eq=brightness={brightness}:contrast={contrast}:saturation={saturation}\"));\n        }\n\n        // Advanced grading is intentionally composed from standard FFmpeg filters available in the\n        // bundled Windows build. Exposure is a real per-channel luminance multiplier; highlights/shadows\n        // use a monotonic five-point tone curve; temperature/tint use colorbalance while preserving lightness.\n        var exposure = Math.Clamp(clip.ExposureStops, -3, 3);\n        if (Math.Abs(exposure) > 1e-6)\n        {\n            var factor = Math.Pow(2, exposure);\n            parts.Add(FormattableString.Invariant(\n                $\"lutrgb=r='val*{factor}':g='val*{factor}':b='val*{factor}'\"));\n        }\n\n        var shadows = Math.Clamp(clip.Shadows, -1, 1);\n        var highlights = Math.Clamp(clip.Highlights, -1, 1);\n        if (Math.Abs(shadows) > 1e-6 || Math.Abs(highlights) > 1e-6)\n        {\n            var shadowY = Math.Clamp(0.25 + shadows * 0.18, 0.02, 0.48);\n            var highlightY = Math.Clamp(0.75 + highlights * 0.18, 0.52, 0.98);\n            parts.Add(FormattableString.Invariant(\n                $\"curves=all='0/0 0.25/{shadowY} 0.5/0.5 0.75/{highlightY} 1/1'\"));\n        }\n\n        var temperature = Math.Clamp(clip.Temperature, -1, 1);\n        var tint = Math.Clamp(clip.Tint, -1, 1);\n        if (Math.Abs(temperature) > 1e-6 || Math.Abs(tint) > 1e-6)\n        {\n            var redShadows = temperature * 0.25;\n            var blueShadows = -temperature * 0.25;\n            var redMid = tint * 0.10;\n            var greenMid = -tint * 0.20;\n            var blueMid = tint * 0.10;\n            parts.Add(FormattableString.Invariant(\n                $\"colorbalance=rs={redShadows}:bs={blueShadows}:rm={redMid}:gm={greenMid}:bm={blueMid}:pl=1\"));\n        }\n\n        return parts.Count == 0 ? string.Empty : \",\" + string.Join(\",\", parts);"
)

# Clip VM: add one grouped callback and bindable properties.
replace_once(
    "src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs",
    "    private readonly Action<string, ClipVideoEffect, double, double, double, double>? _onEffectsChanged;\n",
    "    private readonly Action<string, ClipVideoEffect, double, double, double, double>? _onEffectsChanged;\n    private readonly Action<string, ClipColorGradingSettings>? _onColorGradingChanged;\n"
)
replace_once(
    "src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs",
    "    /// <summary>0.5 = slow motion, 2 = double speed. Changing it explicitly disables a velocity curve.</summary>\n    public double SpeedMultiplier\n",
    "    private void PushColorGrading(Func<ClipColorGradingSettings, ClipColorGradingSettings> mutate)\n    {\n        var current = new ClipColorGradingSettings(ExposureStops, Highlights, Shadows, Temperature, Tint);\n        _onColorGradingChanged?.Invoke(Clip.Id, mutate(current));\n    }\n\n    public double ExposureStops\n    {\n        get => Clip.ExposureStops;\n        set { if (Math.Abs(Clip.ExposureStops - value) < 1e-6) return; PushColorGrading(s => s with { ExposureStops = value }); }\n    }\n    public double Highlights\n    {\n        get => Clip.Highlights;\n        set { if (Math.Abs(Clip.Highlights - value) < 1e-6) return; PushColorGrading(s => s with { Highlights = value }); }\n    }\n    public double Shadows\n    {\n        get => Clip.Shadows;\n        set { if (Math.Abs(Clip.Shadows - value) < 1e-6) return; PushColorGrading(s => s with { Shadows = value }); }\n    }\n    public double Temperature\n    {\n        get => Clip.Temperature;\n        set { if (Math.Abs(Clip.Temperature - value) < 1e-6) return; PushColorGrading(s => s with { Temperature = value }); }\n    }\n    public double Tint\n    {\n        get => Clip.Tint;\n        set { if (Math.Abs(Clip.Tint - value) < 1e-6) return; PushColorGrading(s => s with { Tint = value }); }\n    }\n\n    /// <summary>0.5 = slow motion, 2 = double speed. Changing it explicitly disables a velocity curve.</summary>\n    public double SpeedMultiplier\n"
)
replace_once(
    "src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs",
    "        Action<string, MotionTrackingRegion>? onMotionTrackingRequested = null,\n        Action<string, bool>? onAutoReframeChanged = null)\n",
    "        Action<string, MotionTrackingRegion>? onMotionTrackingRequested = null,\n        Action<string, bool>? onAutoReframeChanged = null,\n        Action<string, ClipColorGradingSettings>? onColorGradingChanged = null)\n"
)
replace_once(
    "src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs",
    "        _onEffectsChanged = onEffectsChanged;\n",
    "        _onEffectsChanged = onEffectsChanged;\n        _onColorGradingChanged = onColorGradingChanged;\n"
)

# Timeline VM wires UI callback through TimelineEditSession.
replace_once(
    "src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs",
    "        void OnSpeedCurvePresetChanged(string clipId, SpeedCurvePreset preset)\n",
    "        void OnColorGradingChanged(string clipId, ClipColorGradingSettings settings)\n        {\n            _session.SetColorGrading(clipId, settings);\n            RefreshFromSession();\n        }\n        void OnSpeedCurvePresetChanged(string clipId, SpeedCurvePreset preset)\n"
)
replace_once(
    "src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs",
    "            OnTrackingRegionChanged, OnMotionTrackingRequested, OnAutoReframeChanged)\n",
    "            OnTrackingRegionChanged, OnMotionTrackingRequested, OnAutoReframeChanged, OnColorGradingChanged)\n"
)

# Active Studio 2026 inspector: separate Boja tab to avoid a giant Video control wall.
replace_once(
    "src/NPVideoStudio.App/Views/ModernInspectorView.axaml",
    "      <TabItem Header=\"Audio\" IsVisible=\"{Binding IsAudioClip}\">",
    "      <TabItem Header=\"Boja\" IsVisible=\"{Binding IsPictureClip}\">\n        <ScrollViewer>\n          <StackPanel Spacing=\"10\" Margin=\"2,10,2,2\">\n            <Border Classes=\"inspectorSection\">\n              <StackPanel Spacing=\"8\">\n                <TextBlock Text=\"Ton\" Classes=\"section\" />\n                <TextBlock Text=\"Exposure (stops)\" Classes=\"subtle\"/>\n                <Slider Minimum=\"-3\" Maximum=\"3\" Value=\"{Binding ExposureStops}\" TickFrequency=\"0.1\"/>\n                <Grid ColumnDefinitions=\"*,*\">\n                  <StackPanel Spacing=\"4\"><TextBlock Text=\"Highlights\" Classes=\"subtle\"/><Slider Minimum=\"-1\" Maximum=\"1\" Value=\"{Binding Highlights}\"/></StackPanel>\n                  <StackPanel Grid.Column=\"1\" Spacing=\"4\" Margin=\"8,0,0,0\"><TextBlock Text=\"Shadows\" Classes=\"subtle\"/><Slider Minimum=\"-1\" Maximum=\"1\" Value=\"{Binding Shadows}\"/></StackPanel>\n                </Grid>\n              </StackPanel>\n            </Border>\n            <Border Classes=\"inspectorSection\">\n              <StackPanel Spacing=\"8\">\n                <TextBlock Text=\"White Balance\" Classes=\"section\" />\n                <TextBlock Text=\"Temperature  •  hladno ↔ toplo\" Classes=\"subtle\"/>\n                <Slider Minimum=\"-1\" Maximum=\"1\" Value=\"{Binding Temperature}\"/>\n                <TextBlock Text=\"Tint  •  zeleno ↔ magenta\" Classes=\"subtle\"/>\n                <Slider Minimum=\"-1\" Maximum=\"1\" Value=\"{Binding Tint}\"/>\n                <TextBlock Text=\"Neutralno je 0. Sve vrednosti se čuvaju u projektu i ulaze u preview/export preko FFmpeg-a.\" Classes=\"subtle\" TextWrapping=\"Wrap\"/>\n              </StackPanel>\n            </Border>\n          </StackPanel>\n        </ScrollViewer>\n      </TabItem>\n\n      <TabItem Header=\"Audio\" IsVisible=\"{Binding IsAudioClip}\">"
)

# Tests: undo/redo, persistence, active UI contract and real FFmpeg syntax/execution.
Path("tests/NPVideoStudio.UnitTests/AdvancedColorGradingTests.cs").write_text(r'''using System.Diagnostics;
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

        session.SetColorGrading(clip.Id, new ClipColorGradingSettings(9, .4, -.3, .5, -.6));
        var edited = session.Tracks.Single().Clips.Single();
        Assert.Equal(3, edited.ExposureStops);
        Assert.Equal(.4, edited.Highlights, 6);
        Assert.Equal(-.3, edited.Shadows, 6);
        Assert.Equal(.5, edited.Temperature, 6);
        Assert.Equal(-.6, edited.Tint, 6);

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
                    new() { MediaAssetId = "m", SourceTrimOutSeconds = 1, ExposureStops = .75, Highlights = .2, Shadows = -.15, Temperature = .3, Tint = -.25 }
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
            Tint = -.3
        };
        var filters = FfmpegFilterGraphBuilder.BuildEffectFilters(clip);
        Assert.Contains("lutrgb=", filters);
        Assert.Contains("curves=all=", filters);
        Assert.Contains("colorbalance=", filters);
    }

    [Fact]
    public void Studio2026Inspector_ExposesRealColorBindings()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "Views", "ModernInspectorView.axaml"));
        Assert.Contains("Header=\"Boja\"", xaml);
        foreach (var binding in new[] { "ExposureStops", "Highlights", "Shadows", "Temperature", "Tint" })
            Assert.Contains($"Binding {binding}", xaml);
    }

    [Fact]
    public async Task RealFfmpeg_ExecutesAdvancedColorFilters()
    {
        var ffmpeg = FfmpegLocator.ResolveFfmpegPath(null);
        var dir = Path.Combine(Path.GetTempPath(), "npvs-grade-ffmpeg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var output = Path.Combine(dir, "graded.mp4");
            var clip = new TimelineClip { ExposureStops = .5, Highlights = .2, Shadows = -.1, Temperature = .25, Tint = -.15 };
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
''', encoding="utf-8")

# This materializer is one-shot and must not remain in the final feature diff.
for helper in [
    Path("scripts/materialize-color-grading.py"),
    Path("scripts/run-color-grading.trigger"),
    Path(".github/workflows/materialize-color-grading.yml"),
]:
    if helper.exists():
        helper.unlink()
