using NPVideoStudio.Core.Diagnostics;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Diagnostics;
using NPVideoStudio.Domain;
using NPVideoStudio.Infrastructure.Persistence;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Fake so the dependency-manager tests don't need a real Whisper model on disk.</summary>
public sealed class FakeLyricSearchService : ILyricSearchService
{
    public bool IsModelReady { get; set; }
    public string ModelSizeLabel => "~75 MB (test)";

    public Task DownloadModelAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        IsModelReady = true;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LyricMatch>> FindPhraseInSongAsync(string audioFilePath, string phrase, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LyricMatch>>(Array.Empty<LyricMatch>());

    public Task ExportMatchAsync(string audioFilePath, LyricMatch match, string outputFilePath, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public class DependencyManagerServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"npvs_depmgr_test_{Guid.NewGuid():N}");
    private readonly SettingsService _settingsService;
    private readonly FakeLyricSearchService _lyricSearchService = new();
    private readonly DependencyManagerService _service;

    public DependencyManagerServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
        _settingsService = new SettingsService(Path.Combine(_tempDir, "settings.json"));
        _settingsService.LoadAsync().GetAwaiter().GetResult();
        _service = new DependencyManagerService(_settingsService, _lyricSearchService);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task GetDependenciesAsync_ReturnsFiveEntries()
    {
        var results = await _service.GetDependenciesAsync();

        Assert.Equal(5, results.Count);
        Assert.Contains(results, d => d.Name == "FFmpeg");
        Assert.Contains(results, d => d.Name == "FFprobe");
        Assert.Contains(results, d => d.Name == "yt-dlp");
        Assert.Contains(results, d => d.Name.Contains("fpcalc"));
        Assert.Contains(results, d => d.Name.Contains("Whisper"));
    }

    [Fact]
    public async Task GetDependenciesAsync_FfmpegOnPath_ReportsInstalledWithVersion()
    {
        // ffmpeg/ffprobe are genuinely installed in this environment (apt), so this checks the real
        // version-command exit code path, not a mocked answer.
        var results = await _service.GetDependenciesAsync();
        var ffmpeg = results.Single(d => d.Name == "FFmpeg");

        Assert.Equal(DependencyStatus.Installed, ffmpeg.Status);
        Assert.False(string.IsNullOrWhiteSpace(ffmpeg.Version));
        Assert.True(ffmpeg.CanOpenFolder);
    }

    [Fact]
    public async Task GetDependenciesAsync_YtDlpPathIsNotAnExecutable_ReportsNotInstalled()
    {
        // A *nonexistent* override path would NOT reliably test "not installed": FfmpegLocator.Resolve
        // only honors an override when File.Exists is true, otherwise it falls back to bare "yt-dlp" on
        // PATH - and real CI runners genuinely have yt-dlp installed (via choco) and on PATH, so that
        // fallback would silently find the real tool and this test would flake depending on the
        // environment. Pointing the override at a real file that exists but isn't a valid executable
        // forces FfmpegLocator to use (and fail to run) exactly this path, independent of PATH/environment.
        var notAnExecutable = Path.Combine(_tempDir, "not-a-real-yt-dlp.txt");
        File.WriteAllText(notAnExecutable, "this is not an executable");
        _settingsService.Current.YtDlpPath = notAnExecutable;

        var results = await _service.GetDependenciesAsync();
        var ytDlp = results.Single(d => d.Name == "yt-dlp");

        Assert.Equal(DependencyStatus.NotInstalled, ytDlp.Status);
        Assert.False(ytDlp.CanOpenFolder);
    }

    [Fact]
    public async Task GetDependenciesAsync_WhisperModelNotReady_CanDownloadIsTrue()
    {
        _lyricSearchService.IsModelReady = false;
        var results = await _service.GetDependenciesAsync();
        var whisper = results.Single(d => d.Name.Contains("Whisper"));

        Assert.Equal(DependencyStatus.NotInstalled, whisper.Status);
        Assert.True(whisper.CanDownload);
    }

    [Fact]
    public async Task GetDependenciesAsync_WhisperModelReady_CanDownloadIsFalse()
    {
        _lyricSearchService.IsModelReady = true;
        var results = await _service.GetDependenciesAsync();
        var whisper = results.Single(d => d.Name.Contains("Whisper"));

        Assert.Equal(DependencyStatus.Installed, whisper.Status);
        Assert.False(whisper.CanDownload);
    }

    [Fact]
    public async Task DownloadWhisperModelAsync_DelegatesToLyricSearchService()
    {
        Assert.False(_lyricSearchService.IsModelReady);

        await _service.DownloadWhisperModelAsync();

        Assert.True(_lyricSearchService.IsModelReady);
    }
}
