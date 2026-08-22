using Avalonia;
using NPVideoStudio.App.Views;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// The player panel takes the PROJECT's shape, which is what the user asked for in as many words: "ako
/// je video u vertikalnom položaju hoću da i ovaj plejer bude u vertikalnom položaju".
///
/// A 1080x1920 Shorts project in a wide landscape box showed a sliver of picture between two huge black
/// bars, and zooming only enlarged the picture inside that same wide box - so the whole player could
/// never look vertical. Sizing the panel itself by the ratio fixes that, and the vertical case is
/// exactly the one a width-first implementation gets wrong, so it is pinned down here.
/// </summary>
public class AspectRatioPanelTests
{
    [Fact]
    public void VerticalProject_InAWidePanel_GetsATallNarrowBox()
    {
        var shorts = 1080.0 / 1920.0;

        var size = AspectRatioPanel.ComputeChildSize(new Size(1200, 700), shorts);

        // Height runs out first, so the box is as tall as the space and only as wide as the ratio allows.
        Assert.Equal(700, size.Height, 1);
        Assert.Equal(700 * shorts, size.Width, 1);
        Assert.True(size.Height > size.Width, "a Shorts project must produce a taller-than-wide player");
    }

    [Fact]
    public void LandscapeProject_InATallPanel_GetsAWideShortBox()
    {
        var wide = 16.0 / 9.0;

        var size = AspectRatioPanel.ComputeChildSize(new Size(900, 900), wide);

        Assert.Equal(900, size.Width, 1);
        Assert.Equal(900 / wide, size.Height, 1);
        Assert.True(size.Width > size.Height);
    }

    [Theory]
    [InlineData(1920.0 / 1080.0)]
    [InlineData(1080.0 / 1920.0)]
    [InlineData(1.0)]
    [InlineData(4.0 / 5.0)]
    public void TheBoxAlwaysFits_AndAlwaysKeepsTheRatio(double ratio)
    {
        var available = new Size(1000, 600);

        var size = AspectRatioPanel.ComputeChildSize(available, ratio);

        Assert.True(size.Width <= available.Width + 0.01, $"too wide: {size.Width}");
        Assert.True(size.Height <= available.Height + 0.01, $"too tall: {size.Height}");
        Assert.Equal(ratio, size.Width / size.Height, 3);

        // And it uses the space: one of the two dimensions must be filled, or the player is needlessly small.
        var fillsWidth = Math.Abs(size.Width - available.Width) < 0.01;
        var fillsHeight = Math.Abs(size.Height - available.Height) < 0.01;
        Assert.True(fillsWidth || fillsHeight, "the player should fill the space in at least one direction");
    }

    [Fact]
    public void AnInfiniteDimension_IsDerivedFromTheFiniteOne_RatherThanExploding()
    {
        // Happens inside scrolling/auto-sizing parents. Returning infinity here would break the layout.
        var fromHeight = AspectRatioPanel.ComputeChildSize(new Size(double.PositiveInfinity, 400), 0.5625);
        Assert.Equal(400, fromHeight.Height, 1);
        Assert.Equal(225, fromHeight.Width, 1);

        var fromWidth = AspectRatioPanel.ComputeChildSize(new Size(800, double.PositiveInfinity), 2.0);
        Assert.Equal(800, fromWidth.Width, 1);
        Assert.Equal(400, fromWidth.Height, 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonsenseRatio_IsRefusedInsteadOfCrashingTheLayout(double ratio) =>
        Assert.Equal(default, AspectRatioPanel.ComputeChildSize(new Size(500, 500), ratio));

    [Fact]
    public void AZeroSizedPanel_ProducesNothing() =>
        Assert.Equal(default, AspectRatioPanel.ComputeChildSize(new Size(0, 0), 1.5));
}
