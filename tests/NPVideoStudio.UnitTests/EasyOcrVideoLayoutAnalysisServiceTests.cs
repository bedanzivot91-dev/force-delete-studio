using System.Diagnostics;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public class EasyOcrVideoLayoutAnalysisServiceTests
{
    private const string RealOcrFrameJsonSample =
        "[{\"text\": \"NEDOSTAJEŠ\", \"confidence\": 0.8099526995845159, \"x\": 0.2611111111111111, " +
        "\"y\": 0.003125, \"width\": 0.47685185185185186, \"height\": 0.0640625}, " +
        "{\"text\": \"PUNOO\", \"confidence\": 0.9504507794412234, \"x\": 0.3675925925925926, " +
        "\"y\": 0.04635416666666667, \"width\": 0.26666666666666666, \"height\": 0.04583333333333333}]";

    [Fact]
    public void ParseRegions_RealOcrFrameOutput_ExtractsBothRegions()
    {
        // Captured from an actual `python3 easyocr-helper/ocr_frame.py <frame>` run against a real
        // user-submitted video's on-screen decorative caption text ("NEDOSTAJEŠ PUNOO") - the same
        // frame Tesseract could not read at all. Not invented: this is the real wire shape.
        var regions = EasyOcrVideoLayoutAnalysisService.ParseRegions(RealOcrFrameJsonSample, TimeSpan.FromSeconds(6));

        Assert.Equal(2, regions.Count);
        Assert.Equal("NEDOSTAJEŠ", regions[0].Text);
        Assert.Equal(0.81, regions[0].Confidence, precision: 2);
        Assert.Equal("PUNOO", regions[1].Text);
        Assert.Equal(0.95, regions[1].Confidence, precision: 2);
        Assert.All(regions, r => Assert.Equal(TimeSpan.FromSeconds(6), r.FrameTimestamp));
    }

    [Fact]
    public void ParseRegions_CoordinatesAlreadyNormalized_PassedThroughUnchanged()
    {
        var regions = EasyOcrVideoLayoutAnalysisService.ParseRegions(RealOcrFrameJsonSample, TimeSpan.Zero);

        var first = regions[0];
        Assert.Equal(0.2611111111111111, first.X, precision: 6);
        Assert.Equal(0.003125, first.Y, precision: 6);
        Assert.Equal(0.47685185185185186, first.Width, precision: 6);
        Assert.Equal(0.0640625, first.Height, precision: 6);
    }

    [Fact]
    public void ParseRegions_EmptyArray_ReturnsEmptyList()
    {
        Assert.Empty(EasyOcrVideoLayoutAnalysisService.ParseRegions("[]", TimeSpan.Zero));
    }

    [Fact]
    public void ParseRegions_EmptyString_ReturnsEmptyList()
    {
        Assert.Empty(EasyOcrVideoLayoutAnalysisService.ParseRegions("", TimeSpan.Zero));
    }

    [Fact]
    public void ParseRegions_MalformedJson_ReturnsEmptyListRatherThanThrowing()
    {
        Assert.Empty(EasyOcrVideoLayoutAnalysisService.ParseRegions("{not valid json", TimeSpan.Zero));
    }

    [Fact]
    public void ParseRegions_BlankTextEntry_IsSkipped()
    {
        var regions = EasyOcrVideoLayoutAnalysisService.ParseRegions(
            "[{\"text\": \"  \", \"confidence\": 0.9, \"x\": 0, \"y\": 0, \"width\": 0.1, \"height\": 0.1}]",
            TimeSpan.Zero);

        Assert.Empty(regions);
    }

    /// <summary>
    /// Real end-to-end run: extracts a frame from an actual generated video and OCRs it via the real
    /// bundled Python script (not mocked), same verification method as RenderServiceTests/
    /// FramePreviewServiceTests. Self-skips (returns without asserting) when Python/EasyOCR isn't
    /// installed - this dependency is optional and, unlike ffmpeg/Tesseract, is NOT installed on this
    /// project's Windows CI (installing PyTorch there would add significant CI time for a fallback-only
    /// feature) - see CompositeVideoLayoutAnalysisService and PHASE_STATUS.md. Verified manually against
    /// a real user video during development; this test re-verifies the same script against a synthetic
    /// frame whenever the optional dependency happens to be present (e.g. a dev machine that installed
    /// easyocr-helper/requirements.txt).
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_RealFrameWithPlainText_FindsTextWhenEasyOcrIsInstalled()
    {
        var service = new EasyOcrVideoLayoutAnalysisService(new FakeMediaProbeServiceForOcr());
        if (!await service.IsAvailableAsync())
        {
            return; // EasyOCR not installed in this environment - optional dependency, not a failure.
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"npvs_easyocr_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var videoPath = Path.Combine(tempDir, "clip.mp4");
            await CreateTextClipAsync(videoPath, tempDir);

            var result = await service.AnalyzeAsync(videoPath, sampleFrameCount: 1);

            Assert.NotEmpty(result.DetectedTextRegions);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task CreateTextClipAsync(string outputPath, string workDir)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workDir
            }
        };
        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("lavfi");
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(
            "color=c=black:s=640x360:d=1:r=5,drawtext=text='HELLO':fontcolor=white:fontsize=60:x=(w-text_w)/2:y=(h-text_h)/2");
        process.StartInfo.ArgumentList.Add("-c:v");
        process.StartInfo.ArgumentList.Add("libx264");
        process.StartInfo.ArgumentList.Add(outputPath);

        process.Start();
        await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(File.Exists(outputPath), "Test clip generation failed - is ffmpeg on PATH?");
    }

    private sealed class FakeMediaProbeServiceForOcr : Core.Services.IMediaProbeService
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public async Task<Domain.MediaAsset> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    ArgumentList = { "-v", "error", "-show_entries", "format=duration:stream=width,height", "-of", "json", filePath },
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                }
            };
            process.Start();
            var json = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var duration = double.Parse(root.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            var stream = root.GetProperty("streams")[0];

            return new Domain.MediaAsset
            {
                FilePath = filePath,
                Duration = TimeSpan.FromSeconds(duration),
                Width = stream.GetProperty("width").GetInt32(),
                Height = stream.GetProperty("height").GetInt32()
            };
        }
    }
}
