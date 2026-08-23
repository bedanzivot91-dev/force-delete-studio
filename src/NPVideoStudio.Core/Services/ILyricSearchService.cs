using NPVideoStudio.Domain;

namespace NPVideoStudio.Core.Services;

public interface ILyricSearchService
{
    /// <summary>True if the local speech-recognition model is already downloaded and ready to use.</summary>
    bool IsModelReady { get; }

    /// <summary>Approximate download size, for the consent prompt (spec §38: never download without asking).</summary>
    string ModelSizeLabel { get; }

    /// <summary>The real, resolved path of the speech-recognition model this service will actually use
    /// (bundled next to the exe, or the AppData default) - see <see cref="WhisperModelLocator"/>.</summary>
    string ModelPath { get; }

    /// <summary>Downloads the local recognition model. Must only be called after explicit user consent.</summary>
    Task DownloadModelAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribes the song and returns every place where the recognized lyrics match
    /// <paramref name="phrase"/>, loudest-confidence match first. Never claims certainty - singing
    /// recognition is approximate, and every result carries a confidence score.
    /// </summary>
    Task<IReadOnlyList<LyricMatch>> FindPhraseInSongAsync(
        string audioFilePath,
        string phrase,
        CancellationToken cancellationToken = default);

    /// <summary>Cuts the matched window (with a little padding) out of the source audio into its own file.</summary>
    Task ExportMatchAsync(
        string audioFilePath,
        LyricMatch match,
        string outputFilePath,
        CancellationToken cancellationToken = default);
}
