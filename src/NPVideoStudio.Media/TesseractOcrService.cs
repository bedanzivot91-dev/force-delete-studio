using System.Diagnostics;
using System.Globalization;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;

namespace NPVideoStudio.Media;

/// <summary>
/// Real local OCR-based video layout analysis (spec Phase 7's <see cref="IVideoLayoutAnalysisService"/>),
/// via the Tesseract CLI (Apache 2.0) - not the ONNX/RapidOCR path the spec names. RapidOCR needs an
/// ONNX model file this sandbox has no verified way to download/license-check, whereas Tesseract is a
/// real, immediately-installable system package (same "shell out to an external tool" pattern as ffmpeg/
/// yt-dlp/fpcalc) that was actually installed and run end-to-end while building this service. Face/
/// person/logo/CTA detection is not implemented - see <see cref="VideoLayoutAnalysisResult"/>'s doc
/// comment.
/// </summary>
public sealed class TesseractOcrService : IVideoLayoutAnalysisService
{
    private readonly IMediaProbeService _mediaProbeService;
    private readonly string _ffmpegPath;
    private readonly string _tesseractPath;

    public TesseractOcrService(IMediaProbeService mediaProbeService, string? ffmpegOverridePath = null, string? tesseractOverridePath = null)
    {
        _mediaProbeService = mediaProbeService;
        _ffmpegPath = FfmpegLocator.ResolveFfmpegPath(ffmpegOverridePath);
        _tesseractPath = FfmpegLocator.ResolveTesseractPath(tesseractOverridePath);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        var (found, _) = await FfmpegLocator.TryGetVersionAsync(_tesseractPath, "--version", cancellationToken).ConfigureAwait(false);
        return found;
    }

    public async Task<VideoLayoutAnalysisResult> AnalyzeAsync(
        string videoFilePath, int sampleFrameCount = 5, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(videoFilePath))
        {
            throw new FileNotFoundException("Video fajl nije pronađen.", videoFilePath);
        }

        sampleFrameCount = Math.Max(1, sampleFrameCount);

        var asset = await _mediaProbeService.ProbeAsync(videoFilePath, cancellationToken).ConfigureAwait(false);
        if (asset.Duration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Trajanje videa nije moglo da se odredi.");
        }

        if (asset.Width <= 0 || asset.Height <= 0)
        {
            throw new InvalidOperationException("Rezolucija videa nije mogla da se odredi.");
        }

        var regions = new List<DetectedTextRegion>();
        var tempDir = Path.Combine(Path.GetTempPath(), $"npvs_layout_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            for (var i = 0; i < sampleFrameCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Evenly spaced strictly inside the clip - the very first/last frame is often black/blank.
                var fraction = (i + 1.0) / (sampleFrameCount + 1.0);
                var timestamp = TimeSpan.FromSeconds(asset.Duration.TotalSeconds * fraction);
                var framePath = Path.Combine(tempDir, $"frame_{i}.png");

                await ExtractFrameAsync(videoFilePath, timestamp, framePath, cancellationToken).ConfigureAwait(false);
                if (!File.Exists(framePath))
                {
                    continue;
                }

                var tsv = await RunTesseractAsync(framePath, cancellationToken).ConfigureAwait(false);
                regions.AddRange(ParseTsv(tsv, timestamp, asset.Width, asset.Height));
            }
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Best-effort temp cleanup - a leftover temp dir is not worth failing the whole analysis over.
            }
        }

        return new VideoLayoutAnalysisResult
        {
            SampledFrameCount = sampleFrameCount,
            DetectedTextRegions = regions,
            TextOccupancyByZone = VideoLayoutAggregator.ComputeTextOccupancy(regions, sampleFrameCount)
        };
    }

    private async Task ExtractFrameAsync(string videoFilePath, TimeSpan timestamp, string outputPngPath, CancellationToken cancellationToken)
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
        process.StartInfo.ArgumentList.Add("-ss");
        process.StartInfo.ArgumentList.Add(timestamp.TotalSeconds.ToString(CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(videoFilePath);
        process.StartInfo.ArgumentList.Add("-frames:v");
        process.StartInfo.ArgumentList.Add("1");
        process.StartInfo.ArgumentList.Add("-update");
        process.StartInfo.ArgumentList.Add("1");
        process.StartInfo.ArgumentList.Add(outputPngPath);

        process.Start();
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdErr = await stdErrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stdErr)
                ? $"Izdvajanje kadra nije uspelo (kod {process.ExitCode})."
                : stdErr.Trim());
        }
    }

    private async Task<string> RunTesseractAsync(string imagePath, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _tesseractPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add(imagePath);
        process.StartInfo.ArgumentList.Add("stdout");
        process.StartInfo.ArgumentList.Add("--psm");
        process.StartInfo.ArgumentList.Add("6");
        process.StartInfo.ArgumentList.Add("tsv");

        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdOut = await stdOutTask.ConfigureAwait(false);
        var stdErr = await stdErrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stdErr)
                ? $"OCR nije uspeo (kod {process.ExitCode})."
                : stdErr.Trim());
        }

        return stdOut;
    }

    /// <summary>Parses tesseract's TSV output (one row per detected word) into normalized text regions. Public+static so the real TSV shape can be unit tested without launching a process.</summary>
    public static List<DetectedTextRegion> ParseTsv(string tsv, TimeSpan frameTimestamp, int frameWidth, int frameHeight)
    {
        var regions = new List<DetectedTextRegion>();
        var lines = tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
        {
            return regions;
        }

        for (var i = 1; i < lines.Length; i++)
        {
            var columns = lines[i].TrimEnd('\r').Split('\t');
            // level, page_num, block_num, par_num, line_num, word_num, left, top, width, height, conf, text
            if (columns.Length < 12)
            {
                continue;
            }

            var text = columns[11].Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (!int.TryParse(columns[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var left) ||
                !int.TryParse(columns[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var top) ||
                !int.TryParse(columns[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) ||
                !int.TryParse(columns[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height) ||
                !double.TryParse(columns[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence))
            {
                continue;
            }

            if (confidence < 0 || width <= 0 || height <= 0)
            {
                continue; // tesseract uses confidence -1 for structural (non-word) rows.
            }

            regions.Add(new DetectedTextRegion
            {
                FrameTimestamp = frameTimestamp,
                Text = text,
                Confidence = confidence / 100.0,
                X = (double)left / frameWidth,
                Y = (double)top / frameHeight,
                Width = (double)width / frameWidth,
                Height = (double)height / frameHeight
            });
        }

        return regions;
    }
}
