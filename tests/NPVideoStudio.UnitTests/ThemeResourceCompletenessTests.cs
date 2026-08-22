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
        "ThemeCornerRadius", "ThemeCardCornerRadius", "ThemeBorderThickness"
    };

    private static readonly string[] ExpectedThemeFiles =
    {
        "Studio2026.axaml", "DarkCinematic.axaml", "MinimalLight.axaml", "ProfessionalStudio.axaml",
        "ObsidianNeon.axaml", "ArcticGlass.axaml", "CrimsonCyber.axaml", "MidnightPro.axaml", "OceanGlass.axaml"
    };

    private static string FindThemesFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NPVideoStudio.sln")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Nije pronađen koren repozitorijuma (NPVideoStudio.sln) polazeći od test binarnog fajla.");
        }

        return Path.Combine(dir.FullName, "src", "NPVideoStudio.App", "Themes");
    }

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

    public static IEnumerable<object[]> ThemeFileNames() => ExpectedThemeFiles.Select(f => new object[] { f });
}
