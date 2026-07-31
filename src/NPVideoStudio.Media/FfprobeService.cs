using System.Diagnostics;
using System.Text.Json;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;

namespace NPVideoStudio.Media;

public sealed class FfprobeService : IMediaProbeService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tiff", ".tif" };

    private readonly string _ffprobePath;

    public FfprobeService(string? ffprobeOverridePath = null)
    {
        _ffprobePath = FfmpegLocator.ResolveFfprobePath(ffprobeOverridePath);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        var (found, _) = await FfmpegLocator.TryGetVersionAsync(_ffprobePath, cancellationToken).ConfigureAwait(false);
        return found;
    }

    public async Task<MediaAsset> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return new MediaAsset { FilePath = filePath, IsMissing = true, ProbeError = "Fajl nije pronađen na disku." };
        }

        var asset = new MediaAsset
        {
            FilePath = filePath,
            FileSizeBytes = new FileInfo(filePath).Length
        };

        var extension = Path.GetExtension(filePath);
        if (ImageExtensions.Contains(extension))
        {
            asset.Kind = MediaKind.Image;
        }

        try
        {
            var json = await RunFfprobeAsync(filePath, cancellationToken).ConfigureAwait(false);
            var parsed = JsonSerializer.Deserialize<FfprobeOutput>(json);

            if (parsed is null)
            {
                asset.ProbeError = "ffprobe nije vratio čitljive podatke za ovaj fajl.";
                return asset;
            }

            ApplyProbeResult(asset, parsed);
        }
        catch (Exception ex)
        {
            asset.ProbeError = $"Analiza fajla nije uspela: {ex.Message}";
        }

        return asset;
    }

    private async Task<string> RunFfprobeAsync(string filePath, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _ffprobePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-v");
        process.StartInfo.ArgumentList.Add("quiet");
        process.StartInfo.ArgumentList.Add("-print_format");
        process.StartInfo.ArgumentList.Add("json");
        process.StartInfo.ArgumentList.Add("-show_format");
        process.StartInfo.ArgumentList.Add("-show_streams");
        process.StartInfo.ArgumentList.Add(filePath);

        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdOut = await stdOutTask.ConfigureAwait(false);
        var stdErr = await stdErrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stdErr)
                ? $"ffprobe je vratio kod greške {process.ExitCode}."
                : stdErr.Trim());
        }

        return stdOut;
    }

    private static void ApplyProbeResult(MediaAsset asset, FfprobeOutput parsed)
    {
        var videoStream = parsed.Streams.FirstOrDefault(s => s.CodecType == "video");
        var audioStream = parsed.Streams.FirstOrDefault(s => s.CodecType == "audio");

        asset.HasVideoStream = videoStream is not null;
        asset.HasAudioStream = audioStream is not null;

        if (videoStream is not null)
        {
            asset.VideoCodec = videoStream.CodecName;
            asset.Width = videoStream.Width ?? 0;
            asset.Height = videoStream.Height ?? 0;
            asset.Fps = ParseFrameRate(videoStream.AvgFrameRate) ?? ParseFrameRate(videoStream.RFrameRate) ?? 0;

            if (asset.Kind == MediaKind.Unknown)
            {
                asset.Kind = asset.Fps > 0 || parsed.Streams.Count(s => s.CodecType == "video") > 0 && (videoStream.Duration is not null || parsed.Format?.Duration is not null)
                    ? MediaKind.Video
                    : MediaKind.Image;
            }
        }

        if (audioStream is not null)
        {
            asset.AudioCodec = audioStream.CodecName;
            if (asset.Kind == MediaKind.Unknown)
            {
                asset.Kind = MediaKind.Audio;
            }
        }

        var durationText = parsed.Format?.Duration ?? videoStream?.Duration ?? audioStream?.Duration;
        if (durationText is not null && double.TryParse(durationText, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
        {
            asset.Duration = TimeSpan.FromSeconds(seconds);
        }

        if (asset.Kind == MediaKind.Unknown && asset.Width > 0 && asset.Height > 0)
        {
            asset.Kind = MediaKind.Image;
        }
    }

    private static double? ParseFrameRate(string? fraction)
    {
        if (string.IsNullOrWhiteSpace(fraction))
        {
            return null;
        }

        var parts = fraction.Split('/');
        if (parts.Length != 2)
        {
            return double.TryParse(fraction, System.Globalization.CultureInfo.InvariantCulture, out var single) ? single : null;
        }

        if (double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var num) &&
            double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var den) &&
            den != 0)
        {
            return num / den;
        }

        return null;
    }
}
