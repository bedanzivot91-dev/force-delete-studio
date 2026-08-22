namespace NPVideoStudio.Core.Services;

/// <summary>
/// Builds a single video from one still image and one song (spec Phase 10: "Brzi video od slike i
/// pesme" / "Automatski video sa utisnutim titlovima (na slici)") - a real, common lyric-video pattern
/// (looped still image + audio track), deliberately kept separate from the general
/// <see cref="IRenderService"/>/timeline pipeline rather than teaching that pipeline to treat images as
/// looped video clips, which would be a much larger change for a one-off wizard.
/// </summary>
public interface IQuickVideoService
{
    /// <summary>
    /// <paramref name="songDurationSeconds"/> drives the live progress percentage (ffmpeg's own
    /// `-shortest` flag caps the actual output length to the song regardless). Pass
    /// <paramref name="subtitleSrtPath"/> to burn in an existing .srt file (e.g. from
    /// <see cref="ISubtitleGeneratorService"/>) via ffmpeg's real `subtitles` filter.
    /// </summary>
    Task<string> CreateAsync(
        string imageFilePath,
        string songFilePath,
        double songDurationSeconds,
        string outputFilePath,
        bool overwriteConfirmed,
        string? subtitleSrtPath = null,
        int width = 1920,
        int height = 1080,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
