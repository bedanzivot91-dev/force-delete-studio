using NPVideoStudio.Domain;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class StabilizationModelContractTests
{
    [Fact]
    public void TimelineClip_UsesExistingCompleteLibvidstabSchema()
    {
        var clip = new TimelineClip();

        Assert.False(clip.StabilizationEnabled);
        Assert.Equal(5, clip.StabilizationShakiness);
        Assert.Equal(15, clip.StabilizationAccuracy);
        Assert.Equal(15, clip.StabilizationSmoothing);
        Assert.Equal(0, clip.StabilizationZoomPercent);
        Assert.Equal(1, clip.StabilizationOptimalZoom);
    }
}
