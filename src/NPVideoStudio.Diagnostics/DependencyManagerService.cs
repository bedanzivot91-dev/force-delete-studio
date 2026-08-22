using System.Text.RegularExpressions;
using NPVideoStudio.Core.Diagnostics;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;

namespace NPVideoStudio.Diagnostics;

/// <summary>
/// Real status for every external tool/model the app depends on today. The service never calls a file
/// "installed" merely because it exists: executables must complete their real version command and the
/// local AI worker must report the capabilities required by the features that consume it.
/// </summary>
public sealed class DependencyManagerService : IDependencyManagerService
{
    private readonly ISettingsService _settingsService;
    private readonly ILyricSearchService _lyricSearchService;
    private readonly IAiWorkerClient _aiWorkerClient;

    public DependencyManagerService(
        ISettingsService settingsService, ILyricSearchService lyricSearchService, IAiWorkerClient aiWorkerClient)
    {
        _settingsService = settingsService;
        _lyricSearchService = lyricSearchService;
        _aiWorkerClient = aiWorkerClient;
    }

    public async Task<IReadOnlyList<DependencyInfo>> GetDependenciesAsync(CancellationToken cancellationToken = default)
    {
        return new List<DependencyInfo>
        {
            await CheckToolAsync(
                "FFmpeg",
                FfmpegLocator.ResolveFfmpegPath(_settingsService.Current.FfmpegPath),
                "-version",
                "Neophodan za uvoz, analizu i obradu video/audio fajlova.",
                minimumMajorVersion: 6,
                expectedVersion: "6+",
                license: "GPLv3 build — vidi THIRD_PARTY_NOTICES.md",
                cancellationToken).ConfigureAwait(false),
            await CheckToolAsync(
                "FFprobe",
                FfmpegLocator.ResolveFfprobePath(_settingsService.Current.FfprobePath),
                "-version",
                "Neophodan za analizu trajanja, rezolucije i kodeka medijskih fajlova.",
                minimumMajorVersion: 6,
                expectedVersion: "6+",
                license: "FFmpeg project — vidi THIRD_PARTY_NOTICES.md",
                cancellationToken).ConfigureAwait(false),
            await CheckToolAsync(
                "yt-dlp",
                FfmpegLocator.ResolveYtDlpPath(_settingsService.Current.YtDlpPath),
                "--version",
                "Potreban samo za alat „Preuzmi sa YouTube-a“ - ostatak programa radi i bez njega.",
                minimumMajorVersion: null,
                expectedVersion: null,
                license: "Unlicense — vidi Licenses folder",
                cancellationToken).ConfigureAwait(false),
            await CheckToolAsync(
                "fpcalc (Chromaprint)",
                FfmpegLocator.ResolveFpcalcPath(null),
                "-version",
                "Potreban samo za prepoznavanje pesama u „Moje pesme“ (otisak pesme) - ostatak programa radi i bez njega.",
                minimumMajorVersion: 1,
                expectedVersion: "1.1+",
                license: "Chromaprint/LGPL 2.1 — vidi THIRD_PARTY_NOTICES.md",
                cancellationToken).ConfigureAwait(false),
            CheckWhisperModel(),
            await CheckAiWorkerAsync(cancellationToken).ConfigureAwait(false),
            await CheckToolAsync(
                "Tesseract OCR",
                FfmpegLocator.ResolveTesseractPath(null),
                "--version",
                "Potreban samo za analizu rasporeda videa (prepoznavanje postojećeg teksta u kadru) - ostatak programa radi i bez njega.",
                minimumMajorVersion: 5,
                expectedVersion: "5+",
                license: "Apache 2.0 / Leptonica BSD — vidi Licenses folder",
                cancellationToken).ConfigureAwait(false)
        };
    }

    private static async Task<DependencyInfo> CheckToolAsync(
        string name,
        string path,
        string versionArgument,
        string whyItMatters,
        int? minimumMajorVersion,
        string? expectedVersion,
        string? license,
        CancellationToken cancellationToken)
    {
        var concreteFileExists = Path.IsPathFullyQualified(path) && File.Exists(path);
        var (found, version) = await FfmpegLocator.TryGetVersionAsync(path, versionArgument, cancellationToken).ConfigureAwait(false);

        DependencyStatus status;
        string details;
        if (!found)
        {
            // If NP resolved a concrete file and that file exists but its real version command cannot
            // run successfully, reporting "not installed" hides a damaged/wrong executable. A bare PATH
            // command that cannot be started is simply absent from the machine.
            status = concreteFileExists ? DependencyStatus.Corrupt : DependencyStatus.NotInstalled;
            details = concreteFileExists
                ? $"Fajl postoji, ali provera verzije nije uspela: {path}"
                : $"Tražena putanja/komanda: {path}";
        }
        else if (minimumMajorVersion is int requiredMajor)
        {
            var actualMajor = ExtractMajorVersion(version);
            if (actualMajor is null || actualMajor.Value < requiredMajor)
            {
                status = DependencyStatus.Incompatible;
                details = actualMajor is null
                    ? $"Alat radi, ali verziju nije moguće pouzdano protumačiti. Potrebno: {expectedVersion}. Putanja: {path}"
                    : $"Pronađena glavna verzija {actualMajor}; potrebno {expectedVersion}. Putanja: {path}";
            }
            else
            {
                status = DependencyStatus.Installed;
                details = $"Putanja: {path}";
            }
        }
        else
        {
            status = DependencyStatus.Installed;
            details = $"Putanja: {path}";
        }

        return new DependencyInfo
        {
            Name = name,
            Status = status,
            Version = version,
            ExpectedVersion = expectedVersion,
            Path = found || concreteFileExists ? path : null,
            WhyItMatters = whyItMatters,
            CanOpenFolder = concreteFileExists,
            License = license,
            LastCheckedUtc = DateTimeOffset.UtcNow,
            TechnicalDetails = details
        };
    }

    /// <summary>Extracts the first conventional dotted-version major component from real command output.
    /// FFmpeg emits "ffmpeg version 9.0.1", Tesseract emits "tesseract 5.x", and fpcalc emits a 1.x
    /// version. We deliberately do not apply this policy to yt-dlp's date-style version.</summary>
    private static int? ExtractMajorVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = Regex.Match(text, @"(?<!\d)(\d+)\.\d+");
        return match.Success && int.TryParse(match.Groups[1].Value, out var major) ? major : null;
    }

    private DependencyInfo CheckWhisperModel()
    {
        var ready = _lyricSearchService.IsModelReady;
        var modelPath = _lyricSearchService.ModelPath;
        var modelFileExists = !string.IsNullOrWhiteSpace(modelPath) && File.Exists(modelPath);
        var status = ready
            ? DependencyStatus.Installed
            : modelFileExists ? DependencyStatus.Corrupt : DependencyStatus.NotInstalled;

        return new DependencyInfo
        {
            Name = "Whisper model (prepoznavanje govora)",
            Status = status,
            Version = ready ? "tiny" : null,
            ExpectedVersion = "ggml-tiny.bin",
            Path = ready || modelFileExists ? modelPath : null,
            WhyItMatters = "Potreban za alate „Pronađi tekst u pesmi“ i „Generiši titlove“ - ostatak programa radi i bez njega.",
            CanDownload = !ready,
            CanRepair = !ready,
            CanOpenFolder = ready || modelFileExists,
            License = "Whisper/whisper.cpp — vidi THIRD_PARTY_NOTICES.md",
            LastCheckedUtc = DateTimeOffset.UtcNow,
            TechnicalDetails = ready
                ? $"Putanja: {modelPath}"
                : modelFileExists
                    ? $"Model postoji na {modelPath}, ali ga servis ne prihvata kao spreman; preuzimanje ponovo može da ga popravi."
                    : _lyricSearchService.ModelSizeLabel
        };
    }

    private async Task<DependencyInfo> CheckAiWorkerAsync(CancellationToken cancellationToken)
    {
        var capabilities = await _aiWorkerClient.CheckCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        var allRequiredEngines = capabilities.WorkerReachable &&
                                 capabilities.FasterWhisperAvailable &&
                                 capabilities.DemucsAvailable &&
                                 capabilities.LyricAlignAvailable &&
                                 capabilities.OpenCvAvailable;

        var pythonVersion = ParsePythonVersion(capabilities.PythonVersion);
        var pythonCompatible = pythonVersion is { Major: 3, Minor: >= 12 } || pythonVersion is { Major: > 3 };

        var status = !allRequiredEngines
            ? DependencyStatus.NotInstalled
            : pythonCompatible ? DependencyStatus.Installed : DependencyStatus.Incompatible;

        var details = capabilities.WorkerReachable
            ? $"faster-whisper: {(capabilities.FasterWhisperAvailable ? "da" : "ne")}, " +
              $"WhisperX: {(capabilities.WhisperXAvailable ? "da" : "ne")}, " +
              $"Demucs: {(capabilities.DemucsAvailable ? "da" : "ne")}, " +
              $"lyric-align: {(capabilities.LyricAlignAvailable ? "da" : "ne")}, " +
              $"OpenCV/CSRT: {(capabilities.OpenCvAvailable ? "da" : "ne")}" +
              (capabilities.PythonVersion is null ? "" : $" (Python {capabilities.PythonVersion})") +
              (allRequiredEngines && !pythonCompatible ? "; potreban Python 3.12+" : string.Empty)
            : capabilities.Error ?? "AI worker nije dostupan.";

        return new DependencyInfo
        {
            Name = "AI radnik (napredna obrada govora)",
            Status = status,
            Version = capabilities.PythonVersion,
            ExpectedVersion = "Python 3.12+ + faster-whisper + Demucs + lyric-align + OpenCV/CSRT",
            WhyItMatters = "Za pesme moraju raditi faster-whisper, Demucs i lyric-align; OpenCV/CSRT je potreban za Motion Tracking i Auto Reframe. WhisperX je opcion za napredno poravnanje.",
            CanRepair = status != DependencyStatus.Installed,
            License = "Komponente imaju zasebne licence — vidi THIRD_PARTY_NOTICES.md",
            LastCheckedUtc = DateTimeOffset.UtcNow,
            TechnicalDetails = details
        };
    }

    private static Version? ParsePythonVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var match = Regex.Match(version, @"(\d+)\.(\d+)(?:\.(\d+))?");
        if (!match.Success) return null;
        var major = int.Parse(match.Groups[1].Value);
        var minor = int.Parse(match.Groups[2].Value);
        var patch = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
        return new Version(major, minor, patch);
    }

    public Task DownloadWhisperModelAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        _lyricSearchService.DownloadModelAsync(progress, cancellationToken);

    public Task InstallSongAiAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        _aiWorkerClient.InstallSongAiAsync(progress, cancellationToken);

    public void OpenContainingFolder(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true
        });
    }
}
