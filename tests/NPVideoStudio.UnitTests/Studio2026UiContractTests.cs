using Avalonia.Headless.XUnit;
using NPVideoStudio.App.Views;
using NPVideoStudio.Domain;
using Xunit;

namespace NPVideoStudio.UnitTests;

public class Studio2026UiContractTests
{
    [Fact]
    public void NewInstall_DefaultsToStudio2026()
    {
        Assert.Equal(AppTheme.Studio2026, new AppSettings().Theme);
    }

    [Fact]
    public void PersistedDarkCinematic_UsesSameModernVisualLanguage()
    {
        var root = FindRepositoryRoot();
        var legacy = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "Themes", "DarkCinematic.axaml"));
        var modern = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "Themes", "Studio2026.axaml"));

        foreach (var marker in new[] { "#090A0D", "#7C5CFC", "<CornerRadius x:Key=\"ThemeCardCornerRadius\">14</CornerRadius>" })
        {
            Assert.Contains(marker, legacy);
            Assert.Contains(marker, modern);
        }
    }

    [Fact]
    public void Workspace_ActivatesModernHeaderWorkflowBarAndTabbedInspector()
    {
        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "Views", "WorkspaceView.axaml.cs"));
        var inspector = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "Views", "ModernInspectorView.axaml"));
        var commandBar = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "Views", "ModernWorkspaceCommandBarView.axaml"));

        Assert.Contains("ProjectHeader.Child = new ModernWorkspaceHeaderView()", code);
        Assert.Contains("CaptionToolbar.Child = new ModernWorkspaceCommandBarView()", code);
        Assert.Contains("InspectorPanel.Child = new ModernInspectorView()", code);

        foreach (var tab in new[] { "Osnovno", "Tekst", "Video", "Audio", "Transform", "Overlay", "Animacija" })
        {
            Assert.Contains($"Header=\"{tab}\"", inspector);
        }

        foreach (var binding in new[]
        {
            "TrimInSeconds", "TrimOutSeconds", "TextContent", "FontChoice", "SpeedMultiplier",
            "RotationDegrees", "ScalePercent", "ChromaKeyEnabled", "MaskType", "BlendMode",
            "AddKeyframeAtPlayheadCommand"
        })
        {
            Assert.Contains(binding, inspector);
        }

        Assert.Contains("GenerateCaptionsForVideoCommand", commandBar);
        Assert.Contains("GenerateKaraokeCaptionsForVideoCommand", commandBar);
        Assert.Contains("SyncVerifiedLyricsCommand", commandBar);
        Assert.Contains("RenderRealPreviewAroundPlayheadCommand", commandBar);
    }

    [AvaloniaFact]
    public void ModernChromeViews_ConstructUnderHeadlessAvalonia()
    {
        Assert.NotNull(new ModernWorkspaceHeaderView());
        Assert.NotNull(new ModernWorkspaceCommandBarView());
        Assert.NotNull(new ModernInspectorView());
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NPVideoStudio.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
