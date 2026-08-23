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
        Assert.All(results, result => Assert.NotEqual(default, result.LastCheckedUtc));
    }

    [Fact]
    public async Task GetDependenciesAsync_AiWorkerUnreachable_ReportsNotInstalled()
    {
        _aiWorkerClient.CapabilitiesToReturn = new AiWorkerCapabilities { WorkerReachable = false, Error = "python3 not found" };

        var results = await _service.GetDependenciesAsync();
        var aiWorker = results.Single(d => d.Name.Contains("AI radnik"));

        Assert.Equal(DependencyStatus.NotInstalled, aiWorker.Status);
        Assert.Contains("python3 not found", aiWorker.TechnicalDetails);
        Assert.True(aiWorker.CanRepair);
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
    public async Task GetDependenciesAsync_AiWorkerWithAllEnginesButPython311_ReportsIncompatible()
    {
        _aiWorkerClient.CapabilitiesToReturn = new AiWorkerCapabilities
        {
            WorkerReachable = true,
            PythonVersion = "3.11.9",
            FasterWhisperAvailable = true,
            DemucsAvailable = true,
            LyricAlignAvailable = true,
            OpenCvAvailable = true,
            BackgroundRemovalAvailable = true,
            TranslationAvailable = true
        };

        var results = await _service.GetDependenciesAsync();
        var aiWorker = results.Single(d => d.Name.Contains("AI radnik"));

        Assert.Equal(DependencyStatus.Incompatible, aiWorker.Status);
        Assert.Contains("Python 3.12+", aiWorker.TechnicalDetails);
        Assert.True(aiWorker.CanRepair);
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
            OpenCvAvailable = true,
            BackgroundRemovalAvailable = true,
            TranslationAvailable = true
        };

        var results = await _service.GetDependenciesAsync();
        var aiWorker = results.Single(d => d.Name.Contains("AI radnik"));

        Assert.Equal(DependencyStatus.Installed, aiWorker.Status);
        Assert.False(aiWorker.CanRepair);
    }

    [Fact]
    public async Task GetDependenciesAsync_FfmpegOnPath_ReportsInstalledWithVersionAndConcreteFolder()
    {
        var results = await _service.GetDependenciesAsync();
        var ffmpeg = results.Single(d => d.Name == "FFmpeg");

        Assert.Equal(DependencyStatus.Installed, ffmpeg.Status);
        Assert.False(string.IsNullOrWhiteSpace(ffmpeg.Version));
        Assert.True(ffmpeg.CanOpenFolder);
        Assert.False(string.IsNullOrWhiteSpace(ffmpeg.Path));
        Assert.False(string.IsNullOrWhiteSpace(ffmpeg.ExpectedVersion));
    }

    [Fact]
    public async Task GetDependenciesAsync_ExistingInvalidExecutable_ReportsCorruptInsteadOfMissing()
    {
        var notAnExecutable = Path.Combine(_tempDir, "not-a-real-yt-dlp.txt");
        File.WriteAllText(notAnExecutable, "this is not an executable");
        _settingsService.Current.YtDlpPath = notAnExecutable;

        var results = await _service.GetDependenciesAsync();
        var ytDlp = results.Single(d => d.Name == "yt-dlp");

        Assert.Equal(DependencyStatus.Corrupt, ytDlp.Status);
        Assert.True(ytDlp.CanOpenFolder);
        Assert.Equal(Path.GetFullPath(notAnExecutable), ytDlp.Path);
        Assert.Contains("provera verzije nije uspela", ytDlp.TechnicalDetails);
    }

    [Fact]
    public async Task GetDependenciesAsync_WhisperModelNotReady_CanDownloadIsTrue()
    {
        _lyricSearchService.IsModelReady = false;
        var results = await _service.GetDependenciesAsync();
        var whisper = results.Single(d => d.Name.Contains("Whisper"));

        Assert.Equal(DependencyStatus.NotInstalled, whisper.Status);
        Assert.True(whisper.CanDownload);
        Assert.True(whisper.CanRepair);
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
    public async Task GetDependenciesAsync_WhisperModelReady_ReportsTheServicesActualResolvedModelPath()
    {
        _lyricSearchService.IsModelReady = true;
        _lyricSearchService.ModelPath = "C:\\Program\\Tools\\whisper-models\\ggml-tiny.bin";
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
