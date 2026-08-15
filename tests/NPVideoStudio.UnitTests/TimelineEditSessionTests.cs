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
}
