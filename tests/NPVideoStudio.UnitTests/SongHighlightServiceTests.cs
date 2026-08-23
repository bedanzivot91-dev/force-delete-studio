using System.Diagnostics;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Builds a synthetic song with known quiet/loud/quiet/loud/quiet sections via ffmpeg and verifies
/// the highlight picker actually finds the loud parts and exported clips are the right length -
/// exercising the real ffmpeg astats analysis pipeline, not a mocked one.
/// </summary>
public class SongHighlightServiceTests : IAsyncLifetime
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"npvs_song_test_{Guid.NewGuid():N}");
    private string _songPath = string.Empty;
    private readonly FfprobeService _probeService = new();
    private SongHighlightService _service = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_tempDir);
        _service = new SongHighlightService(_probeService);

        // 100s track: quiet(0-20) loud(20-40) quiet(40-60) loud(60-80) quiet(80-100)
        var quiet = Path.Combine(_tempDir, "q.wav");
        var loud = Path.Combine(_tempDir, "l.wav");
        await RunFfmpegAsync($"-y -f lavfi -i \"sine=frequency=220:duration=20\" -af \"volume=0.03\" -ar 44100 \"{quiet}\"");
        await RunFfmpegAsync($"-y -f lavfi -i \"sine=frequency=220:duration=20\" -af \"volume=0.9\" -ar 44100 \"{loud}\"");

        _songPath = Path.Combine(_tempDir, "song.wav");
        await RunFfmpegAsync(
            $"-y -i \"{quiet}\" -i \"{loud}\" -i \"{quiet}\" -i \"{loud}\" -i \"{quiet}\" " +
            "-filter_complex \"[0:a][1:a][2:a][3:a][4:a]concat=n=5:v=0:a=1[out]\" -map \"[out]\" " +
            $"\"{_songPath}\"");
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
                UseShellExecute = false
            }
        };
        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg nije uspeo: {stderr}");
        }
    }

    [Fact]
    public async Task FindHighlightsAsync_PicksTheLoudSectionsNotTheQuietOnes()
    {
        var highlights = await _service.FindHighlightsAsync(
            _songPath, count: 2, minDuration: TimeSpan.FromSeconds(15), maxDuration: TimeSpan.FromSeconds(15));

        Assert.Equal(2, highlights.Count);
        foreach (var highlight in highlights)
        {
            // Loud sections are [20,40) and [60,80); a 15s window fully inside one of these
            // must start within a range that keeps it entirely inside the loud section.
            var startsInFirstLoud = highlight.Start.TotalSeconds is >= 20 and <= 25;
            var startsInSecondLoud = highlight.Start.TotalSeconds is >= 60 and <= 65;
            Assert.True(startsInFirstLoud || startsInSecondLoud,
                $"Highlight at {highlight.Start} was not inside an expected loud section.");
        }
    }

    [Fact]
    public async Task FindHighlightsAsync_ResultsDoNotOverlap()
    {
        var highlights = await _service.FindHighlightsAsync(
            _songPath, count: 2, minDuration: TimeSpan.FromSeconds(15), maxDuration: TimeSpan.FromSeconds(15));

        Assert.Equal(2, highlights.Count);
        var ordered = highlights.OrderBy(h => h.Start).ToList();
        Assert.True(ordered[0].End <= ordered[1].Start);
    }

    [Fact]
    public async Task ExportHighlightAsync_ProducesAFileOfApproximatelyTheRightDuration()
    {
        var highlights = await _service.FindHighlightsAsync(
            _songPath, count: 1, minDuration: TimeSpan.FromSeconds(15), maxDuration: TimeSpan.FromSeconds(15));
        var highlight = Assert.Single(highlights);

        var outputPath = Path.Combine(_tempDir, "clip.wav");
        await _service.ExportHighlightAsync(_songPath, highlight, outputPath);

        Assert.True(File.Exists(outputPath));
        var exportedAsset = await _probeService.ProbeAsync(outputPath);
        Assert.Null(exportedAsset.ProbeError);
        Assert.InRange(exportedAsset.Duration.TotalSeconds, 14, 16);
        Assert.Equal(outputPath, highlight.ExportedFilePath);
    }

    [Fact]
    public async Task FindHighlightsAsync_TrackShorterThanWindow_ReturnsWholeTrackAsSingleHighlight()
    {
        var shortSong = Path.Combine(_tempDir, "short.wav");
        await RunFfmpegAsync($"-y -f lavfi -i \"sine=frequency=220:duration=5\" -ar 44100 \"{shortSong}\"");

        var highlights = await _service.FindHighlightsAsync(
            shortSong, count: 3, minDuration: TimeSpan.FromSeconds(30), maxDuration: TimeSpan.FromSeconds(50));

        var highlight = Assert.Single(highlights);
        Assert.Equal(TimeSpan.Zero, highlight.Start);
    }

    [Fact]
    public async Task FindHighlightsAsync_MissingFile_ThrowsFileNotFoundException()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _service.FindHighlightsAsync(Path.Combine(_tempDir, "ne_postoji.mp3")));
    }
}
