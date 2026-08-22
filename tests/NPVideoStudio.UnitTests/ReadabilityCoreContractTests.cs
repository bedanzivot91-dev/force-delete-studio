using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace NPVideoStudio.UnitTests;

public class ReadabilityCoreContractTests
{
    [Fact]
    public void GlobalTypography_HasReadableMinimumsForSemanticTextStyles()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "App.axaml"));
        AssertStyleFontAtLeast(xaml, "TextBlock.subtle", 13.5);
        AssertStyleFontAtLeast(xaml, "TextBlock.micro", 12.5);
        AssertStyleFontAtLeast(xaml, "TextBlock.eyebrow", 13.0);
        AssertStyleFontAtLeast(xaml, "TextBlock.section", 16.0);
    }

    [Fact]
    public void SettingsThemeCount_IsDerivedFromRealEnumList_NotHardCodedRoadmapText()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "Views", "SettingsView.axaml"));
        var vm = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "ViewModels", "SettingsViewModel.cs"));
        Assert.Contains("AvailableThemeCount", view);
        Assert.Contains("AvailableThemeCount => AvailableThemes.Count", vm);
        Assert.DoesNotContain("3 od planiranih 10", view);
    }

    [Fact]
    public void EveryExplicitTextBlockFontSizeInApplicationXaml_IsAtLeastTwelvePointFive()
    {
        var root = FindRepositoryRoot();
        var appFolder = Path.Combine(root, "src", "NPVideoStudio.App");
        var themeSegment = $"{Path.DirectorySeparatorChar}Themes{Path.DirectorySeparatorChar}";
        var files = Directory.GetFiles(appFolder, "*.axaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains(themeSegment, StringComparison.OrdinalIgnoreCase));
        var regex = new Regex("<TextBlock\\b[^>]*?\\bFontSize=\\\"(?<size>[0-9.]+)\\\"", RegexOptions.Singleline);
        var failures = new List<string>();

        foreach (var path in files)
        {
            var xaml = File.ReadAllText(path);
            foreach (Match match in regex.Matches(xaml))
            {
                var size = double.Parse(match.Groups["size"].Value, CultureInfo.InvariantCulture);
                if (size < 12.5)
                {
                    failures.Add($"{Path.GetRelativePath(root, path)}: {size:0.##}px");
                }
            }
        }

        Assert.True(failures.Count == 0,
            "User-facing TextBlock declarations below 12.5px remain: " + string.Join(", ", failures));
    }

    [Fact]
    public void Views_DoNotUseLegacyHardcodedTimelineSurface()
    {
        var root = FindRepositoryRoot();
        var views = Path.Combine(root, "src", "NPVideoStudio.App", "Views");
        var failures = Directory.GetFiles(views, "*.axaml", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("Background=\"#1A1A1A\"", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        Assert.True(failures.Length == 0,
            "Legacy hardcoded #1A1A1A surface remains in: " + string.Join(", ", failures));
    }

    private static void AssertStyleFontAtLeast(string xaml, string selector, double minimum)
    {
        var match = Regex.Match(xaml, $"<Style Selector=\\\"{Regex.Escape(selector)}\\\">(?<body>.*?)</Style>", RegexOptions.Singleline);
        Assert.True(match.Success, $"Style {selector} missing.");
        var size = Regex.Match(match.Groups["body"].Value, "FontSize\\\" Value=\\\"(?<size>[0-9.]+)\\\"");
        Assert.True(size.Success, $"FontSize missing for {selector}.");
        Assert.True(double.Parse(size.Groups["size"].Value, CultureInfo.InvariantCulture) >= minimum,
            $"{selector} is below {minimum}px.");
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NPVideoStudio.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
