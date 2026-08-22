using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using NPVideoStudio.Core.Services;

namespace NPVideoStudio.Media;

/// <summary>
/// Real FFmpeg scene-change analysis. FFmpeg's scdet filter emits lavfi.scd.score and lavfi.scd.time when
/// a frame crosses the configured threshold. We reset the filtered segment's PTS to zero, then convert
/// those local timestamps back to the original source clock so timeline trim/speed mapping stays exact.
/// </summary>
public sealed partial class SceneDetectionService : ISceneDetectionService
{
    private readonly string _ffmpegPath;

    [GeneratedRegex(@"lavfi\.scd\.score:\s*(?<score>[0-9]+(?:\.[0-9]+)?),\s*lavfi\.scd\.time:\s*(?<time>-?[0-9]+(?:\.[0-9]+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex SceneLogRegex();

    public SceneDetectionService(string? ffmpegOverridePath = null)
    {
        _ffmpegPath = FfmpegLocator.ResolveFfmpegPath(ffmpegOverridePath);
    }

    public async Task<IReadOnlyList<SceneChange>> DetectAsync(
        string sourceFilePath,
        double sourceStartSeconds,
        double sourceEndSeconds,
        double thresholdPercent = 10.0,
        double minimumSpacingSeconds = 0.35,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("Video za Scene Detection nije pronađen.", sourceFilePath);
        }

        var start = Math.Max(0, sourceStartSeconds);
        var end = Math.Max(start, sourceEndSeconds);
        var duration = end - start;
        if (duration < 0.10)
        {
            return Array.Empty<SceneChange>();
        }

        var threshold = Math.Clamp(thresholdPercent, 0.1, 100.0);
        var spacing = Math.Clamp(minimumSpacingSeconds, 0.05, Math.Max(0.05, duration));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-hide_banner");
        process.StartInfo.ArgumentList.Add("-nostdin");
        process.StartInfo.ArgumentList.Add("-loglevel");
        process.StartInfo.ArgumentList.Add("info");
        process.StartInfo.ArgumentList.Add("-ss");
        process.StartInfo.ArgumentList.Add(start.ToString("0.######", CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add("-t");
        process.StartInfo.ArgumentList.Add(duration.ToString("0.######", CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(sourceFilePath);
        process.StartInfo.ArgumentList.Add("-map");
        process.StartInfo.ArgumentList.Add("0:v:0");
        process.StartInfo.ArgumentList.Add("-vf");
        process.StartInfo.ArgumentList.Add($"setpts=PTS-STARTPTS,scdet=threshold={threshold.ToString("0.###", CultureInfo.InvariantCulture)}:sc_pass=0");
        process.StartInfo.ArgumentList.Add("-an");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("null");
        process.StartInfo.ArgumentList.Add("-");

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("FFmpeg Scene Detection proces nije mogao da se pokrene.");
            }

            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            _ = await stdoutTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                    ? $"FFmpeg Scene Detection nije uspeo (kod {process.ExitCode})."
                    : stderr.Trim());
            }

            return ParseSceneLog(stderr, start, end, spacing);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Cancellation should win over a best-effort process cleanup failure.
            }
            throw;
        }
    }

    internal static IReadOnlyList<SceneChange> ParseSceneLog(
        string stderr,
        double sourceStartSeconds,
        double sourceEndSeconds,
        double minimumSpacingSeconds)
    {
        var duration = Math.Max(0, sourceEndSeconds - sourceStartSeconds);
        var epsilon = Math.Min(0.05, duration / 4.0);
        var spacing = Math.Max(0.05, minimumSpacingSeconds);
        var detected = new List<SceneChange>();

        foreach (Match match in SceneLogRegex().Matches(stderr ?? string.Empty))
        {
            if (!double.TryParse(match.Groups["score"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var score) ||
                !double.TryParse(match.Groups["time"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var localTime))
            {
                continue;
            }

            if (localTime <= epsilon || localTime >= duration - epsilon)
            {
                continue; // Never create a microscopic segment at either visible clip edge.
            }

            var absoluteSourceTime = sourceStartSeconds + localTime;
            if (detected.Count == 0 || absoluteSourceTime - detected[^1].SourceTimeSeconds >= spacing)
            {
                detected.Add(new SceneChange(absoluteSourceTime, score));
                continue;
            }

            // Several adjacent frames can cross the threshold for one hard cut. Keep only the strongest
            // candidate in the minimum-spacing window rather than producing a cluster of tiny clips.
            if (score > detected[^1].Score)
            {
                detected[^1] = new SceneChange(absoluteSourceTime, score);
            }
        }

        return detected;
    }
}
