namespace NPVideoStudio.Domain;

/// <summary>
/// A candidate short clip auto-picked from a song by loudness analysis - meant as a starting point
/// for a YouTube Shorts / TikTok / Reel song-announcement teaser. This is a loudness heuristic, not
/// verified chorus/hook detection, so it is always presented to the user as a suggestion they can
/// adjust, never as a guaranteed "this is the chorus" claim (spec §16/29/53).
/// </summary>
public sealed class SongHighlight
{
    public required TimeSpan Start { get; init; }
    public required TimeSpan Duration { get; init; }
    public required double AverageLoudnessDb { get; init; }

    public TimeSpan End => Start + Duration;

    /// <summary>Absolute path to the exported clip file once export has run. Null until then.</summary>
    public string? ExportedFilePath { get; set; }
}
