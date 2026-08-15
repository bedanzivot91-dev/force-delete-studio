using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using Serilog;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Fake so QuickVideoViewModel tests don't need real ffmpeg - QuickVideoService's own real
/// process behavior is already covered by QuickVideoServiceTests.cs.</summary>
public sealed class FakeQuickVideoService : IQuickVideoService
{
    public Func<string, string, double, string, bool, string?, IProgress<double>?, CancellationToken, Task<string>>? Handler { get; set; }
    public string? LastSubtitleSrtPath { get; private set; }

    public Task<string> CreateAsync(
        string imageFilePath, string songFilePath, double songDurationSeconds, string outputFilePath,
        bool overwriteConfirmed, string? subtitleSrtPath = null, int width = 1920, int height = 1080,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        LastSubtitleSrtPath = subtitleSrtPath;
        return Handler is null
            ? Task.FromResult(outputFilePath)
            : Handler(imageFilePath, songFilePath, songDurationSeconds, outputFilePath, overwriteConfirmed, subtitleSrtPath, progress, cancellationToken);
    }
}

/// <summary>Fake so QuickVideoViewModel tests can control model-readiness/download and SRT generation
/// without a real Whisper model.</summary>
public sealed class FakeSubtitleGeneratorService : ISubtitleGeneratorService
{
    public bool IsModelReady { get; set; }
    public string ModelSizeLabel => "75 MB";
    public string? SrtToReturn { get; set; } = "1\n00:00:00,000 --> 00:00:01,000\ntest\n\n";
    public bool ThrowOnGenerate { get; set; }
    public IReadOnlyList<TranscribedCaptionSegment> SegmentsToReturn { get; set; } = Array.Empty<TranscribedCaptionSegment>();

    public Task DownloadModelAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        IsModelReady = true;
        return Task.CompletedTask;
    }

    public async Task<string> GenerateSrtAsync(string mediaFilePath, string outputSrtPath, CancellationToken cancellationToken = default)
    {
        if (ThrowOnGenerate)
        {
            throw new InvalidOperationException("prepoznavanje govora nije uspelo");
        }

        await File.WriteAllTextAsync(outputSrtPath, SrtToReturn, cancellationToken);
        return outputSrtPath;
    }

    public Task<IReadOnlyList<TranscribedCaptionSegment>> TranscribeAsync(string mediaFilePath, CancellationToken cancellationToken = default)
    {
        if (ThrowOnGenerate)
        {
            throw new InvalidOperationException("prepoznavanje govora nije uspelo");
        }

        return Task.FromResult(SegmentsToReturn);
    }
}

/// <summary>Fake so QuickVideoViewModel tests can control the probed song duration without real ffprobe.</summary>
public sealed class FakeMediaProbeServiceWithDuration : IMediaProbeService
{
    public TimeSpan DurationToReturn { get; set; } = TimeSpan.FromSeconds(5);

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<MediaAsset> ProbeAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(new MediaAsset { FilePath = filePath, Duration = DurationToReturn });
}

public sealed class QuickVideoViewModelTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"npvs_quickvideovm_test_{Guid.NewGuid():N}");

    public QuickVideoViewModelTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private QuickVideoViewModel Create(
        FakeQuickVideoService? quickVideo = null, FakeSubtitleGeneratorService? subtitles = null,
        FakeMediaProbeServiceWithDuration? probe = null, bool initialAutoCaptions = false) => new(
        quickVideo ?? new FakeQuickVideoService(),
        subtitles ?? new FakeSubtitleGeneratorService { IsModelReady = true },
        probe ?? new FakeMediaProbeServiceWithDuration(),
        new FakeStorageService(),
        new LoggerConfiguration().CreateLogger(),
        initialAutoCaptions);

    [Fact]
    public void Construction_InitialAutoCaptionsFalse_StartsUnchecked()
    {
        Assert.False(Create(initialAutoCaptions: false).AutoCaptions);
    }

    [Fact]
    public void Construction_InitialAutoCaptionsTrue_StartsChecked()
    {
        Assert.True(Create(initialAutoCaptions: true).AutoCaptions);
    }

    [Fact]
    public void NeedsModelDownload_AutoCaptionsOnAndModelNotReady_IsTrue()
    {
        var vm = Create(subtitles: new FakeSubtitleGeneratorService { IsModelReady = false }, initialAutoCaptions: true);
        Assert.True(vm.NeedsModelDownload);
    }

    [Fact]
    public void NeedsModelDownload_AutoCaptionsOffRegardlessOfModel_IsFalse()
    {
        var vm = Create(subtitles: new FakeSubtitleGeneratorService { IsModelReady = false }, initialAutoCaptions: false);
        Assert.False(vm.NeedsModelDownload);
    }

    [Fact]
    public async Task StartCommand_MissingFields_CannotExecute()
    {
        var vm = Create();
        Assert.False(vm.StartCommand.CanExecute(null));

        vm.ImageFilePath = "/tmp/img.jpg";
        vm.SongFilePath = "/tmp/song.mp3";
        Assert.False(vm.StartCommand.CanExecute(null)); // no output path yet

        vm.OutputFilePath = Path.Combine(_tempDir, "out.mp4");
        Assert.True(vm.StartCommand.CanExecute(null));
        await Task.CompletedTask;
    }

    [Fact]
    public void StartCommand_AutoCaptionsOnButModelNotReady_CannotExecute()
    {
        var vm = Create(subtitles: new FakeSubtitleGeneratorService { IsModelReady = false }, initialAutoCaptions: true);
        vm.ImageFilePath = "/tmp/img.jpg";
        vm.SongFilePath = "/tmp/song.mp3";
        vm.OutputFilePath = Path.Combine(_tempDir, "out.mp4");

        Assert.False(vm.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task StartAsync_AutoCaptionsOff_CallsQuickVideoWithNullSubtitlePath()
    {
        var quickVideo = new FakeQuickVideoService();
        var vm = Create(quickVideo: quickVideo, initialAutoCaptions: false);
        vm.ImageFilePath = "/tmp/img.jpg";
        vm.SongFilePath = "/tmp/song.mp3";
        vm.OutputFilePath = Path.Combine(_tempDir, "out.mp4");

        await vm.StartCommand.ExecuteAsync(null);

        Assert.Null(quickVideo.LastSubtitleSrtPath);
        Assert.Contains(vm.OutputFilePath, vm.StatusMessage);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task StartAsync_AutoCaptionsOn_GeneratesSrtFirstThenPassesItToQuickVideo()
    {
        var quickVideo = new FakeQuickVideoService();
        var subtitles = new FakeSubtitleGeneratorService { IsModelReady = true };
        var vm = Create(quickVideo: quickVideo, subtitles: subtitles, initialAutoCaptions: true);
        vm.ImageFilePath = "/tmp/img.jpg";
        vm.SongFilePath = "/tmp/song.mp3";
        vm.OutputFilePath = Path.Combine(_tempDir, "out.mp4");

        await vm.StartCommand.ExecuteAsync(null);

        Assert.NotNull(quickVideo.LastSubtitleSrtPath);
        // The temp .srt is cleaned up after the run - only its use during the call is what's under test.
        Assert.False(File.Exists(quickVideo.LastSubtitleSrtPath));
    }

    [Fact]
    public async Task StartAsync_SubtitleGenerationThrows_ReportsFailureAndNeverCallsQuickVideo()
    {
        var quickVideo = new FakeQuickVideoService();
        var subtitles = new FakeSubtitleGeneratorService { IsModelReady = true, ThrowOnGenerate = true };
        var vm = Create(quickVideo: quickVideo, subtitles: subtitles, initialAutoCaptions: true);
        vm.ImageFilePath = "/tmp/img.jpg";
        vm.SongFilePath = "/tmp/song.mp3";
        vm.OutputFilePath = Path.Combine(_tempDir, "out.mp4");

        await vm.StartCommand.ExecuteAsync(null);

        Assert.Null(quickVideo.LastSubtitleSrtPath);
        Assert.Contains("nije uspelo", vm.StatusMessage);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task StartAsync_ExistingOutputNotConfirmedViaPicker_RefusesAndDoesNotCallService()
    {
        var quickVideo = new FakeQuickVideoService();
        var tempFile = Path.Combine(_tempDir, "existing.mp4");
        await File.WriteAllTextAsync(tempFile, "already here");
        var vm = Create(quickVideo: quickVideo);
        vm.ImageFilePath = "/tmp/img.jpg";
        vm.SongFilePath = "/tmp/song.mp3";
        vm.OutputFilePath = tempFile;

        await vm.StartCommand.ExecuteAsync(null);

        Assert.Contains("već postoji", vm.StatusMessage);
        Assert.Equal("already here", await File.ReadAllTextAsync(tempFile));
    }

    [Fact]
    public async Task StartAsync_QuickVideoServiceThrows_ReportsFailureMessage()
    {
        var quickVideo = new FakeQuickVideoService { Handler = (_, _, _, _, _, _, _, _) => throw new InvalidOperationException("ffmpeg nije uspeo") };
        var vm = Create(quickVideo: quickVideo);
        vm.ImageFilePath = "/tmp/img.jpg";
        vm.SongFilePath = "/tmp/song.mp3";
        vm.OutputFilePath = Path.Combine(_tempDir, "out.mp4");

        await vm.StartCommand.ExecuteAsync(null);

        Assert.Contains("ffmpeg nije uspeo", vm.StatusMessage);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public void BackCommand_RaisesBackRequested()
    {
        var vm = Create();
        var raised = false;
        vm.BackRequested += () => raised = true;

        vm.BackCommand.Execute(null);

        Assert.True(raised);
    }
}
