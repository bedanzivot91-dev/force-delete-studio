using NPVideoStudio.Domain;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Covers the safe-area margins ported from the user's other project
/// (PROGRAM-ZA-TEKST-U-VIDEO, text-video-tools.js / text-layout-engine.js).</summary>
public class SafeAreaPresetTests
{
    [Theory]
    [InlineData(1920, 1080, "16:9")]
    [InlineData(1280, 720, "16:9")]
    [InlineData(1080, 1920, "9:16")]
    [InlineData(720, 1280, "9:16")]
    [InlineData(1080, 1080, "1:1")]
    public void ForFrame_PicksThePresetByTheFramesRealShape(int width, int height, string expected)
    {
        Assert.Equal(expected, SafeAreaPreset.ForFrame(width, height).FormatLabel);
    }

    [Fact]
    public void ForFrame_UnusualButClearlyVerticalSize_StillGetsVerticalMarginsNotWidescreenOnes()
    {
        // Not a standard 9:16 size, but unmistakably a vertical video - it must not fall back to 16:9.
        Assert.Equal("9:16", SafeAreaPreset.ForFrame(1000, 1600).FormatLabel);
    }

    [Fact]
    public void ForFrame_NonsenseSize_FallsBackToWidescreenInsteadOfThrowing()
    {
        Assert.Equal("16:9", SafeAreaPreset.ForFrame(0, 0).FormatLabel);
    }

    [Fact]
    public void Vertical_HasABiggerBottomMarginThanWidescreen_BecauseTikTokAndShortsCoverThatBand()
    {
        Assert.True(
            SafeAreaPreset.Vertical9By16.Bottom > SafeAreaPreset.Horizontal16By9.Bottom,
            "Vertikalni format mora imati veću donju marginu - tu su dugmad TikTok-a/Shorts-a.");
    }

    [Fact]
    public void ToPixelRect_ComputesTheUsableRectangleInRealPixels()
    {
        var (x, y, width, height) = SafeAreaPreset.Horizontal16By9.ToPixelRect(1920, 1080);

        Assert.Equal(154, x);   // 1920 * 0.08
        Assert.Equal(86, y);    // 1080 * 0.08
        Assert.Equal(1613, width);  // 1920 * (1 - 0.16)
        Assert.Equal(886, height);  // 1080 * (1 - 0.18)
    }

    [Fact]
    public void Contains_BoxInsideTheMargins_IsAccepted()
    {
        Assert.True(SafeAreaPreset.Horizontal16By9.Contains(0.1, 0.1, 0.9, 0.85));
    }

    [Fact]
    public void Contains_TextTooLowInAVerticalVideo_IsRejected()
    {
        // 0.90 bottom sits inside the band TikTok/Reels draw their own UI over - must be rejected.
        Assert.False(SafeAreaPreset.Vertical9By16.Contains(0.1, 0.7, 0.9, 0.90));
    }
}
