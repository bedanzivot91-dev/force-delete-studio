namespace NPVideoStudio.Domain;

/// <summary>
/// The margins text must stay inside so it isn't covered by the platform's own interface or cropped by
/// the player. Ported from the user's other project (bedanzivot91-dev/PROGRAM-ZA-TEKST-U-VIDEO,
/// <c>text-video-tools.js: getSafeAreaPreset</c> and <c>text-layout-engine.js: SAFE_ZONES</c>) at their
/// request.
///
/// Fractions of the frame, not pixels, so one preset covers every resolution of the same shape.
///
/// The vertical margins for 9:16 are deliberately larger than the horizontal ones, and larger than 16:9's:
/// that is where TikTok, Reels and Shorts stack the caption, the username and the action buttons over the
/// video. Text placed at a "nice" 5% from the bottom of a vertical video is simply not visible to a real
/// viewer on those apps - which is exactly the mistake this preset exists to prevent.
/// </summary>
public sealed record SafeAreaPreset(
    string FormatLabel,
    int ReferenceWidth,
    int ReferenceHeight,
    double Left,
    double Right,
    double Top,
    double Bottom)
{
    /// <summary>Widescreen - YouTube, Facebook.</summary>
    public static readonly SafeAreaPreset Horizontal16By9 =
        new("16:9", 1920, 1080, Left: 0.08, Right: 0.08, Top: 0.08, Bottom: 0.10);

    /// <summary>Vertical - TikTok, Instagram Reels, YouTube Shorts. Biggest bottom margin of the three:
    /// that band is where those apps draw their own caption and buttons.</summary>
    public static readonly SafeAreaPreset Vertical9By16 =
        new("9:16", 1080, 1920, Left: 0.08, Right: 0.08, Top: 0.12, Bottom: 0.16);

    /// <summary>Square - Instagram feed.</summary>
    public static readonly SafeAreaPreset Square1By1 =
        new("1:1", 1080, 1080, Left: 0.08, Right: 0.08, Top: 0.10, Bottom: 0.10);

    /// <summary>Picks the preset by the frame's actual shape. Anything clearly taller than wide is treated
    /// as vertical, clearly wider than tall as widescreen, and near-square as square - matching on the real
    /// aspect ratio rather than requiring an exact 1080x1920, so an unusual export size still gets sane
    /// margins instead of silently falling back to 16:9.</summary>
    public static SafeAreaPreset ForFrame(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return Horizontal16By9;
        }

        var ratio = (double)width / height;

        return ratio switch
        {
            < 0.9 => Vertical9By16,
            <= 1.1 => Square1By1,
            _ => Horizontal16By9
        };
    }

    /// <summary>The usable rectangle in real pixels for a given frame size.</summary>
    public (int X, int Y, int Width, int Height) ToPixelRect(int frameWidth, int frameHeight)
    {
        var x = (int)Math.Round(frameWidth * Left);
        var y = (int)Math.Round(frameHeight * Top);
        var width = (int)Math.Round(frameWidth * (1 - Left - Right));
        var height = (int)Math.Round(frameHeight * (1 - Top - Bottom));

        return (x, y, Math.Max(1, width), Math.Max(1, height));
    }

    /// <summary>True when a text box at these normalized coordinates stays fully inside the safe area.
    /// All four values are fractions of the frame (0-1), same space as the margins themselves.</summary>
    public bool Contains(double boxLeft, double boxTop, double boxRight, double boxBottom) =>
        boxLeft >= Left &&
        boxTop >= Top &&
        boxRight <= 1 - Right &&
        boxBottom <= 1 - Bottom;
}
