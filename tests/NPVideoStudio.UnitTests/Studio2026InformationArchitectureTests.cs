using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class Studio2026InformationArchitectureTests
{
    [Fact]
    public void ModernViews_DoNotUseUnreadablySmallExplicitText()
    {
        var root = FindRepositoryRoot();
        var views = Path.Combine(root, "src", "NPVideoStudio.App", "Views");
        var files = Directory.GetFiles(views, "Modern*.axaml");
        Assert.NotEmpty(files);

        var failures = new List<string>();
        var rx = new Regex("<TextBlock\\b[^>]*?FontSize=\"([0-9]+(?:\\.[0-9]+)?)\"", RegexOptions.CultureInvariant);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (Match match in rx.Matches(text))
            {
                var size = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                if (size < 12.5)
                    failures.Add($"{Path.GetFileName(file)}: FontSize={size}");
            }
        }

        Assert.True(failures.Count == 0, "Unreadable Studio 2026 text: " + string.Join(", ", failures));
    }

    [Fact]
    public void StartScreen_IsGroupedByUserWorkflow_NotOneFlatToolsDump()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "src", "NPVideoStudio.App", "Views", "StartScreenView.axaml");
        var text = File.ReadAllText(path);

        foreach (var group in new[]
        {
            "PROJEKTI I FORMATI",
            "TEKST, TITLOVI I AI",
            "PESME I YOUTUBE",
            "BRZI WORKFLOW I ŠABLONI",
            "SISTEM"
        })
            Assert.Contains(group, text);

        Assert.DoesNotContain("<TextBlock Text=\"Alati\" Classes=\"section\"", text);
        Assert.DoesNotContain("PlannedFeatures", text);

        foreach (var command in new[]
        {
            "NewProjectCommand", "OpenProjectCommand", "NewYouTubeVideoCommand", "NewYouTubeShortsCommand",
            "NewTikTokCommand", "NewInstagramReelCommand", "NewFacebookReelCommand",
            "AddTextToVideoCommand", "OpenSubtitleGeneratorCommand", "OpenCaptionEditorCommand",
            "OpenCaptionStyleGalleryCommand", "OpenLyricSearchCommand", "OpenVideoLayoutAnalyzerCommand",
            "OpenMySongsCommand", "OpenSongHighlightsCommand", "OpenYouTubeDownloadCommand",
            "OpenTemplateGalleryCommand", "OpenQuickVideoCommand", "OpenQuickVideoWithCaptionsCommand",
            "OpenSettingsCommand", "OpenDiagnosticsCommand", "OpenDependencyManagerCommand"
        })
            Assert.Contains(command, text);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NPVideoStudio.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
