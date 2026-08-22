namespace NPVideoStudio.AI;

/// <summary>How comfortable a caption is to actually read at the speed it goes by.</summary>
public enum ReadingSpeedVerdict
{
    /// <summary>Comfortable for any viewer.</summary>
    Comfortable,

    /// <summary>Readable, but fast - fine for a hook line, tiring for a whole song.</summary>
    Fast,

    /// <summary>Faster than a viewer can realistically read; the line should be split or held longer.</summary>
    TooFast
}

public readonly record struct ReadabilityAssessment(
    int CharacterCount,
    double DurationSeconds,
    double CharactersPerSecond,
    ReadingSpeedVerdict Verdict);

/// <summary>
/// Line-breaking and reading-speed rules for on-screen text, ported from the user's other project
/// (bedanzivot91-dev/PROGRAM-ZA-TEKST-U-VIDEO, <c>lyrics-line-breaker.js</c>) at their request to bring
/// its text-in-video features into this app.
///
/// The app could already produce captions (Whisper transcription, lyric matching, SRT/VTT/ASS/LRC
/// export) and style them, but nothing checked whether the result was actually *readable*: a 90-character
/// line held for 900 ms is valid SRT and unreadable video. These are the two rules that decide that -
/// how wide a line may get before it wraps, and how fast the text goes by.
///
/// <see cref="MaxReadingCharactersPerSecond"/> is the standard subtitling upper bound (~15 characters per
/// second) used by broadcast subtitle guidelines, not a number invented here.
/// </summary>
public static class CaptionReadability
{
    public const double MaxReadingCharactersPerSecond = 15.0;

    /// <summary>Below this, even a very short line hasn't been on screen long enough to read and look away.</summary>
    public static readonly TimeSpan MinimumDisplayDuration = TimeSpan.FromMilliseconds(1200);

    public const int DefaultMaxCharactersPerLine = 40;
    public const int DefaultMaxLines = 2;

    /// <summary>
    /// Greedy word wrap: fills each line up to <paramref name="maxCharactersPerLine"/> and never splits a
    /// word. When the text does not fit in <paramref name="maxLines"/>, the remaining words are all kept
    /// on the last line rather than silently dropped - losing lyrics is never the right failure mode;
    /// <see cref="BuildWarnings"/> is what reports that the line is over budget.
    /// </summary>
    public static IReadOnlyList<string> BreakIntoLines(
        string text,
        int maxCharactersPerLine = DefaultMaxCharactersPerLine,
        int maxLines = DefaultMaxLines)
    {
        var words = Tokenize(text);
        if (words.Count == 0)
        {
            return Array.Empty<string>();
        }

        maxCharactersPerLine = Math.Max(1, maxCharactersPerLine);
        maxLines = Math.Max(1, maxLines);

        var lines = new List<string>();
        var current = string.Empty;

        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";

            if (current.Length > 0 && candidate.Length > maxCharactersPerLine && lines.Count < maxLines - 1)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        return lines;
    }

    /// <summary>Reading speed in characters per second, and what that means in practice.</summary>
    public static ReadabilityAssessment Assess(string text, TimeSpan duration)
    {
        var characterCount = (text ?? string.Empty).Trim().Length;
        var seconds = Math.Max(0.001, duration.TotalSeconds);
        var charactersPerSecond = characterCount / seconds;

        var verdict = charactersPerSecond > MaxReadingCharactersPerSecond
            ? ReadingSpeedVerdict.TooFast
            : charactersPerSecond > MaxReadingCharactersPerSecond * 0.8
                ? ReadingSpeedVerdict.Fast
                : ReadingSpeedVerdict.Comfortable;

        return new ReadabilityAssessment(
            characterCount,
            duration.TotalSeconds,
            Math.Round(charactersPerSecond, 2),
            verdict);
    }

    /// <summary>
    /// Every readability problem with one caption line, in Serbian, ready to show next to it. Empty list
    /// means the line is fine - callers can treat "no warnings" as the pass condition.
    /// </summary>
    public static IReadOnlyList<string> BuildWarnings(
        string text,
        TimeSpan duration,
        int maxCharactersPerLine = DefaultMaxCharactersPerLine,
        int maxLines = DefaultMaxLines)
    {
        var warnings = new List<string>();
        var trimmed = (text ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            warnings.Add("Titl nema tekst.");
            return warnings;
        }

        if (duration < MinimumDisplayDuration)
        {
            warnings.Add(
                $"Titl se prikazuje prekratko ({duration.TotalMilliseconds:0} ms) - preporučeno je bar " +
                $"{MinimumDisplayDuration.TotalMilliseconds:0} ms.");
        }

        var assessment = Assess(trimmed, duration);
        if (assessment.Verdict == ReadingSpeedVerdict.TooFast)
        {
            warnings.Add(
                $"Tekst prolazi prebrzo ({assessment.CharactersPerSecond:0.#} znakova u sekundi, granica je " +
                $"{MaxReadingCharactersPerSecond:0}) - podelite ga ili produžite trajanje.");
        }

        var lines = BreakIntoLines(trimmed, maxCharactersPerLine, maxLines);
        if (lines.Count > maxLines)
        {
            warnings.Add($"Titl zauzima {lines.Count} reda, a dozvoljeno je najviše {maxLines}.");
        }

        var overlongLine = lines.FirstOrDefault(line => line.Length > maxCharactersPerLine);
        if (overlongLine is not null)
        {
            warnings.Add(
                $"Red je duži od {maxCharactersPerLine} znakova ({overlongLine.Length}) - na užem ekranu " +
                "može da izađe iz kadra.");
        }

        return warnings;
    }

    private static List<string> Tokenize(string text) =>
        (text ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
}
