using NPVideoStudio.Domain;

namespace NPVideoStudio.Media;

/// <summary>
/// Maps a <see cref="CaptionFontChoice"/> to a real font file ffmpeg's drawtext can load via
/// <c>fontfile=</c>. Deliberately a fixed, small set of standard Windows system fonts (this app is
/// Windows-only) rather than an open-ended font name lookup, which would need fontconfig - not
/// guaranteed present in the bundled gyan.dev ffmpeg build (see CLAUDE.md).
/// </summary>
public static class CaptionFontResolver
{
    private static readonly string WindowsFontsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

    /// <summary>Null for <see cref="CaptionFontChoice.Default"/> (ffmpeg's built-in default font, same
    /// look as before per-clip font choice existed) or when the font file genuinely isn't on disk (e.g.
    /// this method running outside Windows, or a stripped-down Windows install) - callers should omit
    /// <c>fontfile=</c> entirely in that case rather than pass a bad path.</summary>
    public static string? ResolveFontFilePath(CaptionFontChoice choice)
    {
        var fileName = choice switch
        {
            CaptionFontChoice.Arial => "arial.ttf",
            CaptionFontChoice.ArialBold => "arialbd.ttf",
            CaptionFontChoice.Impact => "impact.ttf",
            CaptionFontChoice.ComicSansBold => "comicbd.ttf",
            CaptionFontChoice.Georgia => "georgia.ttf",
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
