using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Real font enumeration + glyph-coverage tests. These read actual font files off the machine running the
/// tests (via SkiaSharp), so they are not asserting against a mock font system.
/// </summary>
public class SystemFontCatalogTests
{
    [Fact]
    public void ListInstalledFonts_OnAMachineWithFonts_FindsRealFontFilesThatExistOnDisk()
    {
        var fonts = SystemFontCatalog.ListInstalledFonts();

        // Both the Linux CI/dev sandbox and Windows have fonts installed; if this ever returns nothing the
        // font picker would silently be empty, which is worth failing on rather than passing vacuously.
        Assert.NotEmpty(fonts);
        Assert.All(fonts, f =>
        {
            Assert.True(File.Exists(f.FilePath), $"Prijavljen font ne postoji na disku: {f.FilePath}");
            Assert.False(string.IsNullOrWhiteSpace(f.FamilyName));
        });
    }

    [Fact]
    public void ListInstalledFonts_NeverReturnsTheSameFileTwice()
    {
        var fonts = SystemFontCatalog.ListInstalledFonts();

        Assert.Equal(
            fonts.Select(f => f.FilePath.ToLowerInvariant()).Distinct().Count(),
            fonts.Count);
    }

    [Fact]
    public void ListInstalledFonts_MissingOrUnreadableDirectory_IsSkippedInsteadOfThrowing()
    {
        var fonts = SystemFontCatalog.ListInstalledFonts(new[] { "/ovaj/folder/sigurno/ne/postoji" });

        Assert.NotEmpty(fonts);
    }

    [Fact]
    public void TryInspect_FileThatIsNotAFont_ReturnsNullInsteadOfThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nije-font-{Guid.NewGuid():N}.ttf");
        File.WriteAllText(path, "ovo nije font");

        try
        {
            Assert.Null(SystemFontCatalog.TryInspect(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SerbianCheck_IsActuallyDiscriminating_NotJustAlwaysTrue()
    {
        var fonts = SystemFontCatalog.ListInstalledFonts();

        // The whole point of the check is that it can say "no". A build where every font passes would mean
        // the glyph probe is broken and the user could still pick a font that ruins their captions.
        Assert.Contains(fonts, f => f.IsUsableForSerbian);
    }

    [Fact]
    public void ListFontsUsableForSerbian_ReturnsOnlyFontsThatPassTheLatinGlyphCheck()
    {
        var usable = SystemFontCatalog.ListFontsUsableForSerbian();

        Assert.All(usable, f => Assert.True(f.SupportsSerbianLatin));
    }

    [Fact]
    public void DisplayLabel_MarksBoldAndItalicInSerbian()
    {
        var bold = new InstalledFont("Arial", "/tmp/a.ttf", IsBold: true, IsItalic: false, true, true);
        var italic = new InstalledFont("Arial", "/tmp/a.ttf", IsBold: false, IsItalic: true, true, true);
        var both = new InstalledFont("Arial", "/tmp/a.ttf", IsBold: true, IsItalic: true, true, true);
        var plain = new InstalledFont("Arial", "/tmp/a.ttf", IsBold: false, IsItalic: false, true, true);

        Assert.Equal("Arial (podebljano)", bold.DisplayLabel);
        Assert.Equal("Arial (kurziv)", italic.DisplayLabel);
        Assert.Equal("Arial (podebljano, kurziv)", both.DisplayLabel);
        Assert.Equal("Arial", plain.DisplayLabel);
    }

    [Fact]
    public void AFontMissingSerbianGlyphs_IsReportedAsUnusable()
    {
        var missing = new InstalledFont("Neki Font", "/tmp/x.ttf", false, false,
            SupportsSerbianLatin: false, SupportsSerbianCyrillic: false);

        Assert.False(missing.IsUsableForSerbian);
    }
}
