using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Pure logic, no process/model involved - fuzzy match + DP alignment against fixed inputs.</summary>
public class KnownSongLyricLocatorTests
{
    private static AiWorkerWord Word(string text, double start, double end, double confidence = 1.0) =>
        new() { Text = text, Start = TimeSpan.FromSeconds(start), End = TimeSpan.FromSeconds(end), Confidence = confidence };

    [Fact]
    public void Align_PerfectAsrMatch_AnchorsEveryWordWithFullConfidence()
    {
        var asr = new[] { Word("sve", 0.0, 0.3), Word("je", 0.3, 0.5), Word("bilo", 0.5, 0.9), Word("lepo", 0.9, 1.3) };

        var result = KnownSongLyricLocator.Align("sve je bilo lepo", asr);

        Assert.Equal(4, result.Count);
        Assert.All(result, w => Assert.True(w.IsAnchor));
        Assert.All(result, w => Assert.False(w.IsInterpolated));
        Assert.Equal(TimeSpan.FromSeconds(0.0), result[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(0.9), result[3].Start);
        Assert.Equal(TimeSpan.FromSeconds(1.3), result[3].End);
        Assert.Equal("sve", result[0].OriginalText);
        Assert.Equal("lepo", result[3].OriginalText);
    }

    [Fact]
    public void Align_AsrMishearsOneWordButCloseEnough_StillAnchorsWithLowerConfidence()
    {
        // "bilu" instead of "bilo" - close enough (1 letter off out of 4) to count as a fuzzy match.
        var asr = new[] { Word("sve", 0.0, 0.3), Word("je", 0.3, 0.5), Word("bilu", 0.5, 0.9), Word("lepo", 0.9, 1.3) };

        var result = KnownSongLyricLocator.Align("sve je bilo lepo", asr);

        Assert.True(result[2].IsAnchor);
        Assert.Equal(TimeSpan.FromSeconds(0.5), result[2].Start);
        Assert.True(result[2].Confidence < 1.0);
        Assert.Equal("bilo", result[2].OriginalText); // verified lyrics text is kept, never replaced by the ASR guess
    }

    [Fact]
    public void Align_AsrMissesAWordEntirely_InterpolatesTheShortInternalGap()
    {
        // ASR only heard "sve", "je", "lepo" - "bilo" was never recognized at all.
        var asr = new[] { Word("sve", 0.0, 0.3), Word("je", 0.3, 0.5), Word("lepo", 0.9, 1.3) };

        var result = KnownSongLyricLocator.Align("sve je bilo lepo", asr);

        Assert.True(result[0].IsAnchor);
        Assert.True(result[1].IsAnchor);
        Assert.True(result[3].IsAnchor);

        var interpolated = result[2];
        Assert.Equal("bilo", interpolated.OriginalText);
        Assert.False(interpolated.IsAnchor);
        Assert.True(interpolated.IsInterpolated);
        Assert.True(interpolated.Start >= result[1].End);
        Assert.True(interpolated.End <= result[3].Start);
    }

    [Fact]
    public void Align_NoAsrWords_LeavesEveryWordUnresolved()
    {
        var result = KnownSongLyricLocator.Align("sve je bilo lepo", Array.Empty<AiWorkerWord>());

        Assert.Equal(4, result.Count);
        Assert.All(result, w => Assert.False(w.IsAnchor));
        Assert.All(result, w => Assert.False(w.IsInterpolated));
        Assert.All(result, w => Assert.Equal(TimeSpan.Zero, w.Start));
    }

    [Fact]
    public void Align_EmptyLyrics_ReturnsEmptyResult()
    {
        var asr = new[] { Word("sve", 0.0, 0.3) };

        var result = KnownSongLyricLocator.Align("   ", asr);

        Assert.Empty(result);
    }

    [Fact]
    public void Align_CompletelyUnrelatedAsr_LeavesWordsUnresolvedRatherThanGuessing()
    {
        // Nothing here is remotely similar to the lyrics - the aligner must not force bad matches.
        var asr = new[] { Word("xyzqq", 5.0, 5.3), Word("wwwzz", 5.3, 5.6) };

        var result = KnownSongLyricLocator.Align("sve je bilo lepo", asr);

        Assert.All(result, w => Assert.False(w.IsAnchor));
    }
}
