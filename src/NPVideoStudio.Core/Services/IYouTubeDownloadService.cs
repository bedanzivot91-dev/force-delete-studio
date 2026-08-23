using NPVideoStudio.Domain;

namespace NPVideoStudio.Core.Services;

/// <summary>
/// Downloads the full audio of a YouTube video the user owns, so it can be fed into the highlight-cutter
/// or lyric-search tools. Restricted to YouTube URLs and requires an explicit ownership confirmation
/// before any download runs (spec-style consent gate, same idea as the Whisper model download).
/// </summary>
public interface IYouTubeDownloadService
{
    Task<YouTubeVideoInfo> GetVideoInfoAsync(string url, CancellationToken cancellationToken = default);

    Task<string> DownloadAudioAsync(
        string url,
        string outputDirectory,
        bool confirmedOwnContent,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
