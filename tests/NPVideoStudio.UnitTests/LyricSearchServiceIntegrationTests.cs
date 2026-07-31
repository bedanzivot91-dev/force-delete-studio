using NPVideoStudio.AI;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Real end-to-end test: downloads the actual Whisper tiny model and transcribes a real (synthesized)
/// speech clip checked into TestAssets, then verifies the phrase is actually found. This needs
/// internet access to huggingface.co, which this repo's CI runners have but some sandboxed dev
/// environments block - see docs/README for why the download path is exercised on CI instead of
/// asserted to work everywhere unconditionally.
/// </summary>
public class LyricSearchServiceIntegrationTests : IAsyncLifetime
{
    private readonly string _modelPath = Path.Combine(Path.GetTempPath(), "npvs_test_models", "ggml-tiny.bin");
    private LyricSearchService _service = null!;
    private string _songPath = string.Empty;

    public Task InitializeAsync()
    {
        _service = new LyricSearchService(modelPathOverride: _modelPath);
        _songPath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "lyric_test_song.mp3");
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FindPhraseInSongAsync_RealTranscription_FindsThePhraseThatWasActuallySpoken()
    {
        if (!_service.IsModelReady)
        {
            await _service.DownloadModelAsync();
        }
        Assert.True(_service.IsModelReady);

        var matches = await _service.FindPhraseInSongAsync(_songPath, "I love you my darling");

        Assert.NotEmpty(matches);
        Assert.All(matches, m => Assert.True(m.Confidence > 0));
        // The phrase is spoken twice in the fixture (~4-8s and ~12-16s); recognition timing on a
        // synthesized voice with the tiny model won't be frame-perfect, so allow a generous window.
        Assert.Contains(matches, m => m.Start < TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task FindPhraseInSongAsync_PhraseNotSpoken_ReturnsNoMatches()
    {
        if (!_service.IsModelReady)
        {
            await _service.DownloadModelAsync();
        }

        var matches = await _service.FindPhraseInSongAsync(_songPath, "purple elephants dance underwater");

        Assert.Empty(matches);
    }

    [Fact]
    public async Task ExportMatchAsync_RealMatch_ProducesAPlayableClip()
    {
        if (!_service.IsModelReady)
        {
            await _service.DownloadModelAsync();
        }

        var matches = await _service.FindPhraseInSongAsync(_songPath, "I love you my darling");
        Assert.NotEmpty(matches);
        var match = matches[0];

        var outputDir = Path.Combine(Path.GetTempPath(), $"npvs_lyric_export_{Guid.NewGuid():N}");
        var outputPath = Path.Combine(outputDir, "match.mp3");

        try
        {
            await _service.ExportMatchAsync(_songPath, match, outputPath);

            Assert.True(File.Exists(outputPath));
            var probe = new FfprobeService();
            var asset = await probe.ProbeAsync(outputPath);
            Assert.Null(asset.ProbeError);
            Assert.True(asset.Duration.TotalSeconds > 0);
        }
        finally
        {
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, recursive: true);
            }
        }
    }
}
