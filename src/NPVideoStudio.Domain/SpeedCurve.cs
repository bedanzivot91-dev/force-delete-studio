namespace NPVideoStudio.Domain;

/// <summary>CapCut-style velocity ramp presets. None keeps the legacy constant SpeedMultiplier path.</summary>
public enum SpeedCurvePreset
{
    None,
    Montage,
    Hero,
    Bullet,
    JumpCut,
    FlashIn,
    FlashOut
}

/// <summary>
/// One velocity control point anchored to an absolute source-file timestamp. Absolute source time is
/// deliberate: trim/split can change the visible window without distorting the authored curve.
/// </summary>
public sealed class SpeedCurvePoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public double SourceTimeSeconds { get; set; }
    public double SpeedMultiplier { get; set; } = 1.0;
}

/// <summary>A renderer cell: source interval played at one equivalent constant tempo.</summary>
public readonly record struct SpeedCurveRenderSegment(
    double SourceStartSeconds,
    double SourceEndSeconds,
    double SpeedMultiplier,
    double OutputStartSeconds,
    double OutputDurationSeconds);

/// <summary>
/// Pure timing math shared by timeline editing and FFmpeg rendering. The authored curve is linearly
/// interpolated between points; duration integrates 1/speed exactly. Rendering can approximate the smooth
/// line with bounded constant-tempo cells whose tempo is chosen from the exact integral, so every cell has
/// the same output duration as the mathematical curve and audio/video cannot accumulate duration drift.
/// </summary>
public static class SpeedCurveMath
{
    public const double MinSpeed = 0.25;
    public const double MaxSpeed = 4.0;

    public static bool HasCurve(TimelineClip clip) =>
        clip.SpeedCurvePreset != SpeedCurvePreset.None && clip.SpeedCurvePoints.Count >= 2;

    public static double OutputDuration(TimelineClip clip) =>
        OutputDuration(clip.SourceTrimInSeconds, clip.SourceTrimOutSeconds, clip.SpeedMultiplier, clip.SpeedCurvePoints,
            clip.SpeedCurvePreset != SpeedCurvePreset.None && clip.SpeedCurvePoints.Count >= 2);

    public static double OutputDuration(
        double sourceStart,
        double sourceEnd,
        double fallbackSpeed,
        IReadOnlyList<SpeedCurvePoint> points,
        bool useCurve = true)
    {
        var length = Math.Max(0, sourceEnd - sourceStart);
        if (length <= 1e-9) return 0;
        if (!useCurve || points.Count < 2)
            return length / ClampSpeed(fallbackSpeed);

        var anchors = BuildAnchors(sourceStart, sourceEnd, fallbackSpeed, points);
        var total = 0.0;
        for (var i = 0; i < anchors.Count - 1; i++)
            total += IntegrateLinearTempo(anchors[i].Time, anchors[i].Speed, anchors[i + 1].Time, anchors[i + 1].Speed);
        return total;
    }

    /// <summary>Maps a clip-local timeline offset back to the absolute source timestamp.</summary>
    public static double SourceTimeAtTimelineOffset(TimelineClip clip, double timelineOffsetSeconds)
    {
        var start = clip.SourceTrimInSeconds;
        var end = clip.SourceTrimOutSeconds;
        if (!HasCurve(clip))
            return Math.Clamp(start + Math.Max(0, timelineOffsetSeconds) * ClampSpeed(clip.SpeedMultiplier), start, end);

        var remaining = Math.Clamp(timelineOffsetSeconds, 0, OutputDuration(clip));
        var anchors = BuildAnchors(start, end, clip.SpeedMultiplier, clip.SpeedCurvePoints);
        for (var i = 0; i < anchors.Count - 1; i++)
        {
            var a = anchors[i];
            var b = anchors[i + 1];
            var segmentDuration = IntegrateLinearTempo(a.Time, a.Speed, b.Time, b.Speed);
            if (remaining <= segmentDuration + 1e-9)
                return Math.Clamp(InvertLinearTempo(a.Time, a.Speed, b.Time, b.Speed, remaining), a.Time, b.Time);
            remaining -= segmentDuration;
        }
        return end;
    }

    /// <summary>Exact output time between two absolute source positions under this clip's curve.</summary>
    public static double OutputDurationBetween(TimelineClip clip, double sourceStart, double sourceEnd)
    {
        var start = Math.Clamp(Math.Min(sourceStart, sourceEnd), clip.SourceTrimInSeconds, clip.SourceTrimOutSeconds);
        var end = Math.Clamp(Math.Max(sourceStart, sourceEnd), clip.SourceTrimInSeconds, clip.SourceTrimOutSeconds);
        return OutputDuration(start, end, clip.SpeedMultiplier, clip.SpeedCurvePoints, HasCurve(clip));
    }

    /// <summary>
    /// Bounded render cells. Each cell's equivalent tempo is sourceLength/exactCurveOutputDuration, so
    /// using these cells for both setpts and runtime audio tempo preserves exact cumulative duration.
    /// </summary>
    public static IReadOnlyList<SpeedCurveRenderSegment> BuildRenderSegments(TimelineClip clip, int maxCellsPerAnchorSpan = 10)
    {
        if (!HasCurve(clip))
        {
            var sourceLength = Math.Max(0, clip.SourceTrimOutSeconds - clip.SourceTrimInSeconds);
            return sourceLength <= 1e-9
                ? Array.Empty<SpeedCurveRenderSegment>()
                : new[] { new SpeedCurveRenderSegment(clip.SourceTrimInSeconds, clip.SourceTrimOutSeconds,
                    ClampSpeed(clip.SpeedMultiplier), 0, sourceLength / ClampSpeed(clip.SpeedMultiplier)) };
        }

        var anchors = BuildAnchors(clip.SourceTrimInSeconds, clip.SourceTrimOutSeconds, clip.SpeedMultiplier, clip.SpeedCurvePoints);
        var result = new List<SpeedCurveRenderSegment>();
        var outputCursor = 0.0;
        var cellsPerSpan = Math.Clamp(maxCellsPerAnchorSpan, 1, 20);

        for (var i = 0; i < anchors.Count - 1; i++)
        {
            var a = anchors[i];
            var b = anchors[i + 1];
            var span = b.Time - a.Time;
            if (span <= 1e-9) continue;

            // More samples on longer spans, but hard-bounded so a long movie never creates a gigantic graph.
            var cells = Math.Clamp((int)Math.Ceiling(span / 0.75), 1, cellsPerSpan);
            for (var c = 0; c < cells; c++)
            {
                var x0 = a.Time + span * c / cells;
                var x1 = a.Time + span * (c + 1) / cells;
                var v0 = Interpolate(a, b, x0);
                var v1 = Interpolate(a, b, x1);
                var output = IntegrateLinearTempo(x0, v0, x1, v1);
                var equivalentTempo = Math.Clamp((x1 - x0) / Math.Max(output, 1e-9), MinSpeed, MaxSpeed);
                result.Add(new SpeedCurveRenderSegment(x0, x1, equivalentTempo, outputCursor, output));
                outputCursor += output;
            }
        }
        return result;
    }

    public static List<SpeedCurvePoint> CreatePreset(SpeedCurvePreset preset, double sourceStart, double sourceEnd)
    {
        if (preset == SpeedCurvePreset.None || sourceEnd - sourceStart <= 0.05) return new();
        var length = sourceEnd - sourceStart;
        (double P, double S)[] shape = preset switch
        {
            SpeedCurvePreset.Montage => new[] { (0d, 1d), (.18, 2.2), (.45, .7), (.72, 2.0), (1d, 1d) },
            SpeedCurvePreset.Hero => new[] { (0d, 1d), (.30, .55), (.52, .35), (.72, .8), (1d, 1.35) },
            SpeedCurvePreset.Bullet => new[] { (0d, 1.25), (.36, 1d), (.50, .28), (.64, 1d), (1d, 1.25) },
            SpeedCurvePreset.JumpCut => new[] { (0d, .8), (.22, 2.8), (.48, .65), (.72, 2.6), (1d, 1d) },
            SpeedCurvePreset.FlashIn => new[] { (0d, 3.2), (.22, 1.8), (.48, 1d), (1d, 1d) },
            SpeedCurvePreset.FlashOut => new[] { (0d, 1d), (.52, 1d), (.78, 1.8), (1d, 3.2) },
            _ => Array.Empty<(double, double)>()
        };
        return shape.Select(x => new SpeedCurvePoint
        {
            SourceTimeSeconds = sourceStart + length * x.P,
            SpeedMultiplier = ClampSpeed(x.S)
        }).ToList();
    }

    private readonly record struct Anchor(double Time, double Speed);

    private static List<Anchor> BuildAnchors(double sourceStart, double sourceEnd, double fallbackSpeed, IReadOnlyList<SpeedCurvePoint> points)
    {
        var sorted = points
            .Where(p => double.IsFinite(p.SourceTimeSeconds) && double.IsFinite(p.SpeedMultiplier))
            .OrderBy(p => p.SourceTimeSeconds)
            .ToArray();
        var anchors = new List<Anchor> { new(sourceStart, EvaluateSpeed(sourceStart, fallbackSpeed, sorted)) };
        foreach (var p in sorted)
        {
            if (p.SourceTimeSeconds > sourceStart + 1e-9 && p.SourceTimeSeconds < sourceEnd - 1e-9)
                anchors.Add(new Anchor(p.SourceTimeSeconds, ClampSpeed(p.SpeedMultiplier)));
        }
        anchors.Add(new Anchor(sourceEnd, EvaluateSpeed(sourceEnd, fallbackSpeed, sorted)));
        return anchors.OrderBy(a => a.Time).ToList();
    }

    private static double EvaluateSpeed(double time, double fallback, IReadOnlyList<SpeedCurvePoint> sorted)
    {
        if (sorted.Count == 0) return ClampSpeed(fallback);
        if (time <= sorted[0].SourceTimeSeconds) return ClampSpeed(sorted[0].SpeedMultiplier);
        if (time >= sorted[^1].SourceTimeSeconds) return ClampSpeed(sorted[^1].SpeedMultiplier);
        for (var i = 0; i < sorted.Count - 1; i++)
        {
            var a = sorted[i];
            var b = sorted[i + 1];
            if (time <= b.SourceTimeSeconds)
            {
                var u = (time - a.SourceTimeSeconds) / Math.Max(1e-9, b.SourceTimeSeconds - a.SourceTimeSeconds);
                return ClampSpeed(a.SpeedMultiplier + (b.SpeedMultiplier - a.SpeedMultiplier) * u);
            }
        }
        return ClampSpeed(fallback);
    }

    private static double Interpolate(Anchor a, Anchor b, double time)
    {
        var u = (time - a.Time) / Math.Max(1e-9, b.Time - a.Time);
        return ClampSpeed(a.Speed + (b.Speed - a.Speed) * u);
    }

    private static double IntegrateLinearTempo(double x0, double v0, double x1, double v1)
    {
        var dx = Math.Max(0, x1 - x0);
        v0 = ClampSpeed(v0);
        v1 = ClampSpeed(v1);
        if (dx <= 1e-9) return 0;
        var slope = (v1 - v0) / dx;
        if (Math.Abs(slope) <= 1e-9) return dx / v0;
        return Math.Log(v1 / v0) / slope;
    }

    private static double InvertLinearTempo(double x0, double v0, double x1, double v1, double outputSeconds)
    {
        var dx = Math.Max(0, x1 - x0);
        v0 = ClampSpeed(v0);
        v1 = ClampSpeed(v1);
        var slope = dx <= 1e-9 ? 0 : (v1 - v0) / dx;
        if (Math.Abs(slope) <= 1e-9) return x0 + outputSeconds * v0;
        return x0 + v0 * (Math.Exp(slope * Math.Max(0, outputSeconds)) - 1) / slope;
    }

    public static double ClampSpeed(double speed) => Math.Clamp(double.IsFinite(speed) ? speed : 1.0, MinSpeed, MaxSpeed);
}
