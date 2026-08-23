using NPVideoStudio.AI;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Real end-to-end test: downloads the actual Whisper tiny model and transcribes a real (synthesized)
/// speech clip checked into TestAssets, then verifies a real .srt file comes out. Needs internet access
/// to huggingface.co, same as LyricSearchServiceIntegrationTests - see that file for why this only runs
/// unconditionally on CI, not in every sandboxed dev environment.
/// </summary>
[Collection("Whisper model tests")]
public class SubtitleGeneratorServiceIntegrationTests : IAsyncLifetime
{
    private readonly string _modelPath = Path.Combine(Path.GetTempPath(), "npvs_test_models", "ggml-tiny.bin");
    private SubtitleGeneratorService _service = null!;
    private string _songPath = string.Empty;

    public Task InitializeAsync()
    {
        _service = new SubtitleGeneratorService(modelPathOverride: _modelPath);
        _songPath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "lyric_test_song.mp3");
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GenerateSrtAsync_RealTranscription_ProducesAWellFormedSrtFile()
    {
        if (!_service.IsModelReady)
        {
            await _service.DownloadModelAsync();
        }
        Assert.True(_service.IsModelReady);

        var outputDir = Path.Combine(Path.GetTempPath(), $"npvs_srt_{Guid.NewGuid():N}");
        var outputPath = Path.Combine(outputDir, "titlovi.srt");

        try
        {
            var resultPath = await _service.GenerateSrtAsync(_songPath, outputPath);

            Assert.Equal(outputPath, resultPath);
            Assert.True(File.Exists(outputPath));

            var content = await File.ReadAllTextAsync(outputPath);
            Assert.NotEmpty(content);
            Assert.StartsWith("1\n", content);
            Assert.Contains("-->", content);
            Assert.Contains("love", content, StringComparison.OrdinalIgnoreCase);
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
