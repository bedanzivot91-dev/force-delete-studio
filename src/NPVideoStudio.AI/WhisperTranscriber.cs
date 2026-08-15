using System.Diagnostics;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Whisper.net;
using Whisper.net.Ggml;

namespace NPVideoStudio.AI;

/// <summary>
/// Shared local speech-to-text engine (Whisper.net) used by both the lyric-search tool and the
/// subtitle generator - the model download, WAV conversion and transcription logic only needs to
/// exist once. The model (~75 MB) is only ever downloaded after the caller explicitly asks (spec §38).
/// </summary>
public sealed class WhisperTranscriber
{
    private readonly string _ffmpegPath;
    private readonly string _modelPath;

    public WhisperTranscriber(string? ffmpegOverridePath = null, string? modelPathOverride = null)
    {
        _ffmpegPath = FfmpegLocator.ResolveFfmpegPath(ffmpegOverridePath);
        _modelPath = modelPathOverride ?? Path.Combine(AppSettings.ModelsFolder(), "ggml-tiny.bin");
    }

    public bool IsModelReady => File.Exists(_modelPath);

    public string ModelSizeLabel => "~75 MB (Whisper tiny, radi lokalno bez interneta nakon preuzimanja)";

    public async Task DownloadModelAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_modelPath)!;
        Directory.CreateDirectory(directory);

        progress?.Report("Preuzimanje modela za prepoznavanje govora...");
        // Unique per call, not a fixed ".tmp" suffix - two callers racing to download the same model
        // (e.g. two tools sharing the default model path) must not collide on the same temp file.
        var tempPath = $"{_modelPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(
                GgmlType.Tiny, QuantizationType.NoQuantization, cancellationToken);
            await using (var fileStream = File.Create(tempPath))
            {
                await modelStream.CopyToAsync(fileStream, cancellationToken);
            }

            File.Move(tempPath, _modelPath, overwrite: true);
            progress?.Report("Model je preuzet i spreman za upotrebu.");
        }
        catch (Exception ex)
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
            throw new InvalidOperationException($"Preuzimanje modela nije uspelo: {ex.Message}", ex);
        }
    }

    public Task<IReadOnlyList<TranscribedSegment>> TranscribeAsync(string audioFilePath, CancellationToken cancellationToken = default) =>
        TranscribeAsync(audioFilePath, wordLevel: false, cancellationToken);

    /// <summary>
    /// Word-level transcription for karaoke-style captions (one short-lived clip per spoken word instead
    /// of per sentence/line) - real per-word timestamps, not evenly-guessed. Uses whisper.cpp's own
    /// documented technique for this (the same one its own CLI's `--max-len 1 --split-on-word` flags use):
    /// <c>WithTokenTimestamps()</c> + <c>SplitOnWord()</c> + a max segment length of 1 word makes
    /// whisper.cpp emit one *segment* per word directly, with real (if noisier than line-level) timing -
    /// deliberately not hand-parsing the raw per-token array ourselves, which whisper.cpp's own docs note
    /// is less reliable than letting it do the word-splitting internally.
    /// </summary>
    public Task<IReadOnlyList<TranscribedSegment>> TranscribeWordsAsync(string audioFilePath, CancellationToken cancellationToken = default) =>
        TranscribeAsync(audioFilePath, wordLevel: true, cancellationToken);

    private async Task<IReadOnlyList<TranscribedSegment>> TranscribeAsync(string audioFilePath, bool wordLevel, CancellationToken cancellationToken)
    {
        if (!IsModelReady)
        {
            throw new InvalidOperationException(
                "Model za prepoznavanje govora nije preuzet. Idite u Podešavanja → AI modeli i preuzmite ga.");
        }

        if (!File.Exists(audioFilePath))
        {
            throw new FileNotFoundException("Audio fajl nije pronađen.", audioFilePath);
        }

        var wavPath = Path.Combine(Path.GetTempPath(), $"npvs_whisper_{Guid.NewGuid():N}.wav");
        try
        {
            await ConvertToWhisperWavAsync(audioFilePath, wavPath, cancellationToken);
            var segments = await RunWhisperAsync(wavPath, wordLevel, cancellationToken);
            return segments.Select(s => new TranscribedSegment(s.Start, s.End, s.Text)).ToList();
        }
        finally
        {
            if (File.Exists(wavPath))
            {
                File.Delete(wavPath);
            }
        }
    }

    private async Task ConvertToWhisperWavAsync(string inputPath, string outputWavPath, CancellationToken cancellationToken)
    {
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
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(inputPath);
        process.StartInfo.ArgumentList.Add("-ar");
        process.StartInfo.ArgumentList.Add("16000");
        process.StartInfo.ArgumentList.Add("-ac");
        process.StartInfo.ArgumentList.Add("1");
        process.StartInfo.ArgumentList.Add("-c:a");
        process.StartInfo.ArgumentList.Add("pcm_s16le");
        process.StartInfo.ArgumentList.Add(outputWavPath);

        process.Start();
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdErr = await stdErrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stdErr)
                ? $"Priprema audio fajla nije uspela (kod {process.ExitCode})."
                : stdErr.Trim());
        }
    }

    private async Task<List<SegmentData>> RunWhisperAsync(string wavPath, bool wordLevel, CancellationToken cancellationToken)
    {
        using var factory = WhisperFactory.FromPath(_modelPath);
        var builder = factory.CreateBuilder().WithLanguage("auto");
        if (wordLevel)
        {
            builder = builder.WithTokenTimestamps().SplitOnWord().WithMaxSegmentLength(1);
        }

        using var processor = builder.Build();

        var segments = new List<SegmentData>();
        await using var stream = File.OpenRead(wavPath);
        await foreach (var segment in processor.ProcessAsync(stream, cancellationToken))
        {
            segments.Add(segment);
        }

        return segments;
    }
}
