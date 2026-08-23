using NPVideoStudio.Domain;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class SafeAreaPreviewTests
{
    [Theory]
    [InlineData(1920, 1080, "16:9", 0.08, 0.08, 0.08, 0.10)]
    [InlineData(1080, 1920, "9:16", 0.08, 0.08, 0.12, 0.16)]
    [InlineData(1080, 1080, "1:1", 0.08, 0.08, 0.10, 0.10)]
    public void ForFrame_ReturnsGuideMarginsUsedByPreview(int width, int height, string label, double left, double right, double top, double bottom)
    {
        var p = SafeAreaPreset.ForFrame(width, height);
        Assert.Equal(label, p.FormatLabel);
        Assert.Equal(left, p.Left, 6);
        Assert.Equal(right, p.Right, 6);
        Assert.Equal(top, p.Top, 6);
        Assert.Equal(bottom, p.Bottom, 6);
    }

    [Fact]
    public void VerticalGuide_PixelRectMatchesNormalizedMargins()
    {
        var r = SafeAreaPreset.Vertical9By16.ToPixelRect(1080, 1920);
        Assert.Equal(86, r.X);
        Assert.Equal(230, r.Y);
        Assert.Equal(907, r.Width);
        Assert.Equal(1382, r.Height);
    }
}