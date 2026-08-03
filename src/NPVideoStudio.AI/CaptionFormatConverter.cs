using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NPVideoStudio.Domain;

namespace NPVideoStudio.AI;

/// <summary>
/// Import/export between the word-level <see cref="CaptionWord"/> model and standard subtitle formats
/// (spec Phase 6: SRT/VTT/ASS/TXT/JSON/LRC). Pure string transforms, no file I/O - callers read/write
/// the file themselves. "Lines" for the block-based formats (SRT/VTT/ASS/TXT) are runs of words ending
/// at a <see cref="CaptionWord.LineBreakAfter"/> word - see that property's doc comment.
///
/// Real, deliberate gaps: ASS has no importer (parsing arbitrary override tags/karaoke timing correctly
/// is a much bigger job than writing a minimal-but-correct file, and a fragile parser would silently
/// mis-import real files - worse than not supporting it yet). TXT has no importer either - plain text
/// carries no timing at all, and inventing timestamps for it would violate this codebase's "never guess"
/// rule far more directly than any other gap in this file.
/// </summary>
public static class CaptionFormatConverter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // Matches both SRT ("00:00:01,000") and VTT ("00:00:01.000") timestamp pairs - they differ only in
    // the comma vs. dot separator, so one block parser serves both formats.
    private static readonly Regex BlockTimestampRegex = new(
        @"(\d{2}):(\d{2}):(\d{2})[,.](\d{3})\s*-->\s*(\d{2}):(\d{2}):(\d{2})[,.](\d{3})", RegexOptions.Compiled);

    private static readonly Regex LrcLineRegex = new(@"^\[(\d{2}):(\d{2})\.(\d{2})\](.*)$", RegexOptions.Compiled);

    public static string ToSrt(IReadOnlyList<CaptionWord> words)
    {
        var builder = new StringBuilder();
        var index = 1;
        foreach (var line in GroupIntoLines(words))
        {
            var (text, start, end) = LineSpan(line);
            if (text is null)
            {
                continue;
            }

            builder.Append(index).Append('\n');
            builder.Append(SrtWriter.FormatTimestamp(start)).Append(" --> ").Append(SrtWriter.FormatTimestamp(end)).Append('\n');
            builder.Append(text).Append('\n').Append('\n');
            index++;
        }

        return builder.ToString();
    }

    public static string ToVtt(IReadOnlyList<CaptionWord> words)
    {
        var builder = new StringBuilder("WEBVTT\n\n");
        foreach (var line in GroupIntoLines(words))
        {
            var (text, start, end) = LineSpan(line);
            if (text is null)
            {
                continue;
            }

            builder.Append(FormatVttTimestamp(start)).Append(" --> ").Append(FormatVttTimestamp(end)).Append('\n');
            builder.Append(text).Append('\n').Append('\n');
        }

        return builder.ToString();
    }

    public static string ToAss(IReadOnlyList<CaptionWord> words)
    {
        var builder = new StringBuilder();
        builder.Append("[Script Info]\nTitle: NP Video Studio captions\nScriptType: v4.00+\n\n");
        builder.Append("[V4+ Styles]\n");
        builder.Append("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n");
        builder.Append("Style: Default,Arial,48,&H00FFFFFF,&H000000FF,&H00000000,&H64000000,0,0,0,0,100,100,0,0,1,2,1,2,10,10,20,1\n\n");
        builder.Append("[Events]\n");
        builder.Append("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n");

        foreach (var line in GroupIntoLines(words))
        {
            var (text, start, end) = LineSpan(line);
            if (text is null)
            {
                continue;
            }

            builder.Append($"Dialogue: 0,{FormatAssTimestamp(start)},{FormatAssTimestamp(end)},Default,,0,0,0,,{EscapeAss(text)}\n");
        }

        return builder.ToString();
    }

    public static string ToPlainText(IReadOnlyList<CaptionWord> words) =>
        string.Join('\n', GroupIntoLines(words)
            .Select(line => LineSpan(line).Text)
            .Where(text => text is not null));

    public static string ToLrc(IReadOnlyList<CaptionWord> words)
    {
        var builder = new StringBuilder();
        foreach (var line in GroupIntoLines(words))
        {
            var (text, start, _) = LineSpan(line);
            if (text is null)
            {
                continue;
            }

            builder.Append(FormatLrcTimestamp(start)).Append(text).Append('\n');
        }

        return builder.ToString();
    }

    public static string ToJson(IReadOnlyList<CaptionWord> words) => JsonSerializer.Serialize(words, JsonOptions);

    public static List<CaptionWord> FromJson(string json) =>
        JsonSerializer.Deserialize<List<CaptionWord>>(json, JsonOptions) ?? new List<CaptionWord>();

    public static List<CaptionWord> FromSrt(string content) => FromBlockFormat(content);

    public static List<CaptionWord> FromVtt(string content) => FromBlockFormat(content);

    public static List<CaptionWord> FromLrc(string content)
    {
        var entries = new List<(TimeSpan Start, string Text)>();
        foreach (var rawLine in content.Split('\n'))
        {
            var match = LrcLineRegex.Match(rawLine.TrimEnd('\r'));
            if (!match.Success)
            {
                continue;
            }

            var start = new TimeSpan(0, 0, int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value),
                int.Parse(match.Groups[3].Value) * 10);
            var text = match.Groups[4].Value.Trim();
            if (text.Length > 0)
            {
                entries.Add((start, text));
            }
        }

        entries.Sort((a, b) => a.Start.CompareTo(b.Start));

        var words = new List<CaptionWord>();
        for (var i = 0; i < entries.Count; i++)
        {
            var (start, text) = entries[i];
            var defaultDuration = TimeSpan.FromSeconds(Math.Max(2, text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length * 0.4));
            var end = i + 1 < entries.Count && entries[i + 1].Start > start ? entries[i + 1].Start : start + defaultDuration;
            words.AddRange(DistributeWordsAcrossSpan(text, start, end, CaptionWordSource.Lrc));
        }

        return words;
    }

    private static List<CaptionWord> FromBlockFormat(string content)
    {
        var words = new List<CaptionWord>();
        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');

        foreach (var block in normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var blockLines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var timestampIndex = Array.FindIndex(blockLines, l => BlockTimestampRegex.IsMatch(l));
            if (timestampIndex < 0)
            {
                continue;
            }

            var match = BlockTimestampRegex.Match(blockLines[timestampIndex]);
            var start = new TimeSpan(0, int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value),
                int.Parse(match.Groups[3].Value), int.Parse(match.Groups[4].Value));
            var end = new TimeSpan(0, int.Parse(match.Groups[5].Value), int.Parse(match.Groups[6].Value),
                int.Parse(match.Groups[7].Value), int.Parse(match.Groups[8].Value));

            var text = string.Join(' ', blockLines.Skip(timestampIndex + 1)).Trim();
            if (text.Length == 0 || end <= start)
            {
                continue;
            }

            words.AddRange(DistributeWordsAcrossSpan(text, start, end, CaptionWordSource.Manual));
        }

        return words;
    }

    /// <summary>Splits one line's text into words with an even timing distribution across [start, end) - the best honest estimate available from a format that doesn't carry per-word timing.</summary>
    private static List<CaptionWord> DistributeWordsAcrossSpan(string text, TimeSpan start, TimeSpan end, CaptionWordSource source)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return new List<CaptionWord>();
        }

        var perWord = (end - start) / tokens.Length;
        var words = new List<CaptionWord>(tokens.Length);
        var cursor = start;
        for (var i = 0; i < tokens.Length; i++)
        {
            var wordEnd = i == tokens.Length - 1 ? end : cursor + perWord;
            words.Add(new CaptionWord
            {
                OriginalText = tokens[i],
                NormalizedText = LyricMatcher.Normalize(tokens[i]),
                Start = cursor,
                End = wordEnd,
                Confidence = 1.0,
                Source = source,
                LineBreakAfter = i == tokens.Length - 1
            });
            cursor = wordEnd;
        }

        return words;
    }

    private static List<List<CaptionWord>> GroupIntoLines(IReadOnlyList<CaptionWord> words)
    {
        var lines = new List<List<CaptionWord>>();
        var current = new List<CaptionWord>();
        foreach (var word in words)
        {
            current.Add(word);
            if (word.LineBreakAfter)
            {
                lines.Add(current);
                current = new List<CaptionWord>();
            }
        }

        if (current.Count > 0)
        {
            lines.Add(current);
        }

        return lines;
    }

    private static (string? Text, TimeSpan Start, TimeSpan End) LineSpan(List<CaptionWord> line)
    {
        var text = string.Join(' ', line.Select(w => w.OriginalText)).Trim();
        var start = line[0].Start;
        var end = line[^1].End;
        return text.Length == 0 || end <= start ? (null, start, end) : (text, start, end);
    }

    private static string FormatVttTimestamp(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
        {
            t = TimeSpan.Zero;
        }

        return $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}.{t.Milliseconds:D3}";
    }

    private static string FormatAssTimestamp(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
        {
            t = TimeSpan.Zero;
        }

        return $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}.{t.Milliseconds / 10:D2}";
    }

    private static string FormatLrcTimestamp(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
        {
            t = TimeSpan.Zero;
        }

        return $"[{(int)t.TotalMinutes:D2}:{t.Seconds:D2}.{t.Milliseconds / 10:D2}]";
    }

    /// <summary>Escapes ASS special characters (spec: "{", "}", "\", newlines) - backslash must be escaped first or the brace-escaping's own backslashes would get double-escaped.</summary>
    private static string EscapeAss(string text) =>
        text.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}").Replace("\r\n", "\\N").Replace("\n", "\\N");
}
