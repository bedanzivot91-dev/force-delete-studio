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
            await CheckAiWorkerAsync(cancellationToken).ConfigureAwait(false)
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
        var modelPath = Path.Combine(AppSettings.ModelsFolder(), "ggml-tiny.bin");

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
        var anyEngine = capabilities.FasterWhisperAvailable || capabilities.WhisperXAvailable || capabilities.DemucsAvailable;
        var installed = capabilities.WorkerReachable && anyEngine;

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
            WhyItMatters = "Potreban samo za profile „Balanced“/„Most accurate“ - profil „Fast“ (Whisper.net) radi i bez njega.",
            TechnicalDetails = details
        };
    }

    public Task DownloadWhisperModelAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        _lyricSearchService.DownloadModelAsync(progress, cancellationToken);

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
