using System.Diagnostics;
using System.Globalization;
using NPVideoStudio.Core.Services;

namespace NPVideoStudio.Media;

/// <summary>
/// Real ffmpeg-based proxy generation (spec Phase 8). Transcodes to a temp file first, then atomically
/// renames into place - same "never leave a half-written file looking valid" rule as every other real
/// download/generation path in this codebase (e.g. <see cref="WhisperTranscriber"/... /> model download,
/// via NPVideoStudio.AI).
/// </summary>
public sealed class ProxyGeneratorService : IProxyGeneratorService
{
    private readonly string _ffmpegPath;

    public ProxyGeneratorService(string? ffmpegOverridePath = null)
    {
        _ffmpegPath = FfmpegLocator.ResolveFfmpegPath(ffmpegOverridePath);
    }

    public async Task<string> GenerateProxyAsync(
        string sourceFilePath, string outputFilePath, int targetHeight = 720, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("Izvorni fajl nije pronađen.", sourceFilePath);
        }

        if (targetHeight <= 0)
        {
            targetHeight = 720;
        }

        var directory = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // ffmpeg infers the output container from the file extension, so the temp name must still end
        // in the real extension (e.g. ".mp4") - a plain ".part" suffix makes muxer auto-detection fail.
        var outputExtension = Path.GetExtension(outputFilePath);
        var outputWithoutExtension = outputFilePath[..^outputExtension.Length];
        var tempPath = $"{outputWithoutExtension}.{Guid.NewGuid():N}{outputExtension}";

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
        process.StartInfo.ArgumentList.Add(sourceFilePath);
        process.StartInfo.ArgumentList.Add("-vf");
        // -2 keeps width even and preserves aspect ratio - libx264 requires even dimensions.
        process.StartInfo.ArgumentList.Add($"scale=-2:{targetHeight.ToString(CultureInfo.InvariantCulture)}");
        process.StartInfo.ArgumentList.Add("-c:v");
        process.StartInfo.ArgumentList.Add("libx264");
        process.StartInfo.ArgumentList.Add("-preset");
        process.StartInfo.ArgumentList.Add("veryfast");
        process.StartInfo.ArgumentList.Add("-crf");
        process.StartInfo.ArgumentList.Add("28");
        process.StartInfo.ArgumentList.Add("-c:a");
        process.StartInfo.ArgumentList.Add("aac");
        process.StartInfo.ArgumentList.Add("-b:a");
        process.StartInfo.ArgumentList.Add("128k");
        process.StartInfo.ArgumentList.Add(tempPath);

        try
        {
            process.Start();
            var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdErr = await stdErrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(stdErr)
                    ? $"Generisanje proxy fajla nije uspelo (kod {process.ExitCode})."
                    : stdErr.Trim());
            }

            if (!File.Exists(tempPath))
            {
                throw new InvalidOperationException("ffmpeg je prijavio uspeh, ali izlazni fajl nije pronađen.");
            }

            File.Move(tempPath, outputFilePath, overwrite: true);
            return outputFilePath;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
