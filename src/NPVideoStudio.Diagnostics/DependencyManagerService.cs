using NPVideoStudio.Core.Diagnostics;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;

namespace NPVideoStudio.Diagnostics;

/// <summary>
/// Real status for every external tool/model the app depends on today. Reuses the same resolution and
/// version-check logic as <see cref="DiagnosticsService"/> (FfmpegLocator) and the already-registered
/// <see cref="ILyricSearchService"/> for the Whisper model, instead of duplicating either.
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
                cancellationToken).ConfigureAwait(false),
            await CheckToolAsync(
                "FFprobe",
                FfmpegLocator.ResolveFfprobePath(_settingsService.Current.FfprobePath),
                "-version",
                "Neophodan za analizu trajanja, rezolucije i kodeka medijskih fajlova.",
                cancellationToken).ConfigureAwait(false),
            await CheckToolAsync(
                "yt-dlp",
                FfmpegLocator.ResolveYtDlpPath(_settingsService.Current.YtDlpPath),
                "--version",
                "Potreban samo za alat „Preuzmi sa YouTube-a“ - ostatak programa radi i bez njega.",
                cancellationToken).ConfigureAwait(false),
            await CheckToolAsync(
                "fpcalc (Chromaprint)",
                FfmpegLocator.ResolveFpcalcPath(null),
                "-version",
                "Potreban samo za prepoznavanje pesama u „Moje pesme“ (otisak pesme) - ostatak programa radi i bez njega.",
                cancellationToken).ConfigureAwait(false),
            CheckWhisperModel(),
            await CheckAiWorkerAsync(cancellationToken).ConfigureAwait(false),
            await CheckToolAsync(
                "Tesseract OCR",
                FfmpegLocator.ResolveTesseractPath(null),
                "--version",
                "Potreban samo za analizu rasporeda videa (prepoznavanje postojećeg teksta u kadru) - ostatak programa radi i bez njega.",
                cancellationToken).ConfigureAwait(false)
        };
    }

    private static async Task<DependencyInfo> CheckToolAsync(
        string name, string path, string versionArgument, string whyItMatters, CancellationToken cancellationToken)
    {
        var (found, version) = await FfmpegLocator.TryGetVersionAsync(path, versionArgument, cancellationToken).ConfigureAwait(false);

        return new DependencyInfo
        {
            Name = name,
            Status = found ? DependencyStatus.Installed : DependencyStatus.NotInstalled,
            Version = version,
            Path = found ? path : null,
            WhyItMatters = whyItMatters,
            CanOpenFolder = found,
            TechnicalDetails = found ? $"Putanja: {path}" : $"Tražena putanja: {path}"
        };
    }

    private DependencyInfo CheckWhisperModel()
    {
        var ready = _lyricSearchService.IsModelReady;
        // Real bug found and fixed: this used to always reconstruct the AppData default path here,
        // even when the model was actually resolved from the bundled Tools/whisper-models/ggml-tiny.bin
        // next to the exe (see WhisperModelLocator) - "Otvori folder" opened a path that didn't exist,
        // or (when not ready) showed no real path for the user to place the file manually.
        var modelPath = _lyricSearchService.ModelPath;

        return new DependencyInfo
        {
            Name = "Whisper model (prepoznavanje govora)",
            Status = ready ? DependencyStatus.Installed : DependencyStatus.NotInstalled,
            Version = ready ? "tiny" : null,
            Path = ready ? modelPath : null,
            WhyItMatters = "Potreban za alate „Pronađi tekst u pesmi“ i „Generiši titlove“ - ostatak programa radi i bez njega.",
            CanDownload = !ready,
            CanOpenFolder = ready,
            TechnicalDetails = ready ? $"Putanja: {modelPath}" : _lyricSearchService.ModelSizeLabel
        };
    }

    private async Task<DependencyInfo> CheckAiWorkerAsync(CancellationToken cancellationToken)
    {
        var capabilities = await _aiWorkerClient.CheckCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        // The song button requires BOTH transcription and vocal separation. Reporting the worker as
        // installed when only Demucs (or only faster-whisper) happened to import made the diagnostics
        // screen green while the actual song workflow still could not do what it promised.
        var installed = capabilities.WorkerReachable &&
                        capabilities.FasterWhisperAvailable &&
                        capabilities.DemucsAvailable;

        var details = capabilities.WorkerReachable
            ? $"faster-whisper: {(capabilities.FasterWhisperAvailable ? "da" : "ne")}, " +
              $"WhisperX: {(capabilities.WhisperXAvailable ? "da" : "ne")}, " +
              $"Demucs: {(capabilities.DemucsAvailable ? "da" : "ne")}" +
              (capabilities.PythonVersion is null ? "" : $" (Python {capabilities.PythonVersion})")
            : capabilities.Error ?? "AI worker nije dostupan.";

        return new DependencyInfo
        {
            Name = "AI radnik (napredna obrada govora)",
            Status = installed ? DependencyStatus.Installed : DependencyStatus.NotInstalled,
            Version = capabilities.PythonVersion,
            WhyItMatters = "Za automatsko prepoznavanje stihova moraju raditi i faster-whisper i Demucs. WhisperX je opcion za napredno poravnanje.",
            TechnicalDetails = details
        };
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
