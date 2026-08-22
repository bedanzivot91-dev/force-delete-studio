using NPVideoStudio.Domain;

namespace NPVideoStudio.App.Services;

/// <summary>
/// The >=3-presets-per-theme catalog (spec Phase 7): 3 presets for each of the 8 themes (24 total),
/// covering all three granularities and spread across all 10 named animation kinds. Colors are taken
/// directly from each theme's own resource dictionary (accent/background/text/accent-subtle) - never
/// invented independently of the theme they belong to, so a preset always looks like it belongs to its
/// theme rather than a generic default.
/// </summary>
public static class CaptionStylePresetCatalog
{
    private static readonly IReadOnlyList<CaptionStylePreset> Presets = new List<CaptionStylePreset>
    {
        // DarkCinematic: bg #0B0C10, text #F4F5F7, accent #FF4757
        new() { Name = "Klasičan", Theme = AppTheme.DarkCinematic, Granularity = CaptionGranularity.LineByLine, Animation = CaptionAnimationKind.Fade, TextColorHex = "#F4F5F7", AccentColorHex = "#FF4757", OutlineOrShadowColorHex = "#000000" },
        new() { Name = "Naglašen", Theme = AppTheme.DarkCinematic, Granularity = CaptionGranularity.WordByWord, Animation = CaptionAnimationKind.Pop, TextColorHex = "#F4F5F7", AccentColorHex = "#FF4757", OutlineOrShadowColorHex = "#000000" },
        new() { Name = "Karaoke sjaj", Theme = AppTheme.DarkCinematic, Granularity = CaptionGranularity.Karaoke, Animation = CaptionAnimationKind.Glow, TextColorHex = "#F4F5F7", AccentColorHex = "#FF4757", OutlineOrShadowColorHex = "#FF4757" },

        // MinimalLight: bg #F4F5F7, text #1B1E27, accent #3B6DF7
        new() { Name = "Čist", Theme = AppTheme.MinimalLight, Granularity = CaptionGranularity.LineByLine, Animation = CaptionAnimationKind.Fade, TextColorHex = "#1B1E27", AccentColorHex = "#3B6DF7", OutlineOrShadowColorHex = "#FFFFFF" },
        new() { Name = "Klizanje", Theme = AppTheme.MinimalLight, Granularity = CaptionGranularity.WordByWord, Animation = CaptionAnimationKind.Slide, TextColorHex = "#1B1E27", AccentColorHex = "#3B6DF7", OutlineOrShadowColorHex = "#FFFFFF" },
        new() { Name = "Karaoke kontura", Theme = AppTheme.MinimalLight, Granularity = CaptionGranularity.Karaoke, Animation = CaptionAnimationKind.Outline, TextColorHex = "#1B1E27", AccentColorHex = "#3B6DF7", OutlineOrShadowColorHex = "#FFFFFF" },

        // ProfessionalStudio: bg #161A20, text #EAEDF1, accent #2FD498
        new() { Name = "Studio", Theme = AppTheme.ProfessionalStudio, Granularity = CaptionGranularity.LineByLine, Animation = CaptionAnimationKind.Fade, TextColorHex = "#EAEDF1", AccentColorHex = "#2FD498", OutlineOrShadowColorHex = "#000000" },
        new() { Name = "Uvećanje", Theme = AppTheme.ProfessionalStudio, Granularity = CaptionGranularity.WordByWord, Animation = CaptionAnimationKind.Scale, TextColorHex = "#EAEDF1", AccentColorHex = "#2FD498", OutlineOrShadowColorHex = "#000000" },
        new() { Name = "Karaoke senka", Theme = AppTheme.ProfessionalStudio, Granularity = CaptionGranularity.Karaoke, Animation = CaptionAnimationKind.Shadow, TextColorHex = "#EAEDF1", AccentColorHex = "#2FD498", OutlineOrShadowColorHex = "#000000" },

        // ObsidianNeon: bg #070A12, text #F7F8FC, accent #8B5CFF
        new() { Name = "Neonski sjaj", Theme = AppTheme.ObsidianNeon, Granularity = CaptionGranularity.LineByLine, Animation = CaptionAnimationKind.Glow, TextColorHex = "#F7F8FC", AccentColorHex = "#8B5CFF", OutlineOrShadowColorHex = "#8B5CFF" },
        new() { Name = "Otkucaj", Theme = AppTheme.ObsidianNeon, Granularity = CaptionGranularity.WordByWord, Animation = CaptionAnimationKind.Bounce, TextColorHex = "#F7F8FC", AccentColorHex = "#8B5CFF", OutlineOrShadowColorHex = "#000000" },
        new() { Name = "Karaoke kontura", Theme = AppTheme.ObsidianNeon, Granularity = CaptionGranularity.Karaoke, Animation = CaptionAnimationKind.Outline, TextColorHex = "#F7F8FC", AccentColorHex = "#8B5CFF", OutlineOrShadowColorHex = "#000000" },

        // ArcticGlass: bg #EAF5FF, text #10233D, accent #1677E8, subtle-accent panel #331677E8
        new() { Name = "Stakleni panel", Theme = AppTheme.ArcticGlass, Granularity = CaptionGranularity.LineByLine, Animation = CaptionAnimationKind.BlurPanel, TextColorHex = "#10233D", AccentColorHex = "#1677E8", OutlineOrShadowColorHex = "#FFFFFF", PanelColorHex = "#331677E8" },
        new() { Name = "Blagi prelaz", Theme = AppTheme.ArcticGlass, Granularity = CaptionGranularity.WordByWord, Animation = CaptionAnimationKind.Fade, TextColorHex = "#10233D", AccentColorHex = "#1677E8", OutlineOrShadowColorHex = "#FFFFFF" },
        new() { Name = "Karaoke gradijent", Theme = AppTheme.ArcticGlass, Granularity = CaptionGranularity.Karaoke, Animation = CaptionAnimationKind.GradientPanel, TextColorHex = "#10233D", AccentColorHex = "#1677E8", OutlineOrShadowColorHex = "#FFFFFF", PanelColorHex = "#331677E8" },

        // CrimsonCyber: bg #05080D, text #F4FAFC, accent #00DCEB
        new() { Name = "Oštra kontura", Theme = AppTheme.CrimsonCyber, Granularity = CaptionGranularity.LineByLine, Animation = CaptionAnimationKind.Outline, TextColorHex = "#F4FAFC", AccentColorHex = "#00DCEB", OutlineOrShadowColorHex = "#000000" },
        new() { Name = "Cyber uvećanje", Theme = AppTheme.CrimsonCyber, Granularity = CaptionGranularity.WordByWord, Animation = CaptionAnimationKind.Scale, TextColorHex = "#F4FAFC", AccentColorHex = "#00DCEB", OutlineOrShadowColorHex = "#000000" },
        new() { Name = "Karaoke sjaj", Theme = AppTheme.CrimsonCyber, Granularity = CaptionGranularity.Karaoke, Animation = CaptionAnimationKind.Glow, TextColorHex = "#F4FAFC", AccentColorHex = "#00DCEB", OutlineOrShadowColorHex = "#00DCEB" },

        // MidnightPro: bg #07111C, text #F4F8FC, accent #258DF5
        new() { Name = "Senka", Theme = AppTheme.MidnightPro, Granularity = CaptionGranularity.LineByLine, Animation = CaptionAnimationKind.Shadow, TextColorHex = "#F4F8FC", AccentColorHex = "#258DF5", OutlineOrShadowColorHex = "#000000" },
        new() { Name = "Klizanje", Theme = AppTheme.MidnightPro, Granularity = CaptionGranularity.WordByWord, Animation = CaptionAnimationKind.Slide, TextColorHex = "#F4F8FC", AccentColorHex = "#258DF5", OutlineOrShadowColorHex = "#000000" },
        new() { Name = "Karaoke iščezavanje", Theme = AppTheme.MidnightPro, Granularity = CaptionGranularity.Karaoke, Animation = CaptionAnimationKind.Fade, TextColorHex = "#F4F8FC", AccentColorHex = "#258DF5", OutlineOrShadowColorHex = "#000000" },

        // OceanGlass: bg #EFFAFC, text #12323A, accent #0098B2, subtle-accent panel #330098B2
        new() { Name = "Gradijent panel", Theme = AppTheme.OceanGlass, Granularity = CaptionGranularity.LineByLine, Animation = CaptionAnimationKind.GradientPanel, TextColorHex = "#12323A", AccentColorHex = "#0098B2", OutlineOrShadowColorHex = "#FFFFFF", PanelColorHex = "#330098B2" },
        new() { Name = "Stakleni panel reč-po-reč", Theme = AppTheme.OceanGlass, Granularity = CaptionGranularity.WordByWord, Animation = CaptionAnimationKind.BlurPanel, TextColorHex = "#12323A", AccentColorHex = "#0098B2", OutlineOrShadowColorHex = "#FFFFFF", PanelColorHex = "#330098B2" },
        new() { Name = "Karaoke otkucaj", Theme = AppTheme.OceanGlass, Granularity = CaptionGranularity.Karaoke, Animation = CaptionAnimationKind.Bounce, TextColorHex = "#12323A", AccentColorHex = "#0098B2", OutlineOrShadowColorHex = "#FFFFFF" }
    };

    public static IReadOnlyList<CaptionStylePreset> All => Presets;

    public static IReadOnlyList<CaptionStylePreset> ForTheme(AppTheme theme) =>
        Presets.Where(p => p.Theme == theme).ToList();
}
