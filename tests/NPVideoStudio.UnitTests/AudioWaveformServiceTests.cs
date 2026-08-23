using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class AudioWaveformServiceTests
{
    [Fact]
    public async Task Missing_source_returns_empty_waveform()
    {
        var service = new AudioWaveformService("definitely-not-used");
        var peaks = await service.ExtractPeaksAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav"), 0, 1);
        Assert.Empty(peaks);
    }

    [Fact]
    public async Task Real_audio_is_reduced_to_normalized_peaks_when_ffmpeg_is_available()
    {
        var ffmpeg = FfmpegLocator.ResolveFfmpegPath(null);
        var available = await FfmpegLocator.TryGetVersionAsync(ffmpeg);
        if (!available.Found) return;
        var source = Path.Combine(AppContext.BaseDirectory, "TestAssets", "lyric_test_song.mp3");
        var peaks = await new AudioWaveformService(ffmpeg).ExtractPeaksAsync(source, 0, 1, 64);
        Assert.Equal(64, peaks.Count);
        Assert.Contains(peaks, value => value > 0);
        Assert.All(peaks, value => Assert.InRange(value, 0, 1));
    }
}
