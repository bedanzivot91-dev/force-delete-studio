using NPVideoStudio.AI;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Tests the pure phrase-matching logic against a hand-built fake transcript - no Whisper model, no
/// network, no ffmpeg. The real end-to-end recognition path (audio -> Whisper -> this matcher) is
/// covered separately in LyricSearchServiceIntegrationTests, which needs a downloaded model.
/// </summary>
public class LyricMatcherTests
{
    private static readonly TranscribedSegment[] FakeTranscript =
    {
        new(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(4), "This is a test song"),
        new(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), "I love you my darling"),
        new(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(12), "This is the chorus part"),
        new(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(16), "I love you my darling"),
        new(TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(20), "This is the end of the song"),
    };

    [Fact]
    public void Search_ExactPhrase_FindsBothOccurrencesWithFullConfidence()
    {
        var matches = LyricMatcher.Search(FakeTranscript, "I love you my darling");

        Assert.Equal(2, matches.Count);
        Assert.All(matches, m => Assert.Equal(1.0, m.Confidence));
    }

    [Fact]
    public void Search_ExactPhrase_MatchWindowCoversTheSourceSegment()
    {
        var matches = LyricMatcher.Search(FakeTranscript, "I love you my darling", padding: TimeSpan.Zero);

        var first = matches.OrderBy(m => m.Start).First();
        Assert.Equal(TimeSpan.FromSeconds(4), first.Start);
        Assert.Equal(TimeSpan.FromSeconds(4), first.Duration);
    }

    [Fact]
    public void Search_PaddingNeverGoesBelowZero_ForAMatchAtTheStartOfTheTrack()
    {
        var matches = LyricMatcher.Search(FakeTranscript, "This is a test song", padding: TimeSpan.FromSeconds(5));

        // A generous 5s padding also drags in a legitimate, lower-confidence fuzzy match near the end
        // of the transcript ("This is the end of the song" shares "this/is/song") - pick the exact
        // (confidence 1.0) match rather than assuming it's the only candidate.
        var match = Assert.Single(matches, m => m.Confidence == 1.0);
        Assert.Equal(TimeSpan.Zero, match.Start);
    }

    /// <summary>Real bug found and fixed: Duration used to always add "pad + pad" regardless of whether
    /// Start actually got clamped to zero above - for a match this close to the start of the track (real
    /// segment 0..4s, 5s of padding requested), the old code produced Start=0, Duration=4+5+5=14s (running
    /// 5s past the segment's real end at 4+5=9s). The clip must run from the real (clamped) Start to
    /// (segment end + pad), never further. This corrected (shorter, accurate) window is also what makes
    /// the search below correctly surface the separate, legitimate fuzzy match near the end of the
    /// transcript instead of it being silently swallowed by an artificially-oversized first window.</summary>
    [Fact]
    public void Search_PaddingClampedAtStart_DurationStillEndsExactlyAtSegmentEndPlusPadding()
    {
        var matches = LyricMatcher.Search(FakeTranscript, "This is a test song", padding: TimeSpan.FromSeconds(5));

        var match = Assert.Single(matches, m => m.Confidence == 1.0);
        Assert.Equal(TimeSpan.Zero, match.Start);
        Assert.Equal(TimeSpan.FromSeconds(9), match.Duration); // segment ends at 4s + 5s padding = 9s from zero
    }

    /// <summary>Real bug found and fixed: LyricMatcher.Normalize used to only lowercase + strip
    /// punctuation - it never transliterated Cyrillic to Latin. Whisper transcribes Serbian
    /// speech/singing in Cyrillic, but the search UI's own watermark invites Latin input
    /// ("npr. volim te draga moja") - so a Latin-typed phrase shared zero tokens with a Cyrillic
    /// transcript and every single search silently returned "not found," which is exactly the reported
    /// "program doesn't recognize the song's lyrics at all" symptom.</summary>
    [Fact]
    public void Search_LatinPhraseAgainstCyrillicTranscript_StillMatches()
    {
        var cyrillicTranscript = new[]
        {
            new TranscribedSegment(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(4), "ово је пробна песма"),
            new TranscribedSegment(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), "волим те драга моја"),
            new TranscribedSegment(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(12), "ово је рефрен песме"),
        };

        var matches = LyricMatcher.Search(cyrillicTranscript, "volim te draga moja");

        var match = Assert.Single(matches);
        Assert.Equal(1.0, match.Confidence);
        Assert.Equal(TimeSpan.FromSeconds(2.5), match.Start); // segment starts at 4s, minus the default 1.5s padding
    }

    /// <summary>Same real bug as above, opposite direction - a Cyrillic-typed search phrase must also
    /// match a Latin transcript (e.g. Whisper occasionally outputs Latin depending on context/model).</summary>
    [Fact]
    public void Search_CyrillicPhraseAgainstLatinTranscript_StillMatches()
    {
        var latinTranscript = new[]
        {
            new TranscribedSegment(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(4), "volim te draga moja"),
        };

        var matches = LyricMatcher.Search(latinTranscript, "волим те драга моја");

        var match = Assert.Single(matches);
        Assert.Equal(1.0, match.Confidence);
    }

    /// <summary>Real bug found and fixed: Serbian Latin diacritics (š/đ/č/ć/ž) were never folded before
    /// matching, so a phrase typed without diacritics (very common - many keyboards don't have them)
    /// never matched a transcript that has them, and vice versa.</summary>
    [Fact]
    public void Search_PhraseWithoutDiacritics_MatchesTranscriptWithDiacritics()
    {
        var transcript = new[]
        {
            new TranscribedSegment(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(4), "šta ćeš draga žao mi je"),
        };

        var matches = LyricMatcher.Search(transcript, "sta ces draga zao mi je");

        var match = Assert.Single(matches);
        Assert.Equal(1.0, match.Confidence);
    }

    [Fact]
    public void Search_PartialWordOverlap_StillMatchesWithLowerConfidence()
    {
        // Missing "my" and swapped word order relative to the transcript - not an exact substring,
        // but shares enough tokens that it should surface as a fuzzy match.
        var matches = LyricMatcher.Search(FakeTranscript, "darling I love you");

        Assert.NotEmpty(matches);
        Assert.All(matches, m => Assert.True(m.Confidence < 1.0));
    }

    [Fact]
    public void Search_PhraseNotInSong_ReturnsNoMatches()
    {
        var matches = LyricMatcher.Search(FakeTranscript, "completely unrelated words nowhere here");

        Assert.Empty(matches);
    }

    [Fact]
    public void Search_EmptyPhrase_ReturnsNoMatches()
    {
        var matches = LyricMatcher.Search(FakeTranscript, "   ");

        Assert.Empty(matches);
    }

    [Fact]
    public void Search_IsCaseAndPunctuationInsensitive()
    {
        var matches = LyricMatcher.Search(FakeTranscript, "I LOVE YOU, MY DARLING!!!");

        Assert.NotEmpty(matches);
        Assert.Equal(1.0, matches[0].Confidence);
    }

    [Fact]
    public void Search_DoesNotReturnOverlappingDuplicateMatchesForTheSameMoment()
    {
        var matches = LyricMatcher.Search(FakeTranscript, "I love you my darling");

        var ordered = matches.OrderBy(m => m.Start).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            Assert.True(ordered[i].Start >= ordered[i - 1].End,
                $"Matches at {ordered[i - 1].Start} and {ordered[i].Start} overlap.");
        }
    }
}
