using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using Xunit;

namespace NPVideoStudio.UnitTests;

public class TimelinePreviewResolverTests
{
    private static MediaAsset Asset(string id, string path) => new() { Id = id, FilePath = path };

    [Fact]
    public void Resolve_NoVideoTrack_ReturnsNull()
    {
        var tracks = new[] { new TimelineTrack { Kind = TimelineTrackKind.Audio } };

        Assert.Null(TimelinePreviewResolver.Resolve(tracks, Array.Empty<MediaAsset>(), 1.0));
    }

    [Fact]
    public void Resolve_HiddenVideoTrack_ReturnsNull()
    {
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video, IsHidden = true };
        track.Clips.Add(new TimelineClip { MediaAssetId = "a", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 5 });

        Assert.Null(TimelinePreviewResolver.Resolve(new[] { track }, new[] { Asset("a", "/media/a.mp4") }, 1.0));
    }

    [Fact]
    public void Resolve_NoClipUnderPlayhead_ReturnsNull()
    {
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        track.Clips.Add(new TimelineClip { MediaAssetId = "a", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 3 });

        Assert.Null(TimelinePreviewResolver.Resolve(new[] { track }, new[] { Asset("a", "/media/a.mp4") }, 5.0));
    }

    [Fact]
    public void Resolve_TextClipWithNoMediaAsset_ReturnsNull()
    {
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        track.Clips.Add(new TimelineClip { TextContent = "Naslov", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 3 });

        Assert.Null(TimelinePreviewResolver.Resolve(new[] { track }, Array.Empty<MediaAsset>(), 1.0));
    }

    [Fact]
    public void Resolve_ClipReferencesMissingAsset_ReturnsNull()
    {
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        track.Clips.Add(new TimelineClip { MediaAssetId = "missing", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 3 });

        Assert.Null(TimelinePreviewResolver.Resolve(new[] { track }, Array.Empty<MediaAsset>(), 1.0));
    }

    [Fact]
    public void Resolve_ClipAtTimelineStart_MapsToSourceTrimIn()
    {
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        track.Clips.Add(new TimelineClip { MediaAssetId = "a", TimelineStartSeconds = 10, SourceTrimInSeconds = 2, SourceTrimOutSeconds = 8 });
        var asset = Asset("a", "/media/a.mp4");

        var result = TimelinePreviewResolver.Resolve(new[] { track }, new[] { asset }, 10.0);

        Assert.NotNull(result);
        Assert.Equal("/media/a.mp4", result.Value.SourceFilePath);
        Assert.Equal(2.0, result.Value.SourceTimestampSeconds, precision: 5);
    }

    [Fact]
    public void Resolve_ClipMidway_AddsOffsetToSourceTrimIn()
    {
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        track.Clips.Add(new TimelineClip { MediaAssetId = "a", TimelineStartSeconds = 10, SourceTrimInSeconds = 2, SourceTrimOutSeconds = 8 });
        var asset = Asset("a", "/media/a.mp4");

        // Playhead at 13.5s = 3.5s into the clip -> source timestamp 2 + 3.5 = 5.5s.
        var result = TimelinePreviewResolver.Resolve(new[] { track }, new[] { asset }, 13.5);

        Assert.NotNull(result);
        Assert.Equal(5.5, result.Value.SourceTimestampSeconds, precision: 5);
    }

    [Fact]
    public void Resolve_MultipleClips_PicksTheOneUnderPlayhead()
    {
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        track.Clips.Add(new TimelineClip { MediaAssetId = "a", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 3 });
        track.Clips.Add(new TimelineClip { MediaAssetId = "b", TimelineStartSeconds = 5, SourceTrimInSeconds = 1, SourceTrimOutSeconds = 4 });
        var assets = new[] { Asset("a", "/media/a.mp4"), Asset("b", "/media/b.mp4") };

        var result = TimelinePreviewResolver.Resolve(new[] { track }, assets, 6.0);

        Assert.NotNull(result);
        Assert.Equal("/media/b.mp4", result.Value.SourceFilePath);
        Assert.Equal(2.0, result.Value.SourceTimestampSeconds, precision: 5); // 1 (trim in) + (6-5)
    }

    [Fact]
    public void Resolve_OnlyFirstVideoTrackConsidered()
    {
        var emptyVideoTrack = new TimelineTrack { Kind = TimelineTrackKind.Video };
        var realVideoTrack = new TimelineTrack { Kind = TimelineTrackKind.Video };
        realVideoTrack.Clips.Add(new TimelineClip { MediaAssetId = "a", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 5 });
        var asset = Asset("a", "/media/a.mp4");

        var result = TimelinePreviewResolver.Resolve(new[] { emptyVideoTrack, realVideoTrack }, new[] { asset }, 1.0);

        Assert.Null(result); // first Video-kind track (empty) wins, even though it has no clips
    }
}
