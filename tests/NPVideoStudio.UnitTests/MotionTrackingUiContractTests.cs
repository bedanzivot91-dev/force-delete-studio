using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class MotionTrackingUiContractTests
{
    [Fact]
    public void RealWorkspaceConstruction_InjectsMotionTrackingService()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "App.axaml.cs"));
        var main = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "ViewModels", "MainWindowViewModel.cs"));
        var workspace = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "ViewModels", "WorkspaceViewModel.cs"));

        Assert.Contains("AddSingleton<IMotionTrackingService, MotionTrackingService>()", app);
        Assert.Contains("GetRequiredService<IMotionTrackingService>()", main);
        Assert.Contains("TrackMotionAndEnableReframeAsync", workspace);
        Assert.Contains("Timeline.ApplyMotionTrackingResult", workspace);
        Assert.Contains("_projectRepository.SaveAsync", workspace);
    }

    [Fact]
    public void StudioInspector_ExposesRoiTrackAndAutoReframeBindings()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "Views", "ModernInspectorView.axaml"));
        foreach (var marker in new[]
        {
            "ModernMotionTrackingPanel", "TrackingCenterXPercent", "TrackingCenterYPercent",
            "TrackingWidthPercent", "TrackingHeightPercent", "TrackMotionCommand", "AutoReframeEnabled",
            "MotionTrackingSummary"
        })
        {
            Assert.Contains(marker, xaml);
        }
    }

    [Fact]
    public void FullAndRangeRenderPaths_PreserveAndApplyAutoReframe()
    {
        var root = FindRepositoryRoot();
        var renderer = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.Media", "FfmpegFilterGraphBuilder.cs"));
        Assert.True(Count(renderer, "BuildAutoReframeFilter(clip, targetWidth, targetHeight)") >= 2,
            "Base video and overlay-video render paths must both apply Auto Reframe.");
        Assert.Contains("MotionTrackingPoints = clip.MotionTrackingPoints.Select", renderer);
        Assert.Contains("AutoReframeEnabled = clip.AutoReframeEnabled", renderer);
    }

    [Fact]
    public void CapabilityCheck_RequiresRealCsrtNotOnlyCv2Import()
    {
        var root = FindRepositoryRoot();
        var worker = File.ReadAllText(Path.Combine(root, "ai-worker", "ai_worker.py"));
        Assert.Contains("def check_opencv_tracking", worker);
        Assert.Contains("TrackerCSRT_create", worker);
        Assert.Contains("check_opencv_tracking()", worker);
    }

    private static int Count(string text, string marker)
    {
        var count = 0;
        var start = 0;
        while ((start = text.IndexOf(marker, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += marker.Length;
        }
        return count;
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NPVideoStudio.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
