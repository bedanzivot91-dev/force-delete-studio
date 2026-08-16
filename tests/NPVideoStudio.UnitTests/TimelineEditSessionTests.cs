using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Pure logic, no process/model involved.</summary>
public class TimelineEditSessionTests
{
    private static TimelineClip Clip(double timelineStart, double trimIn, double trimOut) => new()
    {
        TimelineStartSeconds = timelineStart,
        SourceTrimInSeconds = trimIn,
        SourceTrimOutSeconds = trimOut
    };

    private static TimelineTrack Track(TimelineTrackKind kind, params TimelineClip[] clips) => new()
    {
        Kind = kind,
        Clips = clips.ToList()
    };

    [Fact]
    public void AddTrack_AppendsNewTrackAndSupportsUndo()
    {
        var session = new TimelineEditSession(Array.Empty<TimelineTrack>());
        var newTrack = Track(TimelineTrackKind.Caption);

        session.AddTrack(newTrack);

        Assert.Single(session.Tracks);
        Assert.Equal(TimelineTrackKind.Caption, session.Tracks[0].Kind);

        session.Undo();

        Assert.Empty(session.Tracks);
    }

    [Fact]
    public void RemoveTrack_RemovesMatchingTrackAndSupportsUndo()
    {
        var track = Track(TimelineTrackKind.Video);
        var session = new TimelineEditSession(new[] { track });

        session.RemoveTrack(track.Id);

        Assert.Empty(session.Tracks);

        session.Undo();

        Assert.Single(session.Tracks);
    }

    [Fact]
    public void DeleteClips_RemovesMatchingClipAndSupportsUndo()
    {
        var clip1 = Clip(0, 0, 5);
        var clip2 = Clip(5, 0, 3);
        var track = Track(TimelineTrackKind.Video, clip1, clip2);
        var session = new TimelineEditSession(new[] { track });

        session.DeleteClips(new[] { clip1.Id });

        Assert.Single(session.Tracks[0].Clips);
        Assert.True(session.CanUndo);

        session.Undo();

        Assert.Equal(2, session.Tracks[0].Clips.Count);
    }

    [Fact]
    public void DuplicateClip_PlacesCopyImmediatelyAfterOriginal()
    {
        var clip = Clip(2, 1, 4); // duration 3, timeline 2..5
        var track = Track(TimelineTrackKind.Video, clip);
        var session = new TimelineEditSession(new[] { track });

        session.DuplicateClip(clip.Id);

        Assert.Equal(2, session.Tracks[0].Clips.Count);
        var duplicate = session.Tracks[0].Clips[1];
        Assert.Equal(5, duplicate.TimelineStartSeconds);
        Assert.Equal(1, duplicate.SourceTrimInSeconds);
        Assert.Equal(4, duplicate.SourceTrimOutSeconds);
        Assert.NotEqual(clip.Id, duplicate.Id);
    }

    [Fact]
    public void MoveClip_ToDifferentTrack_RelocatesClip()
    {
        var clip = Clip(0, 0, 5);
        var videoTrack = Track(TimelineTrackKind.Video, clip);
        var captionTrack = Track(TimelineTrackKind.Caption);
        var session = new TimelineEditSession(new[] { videoTrack, captionTrack });

        session.MoveClip(clip.Id, newTimelineStartSeconds: 10, newTrackId: captionTrack.Id);

        Assert.Empty(session.Tracks[0].Clips);
        Assert.Single(session.Tracks[1].Clips);
        Assert.Equal(10, session.Tracks[1].Clips[0].TimelineStartSeconds);
    }

    [Fact]
    public void SplitClip_AtMidpoint_ProducesTwoClipsSpanningOriginalRange()
    {
        var clip = Clip(timelineStart: 0, trimIn: 0, trimOut: 10); // timeline 0..10
        var track = Track(TimelineTrackKind.Video, clip);
        var session = new TimelineEditSession(new[] { track });

        session.SplitClip(clip.Id, atTimelineSeconds: 4);

        Assert.Equal(2, session.Tracks[0].Clips.Count);
        var first = session.Tracks[0].Clips[0];
        var second = session.Tracks[0].Clips[1];

        Assert.Equal(0, first.TimelineStartSeconds);
        Assert.Equal(4, first.TimelineEndSeconds);
        Assert.Equal(4, second.TimelineStartSeconds);
        Assert.Equal(10, second.TimelineEndSeconds);
        Assert.Equal(first.SourceTrimOutSeconds, second.SourceTrimInSeconds);
    }

    [Fact]
    public void SplitClip_TooCloseToEdge_DoesNothing()
    {
        var clip = Clip(0, 0, 1); // very short clip
        var track = Track(TimelineTrackKind.Video, clip);
        var session = new TimelineEditSession(new[] { track });

        session.SplitClip(clip.Id, atTimelineSeconds: 0.02);

        Assert.Single(session.Tracks[0].Clips);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void TrimIn_IncreasesSourceTrimInAndShiftsTimelineStartBySameDelta()
    {
        var clip = Clip(timelineStart: 5, trimIn: 2, trimOut: 10);
        var track = Track(TimelineTrackKind.Video, clip);
        var session = new TimelineEditSession(new[] { track });

        session.TrimIn(clip.Id, newSourceTrimInSeconds: 4); // trimming 2 more seconds off the start

        var trimmed = session.Tracks[0].Clips[0];
        Assert.Equal(4, trimmed.SourceTrimInSeconds);
        Assert.Equal(7, trimmed.TimelineStartSeconds); // 5 + (4-2)
    }

    [Fact]
    public void TrimOut_CannotShrinkBelowMinimumDuration()
    {
        var clip = Clip(0, 2, 10);
        var track = Track(TimelineTrackKind.Video, clip);
        var session = new TimelineEditSession(new[] { track });

        session.TrimOut(clip.Id, newSourceTrimOutSeconds: 2.01); // would leave ~0.01s, below the floor

        var trimmed = session.Tracks[0].Clips[0];
        Assert.True(trimmed.SourceTrimOutSeconds - trimmed.SourceTrimInSeconds >= 0.05 - 1e-9);
    }

    [Fact]
    public void SetFade_SetsBothInAndOutValues()
    {
        var clip = Clip(0, 0, 10);
        var track = Track(TimelineTrackKind.Video, clip);
        var session = new TimelineEditSession(new[] { track });

        session.SetFade(clip.Id, fadeInSeconds: 0.5, fadeOutSeconds: 1.0);

        var updated = session.Tracks[0].Clips[0];
        Assert.Equal(0.5, updated.FadeInSeconds);
        Assert.Equal(1.0, updated.FadeOutSeconds);
    }

    [Fact]
    public void SetTextStyle_UpdatesAllFourFieldsAndSupportsUndo()
    {
        var clip = new TimelineClip { TextContent = "Zdravo", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2 };
        var track = Track(TimelineTrackKind.Caption, clip);
        var session = new TimelineEditSession(new[] { track });

        session.SetTextStyle(clip.Id, CaptionFontChoice.Impact, 48, "#FF0000", CaptionTextPosition.Top);

        var updated = session.Tracks[0].Clips[0];
        Assert.Equal(CaptionFontChoice.Impact, updated.FontChoice);
        Assert.Equal(48, updated.FontSizePx);
        Assert.Equal("#FF0000", updated.TextColor);
        Assert.Equal(CaptionTextPosition.Top, updated.TextPosition);

        session.Undo();

        var reverted = session.Tracks[0].Clips[0];
        Assert.Equal(CaptionFontChoice.Default, reverted.FontChoice);
        Assert.Equal(36, reverted.FontSizePx);
        Assert.Equal("#FFFFFF", reverted.TextColor);
        Assert.Equal(CaptionTextPosition.Bottom, reverted.TextPosition);
    }

    [Fact]
    public void SetTransition_UpdatesTypeAndDurationAndSupportsUndo()
    {
        var clip = Clip(3, 0, 3);
        var track = Track(TimelineTrackKind.Video, Clip(0, 0, 3), clip);
        var session = new TimelineEditSession(new[] { track });

        session.SetTransition(clip.Id, ClipTransitionType.WipeLeft, 0.75);

        var updated = session.Tracks[0].Clips[1];
        Assert.Equal(ClipTransitionType.WipeLeft, updated.TransitionInType);
        Assert.Equal(0.75, updated.TransitionInDurationSeconds);

        session.Undo();

        var reverted = session.Tracks[0].Clips[1];
        Assert.Equal(ClipTransitionType.None, reverted.TransitionInType);
        Assert.Equal(0.5, reverted.TransitionInDurationSeconds);
    }

    [Fact]
    public void SetTextContent_UpdatesTextAndSupportsUndo()
    {
        var clip = new TimelineClip { TextContent = "Pogresno prepoznato", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2 };
        var track = Track(TimelineTrackKind.Caption, clip);
        var session = new TimelineEditSession(new[] { track });

        session.SetTextContent(clip.Id, "Ispravljen tekst");

        Assert.Equal("Ispravljen tekst", session.Tracks[0].Clips[0].TextContent);

        session.Undo();

        Assert.Equal("Pogresno prepoznato", session.Tracks[0].Clips[0].TextContent);
    }

    [Fact]
    public void SetTextContent_OnNonTextClip_DoesNothing()
    {
        var clip = Clip(0, 0, 3); // a plain video clip - TextContent is null
        var track = Track(TimelineTrackKind.Video, clip);
        var session = new TimelineEditSession(new[] { track });

        session.SetTextContent(clip.Id, "Ovo ne bi trebalo da se desi");

        Assert.Null(session.Tracks[0].Clips[0].TextContent);
    }

    [Fact]
    public void SetTextAdvancedStyle_UpdatesAllFieldsAndSupportsUndo()
    {
        var clip = new TimelineClip { TextContent = "Zdravo", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2 };
        var track = Track(TimelineTrackKind.Caption, clip);
        var session = new TimelineEditSession(new[] { track });

        var style = new TextAdvancedStyle(
            OutlineColor: "#FF0000", OutlineWidthPx: 4,
            ShadowColor: "#111111", ShadowOffsetPx: 3,
            HasBackground: false, BackgroundColor: "#00FF00", BackgroundOpacity: 0.25,
            HorizontalAlign: TextHorizontalAlign.Left,
            IsBold: true, IsItalic: true,
            TextCase: TextCaseTransform.UpperCase,
            LineSpacingPx: 8);
        session.SetTextAdvancedStyle(clip.Id, style);

        var updated = session.Tracks[0].Clips[0];
        Assert.Equal("#FF0000", updated.TextOutlineColor);
        Assert.Equal(4, updated.TextOutlineWidthPx);
        Assert.Equal("#111111", updated.TextShadowColor);
        Assert.Equal(3, updated.TextShadowOffsetPx);
        Assert.False(updated.HasTextBackground);
        Assert.Equal("#00FF00", updated.TextBackgroundColor);
        Assert.Equal(0.25, updated.TextBackgroundOpacity);
        Assert.Equal(TextHorizontalAlign.Left, updated.TextHorizontalAlign);
        Assert.True(updated.IsTextBold);
        Assert.True(updated.IsTextItalic);
        Assert.Equal(TextCaseTransform.UpperCase, updated.TextCase);
        Assert.Equal(8, updated.LineSpacingPx);

        session.Undo();

        var reverted = session.Tracks[0].Clips[0];
        Assert.Null(reverted.TextOutlineColor);
        Assert.True(reverted.HasTextBackground);
        Assert.Equal(TextCaseTransform.Normal, reverted.TextCase);
    }

    /// <summary>
    /// Real bug found while writing the test above: <c>TimelineEditSession</c>'s internal clip-cloning
    /// (used by every undo snapshot) had an explicit field list that silently fell behind
    /// <see cref="TimelineClip"/> over past sessions - style A -> style B -> Undo landed on hardcoded
    /// defaults instead of style A, because the snapshot taken before applying style B never actually
    /// captured style A's FontChoice/FontSizePx/etc. in the first place. Existing single-edit undo tests
    /// never caught this because they all started from the already-default state, where the bug produces
    /// the same (correct-looking) result by coincidence.
    /// </summary>
    [Fact]
    public void SetTextStyle_TwoSuccessiveEdits_UndoRevertsToFirstEditNotHardcodedDefaults()
    {
        var clip = new TimelineClip { TextContent = "Zdravo", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2 };
        var track = Track(TimelineTrackKind.Caption, clip);
        var session = new TimelineEditSession(new[] { track });

        session.SetTextStyle(clip.Id, CaptionFontChoice.Georgia, 60, "#00FF00", CaptionTextPosition.Top);
        session.SetTextStyle(clip.Id, CaptionFontChoice.Impact, 24, "#FF0000", CaptionTextPosition.Bottom);

        session.Undo();

        var afterUndo = session.Tracks[0].Clips[0];
        Assert.Equal(CaptionFontChoice.Georgia, afterUndo.FontChoice);
        Assert.Equal(60, afterUndo.FontSizePx);
        Assert.Equal("#00FF00", afterUndo.TextColor);
        Assert.Equal(CaptionTextPosition.Top, afterUndo.TextPosition);
    }

    [Fact]
    public void ApplyTextStyleToAllClipsOnTrack_CopiesStyleToOtherTextClipsOnSameTrackOnly()
    {
        var source = new TimelineClip
        {
            TextContent = "Prvi", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2,
            FontChoice = CaptionFontChoice.Impact, FontSizePx = 60, TextColor = "#00FF00",
            TextOutlineColor = "#FF00FF", IsTextBold = true
        };
        var otherClipOnSameTrack = new TimelineClip { TextContent = "Drugi", TimelineStartSeconds = 2, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 4 };
        var track = Track(TimelineTrackKind.Caption, source, otherClipOnSameTrack);

        var clipOnAnotherTrack = new TimelineClip { TextContent = "Treći", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2 };
        var otherTrack = Track(TimelineTrackKind.Text, clipOnAnotherTrack);

        var session = new TimelineEditSession(new[] { track, otherTrack });

        session.ApplyTextStyleToAllClipsOnTrack(track.Id, source.Id);

        var updatedOther = session.Tracks[0].Clips[1];
        Assert.Equal(CaptionFontChoice.Impact, updatedOther.FontChoice);
        Assert.Equal(60, updatedOther.FontSizePx);
        Assert.Equal("#00FF00", updatedOther.TextColor);
        Assert.Equal("#FF00FF", updatedOther.TextOutlineColor);
        Assert.True(updatedOther.IsTextBold);
        Assert.Equal("Drugi", updatedOther.TextContent); // text content itself must never be touched

        // A clip on a different track is untouched, even though it's also a text clip.
        var untouchedOnOtherTrack = session.Tracks[1].Clips[0];
        Assert.Equal(CaptionFontChoice.Default, untouchedOnOtherTrack.FontChoice);

        session.Undo();
        Assert.Equal(CaptionFontChoice.Default, session.Tracks[0].Clips[1].FontChoice);
    }

    [Fact]
    public void SetTextStyle_ClampsFontSizeToReasonableRange()
    {
        var clip = new TimelineClip { TextContent = "Zdravo", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2 };
        var track = Track(TimelineTrackKind.Caption, clip);
        var session = new TimelineEditSession(new[] { track });

        session.SetTextStyle(clip.Id, CaptionFontChoice.Default, 5000, "#FFFFFF", CaptionTextPosition.Bottom);

        Assert.Equal(200, session.Tracks[0].Clips[0].FontSizePx);
    }

    [Fact]
    public void TrackFlags_LockHideMuteSolo_ToggleIndependently()
    {
        var track = Track(TimelineTrackKind.Video);
        var session = new TimelineEditSession(new[] { track });

        session.SetTrackLocked(track.Id, true);
        session.SetTrackHidden(track.Id, true);
        session.SetTrackMuted(track.Id, true);
        session.SetTrackSolo(track.Id, true);

        var updated = session.Tracks[0];
        Assert.True(updated.IsLocked);
        Assert.True(updated.IsHidden);
        Assert.True(updated.IsMuted);
        Assert.True(updated.IsSolo);
    }

    [Fact]
    public void SnapToNearest_WithinThreshold_ReturnsSnappedValue()
    {
        var result = TimelineEditSession.SnapToNearest(4.98, new[] { 5.0, 10.0 }, thresholdSeconds: 0.2);

        Assert.Equal(5.0, result);
    }

    [Fact]
    public void SnapToNearest_OutsideThreshold_ReturnsOriginalValue()
    {
        var result = TimelineEditSession.SnapToNearest(4.5, new[] { 5.0, 10.0 }, thresholdSeconds: 0.2);

        Assert.Equal(4.5, result);
    }

    [Fact]
    public void Redo_ClearedAfterNewEdit()
    {
        var clip1 = Clip(0, 0, 5);
        var clip2 = Clip(5, 0, 3);
        var track = Track(TimelineTrackKind.Video, clip1, clip2);
        var session = new TimelineEditSession(new[] { track });

        session.DeleteClips(new[] { clip1.Id });
        session.Undo();
        Assert.True(session.CanRedo);

        session.DeleteClips(new[] { clip2.Id });

        Assert.False(session.CanRedo);
    }

    [Fact]
    public void SetLayerPlacement_StoresSizePositionAndOpacity_AndIsUndoableInOneStep()
    {
        var clip = Clip(0, 0, 4);
        var session = new TimelineEditSession(new[] { Track(TimelineTrackKind.ImageOverlay, clip) });

        session.SetLayerPlacement(clip.Id, scalePercent: 30, positionXPercent: 80, positionYPercent: 20, opacity: 0.5);

        var placed = session.Tracks.SelectMany(t => t.Clips).Single(c => c.Id == clip.Id);
        Assert.Equal(30, placed.ScalePercent);
        Assert.Equal(80, placed.PositionXPercent);
        Assert.Equal(20, placed.PositionYPercent);
        Assert.Equal(0.5, placed.Opacity);

        // One undo must take the whole placement change back, not just the last value of it.
        session.Undo();
        var afterUndo = session.Tracks.SelectMany(t => t.Clips).Single(c => c.Id == clip.Id);
        Assert.Equal(100, afterUndo.ScalePercent);
        Assert.Equal(1.0, afterUndo.Opacity);
    }

    [Fact]
    public void SetLayerPlacement_OutOfRangeValues_AreClampedInsteadOfProducingAGraphFfmpegRejects()
    {
        var clip = Clip(0, 0, 4);
        var session = new TimelineEditSession(new[] { Track(TimelineTrackKind.ImageOverlay, clip) });

        session.SetLayerPlacement(clip.Id, scalePercent: 0, positionXPercent: 500, positionYPercent: -40, opacity: 9);

        var placed = session.Tracks.SelectMany(t => t.Clips).Single(c => c.Id == clip.Id);
        Assert.Equal(1, placed.ScalePercent);
        Assert.Equal(100, placed.PositionXPercent);
        Assert.Equal(0, placed.PositionYPercent);
        Assert.Equal(1.0, placed.Opacity);
    }
}
