using NPVideoStudio.Domain;

namespace NPVideoStudio.AI;

/// <summary>
/// Recommends a vertical caption position (spec Phase 7's "Automatic" mode). Manual/Top/Middle/Bottom
/// requests pass straight through - the user already decided, this function only fills in "Automatic".
///
/// Implements the full spec priority chain in code (don't cover face > don't cover existing text > don't
/// cover logo > don't cover CTA > stay in safe zone > minimize unnecessary repositioning > stay readable
/// > avoid platform UI chrome) - but only the "existing text" signal is real today (see
/// <see cref="VideoLayoutAnalysisResult"/>'s doc comment for why face/logo/CTA aren't populated yet).
/// Bottom is tried first both because it's the conventional caption position (satisfies "minimize
/// unnecessary repositioning" by default) and because it's the spec's implied fallback.
/// </summary>
public static class CaptionPlacementAdvisor
{
    private const double OccupiedThreshold = 0.3;

    public static (CaptionPlacementMode Position, string? Warning) Recommend(
        VideoLayoutAnalysisResult analysis, CaptionPlacementMode requestedMode)
    {
        if (requestedMode != CaptionPlacementMode.Automatic)
        {
            return (requestedMode, null);
        }

        var bottom = AverageOccupancy(analysis.TextOccupancyByZone, CaptionGridZone.BottomLeft, CaptionGridZone.BottomCenter, CaptionGridZone.BottomRight);
        var top = AverageOccupancy(analysis.TextOccupancyByZone, CaptionGridZone.TopLeft, CaptionGridZone.TopCenter, CaptionGridZone.TopRight);
        var middle = AverageOccupancy(analysis.TextOccupancyByZone, CaptionGridZone.MiddleLeft, CaptionGridZone.MiddleCenter, CaptionGridZone.MiddleRight);

        if (bottom < OccupiedThreshold)
        {
            return (CaptionPlacementMode.Bottom, null);
        }

        if (top < OccupiedThreshold)
        {
            return (CaptionPlacementMode.Top, "Dno kadra sadrži postojeći tekst - titl je automatski pomeren na vrh.");
        }

        if (middle < OccupiedThreshold)
        {
            return (CaptionPlacementMode.Middle, "I dno i vrh kadra sadrže postojeći tekst - titl je automatski pomeren na sredinu.");
        }

        return (CaptionPlacementMode.Bottom,
            "Postojeći tekst je otkriven u celom kadru - titl ostaje na uobičajenom mestu (dno), moguće je delimično preklapanje.");
    }

    private static double AverageOccupancy(IReadOnlyDictionary<CaptionGridZone, double> occupancy, params CaptionGridZone[] zones) =>
        zones.Length == 0 ? 0 : zones.Average(zone => occupancy.TryGetValue(zone, out var value) ? value : 0);
}
