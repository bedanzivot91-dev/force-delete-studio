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
        // A single segment has nothing to join onto, so it's mapped straight out - no redundant
        // concat=n=1 no-op filter (that changed when real cross-clip transitions were added, which need
        // to join segments one pair at a time instead of one flat concat over everything).
        Assert.Equal("[v0]", plan.VideoMapLabel);
        Assert.Equal("[a0]", plan.AudioMapLabel);
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
        // clipA + filler + clipB are joined two at a time (clipA+filler, then that+clipB) rather than one
        // flat concat=n=3, since a real transition can only ever join exactly two segments at once.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(plan.FilterComplexArgument, "concat=n=2:v=1:a=0").Count);
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

        Assert.Contains("[v0]drawtext=text='Prvi'", plan.FilterComplexArgument);
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

    private static Timeline TimelineWithOneTextClip(MediaAsset asset, TimelineClip textClip)
    {
        var timeline = new Timeline();
        var videoTrack = new TimelineTrack { Kind = TimelineTrackKind.Video };
        videoTrack.Clips.Add(VideoClip(asset.Id, 0, 0, 10));
        timeline.Tracks.Add(videoTrack);

        var captionTrack = new TimelineTrack { Kind = TimelineTrackKind.Caption };
        captionTrack.Clips.Add(textClip);
        timeline.Tracks.Add(captionTrack);
        return timeline;
    }

    [Fact]
    public void Build_TextClipWithOutline_AddsBorderwAndBordercolor()
    {
        var asset = Asset("a");
        var clip = new TimelineClip
        {
            TextContent = "Zdravo", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2,
            TextOutlineColor = "#FF0000", TextOutlineWidthPx = 5
        };
        var plan = FfmpegFilterGraphBuilder.Build(TimelineWithOneTextClip(asset, clip), new[] { asset });

        Assert.Contains("borderw=5:bordercolor=#FF0000", plan.FilterComplexArgument);
    }

    [Fact]
    public void Build_TextClipWithoutOutline_OmitsBorderwEntirely()
    {
        var asset = Asset("a");
        var clip = new TimelineClip { TextContent = "Zdravo", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2 };
        var plan = FfmpegFilterGraphBuilder.Build(TimelineWithOneTextClip(asset, clip), new[] { asset });

        // ":borderw=" (leading colon) distinguishes the outline option from "boxborderw=", which the
        // default-on background box always emits.
        Assert.DoesNotContain(":borderw=", plan.FilterComplexArgument);
    }

    [Fact]
    public void Build_TextClipWithShadow_AddsShadowcolorAndOffsets()
    {
        var asset = Asset("a");
        var clip = new TimelineClip
        {
            TextContent = "Zdravo", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2,
            TextShadowColor = "#333333", TextShadowOffsetPx = 4
        };
        var plan = FfmpegFilterGraphBuilder.Build(TimelineWithOneTextClip(asset, clip), new[] { asset });

        Assert.Contains("shadowcolor=#333333:shadowx=4:shadowy=4", plan.FilterComplexArgument);
    }

    [Fact]
    public void Build_TextClipWithBackgroundDisabled_OmitsBoxEntirely()
    {
        var asset = Asset("a");
        var clip = new TimelineClip
        {
            TextContent = "Zdravo", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2,
            HasTextBackground = false
        };
        var plan = FfmpegFilterGraphBuilder.Build(TimelineWithOneTextClip(asset, clip), new[] { asset });

        Assert.DoesNotContain("box=1", plan.FilterComplexArgument);
    }

    [Fact]
    public void Build_TextClipWithCustomBackground_UsesConfiguredColorAndOpacity()
    {
        var asset = Asset("a");
        var clip = new TimelineClip
        {
            TextContent = "Zdravo", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2,
            TextBackgroundColor = "#00FF00", TextBackgroundOpacity = 0.25
        };
        var plan = FfmpegFilterGraphBuilder.Build(TimelineWithOneTextClip(asset, clip), new[] { asset });

        Assert.Contains("box=1:boxcolor=#00FF00@0.25:boxborderw=10", plan.FilterComplexArgument);
    }

    [Theory]
    [InlineData(TextHorizontalAlign.Left, "w*0.05")]
    [InlineData(TextHorizontalAlign.Center, "(w-text_w)/2")]
    [InlineData(TextHorizontalAlign.Right, "w-text_w-w*0.05")]
    public void Build_TextClip_MapsHorizontalAlignToCorrectXExpression(TextHorizontalAlign align, string expectedX)
    {
        var asset = Asset("a");
        var clip = new TimelineClip
        {
            TextContent = "Zdravo", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2,
            TextHorizontalAlign = align
        };
        var plan = FfmpegFilterGraphBuilder.Build(TimelineWithOneTextClip(asset, clip), new[] { asset });

        Assert.Contains($"x={expectedX}", plan.FilterComplexArgument);
    }

    [Theory]
    [InlineData(TextCaseTransform.UpperCase, "ZDRAVO SVIMA")]
    [InlineData(TextCaseTransform.LowerCase, "zdravo svima")]
    [InlineData(TextCaseTransform.TitleCase, "Zdravo Svima")]
    [InlineData(TextCaseTransform.Normal, "Zdravo svima")]
    public void Build_TextClip_AppliesCaseTransformToBurnedInText(TextCaseTransform textCase, string expectedText)
    {
        var asset = Asset("a");
        var clip = new TimelineClip
        {
            TextContent = "Zdravo svima", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2,
            TextCase = textCase
        };
        var plan = FfmpegFilterGraphBuilder.Build(TimelineWithOneTextClip(asset, clip), new[] { asset });

        Assert.Contains($"text='{expectedText}'", plan.FilterComplexArgument);
    }

    [Fact]
    public void Build_TextClipWithLineSpacing_AddsLineSpacingArgument()
    {
        var asset = Asset("a");
        var clip = new TimelineClip
        {
            TextContent = "Zdravo", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2,
            LineSpacingPx = 12
        };
        var plan = FfmpegFilterGraphBuilder.Build(TimelineWithOneTextClip(asset, clip), new[] { asset });

        Assert.Contains("line_spacing=12", plan.FilterComplexArgument);
    }

    [Fact]
    public void Build_TextClipWithoutFade_OmitsAlphaExpression()
    {
        var asset = Asset("a");
        var clip = new TimelineClip { TextContent = "Zdravo", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2 };
        var plan = FfmpegFilterGraphBuilder.Build(TimelineWithOneTextClip(asset, clip), new[] { asset });

        Assert.DoesNotContain(":alpha=", plan.FilterComplexArgument);
    }

    [Fact]
    public void Build_TextClipWithFadeInAndOut_AddsAlphaExpressionRampingAtStartAndEnd()
    {
        var asset = Asset("a");
        var clip = new TimelineClip
        {
            TextContent = "Zdravo", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2,
            FadeInSeconds = 0.5, FadeOutSeconds = 0.5
        };
        var plan = FfmpegFilterGraphBuilder.Build(TimelineWithOneTextClip(asset, clip), new[] { asset });

        Assert.Contains(":alpha='if(lt(t,0.5)", plan.FilterComplexArgument);
        Assert.Contains("min(1,max(0,(t-0)/0.5))", plan.FilterComplexArgument);
        Assert.Contains("min(1,max(0,(2-t)/0.5))", plan.FilterComplexArgument);
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

    [Fact]
    public void Build_ClipWithTransition_UsesXfadeAndAcrossfadeWithCorrectOffset()
    {
        var assetA = Asset("a");
        var assetB = Asset("b");
        var timeline = new Timeline();
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        track.Clips.Add(VideoClip(assetA.Id, 0, 0, 4)); // 0..4
        track.Clips.Add(new TimelineClip
        {
            MediaAssetId = assetB.Id, TimelineStartSeconds = 4, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 3, // 4..7
            TransitionInType = ClipTransitionType.Fade, TransitionInDurationSeconds = 1
        });
        timeline.Tracks.Add(track);

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { assetA, assetB });

        // clipA is 4s; a 1s transition starting 1s before the end of the running output (offset = 4 - 1 = 3).
        Assert.Contains("xfade=transition=fade:duration=1:offset=3", plan.FilterComplexArgument);
        Assert.Contains("acrossfade=d=1", plan.FilterComplexArgument);
        // Total is shorter than the naive 4+3=7s sum, because the 1s transition overlaps both clips.
        Assert.Equal(6, plan.TotalDurationSeconds);
    }

    [Fact]
    public void Build_TransitionType_None_FallsBackToHardCutConcat()
    {
        var assetA = Asset("a");
        var assetB = Asset("b");
        var timeline = new Timeline();
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        track.Clips.Add(VideoClip(assetA.Id, 0, 0, 4));
        track.Clips.Add(new TimelineClip
        {
            MediaAssetId = assetB.Id, TimelineStartSeconds = 4, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 3,
            TransitionInType = ClipTransitionType.None
        });
        timeline.Tracks.Add(track);

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { assetA, assetB });

        Assert.DoesNotContain("xfade=", plan.FilterComplexArgument);
        Assert.Contains("concat=n=2:v=1:a=0", plan.FilterComplexArgument);
        Assert.Equal(7, plan.TotalDurationSeconds); // no overlap - plain sum
    }

    [Fact]
    public void Build_TransitionRequestedButThereIsARealGap_FallsBackToHardCutInsteadOfCrashing()
    {
        var assetA = Asset("a");
        var assetB = Asset("b");
        var timeline = new Timeline();
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        track.Clips.Add(VideoClip(assetA.Id, 0, 0, 3)); // 0..3
        track.Clips.Add(new TimelineClip
        {
            MediaAssetId = assetB.Id, TimelineStartSeconds = 5, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2, // 2s gap before this clip
            TransitionInType = ClipTransitionType.Fade, TransitionInDurationSeconds = 1
        });
        timeline.Tracks.Add(track);

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { assetA, assetB });

        // Nothing to transition from across a real gap (the filler is what's actually adjacent to clip B) -
        // must not silently drop the gap or crash trying to cross-fade into black filler.
        Assert.DoesNotContain("xfade=", plan.FilterComplexArgument);
        Assert.Contains("color=c=black", plan.FilterComplexArgument);
        Assert.Equal(7, plan.TotalDurationSeconds); // 3 + 2 (gap) + 2
    }

    [Fact]
    public void Build_CaptionAfterATransition_TimestampIsShiftedEarlierByTheOverlapAmount()
    {
        var assetA = Asset("a");
        var assetB = Asset("b");
        var timeline = new Timeline();
        var videoTrack = new TimelineTrack { Kind = TimelineTrackKind.Video };
        videoTrack.Clips.Add(VideoClip(assetA.Id, 0, 0, 4)); // 0..4
        videoTrack.Clips.Add(new TimelineClip
        {
            MediaAssetId = assetB.Id, TimelineStartSeconds = 4, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 3, // 4..7
            TransitionInType = ClipTransitionType.Fade, TransitionInDurationSeconds = 1
        });
        timeline.Tracks.Add(videoTrack);

        // Authored at t=5..6 (inside clip B), but the 1s transition compresses everything at/after t=4
        // by 1s, so it must actually be burned in at t=4..5 in the real (shorter) rendered video.
        var captionTrack = new TimelineTrack { Kind = TimelineTrackKind.Caption };
        captionTrack.Clips.Add(new TimelineClip { TextContent = "Posle prelaza", TimelineStartSeconds = 5, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 1 });
        timeline.Tracks.Add(captionTrack);

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { assetA, assetB });

        Assert.Contains("drawtext=text='Posle prelaza':enable='between(t,4,5)'", plan.FilterComplexArgument);
    }

    [Fact]
    public void ExtractRangeTimeline_ClipFullyInsideRange_KeptWithTimeShiftedToRangeStart()
    {
        var asset = Asset("a");
        var timeline = new Timeline();
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        track.Clips.Add(VideoClip(asset.Id, 10, 0, 5)); // authored 10..15
        timeline.Tracks.Add(track);

        var range = FfmpegFilterGraphBuilder.ExtractRangeTimeline(timeline, rangeStartSeconds: 8, rangeEndSeconds: 20);

        var clip = Assert.Single(range.Tracks[0].Clips);
        Assert.Equal(2, clip.TimelineStartSeconds); // 10 - 8
        Assert.Equal(0, clip.SourceTrimInSeconds);
        Assert.Equal(5, clip.SourceTrimOutSeconds);
    }

    [Fact]
    public void ExtractRangeTimeline_ClipEntirelyOutsideRange_IsExcluded()
    {
        var asset = Asset("a");
        var timeline = new Timeline();
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        track.Clips.Add(VideoClip(asset.Id, 0, 0, 3)); // 0..3
        track.Clips.Add(VideoClip(asset.Id, 20, 0, 3)); // 20..23
        timeline.Tracks.Add(track);

        var range = FfmpegFilterGraphBuilder.ExtractRangeTimeline(timeline, rangeStartSeconds: 8, rangeEndSeconds: 12);

        Assert.Empty(range.Tracks); // neither clip overlaps -> track has no clips -> track itself is dropped
    }

    [Fact]
    public void ExtractRangeTimeline_ClipStraddlesRangeStart_TrimmedAndTransitionCleared()
    {
        var asset = Asset("a");
        var timeline = new Timeline();
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        track.Clips.Add(new TimelineClip
        {
            MediaAssetId = asset.Id, TimelineStartSeconds = 5, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 10, // 5..15
            TransitionInType = ClipTransitionType.Fade, TransitionInDurationSeconds = 1
        });
        timeline.Tracks.Add(track);

        var range = FfmpegFilterGraphBuilder.ExtractRangeTimeline(timeline, rangeStartSeconds: 8, rangeEndSeconds: 20);

        var clip = Assert.Single(range.Tracks[0].Clips);
        Assert.Equal(0, clip.TimelineStartSeconds); // range itself starts at the clip's cut point
        Assert.Equal(3, clip.SourceTrimInSeconds); // 3s of the clip's own start (5..8) was cut off
        Assert.Equal(10, clip.SourceTrimOutSeconds); // end untouched (clip ends before the range does)
        Assert.Equal(ClipTransitionType.None, clip.TransitionInType); // nothing left to transition from
    }

    [Fact]
    public void ExtractRangeTimeline_ClipStraddlesRangeEnd_TrimmedAtEnd()
    {
        var asset = Asset("a");
        var timeline = new Timeline();
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        track.Clips.Add(VideoClip(asset.Id, 0, 0, 10)); // 0..10
        timeline.Tracks.Add(track);

        var range = FfmpegFilterGraphBuilder.ExtractRangeTimeline(timeline, rangeStartSeconds: 0, rangeEndSeconds: 6);

        var clip = Assert.Single(range.Tracks[0].Clips);
        Assert.Equal(0, clip.TimelineStartSeconds);
        Assert.Equal(0, clip.SourceTrimInSeconds);
        Assert.Equal(6, clip.SourceTrimOutSeconds); // cut off at the range's own end
    }

    [Fact]
    public void ExtractRangeTimeline_PreservesTextStyleFieldsOnCaptionClips()
    {
        var timeline = new Timeline();
        var track = new TimelineTrack { Kind = TimelineTrackKind.Caption };
        track.Clips.Add(new TimelineClip
        {
            TextContent = "Zdravo", TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 5,
            FontChoice = CaptionFontChoice.Impact, TextOutlineColor = "#FF0000", IsTextBold = true
        });
        timeline.Tracks.Add(track);

        var range = FfmpegFilterGraphBuilder.ExtractRangeTimeline(timeline, rangeStartSeconds: 0, rangeEndSeconds: 3);

        var clip = Assert.Single(range.Tracks[0].Clips);
        Assert.Equal(CaptionFontChoice.Impact, clip.FontChoice);
        Assert.Equal("#FF0000", clip.TextOutlineColor);
        Assert.True(clip.IsTextBold);
    }

    [Fact]
    public void Build_OnARangeExtractedTimeline_ProducesAShorterRenderableTimeline()
    {
        var asset = Asset("a");
        var timeline = new Timeline();
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        track.Clips.Add(VideoClip(asset.Id, 0, 0, 30)); // a long, 30s clip
        timeline.Tracks.Add(track);

        var range = FfmpegFilterGraphBuilder.ExtractRangeTimeline(timeline, rangeStartSeconds: 10, rangeEndSeconds: 15);
        var plan = FfmpegFilterGraphBuilder.Build(range, new[] { asset });

        Assert.Equal(5, plan.TotalDurationSeconds);
        Assert.Contains("trim=start=10:end=15", plan.FilterComplexArgument);
    }
}
