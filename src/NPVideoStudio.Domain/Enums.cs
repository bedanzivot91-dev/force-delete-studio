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
    DarkCinematic,
    MinimalLight,
    ProfessionalStudio,
    ObsidianNeon,
    ArcticGlass,
    CrimsonCyber,
    MidnightPro,
    OceanGlass
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
