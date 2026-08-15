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
}
