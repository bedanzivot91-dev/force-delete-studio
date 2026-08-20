using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class TimelineEditSessionCloneRegressionTests
{
    [Fact]
    public void Constructor_PreservesLayerPlacementAndPictureEffects()
    {
        var clip = CreateStyledClip();
        var session = new TimelineEditSession(new[] { CreateTrack(clip) });

        AssertPictureState(session.Tracks.Single().Clips.Single());
    }

    [Fact]
    public void UndoRedoSnapshots_DoNotResetLayerPlacementOrPictureEffects()
    {
        var clip = CreateStyledClip();
        var session = new TimelineEditSession(new[] { CreateTrack(clip) });

        session.SetClipMute(clip.Id, true);
        session.Undo();
        var afterUndo = session.Tracks.Single().Clips.Single();
        Assert.False(afterUndo.IsMuted);
        AssertPictureState(afterUndo);

        session.Redo();
        var afterRedo = session.Tracks.Single().Clips.Single();
        Assert.True(afterRedo.IsMuted);
        AssertPictureState(afterRedo);
    }

    [Fact]
    public void ExtractRangeTimeline_PreservesLayerPlacementAndPictureEffects()
    {
        var clip = CreateStyledClip();
        var timeline = new Timeline
        {
            Tracks = new List<TimelineTrack> { CreateTrack(clip) }
        };

        var previewRange = FfmpegFilterGraphBuilder.ExtractRangeTimeline(timeline, 4, 7);

        var rangedClip = previewRange.Tracks.Single().Clips.Single();
        Assert.Equal(0, rangedClip.TimelineStartSeconds, 6);
        // The clip runs at 1.5x, so one second on the authored timeline advances 1.5 seconds
        // through the source media. Range 4..7 therefore maps source 2.5..7.0, not the old
        // speed-ignorant 2..5 expectation.
        Assert.Equal(2.5, rangedClip.SourceTrimInSeconds, 6);
        Assert.Equal(7, rangedClip.SourceTrimOutSeconds, 6);
        AssertPictureState(rangedClip);
    }

    private static TimelineClip CreateStyledClip() => new()
    {
        Id = "clip-1",
        MediaAssetId = "media-1",
        SourceTrimInSeconds = 1,
        SourceTrimOutSeconds = 9,
        TimelineStartSeconds = 3,
        ScalePercent = 37.5,
        PositionXPercent = 71,
        PositionYPercent = 23,
        Opacity = 0.62,
        Effect = ClipVideoEffect.Sepia,
        Brightness = 0.25,
        Contrast = 1.4,
        Saturation = 0.8,
        SpeedMultiplier = 1.5
    };

    private static TimelineTrack CreateTrack(TimelineClip clip) => new()
    {
        Kind = TimelineTrackKind.Video,
        Name = "Video",
        Clips = new List<TimelineClip> { clip }
    };

    private static void AssertPictureState(TimelineClip clip)
    {
        Assert.Equal(37.5, clip.ScalePercent, 6);
        Assert.Equal(71, clip.PositionXPercent, 6);
        Assert.Equal(23, clip.PositionYPercent, 6);
        Assert.Equal(0.62, clip.Opacity, 6);
        Assert.Equal(ClipVideoEffect.Sepia, clip.Effect);
        Assert.Equal(0.25, clip.Brightness, 6);
        Assert.Equal(1.4, clip.Contrast, 6);
        Assert.Equal(0.8, clip.Saturation, 6);
        Assert.Equal(1.5, clip.SpeedMultiplier, 6);
    }
}
