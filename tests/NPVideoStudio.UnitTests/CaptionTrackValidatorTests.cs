using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Covers the caption-track validation/repair ported from the user's other project
/// (PROGRAM-ZA-TEKST-U-VIDEO, text-video-tools.js).</summary>
public class CaptionTrackValidatorTests
{
    private static CaptionWord Caption(string text, double startSeconds, double endSeconds) => new()
    {
        OriginalText = text,
        Start = TimeSpan.FromSeconds(startSeconds),
        End = TimeSpan.FromSeconds(endSeconds)
    };

    [Fact]
    public void Validate_CleanTrack_IsValidWithNoProblems()
    {
        var report = CaptionTrackValidator.Validate(new[]
        {
            Caption("Prva linija", 0, 2),
            Caption("Druga linija", 2, 4)
        });

        Assert.True(report.IsValid);
        Assert.Empty(report.Problems);
        Assert.Equal(2, report.CaptionCount);
    }

    [Fact]
    public void Validate_OverlappingCaptions_ReportedAsAnError()
    {
        var report = CaptionTrackValidator.Validate(new[]
        {
            Caption("Prva", 0, 3),
            Caption("Druga", 2, 5)
        });

        Assert.False(report.IsValid);
        Assert.Contains(report.Problems, p => p.Kind == CaptionProblemKind.Overlap);
    }

    [Fact]
    public void Validate_CaptionPastEndOfVideo_ReportedAsOutOfBounds()
    {
        var report = CaptionTrackValidator.Validate(
            new[] { Caption("Predugacko", 0, 30) },
            totalDuration: TimeSpan.FromSeconds(10));

        Assert.Contains(report.Problems, p => p.Kind == CaptionProblemKind.OutOfBounds);
    }

    [Fact]
    public void Validate_EndBeforeStart_ReportedAsInvalidRange()
    {
        var report = CaptionTrackValidator.Validate(new[] { Caption("Naopako", 5, 2) });

        Assert.Contains(report.Problems, p => p.Kind == CaptionProblemKind.InvalidRange);
    }

    [Fact]
    public void Validate_ShortGap_IsOnlyAWarningNotAnError()
    {
        var report = CaptionTrackValidator.Validate(
            new[] { Caption("Prva", 0, 2), Caption("Druga", 2.05, 4) },
            minimumGap: TimeSpan.FromMilliseconds(200));

        Assert.True(report.IsValid);
        Assert.Contains(report.Warnings, p => p.Kind == CaptionProblemKind.ShortGap);
    }

    [Fact]
    public void Normalize_OverlappingCaptions_TrimsTheEarlierOneAndNeverMovesTheLaterOnesStart()
    {
        var original = new[] { Caption("Prva", 0, 3), Caption("Druga", 2, 5) };

        var fixedUp = CaptionTrackValidator.Normalize(original);

        Assert.Equal(TimeSpan.FromSeconds(2), fixedUp[0].End);
        // The later caption must stay exactly where it was - it is synced to the audio at that moment.
        Assert.Equal(TimeSpan.FromSeconds(2), fixedUp[1].Start);
        Assert.True(CaptionTrackValidator.Validate(fixedUp).IsValid);
    }

    [Fact]
    public void Normalize_DoesNotModifyTheOriginalList()
    {
        var original = new[] { Caption("Prva", 0, 3), Caption("Druga", 2, 5) };

        CaptionTrackValidator.Normalize(original);

        Assert.Equal(TimeSpan.FromSeconds(3), original[0].End);
    }

    [Fact]
    public void Normalize_BlankCaptions_AreDropped()
    {
        var fixedUp = CaptionTrackValidator.Normalize(new[]
        {
            Caption("Prava linija", 0, 2),
            Caption("   ", 3, 4)
        });

        Assert.Single(fixedUp);
        Assert.Equal("Prava linija", fixedUp[0].OriginalText);
    }

    [Fact]
    public void Normalize_CaptionPastEnd_IsClampedToTheMediaLength()
    {
        var fixedUp = CaptionTrackValidator.Normalize(
            new[] { Caption("Predugacko", 0, 30) },
            totalDuration: TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(10), fixedUp[0].End);
    }

    [Fact]
    public void SplitLongCaption_LongLine_SplitsAndDividesTimeProportionallyToWordCount()
    {
        var caption = Caption("jedan dva tri cetiri pet sest sedam osam devet deset", 0, 10);

        var pieces = CaptionTrackValidator.SplitLongCaption(caption, maxCharactersPerLine: 20);

        Assert.True(pieces.Count > 1);
        // Timing must stay contiguous and cover exactly the original span - no gaps, no overrun.
        Assert.Equal(caption.Start, pieces[0].Start);
        Assert.Equal(caption.End, pieces[^1].End);
        for (var i = 0; i < pieces.Count - 1; i++)
        {
            Assert.Equal(pieces[i].End, pieces[i + 1].Start);
        }
        // And no word may be lost in the split.
        Assert.Equal(
            caption.OriginalText.Split(' '),
            pieces.SelectMany(p => p.OriginalText.Split(' ')).ToArray());
    }

    [Fact]
    public void SplitLongCaption_AlreadyShortEnough_IsReturnedUnchanged()
    {
        var caption = Caption("Kratko", 1, 3);

        var pieces = CaptionTrackValidator.SplitLongCaption(caption, maxCharactersPerLine: 40);

        Assert.Single(pieces);
        Assert.Equal("Kratko", pieces[0].OriginalText);
        Assert.Equal(caption.Start, pieces[0].Start);
        Assert.Equal(caption.End, pieces[0].End);
    }

    [Fact]
    public void SplitLongCaption_PreservesConfidenceAndSourceOnEveryPiece()
    {
        var caption = Caption("jedan dva tri cetiri pet sest sedam osam", 0, 8);
        caption.Confidence = 0.42;
        caption.Source = CaptionWordSource.Whisper;
        caption.VerificationStatus = CaptionVerificationStatus.NeedsReview;

        var pieces = CaptionTrackValidator.SplitLongCaption(caption, maxCharactersPerLine: 15);

        Assert.All(pieces, p =>
        {
            Assert.Equal(0.42, p.Confidence);
            Assert.Equal(CaptionWordSource.Whisper, p.Source);
            Assert.Equal(CaptionVerificationStatus.NeedsReview, p.VerificationStatus);
        });
    }
}
