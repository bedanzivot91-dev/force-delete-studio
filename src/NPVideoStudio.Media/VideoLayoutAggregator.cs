using NPVideoStudio.Domain;

namespace NPVideoStudio.Media;

/// <summary>
/// Turns a flat list of per-frame OCR text regions into "how often is each 3x3 grid zone occupied by
/// existing text" (spec Phase 7's occupancy-over-time concept). Pure/testable - no ffmpeg/tesseract
/// involved, just geometry over already-normalized (0..1) bounding boxes.
/// </summary>
public static class VideoLayoutAggregator
{
    private static readonly (double Start, double End, (double Start, double End, CaptionGridZone Zone)[] Columns)[] Rows =
    {
        (0.0, 1.0 / 3, new[]
        {
            (0.0, 1.0 / 3, CaptionGridZone.TopLeft),
            (1.0 / 3, 2.0 / 3, CaptionGridZone.TopCenter),
            (2.0 / 3, 1.0, CaptionGridZone.TopRight)
        }),
        (1.0 / 3, 2.0 / 3, new[]
        {
            (0.0, 1.0 / 3, CaptionGridZone.MiddleLeft),
            (1.0 / 3, 2.0 / 3, CaptionGridZone.MiddleCenter),
            (2.0 / 3, 1.0, CaptionGridZone.MiddleRight)
        }),
        (2.0 / 3, 1.0, new[]
        {
            (0.0, 1.0 / 3, CaptionGridZone.BottomLeft),
            (1.0 / 3, 2.0 / 3, CaptionGridZone.BottomCenter),
            (2.0 / 3, 1.0, CaptionGridZone.BottomRight)
        })
    };

    /// <summary>
    /// For each grid zone, the fraction (0..1) of sampled frames where at least one detected text region
    /// overlapped it - counted per-frame (via <see cref="DetectedTextRegion.FrameTimestamp"/>) so a zone
    /// with many words in one frame doesn't outweigh a zone with one word present across many frames.
    /// </summary>
    public static IReadOnlyDictionary<CaptionGridZone, double> ComputeTextOccupancy(
        IReadOnlyList<DetectedTextRegion> regions, int sampledFrameCount)
    {
        var occupiedFramesByZone = new Dictionary<CaptionGridZone, HashSet<TimeSpan>>();
        foreach (var zone in Enum.GetValues<CaptionGridZone>())
        {
            occupiedFramesByZone[zone] = new HashSet<TimeSpan>();
        }

        foreach (var region in regions)
        {
            foreach (var zone in ZonesOverlapping(region))
            {
                occupiedFramesByZone[zone].Add(region.FrameTimestamp);
            }
        }

        if (sampledFrameCount <= 0)
        {
            return occupiedFramesByZone.ToDictionary(kv => kv.Key, _ => 0.0);
        }

        return occupiedFramesByZone.ToDictionary(kv => kv.Key, kv => (double)kv.Value.Count / sampledFrameCount);
    }

    private static IEnumerable<CaptionGridZone> ZonesOverlapping(DetectedTextRegion region)
    {
        var left = region.X;
        var right = region.X + region.Width;
        var top = region.Y;
        var bottom = region.Y + region.Height;

        foreach (var (rowStart, rowEnd, columns) in Rows)
        {
            if (bottom <= rowStart || top >= rowEnd)
            {
                continue;
            }

            foreach (var (colStart, colEnd, zone) in columns)
            {
                if (right <= colStart || left >= colEnd)
                {
                    continue;
                }

                yield return zone;
            }
        }
    }
}
