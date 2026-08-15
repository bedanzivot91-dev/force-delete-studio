using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;

namespace NPVideoStudio.Media;

/// <summary>
/// Alternative <see cref="IVideoLayoutAnalysisService"/> backed by EasyOCR (Apache 2.0, deep-learning
/// based) via a small bundled Python helper (<c>Tools/easyocr-helper/ocr_frame.py</c>), instead of
/// <see cref="TesseractOcrService"/>'s Tesseract CLI.
///
/// Why this exists alongside Tesseract rather than replacing it: verified directly against a real
/// user video's on-screen decorative caption text (colored, outlined "bubble letter" style, common in
/// short-form video templates) - Tesseract returned garbage for it, EasyOCR with the rs_latin language
/// model read it correctly (81%/95% confidence on the two words tested). Tesseract is a much lighter
/// dependency (a small native binary vs. a Python/PyTorch install) and handles plain
/// document/subtitle-style text at least as well, so it stays the default; this service is the opt-in
/// fallback for exactly the stylized-text failure mode. See <c>docs/PHASE_STATUS.md</c>.
/// </summary>
public sealed class EasyOcrVideoLayoutAnalysisService : IVideoLayoutAnalysisService
{
    private readonly IMediaProbeService _mediaProbeService;
    private readonly string _ffmpegPath;
    private readonly string _pythonPath;
    private readonly string _scriptPath;

    public EasyOcrVideoLayoutAnalysisService(
        IMediaProbeService mediaProbeService,
        string? ffmpegOverridePath = null,
        string? pythonOverridePath = null,
        string? scriptOverridePath = null)
    {
        _mediaProbeService = mediaProbeService;
        _ffmpegPath = FfmpegLocator.ResolveFfmpegPath(ffmpegOverridePath);
        _pythonPath = !string.IsNullOrWhiteSpace(pythonOverridePath)
            ? pythonOverridePath
            : OperatingSystem.IsWindows() ? "python" : "python3";
        _scriptPath = scriptOverridePath
            ?? Path.Combine(AppContext.BaseDirectory, "Tools", "easyocr-helper", "ocr_frame.py");
    }

    /// <summary>
    /// A cheap "is easyocr importable" check (no model load - that's a multi-second, possibly
    /// network-downloading operation and doesn't belong in an availability probe).
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _pythonPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add("import easyocr, PIL");

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return false;
        }

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await stdOutTask.ConfigureAwait(false);
        await stdErrTask.ConfigureAwait(false);

        return process.ExitCode == 0;
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
        var tempDir = Path.Combine(Path.GetTempPath(), $"npvs_easyocr_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            for (var i = 0; i < sampleFrameCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Evenly spaced strictly inside the clip - the very first/last frame is often black/blank
                // (same convention as TesseractOcrService, for the same reason).
                var fraction = (i + 1.0) / (sampleFrameCount + 1.0);
                var timestamp = TimeSpan.FromSeconds(asset.Duration.TotalSeconds * fraction);
                var framePath = Path.Combine(tempDir, $"frame_{i}.png");

                await ExtractFrameAsync(videoFilePath, timestamp, framePath, cancellationToken).ConfigureAwait(false);
                if (!File.Exists(framePath))
                {
                    continue;
                }

                var json = await RunEasyOcrAsync(framePath, cancellationToken).ConfigureAwait(false);
                regions.AddRange(ParseRegions(json, timestamp));
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

    // No-BOM UTF-8, same reasoning as AiWorkerClient: Serbian č/ć/š/ž/đ on Python's stdout only
    // round-trips correctly on Windows if both sides agree on plain UTF-8.
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private async Task<string> RunEasyOcrAsync(string imagePath, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _pythonPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Utf8NoBom,
                StandardErrorEncoding = Utf8NoBom,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add(_scriptPath);
        process.StartInfo.ArgumentList.Add(imagePath);
        process.StartInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdOut = await stdOutTask.ConfigureAwait(false);
        var stdErr = await stdErrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stdErr)
                ? $"EasyOCR nije uspeo (kod {process.ExitCode})."
                : stdErr.Trim());
        }

        return stdOut;
    }

    private sealed class EasyOcrRegionJson
    {
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("confidence")] public double Confidence { get; set; }
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("width")] public double Width { get; set; }
        [JsonPropertyName("height")] public double Height { get; set; }
    }

    /// <summary>Parses <c>ocr_frame.py</c>'s one-line JSON array output. Public+static so the real wire
    /// shape can be unit tested without launching Python.</summary>
    public static List<DetectedTextRegion> ParseRegions(string json, TimeSpan frameTimestamp)
    {
        var regions = new List<DetectedTextRegion>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return regions;
        }

        List<EasyOcrRegionJson>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<List<EasyOcrRegionJson>>(json);
        }
        catch (JsonException)
        {
            return regions;
        }

        if (parsed is null)
        {
            return regions;
        }

        foreach (var r in parsed)
        {
            if (string.IsNullOrWhiteSpace(r.Text) || r.Width <= 0 || r.Height <= 0)
            {
                continue;
            }

            regions.Add(new DetectedTextRegion
            {
                FrameTimestamp = frameTimestamp,
                Text = r.Text,
                Confidence = r.Confidence,
                X = r.X,
                Y = r.Y,
                Width = r.Width,
                Height = r.Height
            });
        }

        return regions;
    }
}
