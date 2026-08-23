using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class CapCutSpeedSyncTests
{
    [Fact]
    public void TimelineDuration_ChangesWithSpeed()
    {
        var clip = new TimelineClip { SourceTrimInSeconds = 2, SourceTrimOutSeconds = 12, SpeedMultiplier = 2 };
        Assert.Equal(5, clip.TimelineDurationSeconds, 6);
        clip.SpeedMultiplier = 0.5;
        Assert.Equal(20, clip.TimelineDurationSeconds, 6);
    }

    [Theory]
    [InlineData(2.0, ",atempo=2")]
    [InlineData(0.5, ",atempo=0.5")]
    [InlineData(4.0, ",atempo=2,atempo=2")]
    [InlineData(0.25, ",atempo=0.5,atempo=0.5")]
    public void AudioSpeedFilter_CoversFullUiRange(double speed, string expected)
    {
        var clip = new TimelineClip { SpeedMultiplier = speed };
        Assert.Equal(expected, FfmpegFilterGraphBuilder.BuildAudioSpeedFilter(clip));
    }

    [Fact]
    public void Build_SpeedChangesVideoAudioAndOutputDurationTogether()
    {
        var asset = new MediaAsset { Id = "m", FilePath = "/media/m.mp4" };
        var clip = new TimelineClip { MediaAssetId = asset.Id, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 10, SpeedMultiplier = 2 };
        var timeline = new Timeline { Tracks = new List<TimelineTrack> { new() { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } } } };
        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { asset });
        Assert.Equal(5, plan.TotalDurationSeconds, 6);
        Assert.Contains("setpts=PTS/2", plan.FilterComplexArgument);
        Assert.Contains("atempo=2", plan.FilterComplexArgument);
    }

    [Fact]
    public void SplitClip_MapsTimelineOffsetBackToSourceAtSpeed()
    {
        var clip = new TimelineClip { Id = "c", MediaAssetId = "m", SourceTrimInSeconds = 0, SourceTrimOutSeconds = 10, SpeedMultiplier = 2 };
        var session = new TimelineEditSession(new[] { new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } } });
        session.SplitClip("c", 2);
        var clips = session.Tracks.Single().Clips.OrderBy(c => c.TimelineStartSeconds).ToArray();
        Assert.Equal(4, clips[0].SourceTrimOutSeconds, 6);
        Assert.Equal(4, clips[1].SourceTrimInSeconds, 6);
    }

    [Fact]
    public void TrimIn_ShiftsTimelineBySourceDeltaDividedBySpeed()
    {
        var clip = new TimelineClip { Id = "c", MediaAssetId = "m", SourceTrimInSeconds = 0, SourceTrimOutSeconds = 10, TimelineStartSeconds = 5, SpeedMultiplier = 2 };
        var session = new TimelineEditSession(new[] { new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } } });
        session.TrimIn("c", 2);
        var edited = session.Tracks.Single().Clips.Single();
        Assert.Equal(6, edited.TimelineStartSeconds, 6);
        Assert.Equal(4, edited.TimelineDurationSeconds, 6);
    }

    [Fact]
    public void ExtractRange_UsesSpeedWhenMappingBackToSource()
    {
        var clip = new TimelineClip { MediaAssetId = "m", SourceTrimInSeconds = 0, SourceTrimOutSeconds = 10, TimelineStartSeconds = 0, SpeedMultiplier = 2 };
        var timeline = new Timeline { Tracks = new List<TimelineTrack> { new() { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } } } };
        var range = FfmpegFilterGraphBuilder.ExtractRangeTimeline(timeline, 1, 3);
        var ranged = range.Tracks.Single().Clips.Single();
        Assert.Equal(2, ranged.SourceTrimInSeconds, 6);
        Assert.Equal(6, ranged.SourceTrimOutSeconds, 6);
        Assert.Equal(2, ranged.TimelineDurationSeconds, 6);
    }
}