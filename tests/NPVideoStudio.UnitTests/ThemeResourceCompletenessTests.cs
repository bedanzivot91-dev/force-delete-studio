using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Parses every Themes/*.axaml file as plain XML (no Avalonia runtime needed) and checks that all
/// semantic resource keys the app's Views actually bind to via DynamicResource are present in each -
/// a theme missing a key would silently render nothing at runtime instead of a build error.
/// </summary>
public class ThemeResourceCompletenessTests
{
    private static readonly string[] RequiredKeys =
    {
        "ThemeBackgroundBrush", "ThemeSurfaceBrush", "ThemePanelBrush", "ThemeHoverBrush",
        "ThemePressedBrush", "ThemeInputBrush", "ThemeAccentBrush", "ThemeAccentHoverBrush",
        "ThemeAccentSubtleBrush", "ThemeTextBrush", "ThemeSubtleTextBrush", "ThemeBorderBrush",
        "ThemeTimelineBrush", "ThemePlayerBrush",
        "ThemeCornerRadius", "ThemeCardCornerRadius", "ThemeBorderThickness"
    };

    private static readonly string[] ExpectedThemeFiles =
    {
        "Studio2026.axaml", "DarkCinematic.axaml", "MinimalLight.axaml", "ProfessionalStudio.axaml",
        "ObsidianNeon.axaml", "ArcticGlass.axaml", "CrimsonCyber.axaml", "MidnightPro.axaml", "OceanGlass.axaml"
    };

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NPVideoStudio.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException(
            "Nije pronađen koren repozitorijuma (NPVideoStudio.sln) polazeći od test binarnog fajla.");
    }

    private static string FindThemesFolder() =>
        Path.Combine(FindRepositoryRoot(), "src", "NPVideoStudio.App", "Themes");

    [Fact]
    public void EveryExpectedThemeFileExists_AndNoUntrackedThemeIsPresent()
    {
        var themesFolder = FindThemesFolder();
        var actual = Directory.GetFiles(themesFolder, "*.axaml").Select(Path.GetFileName).OrderBy(x => x).ToArray();

        Assert.Equal(ExpectedThemeFiles.OrderBy(x => x), actual);
    }

    [Theory]
    [MemberData(nameof(ThemeFileNames))]
    public void EveryTheme_DefinesAllRequiredSemanticKeys(string themeFileName)
    {
        var path = Path.Combine(FindThemesFolder(), themeFileName);
        var doc = XDocument.Load(path);
        var definedKeys = doc.Descendants()
            .Select(e => e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value)
            .Where(k => k is not null)
            .ToHashSet();

        var missing = RequiredKeys.Where(k => !definedKeys.Contains(k)).ToList();
        Assert.True(missing.Count == 0, $"{themeFileName} nedostaju ključevi: {string.Join(", ", missing)}");
    }

    [Fact]
    public void EveryDynamicResourceUsedByAppAndViews_ExistsInEveryTheme()
    {
        var root = FindRepositoryRoot();
        var appFolder = Path.Combine(root, "src", "NPVideoStudio.App");
        var sourceFiles = new[] { Path.Combine(appFolder, "App.axaml") }
            .Concat(Directory.GetFiles(Path.Combine(appFolder, "Views"), "*.axaml", SearchOption.AllDirectories));
        var resourceRegex = new Regex("\\{DynamicResource\\s+(?<key>[A-Za-z_][A-Za-z0-9_]*)\\}");
        var used = sourceFiles
            .SelectMany(path => resourceRegex.Matches(File.ReadAllText(path)).Cast<Match>())
            .Select(match => match.Groups["key"].Value)
            .ToHashSet();

        var failures = new List<string>();
        foreach (var themeFileName in ExpectedThemeFiles)
        {
            var path = Path.Combine(FindThemesFolder(), themeFileName);
            var doc = XDocument.Load(path);
            var keys = doc.Descendants()
                .Select(e => e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value)
                .Where(k => k is not null)
                .ToHashSet();
            var missing = used.Where(key => !keys.Contains(key)).OrderBy(key => key).ToArray();
            if (missing.Length > 0)
            {
                failures.Add($"{themeFileName}: {string.Join(", ", missing)}");
            }
        }

        Assert.True(failures.Count == 0,
            "DynamicResource coverage is incomplete: " + string.Join(" | ", failures));
    }

    public static IEnumerable<object[]> ThemeFileNames() => ExpectedThemeFiles.Select(f => new object[] { f });
}
