using NPVideoStudio.Domain;

namespace NPVideoStudio.Core.Services;

/// <summary>
/// Transcribes an audio or video file with the local Whisper model and writes a standard .srt subtitle
/// file - usable on YouTube/TikTok/Reels uploads or in any other editor, even before NP Video Studio
/// has its own timeline with burned-in captions.
/// </summary>
public interface ISubtitleGeneratorService
{
    /// <summary>True if the local speech-recognition model is already downloaded and ready to use.</summary>
    bool IsModelReady { get; }

    /// <summary>Approximate download size, for the consent prompt (spec §38: never download without asking).</summary>
    string ModelSizeLabel { get; }

    /// <summary>Downloads the local recognition model. Must only be called after explicit user consent.</summary>
    Task DownloadModelAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Transcribes <paramref name="mediaFilePath"/> and writes the result as an .srt file, returning its path.</summary>
    Task<string> GenerateSrtAsync(string mediaFilePath, string outputSrtPath, CancellationToken cancellationToken = default);

    /// <summary>Transcribes <paramref name="mediaFilePath"/> and returns the raw timed segments, for a
    /// caller that wants to place them directly onto a project's timeline as real caption clips instead
    /// of (or in addition to) writing a standalone .srt file.</summary>
    Task<IReadOnlyList<TranscribedCaptionSegment>> TranscribeAsync(string mediaFilePath, CancellationToken cancellationToken = default);
}
