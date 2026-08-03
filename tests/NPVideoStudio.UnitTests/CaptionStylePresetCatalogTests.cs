using NPVideoStudio.App.Services;
using NPVideoStudio.Domain;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Plain data checks, no Avalonia runtime involved - catches a missing theme/typo before it silently renders nothing.</summary>
public class CaptionStylePresetCatalogTests
{
    [Fact]
    public void All_HasExactlyTwentyFourPresets()
    {
        Assert.Equal(24, CaptionStylePresetCatalog.All.Count);
    }

    [Theory]
    [InlineData(AppTheme.DarkCinematic)]
    [InlineData(AppTheme.MinimalLight)]
    [InlineData(AppTheme.ProfessionalStudio)]
    [InlineData(AppTheme.ObsidianNeon)]
    [InlineData(AppTheme.ArcticGlass)]
    [InlineData(AppTheme.CrimsonCyber)]
    [InlineData(AppTheme.MidnightPro)]
    [InlineData(AppTheme.OceanGlass)]
    public void ForTheme_EveryThemeHasAtLeastThreePresets(AppTheme theme)
    {
        var presets = CaptionStylePresetCatalog.ForTheme(theme);

        Assert.True(presets.Count >= 3, $"{theme} has only {presets.Count} presets.");
        Assert.All(presets, p => Assert.Equal(theme, p.Theme));
    }

    [Theory]
    [InlineData(AppTheme.DarkCinematic)]
    [InlineData(AppTheme.MinimalLight)]
    [InlineData(AppTheme.ProfessionalStudio)]
    [InlineData(AppTheme.ObsidianNeon)]
    [InlineData(AppTheme.ArcticGlass)]
    [InlineData(AppTheme.CrimsonCyber)]
    [InlineData(AppTheme.MidnightPro)]
    [InlineData(AppTheme.OceanGlass)]
    public void ForTheme_CoversAllThreeGranularities(AppTheme theme)
    {
        var granularities = CaptionStylePresetCatalog.ForTheme(theme).Select(p => p.Granularity).Distinct().ToList();

        Assert.Contains(CaptionGranularity.LineByLine, granularities);
        Assert.Contains(CaptionGranularity.WordByWord, granularities);
        Assert.Contains(CaptionGranularity.Karaoke, granularities);
    }

    [Fact]
    public void All_EveryPresetHasValidHexColors()
    {
        foreach (var preset in CaptionStylePresetCatalog.All)
        {
            Assert.Matches("^#[0-9A-Fa-f]{6,8}$", preset.TextColorHex);
            Assert.Matches("^#[0-9A-Fa-f]{6,8}$", preset.AccentColorHex);
            Assert.Matches("^#[0-9A-Fa-f]{6,8}$", preset.OutlineOrShadowColorHex);
            if (preset.PanelColorHex is not null)
            {
                Assert.Matches("^#[0-9A-Fa-f]{6,8}$", preset.PanelColorHex);
            }
        }
    }

    [Fact]
    public void All_PanelAnimationsAlwaysProvideAPanelColor()
    {
        var panelPresets = CaptionStylePresetCatalog.All
            .Where(p => p.Animation is CaptionAnimationKind.BlurPanel or CaptionAnimationKind.GradientPanel);

        Assert.All(panelPresets, p => Assert.False(string.IsNullOrEmpty(p.PanelColorHex)));
    }

    [Fact]
    public void All_EveryAnimationKindAppearsAtLeastOnce()
    {
        var usedAnimations = CaptionStylePresetCatalog.All.Select(p => p.Animation).Distinct().ToHashSet();

        foreach (var kind in Enum.GetValues<CaptionAnimationKind>())
        {
            Assert.Contains(kind, usedAnimations);
        }
    }
}
