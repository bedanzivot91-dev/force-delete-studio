using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Pure logic, no process/model involved.</summary>
public class VideoLayoutAggregatorTests
{
    private static DetectedTextRegion Region(double t, double x, double y, double w, double h) => new()
    {
        FrameTimestamp = TimeSpan.FromSeconds(t),
        Text = "test",
        Confidence = 0.9,
        X = x,
        Y = y,
        Width = w,
        Height = h
    };

    [Fact]
    public void ComputeTextOccupancy_NoRegions_AllZonesZero()
    {
        var occupancy = VideoLayoutAggregator.ComputeTextOccupancy(Array.Empty<DetectedTextRegion>(), sampledFrameCount: 5);

        Assert.Equal(9, occupancy.Count);
        Assert.All(occupancy.Values, v => Assert.Equal(0.0, v));
    }

    [Fact]
    public void ComputeTextOccupancy_BottomCenterTextInEveryFrame_ReportsFullOccupancyThere()
    {
        var regions = new[]
        {
            Region(0, x: 0.4, y: 0.85, w: 0.2, h: 0.1),
            Region(1, x: 0.4, y: 0.85, w: 0.2, h: 0.1),
            Region(2, x: 0.4, y: 0.85, w: 0.2, h: 0.1)
        };

        var occupancy = VideoLayoutAggregator.ComputeTextOccupancy(regions, sampledFrameCount: 3);

        Assert.Equal(1.0, occupancy[CaptionGridZone.BottomCenter]);
        Assert.Equal(0.0, occupancy[CaptionGridZone.TopLeft]);
        Assert.Equal(0.0, occupancy[CaptionGridZone.MiddleCenter]);
    }

    [Fact]
    public void ComputeTextOccupancy_RegionSpanningTwoZones_CountsBothZones()
    {
        // A wide watermark spanning the bottom-left and bottom-center cells.
        var region = Region(0, x: 0.2, y: 0.9, w: 0.3, h: 0.08);

        var occupancy = VideoLayoutAggregator.ComputeTextOccupancy(new[] { region }, sampledFrameCount: 1);

        Assert.Equal(1.0, occupancy[CaptionGridZone.BottomLeft]);
        Assert.Equal(1.0, occupancy[CaptionGridZone.BottomCenter]);
        Assert.Equal(0.0, occupancy[CaptionGridZone.BottomRight]);
    }

    [Fact]
    public void ComputeTextOccupancy_SameFrameMultipleWordsInSameZone_CountsFrameOnceNotPerWord()
    {
        // Two words in the same zone in the same frame must not inflate the ratio above 1/3 for that frame.
        var regions = new[]
        {
            Region(0, x: 0.4, y: 0.85, w: 0.05, h: 0.05),
            Region(0, x: 0.45, y: 0.85, w: 0.05, h: 0.05)
        };

        var occupancy = VideoLayoutAggregator.ComputeTextOccupancy(regions, sampledFrameCount: 3);

        Assert.Equal(1.0 / 3, occupancy[CaptionGridZone.BottomCenter], precision: 5);
    }

    [Fact]
    public void ComputeTextOccupancy_ZeroSampledFrames_ReturnsAllZerosWithoutDivideByZero()
    {
        var occupancy = VideoLayoutAggregator.ComputeTextOccupancy(Array.Empty<DetectedTextRegion>(), sampledFrameCount: 0);

        Assert.All(occupancy.Values, v => Assert.Equal(0.0, v));
    }
}
