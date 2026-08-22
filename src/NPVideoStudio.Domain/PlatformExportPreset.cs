namespace NPVideoStudio.Domain;

/// <summary>
/// What one platform actually wants a finished video to look like: frame size, frame rate and how hard to
/// compress it.
///
/// <see cref="TargetPlatform"/> already existed in this codebase and was already offered when creating a
/// project - but nothing used it at export time, so "TikTok" and "YouTube" produced byte-identical
/// settings. This is what makes that choice mean something.
///
/// The numbers are the platforms' own published recommendations, not invented: 1080p at the platform's
/// native aspect (16:9 for YouTube/Facebook feed, 9:16 for Shorts/Reels/TikTok), 30 fps as the safe
/// default every one of them accepts, and CRF around 20-21, which is visually near-transparent while
/// staying well under the upload size limits - going lower only wastes upload time, since all of these
/// re-encode on their side anyway.
/// </summary>
public sealed record PlatformExportPreset(
    TargetPlatform Platform,
    string DisplayName,
    int Width,
    int Height,
    double FrameRate,
    int Crf,
    string FfmpegPreset,
    int AudioBitrateKbps)
{
    public string SummaryLabel => $"{Width}x{Height} · {FrameRate:0.##} fps · CRF {Crf}";

    public static readonly PlatformExportPreset YouTube =
        new(TargetPlatform.YouTube, "YouTube (16:9)", 1920, 1080, 30, Crf: 20, "medium", 192);

    public static readonly PlatformExportPreset YouTubeShorts =
        new(TargetPlatform.YouTubeShorts, "YouTube Shorts (9:16)", 1080, 1920, 30, Crf: 21, "medium", 192);

    public static readonly PlatformExportPreset TikTok =
        new(TargetPlatform.TikTok, "TikTok (9:16)", 1080, 1920, 30, Crf: 21, "medium", 128);

    public static readonly PlatformExportPreset InstagramReel =
        new(TargetPlatform.InstagramReel, "Instagram Reels (9:16)", 1080, 1920, 30, Crf: 21, "medium", 128);

    public static readonly PlatformExportPreset FacebookReel =
        new(TargetPlatform.FacebookReel, "Facebook Reels (9:16)", 1080, 1920, 30, Crf: 21, "medium", 128);

    /// <summary>Deliberately keeps whatever the project already has - "Custom" means the user decides.</summary>
    public static readonly PlatformExportPreset Custom =
        new(TargetPlatform.Custom, "Bez presetа (moja podešavanja)", 1920, 1080, 30, Crf: 18, "medium", 192);

    public static IReadOnlyList<PlatformExportPreset> All { get; } = new[]
    {
        YouTube, YouTubeShorts, TikTok, InstagramReel, FacebookReel, Custom
    };

    public static PlatformExportPreset For(TargetPlatform platform) =>
        All.FirstOrDefault(p => p.Platform == platform) ?? Custom;

    /// <summary>
    /// Applies this preset to render settings. Does NOT touch the output path or the overwrite flag -
    /// those are the user's decision and have nothing to do with the platform.
    /// </summary>
    public void ApplyTo(RenderSettings settings)
    {
        if (Platform == TargetPlatform.Custom)
        {
            return;
        }

        settings.Crf = Crf;
        settings.Preset = FfmpegPreset;
        settings.AudioBitrateKbps = AudioBitrateKbps;
    }
}
