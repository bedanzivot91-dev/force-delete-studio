using System.Text;

namespace NPVideoStudio.AI;

/// <summary>
/// Serbian Cyrillic <-> Latin transliteration (spec Phase 5). Pure and lossless in both directions for
/// standard Serbian orthography: three Cyrillic letters (љ/њ/џ) are digraphs in Latin (lj/nj/dž), and
/// this is the only place in the codebase allowed to produce a converted copy of lyrics/captions - it
/// must never be used to overwrite a <see cref="Domain.SongLibraryEntry.FullLyrics"/> or similar stored
/// original, only to render an alternate-script view of it (spec: "don't let normalization overwrite
/// the original text").
/// </summary>
public static class SerbianScriptConverter
{
    // Ordered longest-key-first so "Nj"/"NJ"/"nj" (etc.) are matched before the single letter "N"/"n".
    private static readonly (string Cyrillic, string Latin)[] Digraphs =
    {
        ("Љ", "Lj"), ("Њ", "Nj"), ("Џ", "Dž")
    };

    private static readonly (string Cyrillic, string Latin)[] SingleLetters =
    {
        ("А", "A"), ("Б", "B"), ("В", "V"), ("Г", "G"), ("Д", "D"), ("Ђ", "Đ"), ("Е", "E"), ("Ж", "Ž"),
        ("З", "Z"), ("И", "I"), ("Ј", "J"), ("К", "K"), ("Л", "L"), ("М", "M"), ("Н", "N"), ("О", "O"),
        ("П", "P"), ("Р", "R"), ("С", "S"), ("Т", "T"), ("Ћ", "Ć"), ("У", "U"), ("Ф", "F"), ("Х", "H"),
        ("Ц", "C"), ("Ч", "Č"), ("Ш", "Š")
    };

    private static readonly Dictionary<char, string> CyrillicToLatinMap = BuildCyrillicToLatinMap();
    private static readonly List<(string Latin, string Cyrillic)> LatinToCyrillicOrdered = BuildLatinToCyrillicList();

    /// <summary>True if the text contains at least one Serbian Cyrillic letter (a quick script guess, not a full language detector).</summary>
    public static bool ContainsCyrillic(string text)
    {
        foreach (var ch in text)
        {
            if (CyrillicToLatinMap.ContainsKey(char.ToUpperInvariant(ch)))
            {
                return true;
            }
        }
        return false;
    }

    public static string ToLatin(string cyrillicText)
    {
        var builder = new StringBuilder(cyrillicText.Length);
        for (var i = 0; i < cyrillicText.Length; i++)
        {
            var ch = cyrillicText[i];
            var upper = char.ToUpperInvariant(ch);
            if (!CyrillicToLatinMap.TryGetValue(upper, out var latinUpper))
            {
                builder.Append(ch);
                continue;
            }

            if (!char.IsUpper(ch))
            {
                builder.Append(latinUpper.ToLowerInvariant());
                continue;
            }

            // Љ/Њ/Џ have no separate "all-caps vs title-case" Cyrillic form - a single uppercase
            // codepoint stands for both "LJUBAV" and "Ljubav". Disambiguate by peeking at the next
            // letter: lowercase next letter means this was just a capitalized word ("Lj"), anything
            // else (another uppercase letter, digit, punctuation, or end of string) means all-caps ("LJ").
            if (latinUpper.Length > 1)
            {
                var nextIsLowerLetter = i + 1 < cyrillicText.Length && char.IsLower(cyrillicText[i + 1]);
                builder.Append(nextIsLowerLetter
                    ? char.ToUpperInvariant(latinUpper[0]) + latinUpper[1..].ToLowerInvariant()
                    : latinUpper);
            }
            else
            {
                builder.Append(latinUpper);
            }
        }
        return builder.ToString();
    }

    public static string ToCyrillic(string latinText)
    {
        var builder = new StringBuilder(latinText.Length);
        var i = 0;
        while (i < latinText.Length)
        {
            var matched = false;
            foreach (var (latin, cyrillic) in LatinToCyrillicOrdered)
            {
                if (i + latin.Length > latinText.Length)
                {
                    continue;
                }

                if (string.Compare(latinText, i, latin, 0, latin.Length, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    continue;
                }

                var sourceSlice = latinText.Substring(i, latin.Length);
                builder.Append(MatchCyrillicCase(sourceSlice, cyrillic));
                i += latin.Length;
                matched = true;
                break;
            }

            if (!matched)
            {
                builder.Append(latinText[i]);
                i++;
            }
        }
        return builder.ToString();
    }

    /// <summary>
    /// Preserves the source casing pattern for a (possibly multi-character) matched span: all-upper
    /// source -> all-upper Cyrillic, otherwise title-case-first-letter-only or all-lowercase, matching
    /// how "NJ"/"Nj"/"nj" should each map to the single Cyrillic letter's upper/title/lower form.
    /// </summary>
    private static string MatchCyrillicCase(string source, string cyrillicUpper)
    {
        if (source.Length > 0 && source.All(c => !char.IsLetter(c) || char.IsUpper(c)))
        {
            return cyrillicUpper;
        }
        if (source.Length > 0 && char.IsUpper(source[0]))
        {
            return cyrillicUpper.Length == 1
                ? cyrillicUpper
                : char.ToUpperInvariant(cyrillicUpper[0]) + cyrillicUpper[1..].ToLowerInvariant();
        }
        return cyrillicUpper.ToLowerInvariant();
    }

    private static Dictionary<char, string> BuildCyrillicToLatinMap()
    {
        var map = new Dictionary<char, string>();
        foreach (var (cyrillic, latin) in Digraphs.Concat(SingleLetters))
        {
            map[cyrillic[0]] = latin.ToUpperInvariant();
        }
        return map;
    }

    private static List<(string Latin, string Cyrillic)> BuildLatinToCyrillicList()
    {
        // Digraphs first so "dž"/"lj"/"nj" are consumed before their leading single letter would match.
        var list = Digraphs.Select(d => (d.Latin.ToUpperInvariant(), d.Cyrillic)).ToList();
        list.AddRange(SingleLetters.Select(s => (s.Latin.ToUpperInvariant(), s.Cyrillic)));
        return list;
    }
}
