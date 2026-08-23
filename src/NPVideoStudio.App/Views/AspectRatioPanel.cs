using Avalonia;
using Avalonia.Controls;

namespace NPVideoStudio.App.Views;

/// <summary>
/// Gives its child the largest box of a fixed width:height ratio that fits, and centres it.
///
/// This is what makes the player take the SHAPE of the project. A 1080x1920 Shorts project in a wide
/// landscape panel used to show as a thin strip of picture with enormous black bars either side, and the
/// zoom controls only enlarged the picture inside that same wide box - which is exactly the complaint
/// that the player "ne može da uveća kompletan video, već samo taj deo gde je video plejer". With the
/// panel itself made tall and narrow, the video fills it and the whole player is vertical.
/// </summary>
public sealed class AspectRatioPanel : Decorator
{
    /// <summary>Width divided by height. 16/9 for landscape, 9/16 for Shorts/TikTok.</summary>
    public static readonly StyledProperty<double> RatioProperty =
        AvaloniaProperty.Register<AspectRatioPanel, double>(nameof(Ratio), 16.0 / 9.0);

    public double Ratio
    {
        get => GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    static AspectRatioPanel()
    {
        AffectsMeasure<AspectRatioPanel>(RatioProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = ComputeChildSize(availableSize, Ratio);
        Child?.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = ComputeChildSize(finalSize, Ratio);

        Child?.Arrange(new Rect(
            (finalSize.Width - size.Width) / 2,
            (finalSize.Height - size.Height) / 2,
            size.Width,
            size.Height));

        return finalSize;
    }

    /// <summary>
    /// The biggest box of <paramref name="ratio"/> that fits in <paramref name="available"/>. Static and
    /// pure so the sizing is unit tested rather than judged by eye - the vertical case is the one that
    /// was wrong on screen, and it is the one a width-first implementation gets wrong.
    /// </summary>
    public static Size ComputeChildSize(Size available, double ratio)
    {
        if (ratio <= 0)
        {
            return default;
        }

        // An infinite dimension happens inside scrolling/auto-sizing parents; fall back to deriving it
        // from the finite one instead of returning infinity, which would make the layout explode.
        var width = double.IsInfinity(available.Width) ? double.NaN : available.Width;
        var height = double.IsInfinity(available.Height) ? double.NaN : available.Height;

        if (double.IsNaN(width) && double.IsNaN(height))
        {
            return default;
        }

        if (double.IsNaN(width))
        {
            return new Size(height * ratio, height);
        }

        if (double.IsNaN(height))
        {
            return new Size(width, width / ratio);
        }

        if (width <= 0 || height <= 0)
        {
            return default;
        }

        // Fit by whichever dimension runs out first.
        return width / height > ratio
            ? new Size(height * ratio, height)
            : new Size(width, width / ratio);
    }
}
