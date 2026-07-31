using System.Text.RegularExpressions;
using NPVideoStudio.Domain;

namespace NPVideoStudio.AI;

/// <summary>A transcribed stretch of audio, decoupled from Whisper.net's own type so the matching logic below is testable without a model or any network access.</summary>
public readonly record struct TranscribedSegment(TimeSpan Start, TimeSpan End, string Text);

/// <summary>
/// Pure phrase-matching logic over an already-transcribed segment list. Kept separate from
/// <see cref="LyricSearchService"/> (which needs a real Whisper model) so the matching behavior
/// itself - normalization, fuzzy overlap, dedup - can be unit tested without any network access.
/// </summary>
public static class LyricMatcher
{
    private static readonly Regex PunctuationRegex = new(@"[^\p{L}\p{N}\s]", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly TimeSpan DefaultPadding = TimeSpan.FromSeconds(1.5);

    public static List<LyricMatch> Search(IReadOnlyList<TranscribedSegment> segments, string phrase, TimeSpan? padding = null)
    {
        var pad = padding ?? DefaultPadding;
        var normalizedPhrase = Normalize(phrase);
        var phraseTokens = normalizedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (phraseTokens.Count == 0)
        {
            return new List<LyricMatch>();
        }

        var candidates = new List<LyricMatch>();

        for (var windowSize = 1; windowSize <= 3; windowSize++)
        {
            for (var i = 0; i + windowSize <= segments.Count; i++)
            {
                var window = segments.Skip(i).Take(windowSize).ToList();
                var windowText = string.Join(" ", window.Select(s => s.Text)).Trim();
                var normalizedWindow = Normalize(windowText);
                if (normalizedWindow.Length == 0)
                {
                    continue;
                }

                double confidence;
                if (normalizedWindow.Contains(normalizedPhrase, StringComparison.Ordinal))
                {
                    confidence = 1.0;
                }
                else
                {
                    var windowTokens = normalizedWindow.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                    var overlap = phraseTokens.Count(t => windowTokens.Contains(t));
                    var ratio = (double)overlap / phraseTokens.Count;
                    if (ratio < 0.6)
                    {
                        continue;
                    }
                    confidence = ratio * 0.85;
                }

                var start = window[0].Start;
                var end = window[^1].End;
                var paddedStart = start - pad < TimeSpan.Zero ? TimeSpan.Zero : start - pad;

                candidates.Add(new LyricMatch
                {
                    Start = paddedStart,
                    Duration = (end - start) + pad + pad,
                    RecognizedText = windowText,
                    Confidence = confidence
                });
            }
        }

        // Keep the highest-confidence, tightest match per overlapping time region. Without the
        // duration tiebreak, a multi-segment window that merely happens to contain an exact match
        // (plus a lot of unrelated surrounding text) would beat the precise single-segment match at
        // equal confidence, just for starting earlier - the opposite of what "find this phrase" means.
        var deduped = new List<LyricMatch>();
        foreach (var candidate in candidates.OrderByDescending(c => c.Confidence).ThenBy(c => c.Duration).ThenBy(c => c.Start))
        {
            var overlapsExisting = deduped.Any(existing =>
                candidate.Start < existing.End && candidate.End > existing.Start);
            if (!overlapsExisting)
            {
                deduped.Add(candidate);
            }
        }

        return deduped.OrderByDescending(m => m.Confidence).ThenBy(m => m.Start).Take(10).ToList();
    }

    public static string Normalize(string text)
    {
        var noPunctuation = PunctuationRegex.Replace(text.ToLowerInvariant(), " ");
        return WhitespaceRegex.Replace(noPunctuation, " ").Trim();
    }
}
