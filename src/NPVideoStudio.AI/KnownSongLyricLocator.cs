using NPVideoStudio.Domain;

namespace NPVideoStudio.AI;

/// <summary>One verified-lyrics word after alignment against the clip's ASR output.</summary>
public readonly record struct AlignedLyricWord(
    string OriginalText,
    TimeSpan Start,
    TimeSpan End,
    double Confidence,
    bool IsAnchor,
    bool IsInterpolated);

/// <summary>
/// Known-song path (spec Phase 5): the song is already fingerprint-matched and its lyrics are already
/// verified in the library, so ASR is never trusted for the *words* - only for *timing*. This aligns
/// the verified lyrics text against words the AI worker actually heard in this clip (fuzzy match + DP,
/// same family of algorithm as classic sequence alignment/forced alignment), so the exported captions
/// carry the correct text at the time it's actually sung, without ever inventing text ASR didn't hear
/// and without ever discarding the verified lyrics in favor of an ASR guess.
///
/// Interpolation only fills short internal gaps between two confident anchors, per spec - a run of
/// lyrics words with no anchor on either side (e.g. ASR missed an entire verse) is left unresolved
/// (<see cref="AlignedLyricWord.IsAnchor"/> and <see cref="AlignedLyricWord.IsInterpolated"/> both
/// false) rather than guessing a timestamp for it.
/// </summary>
public static class KnownSongLyricLocator
{
    private const double GapPenalty = 0.4;
    private const double MatchThreshold = 0.6;
    private const int MaxUnanchoredRunToInterpolate = 12;

    public static IReadOnlyList<AlignedLyricWord> Align(string verifiedLyrics, IReadOnlyList<AiWorkerWord> asrWords)
    {
        var lyricTokens = Tokenize(verifiedLyrics);
        if (lyricTokens.Count == 0)
        {
            return Array.Empty<AlignedLyricWord>();
        }

        if (asrWords.Count == 0)
        {
            return lyricTokens.Select(t => new AlignedLyricWord(t, TimeSpan.Zero, TimeSpan.Zero, 0, false, false)).ToList();
        }

        var normalizedLyrics = lyricTokens.Select(LyricMatcher.Normalize).ToArray();
        var normalizedAsr = asrWords.Select(w => LyricMatcher.Normalize(w.Text)).ToArray();

        var anchors = AlignWithDp(normalizedLyrics, normalizedAsr, asrWords);
        return Interpolate(lyricTokens, anchors);
    }

    private static List<string> Tokenize(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>
    /// Needleman-Wunsch-style global alignment: dp[i][j] is the best score aligning the first i lyric
    /// tokens against the first j ASR words. Diagonal = align token i with word j (reward scaled by
    /// fuzzy similarity), up/left = skip one side at a fixed gap cost.
    /// </summary>
    private static (int LyricIndex, AiWorkerWord Word, double Confidence)?[] AlignWithDp(
        string[] normalizedLyrics, string[] normalizedAsr, IReadOnlyList<AiWorkerWord> asrWords)
    {
        var n = normalizedLyrics.Length;
        var m = normalizedAsr.Length;
        var dp = new double[n + 1, m + 1];
        var move = new byte[n + 1, m + 1]; // 0 = diagonal, 1 = up (skip lyric), 2 = left (skip asr)

        for (var i = 1; i <= n; i++)
        {
            dp[i, 0] = dp[i - 1, 0] - GapPenalty;
            move[i, 0] = 1;
        }
        for (var j = 1; j <= m; j++)
        {
            dp[0, j] = dp[0, j - 1] - GapPenalty;
            move[0, j] = 2;
        }

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var similarity = Similarity(normalizedLyrics[i - 1], normalizedAsr[j - 1]);
                var matchScore = (similarity - 0.5) * 2.0;

                var diagonal = dp[i - 1, j - 1] + matchScore;
                var up = dp[i - 1, j] - GapPenalty;
                var left = dp[i, j - 1] - GapPenalty;

                if (diagonal >= up && diagonal >= left)
                {
                    dp[i, j] = diagonal;
                    move[i, j] = 0;
                }
                else if (up >= left)
                {
                    dp[i, j] = up;
                    move[i, j] = 1;
                }
                else
                {
                    dp[i, j] = left;
                    move[i, j] = 2;
                }
            }
        }

        var anchors = new (int LyricIndex, AiWorkerWord Word, double Confidence)?[n];
        var row = n;
        var col = m;
        while (row > 0 || col > 0)
        {
            if (row > 0 && col > 0 && move[row, col] == 0)
            {
                var similarity = Similarity(normalizedLyrics[row - 1], normalizedAsr[col - 1]);
                if (similarity >= MatchThreshold)
                {
                    anchors[row - 1] = (row - 1, asrWords[col - 1], similarity);
                }
                row--;
                col--;
            }
            else if (row > 0 && (col == 0 || move[row, col] == 1))
            {
                row--;
            }
            else
            {
                col--;
            }
        }

        return anchors;
    }

    private static List<AlignedLyricWord> Interpolate(
        List<string> lyricTokens, (int LyricIndex, AiWorkerWord Word, double Confidence)?[] anchors)
    {
        var result = new AlignedLyricWord[lyricTokens.Count];
        var anchorIndices = new List<int>();
        for (var i = 0; i < anchors.Length; i++)
        {
            if (anchors[i] is { } anchor)
            {
                result[i] = new AlignedLyricWord(lyricTokens[i], anchor.Word.Start, anchor.Word.End, anchor.Confidence, true, false);
                anchorIndices.Add(i);
            }
        }

        if (anchorIndices.Count == 0)
        {
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = new AlignedLyricWord(lyricTokens[i], TimeSpan.Zero, TimeSpan.Zero, 0, false, false);
            }
            return result.ToList();
        }

        // Internal gaps: interpolate linearly by token position between the two bounding anchors.
        for (var a = 0; a < anchorIndices.Count - 1; a++)
        {
            var left = anchorIndices[a];
            var right = anchorIndices[a + 1];
            var gapLength = right - left - 1;
            if (gapLength <= 0 || gapLength > MaxUnanchoredRunToInterpolate)
            {
                continue;
            }

            var leftAnchor = result[left];
            var rightAnchor = result[right];
            var totalSpan = rightAnchor.Start - leftAnchor.End;
            if (totalSpan < TimeSpan.Zero)
            {
                continue;
            }

            for (var k = 1; k <= gapLength; k++)
            {
                var fractionStart = totalSpan * ((double)(k - 1) / (gapLength + 1));
                var fractionEnd = totalSpan * ((double)k / (gapLength + 1));
                var start = leftAnchor.End + fractionStart;
                var end = leftAnchor.End + fractionEnd;
                result[left + k] = new AlignedLyricWord(lyricTokens[left + k], start, end, 0, false, true);
            }
        }

        // Leading/trailing runs (before the first or after the last anchor) are left unresolved -
        // extrapolating from a single anchor with no second data point to confirm tempo is a guess,
        // and the spec says never invent timing for a whole unanchored stretch.
        for (var i = 0; i < result.Length; i++)
        {
            if (result[i].OriginalText is null)
            {
                result[i] = new AlignedLyricWord(lyricTokens[i], TimeSpan.Zero, TimeSpan.Zero, 0, false, false);
            }
        }

        return result.ToList();
    }

    private static double Similarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0)
        {
            return 1.0;
        }
        if (a.Length == 0 || b.Length == 0)
        {
            return 0.0;
        }

        var distance = LevenshteinDistance(a, b);
        var maxLength = Math.Max(a.Length, b.Length);
        return 1.0 - (double)distance / maxLength;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
