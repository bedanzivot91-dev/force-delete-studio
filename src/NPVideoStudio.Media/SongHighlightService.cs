using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;

namespace NPVideoStudio.Media;

/// <summary>
/// Finds loud sections of a song via ffmpeg's astats filter (1-second RMS loudness windows) and cuts
/// them out as standalone audio clips. This is a loudness heuristic meant to give a fast starting
/// point for song-announcement Shorts/Reels - it does not detect the chorus or a musical "hook",
/// just the loudest non-overlapping stretches, and the app must never claim otherwise.
/// </summary>
public sealed class SongHighlightService : ISongHighlightService
{
    private static readonly Regex TimeRegex = new(@"pts_time:(?<t>[\d.]+)", RegexOptions.Compiled);
    private static readonly Regex RmsRegex = new(@"RMS_level=(?<db>-?[\d.]+)", RegexOptions.Compiled);

    private readonly string _ffmpegPath;
    private readonly IMediaProbeService _mediaProbeService;

    public SongHighlightService(IMediaProbeService mediaProbeService, string? ffmpegOverridePath = null)
    {
        _mediaProbeService = mediaProbeService;
        _ffmpegPath = FfmpegLocator.ResolveFfmpegPath(ffmpegOverridePath);
    }

    public async Task<IReadOnlyList<SongHighlight>> FindHighlightsAsync(
        string audioFilePath,
        int count = 3,
        TimeSpan? minDuration = null,
        TimeSpan? maxDuration = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(audioFilePath))
        {
            throw new FileNotFoundException("Audio fajl nije pronađen.", audioFilePath);
        }

        var min = minDuration ?? TimeSpan.FromSeconds(30);
        var max = maxDuration ?? TimeSpan.FromSeconds(50);
        var windowSeconds = (int)Math.Round((min.TotalSeconds + max.TotalSeconds) / 2.0);

        var asset = await _mediaProbeService.ProbeAsync(audioFilePath, cancellationToken);
        if (asset.ProbeError is not null)
        {
            throw new InvalidOperationException($"Pesma nije mogla da se analizira: {asset.ProbeError}");
        }

        var totalSeconds = (int)Math.Floor(asset.Duration.TotalSeconds);
        if (totalSeconds <= 0)
        {
            throw new InvalidOperationException("Nije moguće utvrditi trajanje pesme.");
        }

        if (windowSeconds >= totalSeconds)
        {
            // Track is shorter than one highlight window - the whole track is the only candidate.
            return new[]
            {
                new SongHighlight
                {
                    Start = TimeSpan.Zero,
                    Duration = asset.Duration,
                    AverageLoudnessDb = 0
                }
            };
        }

        var perSecondLoudness = await AnalyzeLoudnessPerSecondAsync(audioFilePath, cancellationToken);
        return PickNonOverlappingHighlights(perSecondLoudness, totalSeconds, windowSeconds, count);
    }

    private async Task<double[]> AnalyzeLoudnessPerSecondAsync(string audioFilePath, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(audioFilePath);
        process.StartInfo.ArgumentList.Add("-af");
        process.StartInfo.ArgumentList.Add(
            "aresample=44100,asetnsamples=n=44100,astats=metadata=1:reset=1,ametadata=print:key=lavfi.astats.Overall.RMS_level:file=-");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("null");
        process.StartInfo.ArgumentList.Add("-");

        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stdErr)
                ? $"Analiza glasnoće nije uspela (kod {process.ExitCode})."
                : stdErr.Trim());
        }

        var times = TimeRegex.Matches(stdOut);
        var levels = RmsRegex.Matches(stdOut);
        var count = Math.Min(times.Count, levels.Count);

        var result = new List<double>(count);
        for (var i = 0; i < count; i++)
        {
            var second = (int)double.Parse(times[i].Groups["t"].Value, CultureInfo.InvariantCulture);
            var db = double.Parse(levels[i].Groups["db"].Value, CultureInfo.InvariantCulture);

            while (result.Count < second)
            {
                result.Add(-90.0); // silence-filled gap, keeps the index aligned to whole seconds
            }
            if (result.Count == second)
            {
                result.Add(db);
            }
        }

        return result.ToArray();
    }

    private static List<SongHighlight> PickNonOverlappingHighlights(
        double[] perSecondDb, int totalSeconds, int windowSeconds, int count)
    {
        // Loudness in dB isn't linearly additive - convert to linear power for a correct window average.
        var linear = perSecondDb.Select(db => Math.Pow(10, db / 20.0)).ToArray();

        var prefixSums = new double[linear.Length + 1];
        for (var i = 0; i < linear.Length; i++)
        {
            prefixSums[i + 1] = prefixSums[i] + linear[i];
        }

        var lastStart = Math.Min(totalSeconds, linear.Length) - windowSeconds;
        var candidates = new List<(int Start, double AverageLinear)>();
        for (var start = 0; start <= lastStart; start++)
        {
            var sum = prefixSums[start + windowSeconds] - prefixSums[start];
            candidates.Add((start, sum / windowSeconds));
        }

        candidates.Sort((a, b) => b.AverageLinear.CompareTo(a.AverageLinear));

        var picked = new List<SongHighlight>();
        foreach (var candidate in candidates)
        {
            if (picked.Count >= count)
            {
                break;
            }

            var candidateEnd = candidate.Start + windowSeconds;
            var overlaps = picked.Any(p =>
                candidate.Start < p.Start.TotalSeconds + p.Duration.TotalSeconds && candidateEnd > p.Start.TotalSeconds);

            if (overlaps)
            {
                continue;
            }

            var averageDb = 20 * Math.Log10(Math.Max(candidate.AverageLinear, 1e-9));
            picked.Add(new SongHighlight
            {
                Start = TimeSpan.FromSeconds(candidate.Start),
                Duration = TimeSpan.FromSeconds(windowSeconds),
                AverageLoudnessDb = averageDb
            });
        }

        return picked.OrderBy(h => h.Start).ToList();
    }

    public async Task ExportHighlightAsync(
        string audioFilePath, SongHighlight highlight, string outputFilePath, CancellationToken cancellationToken = default)
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
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-ss");
        process.StartInfo.ArgumentList.Add(highlight.Start.TotalSeconds.ToString(CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(audioFilePath);
        process.StartInfo.ArgumentList.Add("-t");
        process.StartInfo.ArgumentList.Add(highlight.Duration.TotalSeconds.ToString(CultureInfo.InvariantCulture));
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

        highlight.ExportedFilePath = outputFilePath;
    }
}
