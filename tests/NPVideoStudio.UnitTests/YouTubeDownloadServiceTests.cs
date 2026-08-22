using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Real integration tests for YouTubeDownloadService against a fake yt-dlp process (tests/FakeYtDlp) -
/// this is the "yt-dlp servis sa mock procesom" test the master spec calls out as a real, previously
/// missing gap (FUNCTION_MATRIX.md: ytdl.fetch-info/ytdl.download were IMPLEMENTED_NOT_RUNTIME_VERIFIED).
/// Exercises the actual process-orchestration code (argument construction, JSON parsing, output file
/// resolution/renaming) without needing the real yt-dlp binary or network access.
/// </summary>
public class YouTubeDownloadServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"npvs_ytdlp_test_{Guid.NewGuid():N}");
    private readonly string _fakeYtDlpPath;
    private readonly YouTubeDownloadService _service;

    public YouTubeDownloadServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
        var exeName = OperatingSystem.IsWindows() ? "FakeYtDlp.exe" : "FakeYtDlp";
        _fakeYtDlpPath = Path.Combine(AppContext.BaseDirectory, exeName);
        Assert.True(File.Exists(_fakeYtDlpPath), $"Fake yt-dlp nije pronađen na {_fakeYtDlpPath} - proveriti ProjectReference ka tests/FakeYtDlp.");

        _service = new YouTubeDownloadService(ytDlpOverridePath: _fakeYtDlpPath);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task GetVideoInfoAsync_RealProcessCall_ParsesJsonCorrectly()
    {
        var info = await _service.GetVideoInfoAsync("https://www.youtube.com/watch?v=abc123");

        Assert.Equal("abc123", info.VideoId);
        Assert.Equal("Fake Test Song abc123", info.Title);
        Assert.Equal("Fake Channel", info.Uploader);
        Assert.Equal(TimeSpan.FromSeconds(12.5), info.Duration);
    }

    [Fact]
    public async Task GetVideoInfoAsync_NonYouTubeUrl_ThrowsWithoutStartingAProcess()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetVideoInfoAsync("https://vimeo.com/12345"));
    }

    [Fact]
    public async Task GetVideoInfoAsync_ProcessReturnsNonZeroExitCode_ThrowsWithStdErrMessage()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.GetVideoInfoAsync("https://www.youtube.com/watch?v=faildownload"));

        Assert.Contains("simulated failure", ex.Message);
    }

    [Fact]
    public async Task DownloadAudioAsync_WithoutOwnershipConfirmation_ThrowsAndNeverStartsAProcess()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.DownloadAudioAsync("https://www.youtube.com/watch?v=abc123", _tempDir, confirmedOwnContent: false));

        Assert.Contains("sadržaj koji je vaš", ex.Message);
        Assert.Empty(Directory.GetFiles(_tempDir));
    }

    [Fact]
    public async Task DownloadAudioAsync_RealProcessCall_ProducesFileNamedAfterSanitizedTitle()
    {
        var resultPath = await _service.DownloadAudioAsync(
            "https://www.youtube.com/watch?v=abc123", _tempDir, confirmedOwnContent: true);

        Assert.True(File.Exists(resultPath));
        Assert.Equal("Fake Test Song abc123.mp3", Path.GetFileName(resultPath));
        Assert.Equal("fake-mp3-content", await File.ReadAllTextAsync(resultPath));
        // The intermediate {videoId}.mp3 file (yt-dlp's own naming) should have been renamed away, not left behind.
        Assert.False(File.Exists(Path.Combine(_tempDir, "abc123.mp3")));
    }

    [Fact]
    public async Task DownloadAudioAsync_ProcessFails_ThrowsAndLeavesNoOutputFile()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.DownloadAudioAsync("https://www.youtube.com/watch?v=faildownload", _tempDir, confirmedOwnContent: true));

        Assert.Empty(Directory.GetFiles(_tempDir));
    }
}
