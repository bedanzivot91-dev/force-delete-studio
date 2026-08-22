using NPVideoStudio.Domain;

namespace NPVideoStudio.Media;

/// <summary>Resolves either a real installed font chosen by the user or a legacy built-in font preset to
/// the exact file ffmpeg drawtext should load. Paths are preferred, but family-name fallback keeps a
/// project portable between Windows machines whose font file locations differ.</summary>
public static class CaptionFontResolver
{
    private static readonly string WindowsFontsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

    private static readonly Lazy<IReadOnlyList<InstalledFont>> InstalledFonts = new(
        () => SystemFontCatalog.ListInstalledFonts(),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static string? ResolveFontFilePath(TimelineClip clip) => ResolveFontFilePath(
        clip.FontChoice,
        clip.IsTextBold,
        clip.IsTextItalic,
        clip.TextFontFilePath,
        clip.TextFontFamilyName);

    /// <summary>Returns a real readable font file or null. A custom exact path wins. If it disappeared
    /// (for example the project was moved to another PC), the resolver looks up the saved family and the
    /// closest bold/italic variant. Only then does it fall back to the legacy enum mapping.</summary>
    public static string? ResolveFontFilePath(
        CaptionFontChoice choice,
        bool isBold = false,
        bool isItalic = false,
        string? installedFontFilePath = null,
        string? installedFontFamilyName = null)
    {
        if (!string.IsNullOrWhiteSpace(installedFontFilePath) && File.Exists(installedFontFilePath))
        {
            return installedFontFilePath;
        }

        if (!string.IsNullOrWhiteSpace(installedFontFamilyName))
        {
            var candidates = InstalledFonts.Value
                .Where(f => string.Equals(f.FamilyName, installedFontFamilyName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.IsBold == isBold ? 0 : 1)
                .ThenBy(f => f.IsItalic == isItalic ? 0 : 1)
                .ThenBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var matchingInstalled = candidates.FirstOrDefault();
            if (matchingInstalled is not null && File.Exists(matchingInstalled.FilePath))
            {
                return matchingInstalled.FilePath;
            }
        }

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
