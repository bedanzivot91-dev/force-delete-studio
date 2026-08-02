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

    public DependencyManagerService(ISettingsService settingsService, ILyricSearchService lyricSearchService)
    {
        _settingsService = settingsService;
        _lyricSearchService = lyricSearchService;
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
            CheckWhisperModel()
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
