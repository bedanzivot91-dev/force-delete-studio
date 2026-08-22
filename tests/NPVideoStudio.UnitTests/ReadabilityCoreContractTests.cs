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

    private static void AssertStyleFontAtLeast(string xaml, string selector, double minimum)
    {
        var match = Regex.Match(xaml, $"<Style Selector=\\\"{Regex.Escape(selector)}\\\">(?<body>.*?)</Style>", RegexOptions.Singleline);
        Assert.True(match.Success, $"Style {selector} missing.");
        var size = Regex.Match(match.Groups["body"].Value, "FontSize\\\" Value=\\\"(?<size>[0-9.]+)\\\"");
        Assert.True(size.Success, $"FontSize missing for {selector}.");
        Assert.True(double.Parse(size.Groups["size"].Value, System.Globalization.CultureInfo.InvariantCulture) >= minimum,
  $"{selector} is below {minimum}px.");
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NPVideoStudio.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
