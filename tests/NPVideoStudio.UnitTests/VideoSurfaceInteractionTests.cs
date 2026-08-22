using Avalonia;
using NPVideoStudio.App.Views;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// The zoom/pan behaviour the user asked for by name: "da video moze da se poveca smanji, da se
/// interektira sa videom, da se pomera".
///
/// None of this was possible while the picture was LibVLCSharp's VideoView, because that is "a detached
/// window over your video control" (VideoLAN's README) - a separate OS window Avalonia cannot scale,
/// clip or hit-test. Now the picture is painted by Avalonia, the geometry is plain arithmetic, and plain
/// arithmetic can be tested exactly rather than eyeballed.
/// </summary>
public class VideoSurfaceInteractionTests
{
    private static readonly Size Panel = new(800, 450);
    private static readonly PixelSize Source = new(1920, 1080);

    [Fact]
    public void AtFit_ThePictureFillsThePanelAndCannotBeMoved()
    {
        var rect = VideoSurface.ComputeDestination(Panel, Source, zoom: 1.0, pan: default);

        Assert.Equal(0, rect.X, 1);
        Assert.Equal(0, rect.Y, 1);
        Assert.Equal(800, rect.Width, 1);
        Assert.Equal(450, rect.Height, 1);

        var clamped = VideoSurface.ClampPan(new Point(300, 300), Panel, Source, zoom: 1.0);
        Assert.Equal(0, clamped.X, 3);
        Assert.Equal(0, clamped.Y, 3);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(2.0)]
    [InlineData(4.0)]
    public void ZoomingIn_ReallyEnlargesThePicture(double zoom)
    {
        var fit = VideoSurface.ComputeDestination(Panel, Source, 1.0, default);
        var zoomed = VideoSurface.ComputeDestination(Panel, Source, zoom, default);

        Assert.Equal(fit.Width * zoom, zoomed.Width, 1);
        Assert.Equal(fit.Height * zoom, zoomed.Height, 1);
        Assert.Equal(fit.Width / fit.Height, zoomed.Width / zoomed.Height, 3);
    }

    [Fact]
    public void WhenZoomedIn_ThePictureCanBeDragged_ButNotOffThePanel()
    {
        const double zoom = 2.0;

        var moved = VideoSurface.ComputeDestination(Panel, Source, zoom, new Point(120, 60));
        var still = VideoSurface.ComputeDestination(Panel, Source, zoom, default);

        Assert.Equal(still.X + 120, moved.X, 1);
        Assert.Equal(still.Y + 60, moved.Y, 1);

        var extreme = VideoSurface.ComputeDestination(Panel, Source, zoom, new Point(99999, 99999));
        Assert.True(extreme.X <= 0.001, $"left edge slid inside the panel: {extreme.X}");
        Assert.True(extreme.Y <= 0.001, $"top edge slid inside the panel: {extreme.Y}");
        Assert.True(extreme.Right >= Panel.Width - 0.001, $"right edge slid inside the panel: {extreme.Right}");
        Assert.True(extreme.Bottom >= Panel.Height - 0.001, $"bottom edge slid inside the panel: {extreme.Bottom}");
    }

    [Fact]
    public void ZoomIsClampedToAUsableRange()
    {
        Assert.Equal(default, VideoSurface.ClampPan(new Point(5, 5), Panel, Source, VideoSurface.MinZoom));

        // These are deliberate public UX bounds. Exact assertions retain the regression protection while
        // avoiding a compiler warning caused by asking whether compile-time constants match a range pattern.
        Assert.Equal(1.0, VideoSurface.MinZoom);
        Assert.Equal(8.0, VideoSurface.MaxZoom);
    }

    [Theory]
    [InlineData(200, 100)]
    [InlineData(400, 225)]
    [InlineData(650, 380)]
    public void ZoomingAboutAPoint_KeepsThatPointOverTheSameSpotInTheVideo(double originX, double originY)
    {
        var origin = new Point(originX, originY);
        const double oldZoom = 1.0;
        const double newZoom = 2.5;

        var before = VideoSurface.ComputeDestination(Panel, Source, oldZoom, default);
        var fractionX = (origin.X - before.X) / before.Width;
        var fractionY = (origin.Y - before.Y) / before.Height;

        var pan = VideoSurface.ZoomPanAbout(origin, Panel, Source, oldZoom, newZoom, default);
        var after = VideoSurface.ComputeDestination(Panel, Source, newZoom, pan);

        var landedX = after.X + fractionX * after.Width;
        var landedY = after.Y + fractionY * after.Height;

        Assert.Equal(origin.X, landedX, 1);
        Assert.Equal(origin.Y, landedY, 1);
    }

    [Fact]
    public void AVerticalVideoInAWidePanel_IsLetterboxedNotStretched()
    {
        var vertical = new PixelSize(1080, 1920);
        var rect = VideoSurface.ComputeDestination(Panel, vertical, 1.0, default);

        Assert.Equal(450, rect.Height, 1);
        Assert.Equal(450 * 1080.0 / 1920.0, rect.Width, 1);
        Assert.True(rect.X > 0, "vertical video should be centred with bars, not pinned left");
    }

    [Fact]
    public void ZeroSizedPanelOrSource_IsHandledWithoutThrowing()
    {
        Assert.Equal(default, VideoSurface.ComputeDestination(new Size(0, 0), Source, 2, new Point(5, 5)));
        Assert.Equal(default, VideoSurface.ComputeDestination(Panel, new PixelSize(0, 0), 2, new Point(5, 5)));
        Assert.Equal(default, VideoSurface.ZoomPanAbout(new Point(1, 1), new Size(0, 0), Source, 1, 2, default));
    }
}
