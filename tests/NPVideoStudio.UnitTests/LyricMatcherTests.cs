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

        var match = Assert.Single(matches);
        Assert.Equal(TimeSpan.Zero, match.Start);
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
