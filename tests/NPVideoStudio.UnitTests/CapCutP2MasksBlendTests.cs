using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class CapCutP2MasksBlendTests
{
    [Fact]
    public void Session_SetClipCompositing_PersistsAndUndoRestores()
    {
        var clip = new TimelineClip { Id = "c", MediaAssetId = "m", SourceTrimOutSeconds = 5 };
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } };
        var session = new TimelineEditSession(new[] { track });

        session.SetClipCompositing("c", new ClipCompositingSettings(
            ClipMaskType.Circle, 40, 60, 70, 65, 12, 25, true, ClipBlendMode.Screen));

        var changed = session.Tracks.Single().Clips.Single();
        Assert.Equal(ClipMaskType.Circle, changed.MaskType);
        Assert.Equal(40, changed.MaskCenterXPercent, 6);
        Assert.Equal(12, changed.MaskFeatherPercent, 6);
        Assert.True(changed.MaskInvert);
        Assert.Equal(ClipBlendMode.Screen, changed.BlendMode);

        session.Undo();
        var restored = session.Tracks.Single().Clips.Single();
        Assert.Equal(ClipMaskType.None, restored.MaskType);
        Assert.False(restored.MaskInvert);
        Assert.Equal(ClipBlendMode.Normal, restored.BlendMode);
    }

    [Theory]
    [InlineData(ClipMaskType.Rectangle)]
    [InlineData(ClipMaskType.Circle)]
    [InlineData(ClipMaskType.Linear)]
    public void BuildMaskFilter_EmitsRealAlphaGeq(ClipMaskType type)
    {
        var clip = new TimelineClip
        {
            MaskType = type,
            MaskCenterXPercent = 50,
            MaskCenterYPercent = 50,
            MaskWidthPercent = 60,
            MaskHeightPercent = 70,
            MaskFeatherPercent = 8,
            MaskRotationDegrees = 20
        };
        var filter = FfmpegFilterGraphBuilder.BuildMaskFilter(clip);
        Assert.Contains("geq=", filter);
        Assert.Contains("alpha(X,Y)", filter);
        Assert.Contains("clip(", filter);
    }

    [Fact]
    public void Build_OverlayMaskBlendAndDelayedPts_AreInRealGraph()
    {
        var baseAsset = new MediaAsset { Id = "base", FilePath = "/media/base.mp4" };
        var overlayAsset = new MediaAsset { Id = "ov", FilePath = "/media/ov.mp4" };
        var timeline = new Timeline
        {
            Tracks = new List<TimelineTrack>
            {
                new()
                {
                    Kind = TimelineTrackKind.Video,
                    Clips = new List<TimelineClip>
                    {
                        new() { MediaAssetId = baseAsset.Id, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 10, TimelineStartSeconds = 0 }
                    }
                },
                new()
                {
                    Kind = TimelineTrackKind.Video,
                    Clips = new List<TimelineClip>
                    {
                        new()
                        {
                            MediaAssetId = overlayAsset.Id, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2, TimelineStartSeconds = 5,
                            MaskType = ClipMaskType.Circle, MaskWidthPercent = 60, MaskHeightPercent = 60,
                            MaskFeatherPercent = 5, BlendMode = ClipBlendMode.Screen
                        }
                    }
                }
            }
        };

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { baseAsset, overlayAsset }, 320, 240);
        Assert.Contains("geq=", plan.FilterComplexArgument);
        Assert.Contains("blend=all_mode=screen", plan.FilterComplexArgument);
        Assert.Contains("lutrgb=a=255", plan.FilterComplexArgument);
        Assert.Contains("blend=all_mode=screen", plan.FilterComplexArgument);
        Assert.Contains("setpts=PTS+5/TB", plan.FilterComplexArgument);
    }

    [Fact]
    public void ExtractRangeTimeline_PreservesMaskAndBlendState()
    {
        var clip = new TimelineClip
        {
            MediaAssetId = "m", SourceTrimOutSeconds = 8, TimelineStartSeconds = 2,
            MaskType = ClipMaskType.Rectangle, MaskCenterXPercent = 30, MaskCenterYPercent = 70,
            MaskWidthPercent = 55, MaskHeightPercent = 45, MaskFeatherPercent = 9,
            MaskRotationDegrees = -15, MaskInvert = true, BlendMode = ClipBlendMode.Multiply
        };
        var timeline = new Timeline
        {
            Tracks = new List<TimelineTrack> { new() { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } } }
        };

        var range = FfmpegFilterGraphBuilder.ExtractRangeTimeline(timeline, 3, 5);
        var copied = range.Tracks.Single().Clips.Single();
        Assert.Equal(ClipMaskType.Rectangle, copied.MaskType);
        Assert.Equal(30, copied.MaskCenterXPercent, 6);
        Assert.Equal(9, copied.MaskFeatherPercent, 6);
        Assert.Equal(-15, copied.MaskRotationDegrees, 6);
        Assert.True(copied.MaskInvert);
        Assert.Equal(ClipBlendMode.Multiply, copied.BlendMode);
    }
}