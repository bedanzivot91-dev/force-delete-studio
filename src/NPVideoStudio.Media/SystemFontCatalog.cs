using SkiaSharp;

namespace NPVideoStudio.Media;

/// <summary>One real font file found on this machine, with everything the app needs to decide whether it
/// is usable for Serbian captions.</summary>
public sealed record InstalledFont(
    string FamilyName,
    string FilePath,
    bool IsBold,
    bool IsItalic,
    bool SupportsSerbianLatin,
    bool SupportsSerbianCyrillic)
{
    /// <summary>Usable for this app's captions: Serbian Latin is the app's default script, so a font that
    /// can't draw š/đ/č/ć/ž is not merely imperfect here - it will render the user's own lyrics as boxes
    /// or blanks.</summary>
    public bool IsUsableForSerbian => SupportsSerbianLatin;

    public string DisplayLabel => (IsBold, IsItalic) switch
    {
        (true, true) => $"{FamilyName} (podebljano, kurziv)",
        (true, false) => $"{FamilyName} (podebljano)",
        (false, true) => $"{FamilyName} (kurziv)",
        _ => FamilyName
    };
}

/// <summary>
/// Real font management: lists the font files actually installed on this machine and checks, per font,
/// whether it can genuinely draw Serbian characters.
///
/// Why the glyph check is the whole point rather than a nicety: this project already got burned by
/// exactly this. The bundled Inter font was dropped from the app (see Program.cs's comment) after it was
/// found to corrupt uppercase Š/Č/Ž at several sizes - a font can be installed, load fine, report a name,
/// and still have no glyph for the characters this app's users actually type. Listing font names without
/// verifying coverage would hand the user a font that silently ruins their captions.
///
/// Enumerates font FILES rather than family names on purpose: ffmpeg's drawtext is driven here through
/// <c>fontfile=</c> (see <see cref="CaptionFontResolver"/>), because <c>font=</c> needs fontconfig, which
/// is not guaranteed present in the bundled Windows ffmpeg build. A family name we cannot turn into a
/// file path is of no use to the renderer.
/// </summary>
public static class SystemFontCatalog
{
    /// <summary>The characters that separate a font usable for Serbian Latin from one that isn't.</summary>
    public const string SerbianLatinProbe = "šđčćžŠĐČĆŽ";

    /// <summary>Serbian Cyrillic letters that are absent from many "Cyrillic" fonts (љ, њ, ћ, џ, ђ, ј).</summary>
    public const string SerbianCyrillicProbe = "љњћџђјЉЊЋЏЂЈ";

    private static readonly string[] FontExtensions = { ".ttf", ".otf", ".ttc" };

    /// <summary>
    /// Every font file this machine has, newest-style user fonts included. Never throws: a font directory
    /// that doesn't exist, or a single unreadable/corrupt font file, is skipped rather than failing the
    /// whole listing - one bad font must not make the font picker unusable.
    /// </summary>
    public static IReadOnlyList<InstalledFont> ListInstalledFonts(IEnumerable<string>? extraDirectories = null)
    {
        var directories = new List<string>();

        foreach (var folder in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.LocalApplicationData })
        {
            var root = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            directories.Add(Path.Combine(root, "Fonts"));
            directories.Add(Path.Combine(root, "Microsoft", "Windows", "Fonts"));
        }

        // Linux/dev-sandbox locations, so this is testable off Windows too.
        directories.Add("/usr/share/fonts");
        directories.Add("/usr/local/share/fonts");

        if (extraDirectories is not null)
        {
            directories.AddRange(extraDirectories);
        }

        var results = new List<InstalledFont>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in directories.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Where(f => FontExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
            }
            catch
            {
                continue; // unreadable directory - skip, never fail the whole listing
            }

            foreach (var file in files)
            {
                if (!seenPaths.Add(file))
                {
                    continue;
                }

                var font = TryInspect(file);
                if (font is not null)
                {
                    results.Add(font);
                }
            }
        }

        return results
            .OrderBy(f => f.FamilyName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(f => f.IsBold)
            .ThenBy(f => f.IsItalic)
            .ToList();
    }

    /// <summary>Only the fonts that can actually draw Serbian Latin - what the caption font picker should
    /// offer, so a user cannot pick a font that will turn their lyrics into boxes.</summary>
    public static IReadOnlyList<InstalledFont> ListFontsUsableForSerbian(IEnumerable<string>? extraDirectories = null) =>
        ListInstalledFonts(extraDirectories).Where(f => f.IsUsableForSerbian).ToList();

    /// <summary>Reads one font file. Returns null if it is not a font this machine's Skia build can open
    /// (corrupt, unsupported, or a bitmap-only legacy .fon renamed to .ttf).</summary>
    public static InstalledFont? TryInspect(string filePath)
    {
        try
        {
            using var typeface = SKTypeface.FromFile(filePath);
            if (typeface is null)
            {
                return null;
            }

            return new InstalledFont(
                FamilyName: typeface.FamilyName,
                FilePath: filePath,
                IsBold: typeface.FontStyle.Weight >= (int)SKFontStyleWeight.SemiBold,
                IsItalic: typeface.FontStyle.Slant != SKFontStyleSlant.Upright,
                SupportsSerbianLatin: ContainsAllCharacters(typeface, SerbianLatinProbe),
                SupportsSerbianCyrillic: ContainsAllCharacters(typeface, SerbianCyrillicProbe));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// True only when the font has a real glyph for EVERY character given. A missing glyph comes back from
    /// Skia as glyph id 0 (".notdef", the box/blank), which is exactly the silent failure this guards
    /// against - so "any glyph missing" means "not usable", not "mostly fine".
    /// </summary>
    public static bool ContainsAllCharacters(SKTypeface typeface, string characters)
    {
        foreach (var character in characters)
        {
            if (typeface.GetGlyph(character) == 0)
            {
                return false;
            }
        }

        return true;
    }
}
