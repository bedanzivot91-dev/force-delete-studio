using NPVideoStudio.Domain;

namespace NPVideoStudio.Media;

/// <summary>
/// Maps a <see cref="CaptionFontChoice"/> (+ optional bold/italic toggles) to a real font file ffmpeg's
/// drawtext can load via <c>fontfile=</c>. Deliberately a fixed, small set of standard Windows system
/// fonts (this app is Windows-only) rather than an open-ended font name lookup, which would need
/// fontconfig - not guaranteed present in the bundled gyan.dev ffmpeg build (see CLAUDE.md).
/// </summary>
public static class CaptionFontResolver
{
    private static readonly string WindowsFontsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

    /// <summary>Null for <see cref="CaptionFontChoice.Default"/> (ffmpeg's built-in default font, same
    /// look as before per-clip font choice existed) or when the font file genuinely isn't on disk (e.g.
    /// this method running outside Windows, or a stripped-down Windows install) - callers should omit
    /// <c>fontfile=</c> entirely in that case rather than pass a bad path.
    ///
    /// <paramref name="isBold"/>/<paramref name="isItalic"/> are independent per-clip toggles, separate
    /// from the "already permanently bold" <see cref="CaptionFontChoice.ArialBold"/>/
    /// <see cref="CaptionFontChoice.ComicSansBold"/> presets - either source can make the effective weight
    /// bold, so an existing ArialBold clip stays bold with the toggle off (backward compatible), while the
    /// toggle can also make a plain Arial/Georgia clip bold without switching font choice.</summary>
    public static string? ResolveFontFilePath(CaptionFontChoice choice, bool isBold = false, bool isItalic = false)
    {
        var (family, choiceIsBold) = choice switch
        {
            CaptionFontChoice.Arial => ("Arial", false),
            CaptionFontChoice.ArialBold => ("Arial", true),
            CaptionFontChoice.Impact => ("Impact", false),
            CaptionFontChoice.ComicSansBold => ("ComicSansMS", true),
            CaptionFontChoice.Georgia => ("Georgia", false),
            _ => ((string?)null, false)
        };

        if (family is null)
        {
            return null;
        }

        var effectiveBold = isBold || choiceIsBold;
        var fileName = (family, effectiveBold, isItalic) switch
        {
            ("Arial", false, false) => "arial.ttf",
            ("Arial", true, false) => "arialbd.ttf",
            ("Arial", false, true) => "ariali.ttf",
            ("Arial", true, true) => "arialbi.ttf",
            ("Georgia", false, false) => "georgia.ttf",
            ("Georgia", true, false) => "georgiab.ttf",
            ("Georgia", false, true) => "georgiai.ttf",
            ("Georgia", true, true) => "georgiaz.ttf",
            ("ComicSansMS", false, false) => "comic.ttf",
            ("ComicSansMS", true, false) => "comicbd.ttf",
            ("ComicSansMS", false, true) => "comici.ttf",
            ("ComicSansMS", true, true) => "comicz.ttf",
            ("Impact", _, _) => "impact.ttf",
            _ => null
        };

        if (fileName is null)
        {
            return null;
        }

        var fullPath = Path.Combine(WindowsFontsDir, fileName);
        return File.Exists(fullPath) ? fullPath : null;
    }
}
