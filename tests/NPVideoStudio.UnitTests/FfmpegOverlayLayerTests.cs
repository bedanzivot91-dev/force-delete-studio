using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Covers the CapCut-style layer compositing (picture-in-picture / stickers / logo) added to the render
/// pipeline. Researched against how OpenShot's libopenshot describes its own model ("Multi-Layer
/// Compositing", per-clip placement) - the concept this closes is the one the builder's own doc comment
/// listed as missing: "only the first non-empty Video track is rendered (no multi-video-track layering)".
/// </summary>
public class FfmpegOverlayLayerTests
{
    private static MediaAsset Asset(string id, string path) => new()
    {
        Id = id,
        FilePath = path,
        Kind = MediaKind.Video,
        Duration = TimeSpan.FromSeconds(10),
        Width = 1920,
        Height = 1080
    };

    private static TimelineClip Clip(string assetId, double start, double trimOut) => new()
    {
        MediaAssetId = assetId,
        TimelineStartSeconds = start,
        SourceTrimInSeconds = 0,
        SourceTrimOutSeconds = trimOut
    };

    private static (Timeline Timeline, List<MediaAsset> Library) BaseTimeline()
    {
        var baseAsset = Asset("base", "/tmp/base.mp4");
        var timeline = new Timeline
        {
            Tracks =
            {
                new TimelineTrack
                {
                    Kind = TimelineTrackKind.Video,
                    Name = "Glavni video",
                    Clips = { Clip("base", 0, 5) }
                }
            }
        };

        return (timeline, new List<MediaAsset> { baseAsset });
    }

    [Fact]
    public void Build_SingleVideoTrack_ProducesNoOverlayFiltersAtAll()
    {
        var (timeline, library) = BaseTimeline();

        var plan = FfmpegFilterGraphBuilder.Build(timeline, library);

        // The feature must cost nothing when unused - an unchanged project renders the same graph as before.
        Assert.DoesNotContain("overlay=", plan.FilterComplexArgument);
    }

    [Fact]
    public void Build_SecondVideoTrack_IsCompositedOverTheFirst()
    {
        var (timeline, library) = BaseTimeline();
        library.Add(Asset("pip", "/tmp/pip.mp4"));
        timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Video,
            Name = "Sloj 2",
            Clips = { Clip("pip", 1, 3) }
        });

        var plan = FfmpegFilterGraphBuilder.Build(timeline, library);

        Assert.Contains("overlay=", plan.FilterComplexArgument);
        Assert.Contains("/tmp/pip.mp4", plan.InputFilePaths);
    }

    [Fact]
    public void Build_ImageOverlayTrack_IsComposited()
    {
        var (timeline, library) = BaseTimeline();
        library.Add(Asset("logo", "/tmp/logo.png"));
        timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.ImageOverlay,
            Name = "Logo",
            Clips = { Clip("logo", 0, 5) }
        });

        var plan = FfmpegFilterGraphBuilder.Build(timeline, library);

        Assert.Contains("overlay=", plan.FilterComplexArgument);
        Assert.Contains("/tmp/logo.png", plan.InputFilePaths);
    }

    [Fact]
    public void Build_HiddenOverlayTrack_IsSkipped()
    {
        var (timeline, library) = BaseTimeline();
        library.Add(Asset("pip", "/tmp/pip.mp4"));
        timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Video,
            Name = "Sakriven sloj",
            IsHidden = true,
            Clips = { Clip("pip", 1, 3) }
        });

        var plan = FfmpegFilterGraphBuilder.Build(timeline, library);

        Assert.DoesNotContain("overlay=", plan.FilterComplexArgument);
    }

    [Fact]
    public void Build_OverlayScale_ShrinksTheLayerToThatPercentOfTheFrameWidth()
    {
        var (timeline, library) = BaseTimeline();
        library.Add(Asset("pip", "/tmp/pip.mp4"));
        var pip = Clip("pip", 0, 4);
        pip.ScalePercent = 25;
        timeline.Tracks.Add(new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = { pip } });

        var plan = FfmpegFilterGraphBuilder.Build(timeline, library, targetWidth: 1920, targetHeight: 1080);

        // 25% of 1920, and -1 so the height follows the source aspect ratio instead of stretching.
        Assert.Contains("scale=480:-1", plan.FilterComplexArgument);
    }

    [Fact]
    public void Build_OverlayOpacity_IsAppliedThroughTheAlphaChannel()
    {
        var (timeline, library) = BaseTimeline();
        library.Add(Asset("pip", "/tmp/pip.mp4"));
        var pip = Clip("pip", 0, 4);
        pip.Opacity = 0.5;
        timeline.Tracks.Add(new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = { pip } });

        var plan = FfmpegFilterGraphBuilder.Build(timeline, library);

        Assert.Contains("format=rgba,colorchannelmixer=aa=0.5", plan.FilterComplexArgument);
    }

    [Fact]
    public void Build_OverlayIsOnlyDrawnDuringItsOwnTimeRange()
    {
        var (timeline, library) = BaseTimeline();
        library.Add(Asset("pip", "/tmp/pip.mp4"));
        var pip = Clip("pip", 1, 3); // starts at 1s, 3s long -> visible 1s..4s
        timeline.Tracks.Add(new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = { pip } });

        var plan = FfmpegFilterGraphBuilder.Build(timeline, library);

        Assert.Contains("enable='between(t,1,4)'", plan.FilterComplexArgument);
    }

    [Fact]
    public void Build_OverlayPosition_IsAnchoredOnTheLayersCentreNotItsCorner()
    {
        var (timeline, library) = BaseTimeline();
        library.Add(Asset("pip", "/tmp/pip.mp4"));
        var pip = Clip("pip", 0, 4);
        pip.PositionXPercent = 100; // hard right
        pip.PositionYPercent = 0;   // hard top
        timeline.Tracks.Add(new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = { pip } });

        var plan = FfmpegFilterGraphBuilder.Build(timeline, library);

        Assert.Contains("(main_w*1)-(overlay_w/2)", plan.FilterComplexArgument);
        Assert.Contains("(main_h*0)-(overlay_h/2)", plan.FilterComplexArgument);
    }

    [Fact]
    public void Build_TextIsBurnedInAfterOverlays_SoCaptionsAreNeverHiddenBehindASticker()
    {
        var (timeline, library) = BaseTimeline();
        library.Add(Asset("logo", "/tmp/logo.png"));
        timeline.Tracks.Add(new TimelineTrack { Kind = TimelineTrackKind.ImageOverlay, Clips = { Clip("logo", 0, 5) } });
        timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Caption,
            Clips = { new TimelineClip { TextContent = "Zdravo", TimelineStartSeconds = 0, SourceTrimOutSeconds = 3 } }
        });

        var plan = FfmpegFilterGraphBuilder.Build(timeline, library);

        var overlayAt = plan.FilterComplexArgument.IndexOf("overlay=", StringComparison.Ordinal);
        var drawtextAt = plan.FilterComplexArgument.IndexOf("drawtext=", StringComparison.Ordinal);

        Assert.True(overlayAt >= 0 && drawtextAt >= 0, "Očekivani su i overlay i drawtext u grafu.");
        Assert.True(overlayAt < drawtextAt, "Tekst mora da se crta POSLE slojeva, da ga sloj ne prekrije.");
    }

    [Fact]
    public void Build_MultipleOverlays_AreStackedInTrackOrder()
    {
        var (timeline, library) = BaseTimeline();
        library.Add(Asset("prvi", "/tmp/prvi.mp4"));
        library.Add(Asset("drugi", "/tmp/drugi.png"));
        timeline.Tracks.Add(new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = { Clip("prvi", 0, 4) } });
        timeline.Tracks.Add(new TimelineTrack { Kind = TimelineTrackKind.ImageOverlay, Clips = { Clip("drugi", 0, 4) } });

        var plan = FfmpegFilterGraphBuilder.Build(timeline, library);

        Assert.Equal(2, plan.FilterComplexArgument.Split("overlay=").Length - 1);
        Assert.Equal("/tmp/prvi.mp4", plan.InputFilePaths[^2]);
        Assert.Equal("/tmp/drugi.png", plan.InputFilePaths[^1]);
    }

    [Fact]
    public void Build_OverlayReferencingAMissingAsset_FailsLoudlyInSerbian()
    {
        var (timeline, library) = BaseTimeline();
        timeline.Tracks.Add(new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = { Clip("ne-postoji", 0, 4) } });

        var ex = Assert.Throws<InvalidOperationException>(() => FfmpegFilterGraphBuilder.Build(timeline, library));

        Assert.Contains("Sloj referencira medij koji ne postoji", ex.Message);
    }
}
