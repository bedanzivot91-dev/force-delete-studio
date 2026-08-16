using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Pure logic, no process/model involved.</summary>
public class CaptionEditSessionTests
{
    private static CaptionWord Word(string text, double start, double end, bool lineBreak = false) => new()
    {
        OriginalText = text,
        Start = TimeSpan.FromSeconds(start),
        End = TimeSpan.FromSeconds(end),
        LineBreakAfter = lineBreak
    };

    [Fact]
    public void DeleteWords_RemovesMatchingWordsAndSupportsUndo()
    {
        var w1 = Word("sve", 0, 1);
        var w2 = Word("je", 1, 2);
        var session = new CaptionEditSession(new[] { w1, w2 });

        session.DeleteWords(new[] { w1.Id });

        Assert.Single(session.Words);
        Assert.Equal("je", session.Words[0].OriginalText);
        Assert.True(session.CanUndo);

        session.Undo();

        Assert.Equal(2, session.Words.Count);
        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);

        session.Redo();

        Assert.Single(session.Words);
    }

    [Fact]
    public void SplitWord_DividesTextAndTimeProportionally()
    {
        var word = Word("slovo", 0, 10);
        var session = new CaptionEditSession(new[] { word });

        session.SplitWord(word.Id, 0.4);

        Assert.Equal(2, session.Words.Count);
        var first = session.Words[0];
        var second = session.Words[1];
        Assert.Equal("sl", first.OriginalText.Trim());
        Assert.Equal("ovo", second.OriginalText.Trim());
        Assert.Equal(TimeSpan.Zero, first.Start);
        Assert.Equal(TimeSpan.FromSeconds(4), first.End);
        Assert.Equal(first.End, second.Start);
        Assert.Equal(TimeSpan.FromSeconds(10), second.End);
    }

    [Fact]
    public void SplitWord_TooShortText_DoesNothing()
    {
        var word = Word("a", 0, 1);
        var session = new CaptionEditSession(new[] { word });

        session.SplitWord(word.Id, 0.5);

        Assert.Single(session.Words);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void MergeWithNext_CombinesTextAndSpansFullDuration()
    {
        var w1 = Word("sve", 0, 1);
        var w2 = Word("je", 1, 2.5);
        var session = new CaptionEditSession(new[] { w1, w2 });

        session.MergeWithNext(w1.Id);

        Assert.Single(session.Words);
        var merged = session.Words[0];
        Assert.Equal("sve je", merged.OriginalText);
        Assert.Equal(TimeSpan.Zero, merged.Start);
        Assert.Equal(TimeSpan.FromSeconds(2.5), merged.End);
    }

    [Fact]
    public void NudgeTiming_WithoutRipple_OnlyMovesTargetedWord()
    {
        var w1 = Word("sve", 0, 1);
        var w2 = Word("je", 1, 2);
        var session = new CaptionEditSession(new[] { w1, w2 });

        session.NudgeTiming(w1.Id, TimeSpan.FromSeconds(0.5), ripple: false);

        Assert.Equal(TimeSpan.FromSeconds(0.5), session.Words[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(1.5), session.Words[0].End);
        Assert.Equal(TimeSpan.FromSeconds(1), session.Words[1].Start); // unchanged
    }

    [Fact]
    public void NudgeTiming_WithRipple_ShiftsAllFollowingWords()
    {
        var w1 = Word("sve", 0, 1);
        var w2 = Word("je", 1, 2);
        var w3 = Word("bilo", 2, 3);
        var session = new CaptionEditSession(new[] { w1, w2, w3 });

        session.NudgeTiming(w1.Id, TimeSpan.FromSeconds(0.5), ripple: true);

        Assert.Equal(TimeSpan.FromSeconds(0.5), session.Words[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(1.5), session.Words[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(2.5), session.Words[2].Start);
    }

    [Fact]
    public void NudgeTiming_NegativeDeltaPastZero_ClampsStartToZeroAndPreservesDuration()
    {
        var word = Word("sve", 0.2, 1.2);
        var session = new CaptionEditSession(new[] { word });

        session.NudgeTiming(word.Id, TimeSpan.FromSeconds(-1), ripple: false);

        Assert.Equal(TimeSpan.Zero, session.Words[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(1), session.Words[0].End); // duration (1s) preserved
    }

    [Fact]
    public void FindAndReplace_ReplacesAllMatchesCaseInsensitiveByDefault()
    {
        var w1 = Word("Sve", 0, 1);
        var w2 = Word("je", 1, 2);
        var session = new CaptionEditSession(new[] { w1, w2 });

        var count = session.FindAndReplace("sve", "Ništa");

        Assert.Equal(1, count);
        Assert.Equal("Ništa", session.Words[0].OriginalText);
    }

    [Fact]
    public void FindAndReplace_NoMatches_ReturnsZeroAndDoesNotPushUndo()
    {
        var word = Word("sve", 0, 1);
        var session = new CaptionEditSession(new[] { word });

        var count = session.FindAndReplace("nepostojece", "x");

        Assert.Equal(0, count);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void ConvertScript_TransformsEveryWordAndSupportsUndo()
    {
        var w1 = Word("Ljubav", 0, 1);
        var w2 = Word("je", 1, 2);
        var session = new CaptionEditSession(new[] { w1, w2 });

        session.ConvertScript(SerbianScriptConverter.ToCyrillic);

        Assert.Equal("Љубав", session.Words[0].OriginalText);
        Assert.Equal("је", session.Words[1].OriginalText);
        Assert.True(session.CanUndo);

        session.Undo();

        Assert.Equal("Ljubav", session.Words[0].OriginalText);
    }

    [Fact]
    public void Redo_ClearedAfterNewEdit()
    {
        var w1 = Word("sve", 0, 1);
        var w2 = Word("je", 1, 2);
        var session = new CaptionEditSession(new[] { w1, w2 });

        session.DeleteWords(new[] { w1.Id });
        session.Undo();
        Assert.True(session.CanRedo);

        session.DeleteWords(new[] { w2.Id }); // a fresh edit should clear the redo stack

        Assert.False(session.CanRedo);
    }

    [Fact]
    public void ReplaceAll_SwapsTheWholeDocumentInOneUndoableStep()
    {
        var original = new[] { Word("prva", 0, 3), Word("druga", 2, 5) };
        var session = new CaptionEditSession(original);

        // Exactly what the bulk caption-quality repair does: recompute many captions' timing at once.
        var repaired = NPVideoStudio.AI.CaptionTrackValidator.Normalize(session.Words.ToList());
        session.ReplaceAll(repaired);

        Assert.Equal(TimeSpan.FromSeconds(2), session.Words[0].End);

        // One Undo must take the whole bulk fix back, not just the last caption of it.
        session.Undo();
        Assert.Equal(TimeSpan.FromSeconds(3), session.Words[0].End);
    }

    [Fact]
    public void ReplaceAll_KeepsWordsOrderedByStartTime()
    {
        var session = new CaptionEditSession(new[] { Word("prva", 0, 1) });

        session.ReplaceAll(new[] { Word("kasnija", 5, 6), Word("ranija", 1, 2) });

        Assert.Equal("ranija", session.Words[0].OriginalText);
        Assert.Equal("kasnija", session.Words[1].OriginalText);
    }
}
