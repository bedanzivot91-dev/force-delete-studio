using System.Diagnostics;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Whisper.net;
using Whisper.net.Ggml;

namespace NPVideoStudio.AI;

/// <summary>
/// Finds a typed lyric phrase inside a song using a local Whisper speech-recognition model, then can
/// cut the matched moment out as a clip. Singing recognition is far less reliable than spoken speech,
/// so results always carry a confidence score and the raw recognized text - never a bare "found it".
/// The model (~75 MB) is only ever downloaded after the caller explicitly asks (spec §38).
/// </summary>
public sealed class LyricSearchService : ILyricSearchService
{
    private readonly string _ffmpegPath;
    private readonly string _modelPath;

    public LyricSearchService(string? ffmpegOverridePath = null, string? modelPathOverride = null)
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
        var tempPath = _modelPath + ".tmp";

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

    public async Task<IReadOnlyList<LyricMatch>> FindPhraseInSongAsync(
        string audioFilePath, string phrase, CancellationToken cancellationToken = default)
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

        if (string.IsNullOrWhiteSpace(phrase))
        {
            throw new ArgumentException("Unesite tekst koji se traži.", nameof(phrase));
        }

        var wavPath = Path.Combine(Path.GetTempPath(), $"npvs_lyric_{Guid.NewGuid():N}.wav");
        try
        {
            await ConvertToWhisperWavAsync(audioFilePath, wavPath, cancellationToken);
            var segments = await TranscribeAsync(wavPath, cancellationToken);
            var transcribed = segments.Select(s => new TranscribedSegment(s.Start, s.End, s.Text)).ToList();
            return LyricMatcher.Search(transcribed, phrase);
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

    private async Task<List<SegmentData>> TranscribeAsync(string wavPath, CancellationToken cancellationToken)
    {
        using var factory = WhisperFactory.FromPath(_modelPath);
        using var processor = factory.CreateBuilder()
            .WithLanguage("auto")
            .Build();

        var segments = new List<SegmentData>();
        await using var stream = File.OpenRead(wavPath);
        await foreach (var segment in processor.ProcessAsync(stream, cancellationToken))
        {
            segments.Add(segment);
        }

        return segments;
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
