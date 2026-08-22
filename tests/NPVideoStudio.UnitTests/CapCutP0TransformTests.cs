using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class CapCutP0TransformTests
{
    [Fact]
    public void Session_SetClipTransform_PersistsAndUndoRestores()
    {
        var clip = new TimelineClip { Id = "c", MediaAssetId = "m", SourceTrimOutSeconds = 5 };
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } };
        var session = new TimelineEditSession(new[] { track });
        session.SetClipTransform("c", new ClipTransformSettings(90, true, true, 10, 5, 7, 3, true, false, true, "#00FF00", .2, .05));

        var changed = session.Tracks.Single().Clips.Single();
        Assert.Equal(90, changed.RotationDegrees, 6);
        Assert.True(changed.FlipHorizontal);
        Assert.True(changed.FlipVertical);
        Assert.Equal(10, changed.CropLeftPercent, 6);
        Assert.True(changed.IsReversed);
        Assert.True(changed.ChromaKeyEnabled);

        session.Undo();
        var restored = session.Tracks.Single().Clips.Single();
        Assert.Equal(0, restored.RotationDegrees, 6);
        Assert.False(restored.FlipHorizontal);
        Assert.False(restored.IsReversed);
        Assert.False(restored.ChromaKeyEnabled);
    }

    [Fact]
    public void BuildTransformFilters_EmitsCropFlipAndRotation()
    {
        var clip = new TimelineClip
        {
            CropLeftPercent = 10,
            CropRightPercent = 5,
            CropTopPercent = 3,
            CropBottomPercent = 2,
            FlipHorizontal = true,
            FlipVertical = true,
            RotationDegrees = 90
        };
        var filters = FfmpegFilterGraphBuilder.BuildTransformFilters(clip);
        Assert.Contains("crop=", filters);
        Assert.Contains("hflip", filters);
        Assert.Contains("vflip", filters);
        Assert.Contains("rotate=90*PI/180", filters);
    }

    [Fact]
    public void BuildChromaKeyFilter_EmitsRealChromakeyFilter()
    {
        var clip = new TimelineClip { ChromaKeyEnabled = true, ChromaKeyColor = "#00FF00", ChromaKeySimilarity = .18, ChromaKeyBlend = .04 };
        var filter = FfmpegFilterGraphBuilder.BuildChromaKeyFilter(clip);
        Assert.Contains("chromakey=0x00FF00", filter);
        Assert.Contains("0.18", filter);
        Assert.Contains("0.04", filter);
    }

    [Fact]
    public void BuildTemporalVideoFilters_EmitsReverseAndFreeze()
    {
        var clip = new TimelineClip { IsReversed = true, IsFreezeFrame = true };
        var filters = FfmpegFilterGraphBuilder.BuildTemporalVideoFilters(clip, 3);
        Assert.Contains("reverse", filters);
        Assert.Contains("tpad=stop_mode=clone", filters);
    }

    [Fact]
    public void ExtractRangeTimeline_PreservesCapCutP0State()
    {
        var clip = new TimelineClip
        {
            Id = "c", MediaAssetId = "m", SourceTrimOutSeconds = 8, TimelineStartSeconds = 2,
            RotationDegrees = 33, FlipHorizontal = true, CropLeftPercent = 12,
            IsReversed = true, IsFreezeFrame = true, ChromaKeyEnabled = true,
            ChromaKeyColor = "#12AB34", ChromaKeySimilarity = .3, ChromaKeyBlend = .1
        };
        var timeline = new Timeline { Tracks = new List<TimelineTrack> { new() { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } } } };
        var range = FfmpegFilterGraphBuilder.ExtractRangeTimeline(timeline, 3, 5);
        var copied = range.Tracks.Single().Clips.Single();
        Assert.Equal(33, copied.RotationDegrees, 6);
        Assert.True(copied.FlipHorizontal);
        Assert.Equal(12, copied.CropLeftPercent, 6);
        Assert.True(copied.IsReversed);
        Assert.True(copied.IsFreezeFrame);
        Assert.True(copied.ChromaKeyEnabled);
        Assert.Equal("#12AB34", copied.ChromaKeyColor);
    }
}