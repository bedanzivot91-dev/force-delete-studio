using NPVideoStudio.App.Views;
using Xunit;
namespace NPVideoStudio.UnitTests;
public sealed class KeyframeGraphContractTests
{
    [Fact] public void Graph_exposes_real_keyframe_points_and_ranges()
    { Assert.NotNull(KeyframeGraphView.PointsProperty); Assert.NotNull(KeyframeGraphView.DurationProperty); Assert.NotNull(KeyframeGraphView.MinimumProperty); Assert.NotNull(KeyframeGraphView.MaximumProperty); }
}
