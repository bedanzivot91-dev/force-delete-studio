using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Pure parsing logic against a real TSV sample captured from an actual `tesseract ... tsv` run (see
/// ai-worker-adjacent verification notes in PHASE_STATUS.md) - no process launch needed to exercise the
/// parsing itself, but the shape below is not invented: it is exactly what tesseract 5.3.4 emits.
/// </summary>
public class TesseractOcrServiceTests
{
    private const string RealTesseractTsvSample =
        "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n" +
        "1\t1\t0\t0\t0\t0\t0\t0\t320\t100\t-1\t\n" +
        "2\t1\t1\t0\t0\t0\t12\t30\t201\t18\t-1\t\n" +
        "3\t1\t1\t1\t0\t0\t12\t30\t201\t18\t-1\t\n" +
        "4\t1\t1\t1\t1\t0\t12\t30\t201\t18\t-1\t\n" +
        "5\t1\t1\t1\t1\t1\t12\t30\t133\t18\t96.156555\tSUBSCRIBE\n" +
        "5\t1\t1\t1\t1\t2\t156\t30\t57\t18\t96.600365\tNOW\n";

    [Fact]
    public void ParseTsv_RealTesseractOutput_ExtractsWordLevelRegionsOnly()
    {
        var regions = TesseractOcrService.ParseTsv(RealTesseractTsvSample, TimeSpan.FromSeconds(2), frameWidth: 320, frameHeight: 100);

        Assert.Equal(2, regions.Count); // the -1-confidence structural rows (page/block/par/line) are skipped
        Assert.Equal("SUBSCRIBE", regions[0].Text);
        Assert.Equal("NOW", regions[1].Text);
        Assert.All(regions, r => Assert.Equal(TimeSpan.FromSeconds(2), r.FrameTimestamp));
    }

    [Fact]
    public void ParseTsv_NormalizesCoordinatesToZeroOneRange()
    {
        var regions = TesseractOcrService.ParseTsv(RealTesseractTsvSample, TimeSpan.Zero, frameWidth: 320, frameHeight: 100);

        var subscribe = regions[0];
        Assert.Equal(12.0 / 320, subscribe.X, precision: 5);
        Assert.Equal(30.0 / 100, subscribe.Y, precision: 5);
        Assert.Equal(133.0 / 320, subscribe.Width, precision: 5);
        Assert.Equal(18.0 / 100, subscribe.Height, precision: 5);
        Assert.Equal(0.96156555, subscribe.Confidence, precision: 5);
    }

    [Fact]
    public void ParseTsv_EmptyOutput_ReturnsEmptyList()
    {
        var regions = TesseractOcrService.ParseTsv(string.Empty, TimeSpan.Zero, 320, 100);

        Assert.Empty(regions);
    }

    [Fact]
    public void ParseTsv_HeaderOnly_ReturnsEmptyList()
    {
        var regions = TesseractOcrService.ParseTsv("level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n", TimeSpan.Zero, 320, 100);

        Assert.Empty(regions);
    }
}
