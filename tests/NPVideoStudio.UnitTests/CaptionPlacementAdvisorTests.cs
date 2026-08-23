using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Pure logic, no process/model involved.</summary>
public class CaptionPlacementAdvisorTests
{
    private static VideoLayoutAnalysisResult Analysis(Dictionary<CaptionGridZone, double> occupancy) => new()
    {
        SampledFrameCount = 5,
        DetectedTextRegions = Array.Empty<DetectedTextRegion>(),
        TextOccupancyByZone = occupancy
    };

    private static Dictionary<CaptionGridZone, double> AllZonesAt(double value) =>
        Enum.GetValues<CaptionGridZone>().ToDictionary(z => z, _ => value);

    [Theory]
    [InlineData(CaptionPlacementMode.Top)]
    [InlineData(CaptionPlacementMode.Middle)]
    [InlineData(CaptionPlacementMode.Bottom)]
    [InlineData(CaptionPlacementMode.Manual)]
    public void Recommend_NonAutomaticMode_PassesThroughUnchanged(CaptionPlacementMode requested)
    {
        var analysis = Analysis(AllZonesAt(1.0)); // fully occupied everywhere - should still be ignored

        var (position, warning) = CaptionPlacementAdvisor.Recommend(analysis, requested);

        Assert.Equal(requested, position);
        Assert.Null(warning);
    }

    [Fact]
    public void Recommend_Automatic_NoOccupiedText_PrefersBottomWithNoWarning()
    {
        var analysis = Analysis(AllZonesAt(0.0));

        var (position, warning) = CaptionPlacementAdvisor.Recommend(analysis, CaptionPlacementMode.Automatic);

        Assert.Equal(CaptionPlacementMode.Bottom, position);
        Assert.Null(warning);
    }

    [Fact]
    public void Recommend_Automatic_BottomOccupied_FallsBackToTopWithWarning()
    {
        var occupancy = AllZonesAt(0.0);
        occupancy[CaptionGridZone.BottomCenter] = 0.8;
        occupancy[CaptionGridZone.BottomLeft] = 0.8;
        occupancy[CaptionGridZone.BottomRight] = 0.8;

        var (position, warning) = CaptionPlacementAdvisor.Recommend(Analysis(occupancy), CaptionPlacementMode.Automatic);

        Assert.Equal(CaptionPlacementMode.Top, position);
        Assert.NotNull(warning);
    }

    [Fact]
    public void Recommend_Automatic_TopAndBottomOccupied_FallsBackToMiddleWithWarning()
    {
        var occupancy = AllZonesAt(0.0);
        foreach (var zone in new[] { CaptionGridZone.TopLeft, CaptionGridZone.TopCenter, CaptionGridZone.TopRight,
                     CaptionGridZone.BottomLeft, CaptionGridZone.BottomCenter, CaptionGridZone.BottomRight })
        {
            occupancy[zone] = 0.9;
        }

        var (position, warning) = CaptionPlacementAdvisor.Recommend(Analysis(occupancy), CaptionPlacementMode.Automatic);

        Assert.Equal(CaptionPlacementMode.Middle, position);
        Assert.NotNull(warning);
    }

    [Fact]
    public void Recommend_Automatic_EverythingOccupied_FallsBackToBottomWithOverlapWarning()
    {
        var analysis = Analysis(AllZonesAt(1.0));

        var (position, warning) = CaptionPlacementAdvisor.Recommend(analysis, CaptionPlacementMode.Automatic);

        Assert.Equal(CaptionPlacementMode.Bottom, position);
        Assert.NotNull(warning);
    }
}
