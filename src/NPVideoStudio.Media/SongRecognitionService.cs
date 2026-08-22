using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;

namespace NPVideoStudio.Media;

/// <summary>
/// Real Chromaprint fingerprinting via the external `fpcalc` tool, on five fixed windows of the track
/// (start/quarter/mid/three-quarter/end, spec Phase 4), each extracted first via ffmpeg since fpcalc
/// itself only ever reads from the start of whatever file it's given. Matching requires at least 2
/// agreeing windows plus a confidence floor and a sane duration ratio before it is ever eligible for
/// auto-accept - never guesses off a single window (spec Phase 4).
/// </summary>
public sealed class SongRecognitionService : ISongRecognitionService
{
    private const double AgreementThreshold = 0.65;
    private const double AutoAcceptConfidence = 0.80;
    private const int MinAutoAcceptAgreeingWindows = 2;

    private static readonly (string Label, double FractionOfDuration)[] WindowPositions =
    {
        ("start", 0.0),
        ("quarter", 0.25),
        ("mid", 0.5),
        ("three_quarter", 0.75),
        ("end", 1.0)
    };

    private readonly string _ffmpegPath;
    private readonly string _fpcalcPath;
    private readonly IMediaProbeService _mediaProbeService;
    private readonly int _windowSeconds;

    public SongRecognitionService(
        IMediaProbeService mediaProbeService,
        string? ffmpegOverridePath = null,
        string? fpcalcOverridePath = null,
        int windowSeconds = 8)
    {
        _mediaProbeService = mediaProbeService;
        _ffmpegPath = FfmpegLocator.ResolveFfmpegPath(ffmpegOverridePath);
        _fpcalcPath = FfmpegLocator.ResolveFpcalcPath(fpcalcOverridePath);
        _windowSeconds = Math.Clamp(windowSeconds, 5, 15);
    }

    public async Task<SongFingerprintResult> ComputeFingerprintAsync(string audioFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(audioFilePath))
        {
            throw new FileNotFoundException("Audio fajl nije pronađen.", audioFilePath);
        }

        var asset = await _mediaProbeService.ProbeAsync(audioFilePath, cancellationToken).ConfigureAwait(false);
        if (asset.ProbeError is not null)
        {
            throw new InvalidOperationException($"Pesma nije mogla da se analizira: {asset.ProbeError}");
        }

        var durationSeconds = asset.Duration.TotalSeconds;
        if (durationSeconds <= 0)
        {
            throw new InvalidOperationException("Nije moguće utvrditi trajanje pesme.");
        }

        var windows = new List<SongFingerprintWindow>();
        var tempDir = Path.Combine(Path.GetTempPath(), "npvs-fingerprint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            foreach (var (label, fraction) in WindowPositions)
            {
                var clipLength = Math.Min(_windowSeconds, durationSeconds);
                var maxStart = Math.Max(0, durationSeconds - clipLength);
                var offsetSeconds = Math.Clamp(durationSeconds * fraction, 0, maxStart);

                var clipPath = Path.Combine(tempDir, $"{label}.wav");
                await ExtractClipAsync(audioFilePath, offsetSeconds, clipLength, clipPath, cancellationToken).ConfigureAwait(false);

                var raw = await RunFpcalcAsync(clipPath, cancellationToken).ConfigureAwait(false);
                windows.Add(new SongFingerprintWindow { Label = label, OffsetSeconds = offsetSeconds, Raw = raw });
            }
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return new SongFingerprintResult { DurationSeconds = durationSeconds, Windows = windows };
    }

    public IReadOnlyList<SongMatchCandidate> FindMatches(SongFingerprintResult candidate, IReadOnlyList<SongLibraryEntry> library)
    {
        var results = new List<SongMatchCandidate>();

        foreach (var entry in library)
        {
            SongFingerprintResult libraryFingerprint;
            try
            {
                libraryFingerprint = JsonSerializer.Deserialize<SongFingerprintResult>(entry.Fingerprint)
                    ?? throw new InvalidOperationException("empty");
            }
            catch (Exception)
            {
                continue; // corrupt/legacy record - not a match candidate, not a crash
            }

            var agreeing = 0;
            var conflicting = 0;
            var agreeingSimilarities = new List<double>();

            foreach (var window in candidate.Windows)
            {
                var candidateRaw = FingerprintMatcher.ParseRaw(window.Raw);
                var bestForWindow = 0.0;

                foreach (var libraryWindow in libraryFingerprint.Windows)
                {
                    var libraryRaw = FingerprintMatcher.ParseRaw(libraryWindow.Raw);
                    var (similarity, _) = FingerprintMatcher.Compare(candidateRaw, libraryRaw);
                    bestForWindow = Math.Max(bestForWindow, similarity);
                }

                if (bestForWindow >= AgreementThreshold)
                {
                    agreeing++;
                    agreeingSimilarities.Add(bestForWindow);
                }
                else
                {
                    conflicting++;
                }
            }

            if (agreeing == 0)
            {
                continue;
            }

            var confidence = agreeingSimilarities.Average();
            var durationRatio = libraryFingerprint.DurationSeconds > 0
                ? candidate.DurationSeconds / libraryFingerprint.DurationSeconds
                : 1.0;

            var warnings = new List<string>();
            if (durationRatio is < 0.9 or > 1.1)
            {
                warnings.Add("Trajanje pesme se značajno razlikuje od zapisa u biblioteci.");
            }

            var autoAcceptEligible = agreeing >= MinAutoAcceptAgreeingWindows
                && confidence >= AutoAcceptConfidence
                && durationRatio is >= 0.9 and <= 1.1;

            results.Add(new SongMatchCandidate
            {
                LibraryEntryId = entry.Id,
                Title = entry.Title,
                Confidence = confidence,
                AgreeingWindows = agreeing,
                ConflictingWindows = conflicting,
                DurationRatio = durationRatio,
                AutoAcceptEligible = autoAcceptEligible,
                Warnings = warnings
            });
        }

        return results.OrderByDescending(r => r.Confidence).ThenByDescending(r => r.AgreeingWindows).Take(3).ToList();
    }

    private async Task ExtractClipAsync(
        string sourcePath, double startSeconds, double lengthSeconds, string outputPath, CancellationToken cancellationToken)
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

        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-ss");
        process.StartInfo.ArgumentList.Add(startSeconds.ToString(CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(sourcePath);
        process.StartInfo.ArgumentList.Add("-t");
        process.StartInfo.ArgumentList.Add(lengthSeconds.ToString(CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add("-vn");
        process.StartInfo.ArgumentList.Add("-ac");
        process.StartInfo.ArgumentList.Add("1");
        process.StartInfo.ArgumentList.Add(outputPath);

        process.Start();
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdErr = await stdErrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stdErr)
                ? $"Izdvajanje isečka za analizu otiska nije uspelo (kod {process.ExitCode})."
                : stdErr.Trim());
        }
    }

    private async Task<string> RunFpcalcAsync(string clipPath, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _fpcalcPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-raw");
        process.StartInfo.ArgumentList.Add(clipPath);

        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdOut = await stdOutTask.ConfigureAwait(false);
        var stdErr = await stdErrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stdErr)
                ? $"fpcalc nije uspeo (kod {process.ExitCode}). Da li je Chromaprint (fpcalc) instaliran?"
                : stdErr.Trim());
        }

        var line = stdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => l.StartsWith("FINGERPRINT=", StringComparison.Ordinal));

        return line is null ? string.Empty : line["FINGERPRINT=".Length..].Trim();
    }
}
