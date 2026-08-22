namespace NPVideoStudio.Domain;

/// <summary>
/// One caption style preset (spec Phase 7: each of the 8 themes gets >=3 presets covering line-by-line/
/// word-by-word/karaoke granularity with a named animation treatment). Colors are plain hex strings, not
/// Avalonia brushes - this is a Domain type with no UI dependency; the App layer's catalog is what ties
/// specific presets to specific <see cref="AppTheme"/> values using each theme's real accent/background/
/// text colors (never invented colors unrelated to the theme they belong to).
/// </summary>
public sealed class CaptionStylePreset
{
    public required string Name { get; init; }
    public required AppTheme Theme { get; init; }
    public required CaptionGranularity Granularity { get; init; }
    public required CaptionAnimationKind Animation { get; init; }
    public required string TextColorHex { get; init; }
    public required string AccentColorHex { get; init; }
    public required string OutlineOrShadowColorHex { get; init; }

    /// <summary>Background panel color for BlurPanel/GradientPanel treatments - ignored by other animation kinds.</summary>
    public string? PanelColorHex { get; init; }
}

/// <summary>
/// Safe margins for one aspect ratio (spec Phase 7), expressed as a fraction (0..1) of frame height/width
/// reserved on each edge for platform UI chrome (e.g. TikTok/Shorts side icons and bottom caption/CTA
/// bar). These are documented estimates based on published platform safe-area guidance, not measured
/// from any specific app version - see <see cref="CaptionSafeMargins.ForAspectRatio"/>'s doc comment.
/// </summary>
public sealed class CaptionSafeMargins
{
    public required double TopRatio { get; init; }
    public required double BottomRatio { get; init; }
    public required double LeftRatio { get; init; }
    public required double RightRatio { get; init; }

    /// <summary>
    /// Rough, documented estimates (not measured from a specific app build): vertical formats reserve
    /// much more top/bottom space for platform chrome (status bar, follow/like/share rail, caption/CTA
    /// area) than horizontal formats, which mostly only need a small letterbox-safe margin.
    /// </summary>
    public static CaptionSafeMargins ForAspectRatio(AspectRatioPreset preset) => preset switch
    {
        AspectRatioPreset.Vertical9x16 => new CaptionSafeMargins { TopRatio = 0.10, BottomRatio = 0.20, LeftRatio = 0.05, RightRatio = 0.12 },
        AspectRatioPreset.Portrait4x5 => new CaptionSafeMargins { TopRatio = 0.08, BottomRatio = 0.14, LeftRatio = 0.05, RightRatio = 0.08 },
        AspectRatioPreset.Square1x1 => new CaptionSafeMargins { TopRatio = 0.06, BottomRatio = 0.10, LeftRatio = 0.05, RightRatio = 0.05 },
        AspectRatioPreset.Ultrawide21x9 => new CaptionSafeMargins { TopRatio = 0.04, BottomRatio = 0.08, LeftRatio = 0.04, RightRatio = 0.04 },
        _ => new CaptionSafeMargins { TopRatio = 0.05, BottomRatio = 0.10, LeftRatio = 0.05, RightRatio = 0.05 } // 16:9 / Custom
    };
}
