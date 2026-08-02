using NPVideoStudio.Core.Services;

namespace NPVideoStudio.AI;

public sealed class SubtitleGeneratorService : ISubtitleGeneratorService
{
    private readonly WhisperTranscriber _transcriber;

    public SubtitleGeneratorService(string? ffmpegOverridePath = null, string? modelPathOverride = null)
    {
        _transcriber = new WhisperTranscriber(ffmpegOverridePath, modelPathOverride);
    }

    public bool IsModelReady => _transcriber.IsModelReady;

    public string ModelSizeLabel => _transcriber.ModelSizeLabel;

    public Task DownloadModelAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        _transcriber.DownloadModelAsync(progress, cancellationToken);

    public async Task<string> GenerateSrtAsync(string mediaFilePath, string outputSrtPath, CancellationToken cancellationToken = default)
    {
        var segments = await _transcriber.TranscribeAsync(mediaFilePath, cancellationToken);
        var content = SrtWriter.Write(segments);

        var directory = Path.GetDirectoryName(outputSrtPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(outputSrtPath, content, cancellationToken);
        return outputSrtPath;
    }
}
