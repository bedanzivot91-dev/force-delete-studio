namespace NPVideoStudio.Domain;

/// <summary>
/// One piece of text an OCR pass found in one sampled video frame. Coordinates are normalized (0..1)
/// relative to that frame's width/height so they're resolution-independent.
/// </summary>
public sealed class DetectedTextRegion
{
    public required TimeSpan FrameTimestamp { get; init; }
    public required string Text { get; init; }
    public required double Confidence { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
}

/// <summary>
/// Result of analyzing a video for existing on-screen content (spec Phase 7's <c>IVideoLayoutAnalysisService</c>).
///
/// Honest scope: only text detection is real (local Tesseract OCR - see <c>TesseractOcrService</c> in
/// NPVideoStudio.Media) - this sandbox has no verified path to a real, licensed face/person/logo/CTA
/// detection model, and faking those fields as "always empty = nothing detected" would be indistinguishable
/// from "not analyzed at all", which is actively misleading. <see cref="TextOccupancyByZone"/> is the one
/// real, populated signal; <see cref="AI.CaptionPlacementAdvisor"/>'s priority algorithm is written to
/// accept the full spec priority chain (face > text > logo > CTA > safe zone > ...) so a future phase can
/// plug in real face/logo/CTA detection without changing the algorithm - only this result type would gain
/// the extra fields.
/// </summary>
public sealed class VideoLayoutAnalysisResult
{
    public required int SampledFrameCount { get; init; }
    public required IReadOnlyList<DetectedTextRegion> DetectedTextRegions { get; init; }

    /// <summary>Fraction (0..1) of sampled frames where each grid zone overlapped a detected text region - higher means "more often occupied by existing text".</summary>
    public required IReadOnlyDictionary<CaptionGridZone, double> TextOccupancyByZone { get; init; }
}
