using System.Diagnostics;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Exercises the real ffprobe/ffmpeg binaries on the machine, not a mock - if ffmpeg isn't on PATH
/// these tests fail loudly instead of silently passing, which is the point (spec §45/53).
/// </summary>
public class FfprobeServiceTests : IAsyncLifetime
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"npvs_media_test_{Guid.NewGuid():N}");
    private string _videoWithAudioPath = string.Empty;
    private string _videoOnlyPath = string.Empty;
    private string _audioOnlyPath = string.Empty;
    private string _unusualNamePath = string.Empty;
    private readonly FfprobeService _service = new();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_tempDir);

        _videoWithAudioPath = Path.Combine(_tempDir, "video_i_audio.mp4");
        await RunFfmpegAsync($"-y -f lavfi -i testsrc=duration=1:size=64x64:rate=25 -f lavfi -i sine=frequency=1000:duration=1 -shortest -pix_fmt yuv420p \"{_videoWithAudioPath}\"");

        _videoOnlyPath = Path.Combine(_tempDir, "samo_video.mp4");
        await RunFfmpegAsync($"-y -f lavfi -i testsrc=duration=1:size=64x64:rate=25 -an -pix_fmt yuv420p \"{_videoOnlyPath}\"");

        _audioOnlyPath = Path.Combine(_tempDir, "samo_audio.mp3");
        await RunFfmpegAsync($"-y -f lavfi -i sine=frequency=440:duration=1 \"{_audioOnlyPath}\"");

        var unusualDir = Path.Combine(_tempDir, "folder sa razmacima i šđčćž");
        Directory.CreateDirectory(unusualDir);
        _unusualNamePath = Path.Combine(unusualDir, "snimak (test) č ć š ž đ.mp4");
        await RunFfmpegAsync($"-y -f lavfi -i testsrc=duration=1:size=64x64:rate=25 -an -pix_fmt yuv420p \"{_unusualNamePath}\"");
    }

    public Task DisposeAsync()
    {
        Directory.Delete(_tempDir, recursive: true);
        return Task.CompletedTask;
    }

    private static async Task RunFfmpegAsync(string arguments)
    {
        var ffmpegPath = FfmpegLocator.ResolveFfmpegPath(null);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };
        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg nije uspeo da napravi test fajl: {stderr}");
        }
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsTrue_WhenFfprobeInstalled()
    {
        Assert.True(await _service.IsAvailableAsync());
    }

    [Fact]
    public async Task ProbeAsync_VideoWithAudio_ReportsBothStreams()
    {
        var asset = await _service.ProbeAsync(_videoWithAudioPath);

        Assert.Null(asset.ProbeError);
        Assert.Equal(MediaKind.Video, asset.Kind);
        Assert.True(asset.HasVideoStream);
        Assert.True(asset.HasAudioStream);
        Assert.Equal(64, asset.Width);
        Assert.Equal(64, asset.Height);
        Assert.True(asset.Fps > 0);
        Assert.True(asset.Duration.TotalSeconds > 0);
    }

    [Fact]
    public async Task ProbeAsync_VideoWithoutAudio_ReportsNoAudioStream()
    {
        var asset = await _service.ProbeAsync(_videoOnlyPath);

        Assert.Null(asset.ProbeError);
        Assert.True(asset.HasVideoStream);
        Assert.False(asset.HasAudioStream);
    }

    [Fact]
    public async Task ProbeAsync_AudioOnly_ReportsAudioKindWithNoVideoStream()
    {
        var asset = await _service.ProbeAsync(_audioOnlyPath);

        Assert.Null(asset.ProbeError);
        Assert.False(asset.HasVideoStream);
        Assert.True(asset.HasAudioStream);
        Assert.Equal(MediaKind.Audio, asset.Kind);
    }

    [Fact]
    public async Task ProbeAsync_PathWithSpacesAndSerbianLetters_Succeeds()
    {
        var asset = await _service.ProbeAsync(_unusualNamePath);

        Assert.Null(asset.ProbeError);
        Assert.True(asset.HasVideoStream);
    }

    [Fact]
    public async Task ProbeAsync_MissingFile_ReturnsIsMissingWithoutThrowing()
    {
        var missingPath = Path.Combine(_tempDir, "ne_postoji.mp4");

        var asset = await _service.ProbeAsync(missingPath);

        Assert.True(asset.IsMissing);
        Assert.NotNull(asset.ProbeError);
    }
}
