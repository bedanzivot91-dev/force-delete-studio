namespace NPVideoStudio.Domain;

/// <summary>One tracked object rectangle at an absolute timestamp in the original source file.
/// Coordinates are normalized 0..1 so tracking survives resolution changes and can drive both
/// Auto Reframe and later text/sticker/overlay attachment without storing pixel coordinates.</summary>
public sealed class MotionTrackingPoint
{
    public double SourceTimeSeconds { get; set; }
    public double CenterX { get; set; } = 0.5;
    public double CenterY { get; set; } = 0.5;
    public double Width { get; set; } = 0.25;
    public double Height { get; set; } = 0.25;
    public double Confidence { get; set; } = 1.0;
}

/// <summary>Initial object box selected by the user before CSRT tracking begins.</summary>
public readonly record struct MotionTrackingRegion(
    double CenterX,
    double CenterY,
    double Width,
    double Height)
{
    public MotionTrackingRegion Clamp() => new(
        Math.Clamp(CenterX, 0, 1),
        Math.Clamp(CenterY, 0, 1),
        Math.Clamp(Width, 0.02, 1),
        Math.Clamp(Height, 0.02, 1));
}

/// <summary>Request for the local OpenCV tracker. Source times refer to the original media file.</summary>
public sealed class MotionTrackingRequest
{
    public required string MediaFilePath { get; init; }
    public double SourceStartSeconds { get; init; }
    public double SourceEndSeconds { get; init; }
    public MotionTrackingRegion InitialRegion { get; init; } = new(0.5, 0.5, 0.25, 0.25);
    public double SampleIntervalSeconds { get; init; } = 0.1;
}
