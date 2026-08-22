using System.Runtime.CompilerServices;
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
    public string ModelPath { get; set; } = "/fake/model/path/ggml-tiny.bin";

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

/// <summary>Fake so the dependency-manager tests don't need a real Python/faster-whisper install.</summary>
public sealed class FakeAiWorkerClient : IAiWorkerClient
{
    public AiWorkerCapabilities CapabilitiesToReturn { get; set; } = new() { WorkerReachable = false };

    public Task<AiWorkerCapabilities> CheckCapabilitiesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CapabilitiesToReturn);

    public async IAsyncEnumerable<AiWorkerEvent> RunAsync(AiWorkerRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}

public class DependencyManagerServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"npvs_depmgr_test_{Guid.NewGuid():N}");
    private readonly SettingsService _settingsService;
    private readonly FakeLyricSearchService _lyricSearchService = new();
    private readonly FakeAiWorkerClient _aiWorkerClient = new();
    private readonly DependencyManagerService _service;

    public DependencyManagerServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
        _settingsService = new SettingsService(Path.Combine(_tempDir, "settings.json"));
        _settingsService.LoadAsync().GetAwaiter().GetResult();
        _service = new DependencyManagerService(_settingsService, _lyricSearchService, _aiWorkerClient);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task GetDependenciesAsync_ReturnsSevenEntries()
    {
        var results = await _service.GetDependenciesAsync();

        Assert.Equal(7, results.Count);
        Assert.Contains(results, d => d.Name == "FFmpeg");
        Assert.Contains(results, d => d.Name == "FFprobe");
        Assert.Contains(results, d => d.Name == "yt-dlp");
        Assert.Contains(results, d => d.Name.Contains("fpcalc"));
        Assert.Contains(results, d => d.Name.Contains("Whisper"));
        Assert.Contains(results, d => d.Name.Contains("AI radnik"));
        Assert.Contains(results, d => d.Name.Contains("Tesseract"));
    }

    [Fact]
    public async Task GetDependenciesAsync_AiWorkerUnreachable_ReportsNotInstalled()
    {
        _aiWorkerClient.CapabilitiesToReturn = new AiWorkerCapabilities { WorkerReachable = false, Error = "python3 not found" };

        var results = await _service.GetDependenciesAsync();
        var aiWorker = results.Single(d => d.Name.Contains("AI radnik"));

        Assert.Equal(DependencyStatus.NotInstalled, aiWorker.Status);
        Assert.Contains("python3 not found", aiWorker.TechnicalDetails);
    }

    [Fact]
    public async Task GetDependenciesAsync_AiWorkerReachableButNoEngines_ReportsNotInstalled()
    {
        _aiWorkerClient.CapabilitiesToReturn = new AiWorkerCapabilities { WorkerReachable = true, PythonVersion = "3.11.0" };

        var results = await _service.GetDependenciesAsync();
        var aiWorker = results.Single(d => d.Name.Contains("AI radnik"));

        Assert.Equal(DependencyStatus.NotInstalled, aiWorker.Status);
    }

    [Fact]
    public async Task GetDependenciesAsync_AiWorkerWithOnlyFasterWhisper_ReportsNotInstalled()
    {
        _aiWorkerClient.CapabilitiesToReturn = new AiWorkerCapabilities
        {
            WorkerReachable = true,
            PythonVersion = "3.11.0",
            FasterWhisperAvailable = true
        };

        var results = await _service.GetDependenciesAsync();
        var aiWorker = results.Single(d => d.Name.Contains("AI radnik"));

        Assert.Equal(DependencyStatus.NotInstalled, aiWorker.Status);
        Assert.Equal("3.11.0", aiWorker.Version);
    }

    [Fact]
    public async Task GetDependenciesAsync_AiWorkerWithFasterWhisperDemucsLyricAlignAndOpenCv_ReportsInstalled()
    {
        _aiWorkerClient.CapabilitiesToReturn = new AiWorkerCapabilities
        {
            WorkerReachable = true,
            PythonVersion = "3.12.0",
            FasterWhisperAvailable = true,
            DemucsAvailable = true,
            LyricAlignAvailable = true,
            OpenCvAvailable = true
        };

        var results = await _service.GetDependenciesAsync();
        var aiWorker = results.Single(d => d.Name.Contains("AI radnik"));

        Assert.Equal(DependencyStatus.Installed, aiWorker.Status);
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

    /// <summary>Real bug found and fixed: this used to always reconstruct the AppData default model
    /// path here regardless of where the model was actually resolved from (e.g. a bundled copy next to
    /// the exe) - so "Otvori folder" could point at a path with nothing in it even when the model was
    /// genuinely ready. Must report the service's own real, resolved path instead.</summary>
    [Fact]
    public async Task GetDependenciesAsync_WhisperModelReady_ReportsTheServicesActualResolvedModelPath()
    {
        _lyricSearchService.IsModelReady = true;
        _lyricSearchService.ModelPath = "C:\\Program\\Tools\\whisper-models\\ggml-tiny.bin"; // e.g. a bundled copy, not the AppData default
        var results = await _service.GetDependenciesAsync();
        var whisper = results.Single(d => d.Name.Contains("Whisper"));

        Assert.Equal(_lyricSearchService.ModelPath, whisper.Path);
    }

    [Fact]
    public async Task DownloadWhisperModelAsync_DelegatesToLyricSearchService()
    {
        Assert.False(_lyricSearchService.IsModelReady);

        await _service.DownloadWhisperModelAsync();

        Assert.True(_lyricSearchService.IsModelReady);
    }
}
