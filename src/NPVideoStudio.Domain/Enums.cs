namespace NPVideoStudio.Domain;

public enum AspectRatioPreset
{
    Widescreen16x9,
    Vertical9x16,
    Square1x1,
    Portrait4x5,
    Ultrawide21x9,
    Custom
}

public enum ResolutionPreset
{
    Hd720,
    FullHd1080,
    Qhd1440,
    Uhd4K,
    Custom
}

public enum FrameRatePreset
{
    Fps23_976,
    Fps24,
    Fps25,
    Fps29_97,
    Fps30,
    Fps50,
    Fps59_94,
    Fps60,
    Custom
}

public enum MediaKind
{
    Video,
    Audio,
    Image,
    Unknown
}

public enum TargetPlatform
{
    YouTube,
    YouTubeShorts,
    TikTok,
    InstagramReel,
    FacebookReel,
    Custom
}

public enum AppTheme
{
    Studio2026,
    DarkCinematic,
    MinimalLight,
    ProfessionalStudio,
    ObsidianNeon,
    ArcticGlass,
    CrimsonCyber,
    MidnightPro,
    OceanGlass
}

/// <summary>Controls updates of app-owned command-line/AI tools. Never changes Windows or user media.</summary>
public enum ToolUpdatePolicy
{
    NotifyOnly,
    Automatic,
    Manual
}

/// <summary>File formats the caption editor can import/export (spec Phase 6). Ass has no importer yet - see CaptionFormatConverter's doc comment.</summary>
public enum CaptionFileFormat
{
    Srt,
    Vtt,
    Ass,
    Txt,
    Json,
    Lrc
}

/// <summary>How many words are visible/highlighted at once in a caption style preset (spec Phase 7).</summary>
public enum CaptionGranularity
{
    LineByLine,
    WordByWord,
    Karaoke
}

/// <summary>The visual treatment a caption style preset animates its text with (spec Phase 7's named list).</summary>
public enum CaptionAnimationKind
{
    Pop,
    Scale,
    Slide,
    Fade,
    Bounce,
    Glow,
    Outline,
    Shadow,
    BlurPanel,
    GradientPanel
}

/// <summary>Requested vertical caption placement (spec Phase 7): Automatic defers to <c>CaptionPlacementAdvisor</c>.</summary>
public enum CaptionPlacementMode
{
    Automatic,
    Top,
    Middle,
    Bottom,
    Manual
}

/// <summary>A 3x3 grid cell of a video frame, used to describe where existing content (currently: OCR-detected text) occupies the frame over time.</summary>
public enum CaptionGridZone
{
    TopLeft, TopCenter, TopRight,
    MiddleLeft, MiddleCenter, MiddleRight,
    BottomLeft, BottomCenter, BottomRight
}
