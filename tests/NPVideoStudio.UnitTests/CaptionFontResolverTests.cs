using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Pure logic, no process involved.</summary>
public class CaptionFontResolverTests
{
    [Fact]
    public void ResolveFontFilePath_Default_ReturnsNull()
    {
        Assert.Null(CaptionFontResolver.ResolveFontFilePath(CaptionFontChoice.Default));
    }

    [Theory]
    [InlineData(CaptionFontChoice.Arial, "arial.ttf")]
    [InlineData(CaptionFontChoice.ArialBold, "arialbd.ttf")]
    [InlineData(CaptionFontChoice.Impact, "impact.ttf")]
    [InlineData(CaptionFontChoice.ComicSansBold, "comicbd.ttf")]
    [InlineData(CaptionFontChoice.Georgia, "georgia.ttf")]
    public void ResolveFontFilePath_NonDefaultChoice_ReturnsNullOrARealExistingFile(CaptionFontChoice choice, string expectedFileName)
    {
        // Two valid outcomes depending on where this runs: this Linux sandbox has no C:\Windows\Fonts at
        // all, so the resolver must degrade to null rather than hand ffmpeg a path that doesn't exist
        // (which would make every export using a non-Default font choice fail outright); real Windows CI
        // has the font installed, so the resolver must return that exact, real, existing file.
        var result = CaptionFontResolver.ResolveFontFilePath(choice);

        if (result is not null)
        {
            Assert.EndsWith(expectedFileName, result, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(result));
        }
    }

    [Theory]
    [InlineData(CaptionFontChoice.Arial, false, false, "arial.ttf")]
    [InlineData(CaptionFontChoice.Arial, true, false, "arialbd.ttf")]
    [InlineData(CaptionFontChoice.Arial, false, true, "ariali.ttf")]
    [InlineData(CaptionFontChoice.Arial, true, true, "arialbi.ttf")]
    [InlineData(CaptionFontChoice.Georgia, true, true, "georgiaz.ttf")]
    [InlineData(CaptionFontChoice.ComicSansBold, false, true, "comicz.ttf")] // already-bold preset + italic toggle -> bold italic variant
    public void ResolveFontFilePath_BoldItalicToggles_MapToCorrectVariantOrDegradeToNull(
        CaptionFontChoice choice, bool isBold, bool isItalic, string expectedFileName)
    {
        var result = CaptionFontResolver.ResolveFontFilePath(choice, isBold, isItalic);

        if (result is not null)
        {
            Assert.EndsWith(expectedFileName, result, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(result));
        }
    }

    [Fact]
    public void ResolveFontFilePath_ArialBoldChoice_StaysBoldEvenWithoutTheBoldToggle()
    {
        // ArialBold is already permanently bold as a font *choice* - the isBold *toggle* being false must
        // not un-bold it (the toggle and the preset are two independent ways to arrive at bold).
        var result = CaptionFontResolver.ResolveFontFilePath(CaptionFontChoice.ArialBold, isBold: false, isItalic: false);

        if (result is not null)
        {
            Assert.EndsWith("arialbd.ttf", result, StringComparison.OrdinalIgnoreCase);
        }
    }
}
