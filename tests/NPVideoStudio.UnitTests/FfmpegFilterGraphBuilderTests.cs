using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Pure logic, no process/model involved - the filter-graph string construction itself. The real ffmpeg
/// execution path (does this graph actually render correctly?) is covered by RenderServiceTests.cs.
/// </summary>
public class FfmpegFilterGraphBuilderTests
{
    private static MediaAsset Asset(string id) => new() { Id = id, FilePath = $"/media/{id}.mp4" };

    private static TimelineClip VideoClip(string assetId, double timelineStart, double trimIn, double trimOut) => new()
    {
        MediaAssetId = assetId,
        TimelineStartSeconds = timelineStart,
        SourceTrimInSeconds = trimIn,
        SourceTrimOutSeconds = trimOut
    };

    [Fact]
    public void Build_NoVideoTrack_Throws()
    {
        var timeline = new Timeline();

        Assert.Throws<InvalidOperationException>(() => FfmpegFilterGraphBuilder.Build(timeline, Array.Empty<MediaAsset>()));
    }

    [Fact]
    public void Build_VideoTrackWithNoClips_Throws()
    {
        var timeline = new Timeline();
        timeline.Tracks.Add(new TimelineTrack { Kind = TimelineTrackKind.Video });

        Assert.Throws<InvalidOperationException>(() => FfmpegFilterGraphBuilder.Build(timeline, Array.Empty<MediaAsset>()));
    }

    [Fact]
    public void Build_ClipReferencesMissingAsset_Throws()
    {
        var timeline = new Timeline();
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        track.Clips.Add(VideoClip("does-not-exist", 0, 0, 5));
        timeline.Tracks.Add(track);

        Assert.Throws<InvalidOperationException>(() => FfmpegFilterGraphBuilder.Build(timeline, Array.Empty<MediaAsset>()));
    }

    [Fact]
    public void Build_SingleClip_ProducesOneInputAndCorrectDuration()
    {
        var asset = Asset("a");
        var timeline = new Timeline();
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        track.Clips.Add(VideoClip(asset.Id, 0, 0, 5));
        timeline.Tracks.Add(track);

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { asset }, targetWidth: 640, targetHeight: 360);

        Assert.Single(plan.InputFilePaths);
        Assert.Equal(5, plan.TotalDurationSeconds);
        Assert.Contains("[0:v]trim=start=0:end=5", plan.FilterComplexArgument);
        Assert.Contains("scale=640:360", plan.FilterComplexArgument);
        Assert.Contains("concat=n=1:v=1:a=0", plan.FilterComplexArgument);
        Assert.Equal("[vconcat]", plan.VideoMapLabel);
        Assert.Equal("[aconcat]", plan.AudioMapLabel);
    }

    [Fact]
    public void Build_GapBetweenClips_InsertsBlackAndSilentFillerAndExtendsDuration()
    {
        var assetA = Asset("a");
        var assetB = Asset("b");
        var timeline = new Timeline();
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        track.Clips.Add(VideoClip(assetA.Id, 0, 0, 3));
        track.Clips.Add(VideoClip(assetB.Id, 4, 0, 2)); // 1s gap between t=3 and t=4
        timeline.Tracks.Add(track);

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { assetA, assetB });

        Assert.Contains("color=c=black:s=1920x1080:d=1", plan.FilterComplexArgument);
        Assert.Contains("anullsrc=r=44100:cl=stereo:d=1", plan.FilterComplexArgument);
        Assert.Contains("concat=n=3:v=1:a=0", plan.FilterComplexArgument); // clipA + filler + clipB
        Assert.Equal(6, plan.TotalDurationSeconds); // 3 + 1 (gap) + 2
    }

    [Fact]
    public void Build_NoGapBetweenClips_DoesNotInsertFiller()
    {
        var assetA = Asset("a");
        var assetB = Asset("b");
        var timeline = new Timeline();
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        track.Clips.Add(VideoClip(assetA.Id, 0, 0, 3));
        track.Clips.Add(VideoClip(assetB.Id, 3, 0, 2)); // back-to-back, no gap
        timeline.Tracks.Add(track);

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { assetA, assetB });

        Assert.DoesNotContain("color=c=black", plan.FilterComplexArgument);
        Assert.Contains("concat=n=2:v=1:a=0", plan.FilterComplexArgument);
        Assert.Equal(5, plan.TotalDurationSeconds);
    }

    [Fact]
    public void Build_ClipWithFadeInAndOut_AddsFadeFilters()
    {
        var asset = Asset("a");
        var timeline = new Timeline();
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        track.Clips.Add(new TimelineClip
        {
            MediaAssetId = asset.Id, TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 10,
            FadeInSeconds = 1, FadeOutSeconds = 2
        });
        timeline.Tracks.Add(track);

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { asset });

        Assert.Contains("fade=t=in:st=0:d=1", plan.FilterComplexArgument);
        Assert.Contains("fade=t=out:st=8:d=2", plan.FilterComplexArgument); // 10 - 2
        Assert.Contains("afade=t=in:st=0:d=1", plan.FilterComplexArgument);
        Assert.Contains("afade=t=out:st=8:d=2", plan.FilterComplexArgument);
    }

    [Fact]
    public void Build_MutedClip_UsesZeroVolume()
    {
        var asset = Asset("a");
        var timeline = new Timeline();
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        track.Clips.Add(new TimelineClip { MediaAssetId = asset.Id, TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 5, IsMuted = true, Volume = 1.0 });
        timeline.Tracks.Add(track);

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { asset });

        Assert.Contains("volume=0", plan.FilterComplexArgument);
    }

    [Fact]
    public void Build_TextTrackClip_AddsDrawtextWithTimeWindow()
    {
        var asset = Asset("a");
        var timeline = new Timeline();
        var videoTrack = new TimelineTrack { Kind = TimelineTrackKind.Video };
        videoTrack.Clips.Add(VideoClip(asset.Id, 0, 0, 10));
        timeline.Tracks.Add(videoTrack);

        var textTrack = new TimelineTrack { Kind = TimelineTrackKind.Text };
        textTrack.Clips.Add(new TimelineClip { TextContent = "Zdravo", TimelineStartSeconds = 2, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 3 });
        timeline.Tracks.Add(textTrack);

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { asset });

        Assert.Contains("drawtext=text='Zdravo':enable='between(t,2,5)'", plan.FilterComplexArgument);
        Assert.Equal("[vtext0]", plan.VideoMapLabel);
    }

    [Fact]
    public void Build_MultipleTextClips_ChainsDrawtextFilters()
    {
        var asset = Asset("a");
        var timeline = new Timeline();
        var videoTrack = new TimelineTrack { Kind = TimelineTrackKind.Video };
        videoTrack.Clips.Add(VideoClip(asset.Id, 0, 0, 10));
        timeline.Tracks.Add(videoTrack);

        var captionTrack = new TimelineTrack { Kind = TimelineTrackKind.Caption };
        captionTrack.Clips.Add(new TimelineClip { TextContent = "Prvi", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2 });
        captionTrack.Clips.Add(new TimelineClip { TextContent = "Drugi", TimelineStartSeconds = 2, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2 });
        timeline.Tracks.Add(captionTrack);

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { asset });

        Assert.Contains("[vconcat]drawtext=text='Prvi'", plan.FilterComplexArgument);
        Assert.Contains("[vtext0]drawtext=text='Drugi'", plan.FilterComplexArgument);
        Assert.Equal("[vtext1]", plan.VideoMapLabel);
    }

    [Theory]
    [InlineData("Zdravo: svima", "Zdravo\\: svima")]
    [InlineData("Test's tekst", "Test\\'s tekst")]
    [InlineData(@"Bekslesh \ ovde", @"Bekslesh \\ ovde")]
    [InlineData("Obično, sa zarezom", "Obično, sa zarezom")] // comma needs no escaping once quoted
    public void EscapeDrawtext_EscapesBackslashColonAndQuote(string input, string expected)
    {
        Assert.Equal(expected, FfmpegFilterGraphBuilder.EscapeDrawtext(input));
    }

    [Fact]
    public void Build_TextClipWithCustomStyle_UsesPerClipFontSizeColorAndPosition()
    {
        var asset = Asset("a");
        var timeline = new Timeline();
        var videoTrack = new TimelineTrack { Kind = TimelineTrackKind.Video };
        videoTrack.Clips.Add(VideoClip(asset.Id, 0, 0, 10));
        timeline.Tracks.Add(videoTrack);

        var captionTrack = new TimelineTrack { Kind = TimelineTrackKind.Caption };
        captionTrack.Clips.Add(new TimelineClip
        {
            TextContent = "Zdravo",
            TimelineStartSeconds = 0,
            SourceTrimInSeconds = 0,
            SourceTrimOutSeconds = 2,
            FontSizePx = 60,
            TextColor = "#00FF00",
            TextPosition = CaptionTextPosition.Top
        });
        timeline.Tracks.Add(captionTrack);

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { asset });

        Assert.Contains("fontsize=60", plan.FilterComplexArgument);
        Assert.Contains("fontcolor=#00FF00", plan.FilterComplexArgument);
        Assert.Contains("y=h*0.08", plan.FilterComplexArgument);
    }

    [Theory]
    [InlineData(CaptionTextPosition.Top, "h*0.08")]
    [InlineData(CaptionTextPosition.Middle, "(h-text_h)/2")]
    [InlineData(CaptionTextPosition.Bottom, "h*0.85")]
    public void Build_TextClip_MapsPositionEnumToCorrectYExpression(CaptionTextPosition position, string expectedY)
    {
        var asset = Asset("a");
        var timeline = new Timeline();
        var videoTrack = new TimelineTrack { Kind = TimelineTrackKind.Video };
        videoTrack.Clips.Add(VideoClip(asset.Id, 0, 0, 10));
        timeline.Tracks.Add(videoTrack);

        var captionTrack = new TimelineTrack { Kind = TimelineTrackKind.Caption };
        captionTrack.Clips.Add(new TimelineClip
        {
            TextContent = "Zdravo", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2,
            TextPosition = position
        });
        timeline.Tracks.Add(captionTrack);

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { asset });

        Assert.Contains($"y={expectedY}", plan.FilterComplexArgument);
    }

    [Fact]
    public void Build_TextClip_DefaultStyleMatchesPreviousHardcodedLook()
    {
        var asset = Asset("a");
        var timeline = new Timeline();
        var videoTrack = new TimelineTrack { Kind = TimelineTrackKind.Video };
        videoTrack.Clips.Add(VideoClip(asset.Id, 0, 0, 10));
        timeline.Tracks.Add(videoTrack);

        var captionTrack = new TimelineTrack { Kind = TimelineTrackKind.Caption };
        captionTrack.Clips.Add(new TimelineClip { TextContent = "Zdravo", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2 });
        timeline.Tracks.Add(captionTrack);

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { asset });

        Assert.Contains("fontsize=36", plan.FilterComplexArgument);
        Assert.Contains("fontcolor=#FFFFFF", plan.FilterComplexArgument);
        Assert.Contains("y=h*0.85", plan.FilterComplexArgument);
        Assert.DoesNotContain("fontfile=", plan.FilterComplexArgument);
    }

    [Fact]
    public void Build_OnlyFirstVideoTrackWithClipsIsRendered()
    {
        var assetA = Asset("a");
        var assetB = Asset("b");
        var timeline = new Timeline();
        var emptyVideoTrack = new TimelineTrack { Kind = TimelineTrackKind.Video }; // no clips - should be skipped
        var realVideoTrack = new TimelineTrack { Kind = TimelineTrackKind.Video };
        realVideoTrack.Clips.Add(VideoClip(assetA.Id, 0, 0, 4));
        timeline.Tracks.Add(emptyVideoTrack);
        timeline.Tracks.Add(realVideoTrack);

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { assetA, assetB });

        Assert.Single(plan.InputFilePaths);
        Assert.Equal(assetA.FilePath, plan.InputFilePaths[0]);
    }
}
