using NPVideoStudio.Domain;

namespace NPVideoStudio.Core.Services;

public interface ISongHighlightService
{
    /// <summary>
    /// Analyzes the loudness of an audio file over time and returns up to <paramref name="count"/>
    /// non-overlapping candidate highlight windows, loudest first. Each window is between
    /// <paramref name="minDuration"/> and <paramref name="maxDuration"/> long.
    /// </summary>
    Task<IReadOnlyList<SongHighlight>> FindHighlightsAsync(
        string audioFilePath,
        int count = 3,
        TimeSpan? minDuration = null,
        TimeSpan? maxDuration = null,
        CancellationToken cancellationToken = default);

    /// <summary>Cuts the given highlight window out of the source audio file into its own file at <paramref name="outputFilePath"/>.</summary>
    Task ExportHighlightAsync(
        string audioFilePath,
        SongHighlight highlight,
        string outputFilePath,
        CancellationToken cancellationToken = default);
}
