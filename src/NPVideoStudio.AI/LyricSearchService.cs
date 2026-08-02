using System.Diagnostics;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;

namespace NPVideoStudio.AI;

/// <summary>
/// Finds a typed lyric phrase inside a song using a local Whisper speech-recognition model (via
/// <see cref="WhisperTranscriber"/>), then can cut the matched moment out as a clip. Singing recognition
/// is far less reliable than spoken speech, so results always carry a confidence score and the raw
/// recognized text - never a bare "found it".
/// </summary>
public sealed class LyricSearchService : ILyricSearchService
{
    private readonly WhisperTranscriber _transcriber;
    private readonly string _ffmpegPath;

    public LyricSearchService(string? ffmpegOverridePath = null, string? modelPathOverride = null)
    {
        _transcriber = new WhisperTranscriber(ffmpegOverridePath, modelPathOverride);
        _ffmpegPath = FfmpegLocator.ResolveFfmpegPath(ffmpegOverridePath);
    }

    public bool IsModelReady => _transcriber.IsModelReady;

    public string ModelSizeLabel => _transcriber.ModelSizeLabel;

    public Task DownloadModelAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        _transcriber.DownloadModelAsync(progress, cancellationToken);

    public async Task<IReadOnlyList<LyricMatch>> FindPhraseInSongAsync(
        string audioFilePath, string phrase, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phrase))
        {
            throw new ArgumentException("Unesite tekst koji se traži.", nameof(phrase));
        }

        var segments = await _transcriber.TranscribeAsync(audioFilePath, cancellationToken);
        return LyricMatcher.Search(segments, phrase);
    }

    public async Task ExportMatchAsync(
        string audioFilePath, LyricMatch match, string outputFilePath, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-ss");
        process.StartInfo.ArgumentList.Add(match.Start.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(audioFilePath);
        process.StartInfo.ArgumentList.Add("-t");
        process.StartInfo.ArgumentList.Add(match.Duration.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add("-vn");
        process.StartInfo.ArgumentList.Add(outputFilePath);

        process.Start();
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdErr = await stdErrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stdErr)
                ? $"Izvoz isečka nije uspeo (kod {process.ExitCode})."
                : stdErr.Trim());
        }

        match.ExportedFilePath = outputFilePath;
    }
}
