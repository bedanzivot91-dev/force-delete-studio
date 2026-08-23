namespace NPVideoStudio.Domain;

/// <summary>Properties that can be animated over a clip's own timeline. Time is always local to the
/// rendered clip (0 = its first visible frame), so moving the clip on the main timeline never changes the
/// animation authored inside it.</summary>
public enum ClipKeyframeProperty
{
    PositionX,
    PositionY,
    Scale,
    Rotation,
    Opacity,
    Volume
}

/// <summary>Interpolation used while travelling from the previous keyframe into this keyframe.</summary>
public enum ClipKeyframeEasing
{
    Linear,
    EaseIn,
    EaseOut,
    EaseInOut,
    Hold
}

/// <summary>One persisted animation point. This lives in the project model, not only in the UI.</summary>
public sealed class ClipKeyframe
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ClipKeyframeProperty Property { get; set; }
    public double TimeSeconds { get; set; }
    public double Value { get; set; }
    public ClipKeyframeEasing Easing { get; set; } = ClipKeyframeEasing.Linear;
}

/// <summary>Pure interpolation used by the editor, tests and render-expression builder. Keeping the
/// arithmetic here prevents preview/UI and FFmpeg export from quietly implementing different easing.</summary>
public static class ClipKeyframeEvaluator
{
    public static double ClampValue(ClipKeyframeProperty property, double value) => property switch
    {
        ClipKeyframeProperty.PositionX or ClipKeyframeProperty.PositionY => Math.Clamp(value, -200, 300),
        ClipKeyframeProperty.Scale => Math.Clamp(value, 1, 1000),
        ClipKeyframeProperty.Rotation => Math.Clamp(value, -3600, 3600),
        ClipKeyframeProperty.Opacity => Math.Clamp(value, 0, 1),
        ClipKeyframeProperty.Volume => Math.Clamp(value, 0, 2),
        _ => value
    };

    public static double StaticValue(TimelineClip clip, ClipKeyframeProperty property) => property switch
    {
        ClipKeyframeProperty.PositionX => clip.PositionXPercent,
        ClipKeyframeProperty.PositionY => clip.PositionYPercent,
        ClipKeyframeProperty.Scale => clip.ScalePercent,
        ClipKeyframeProperty.Rotation => clip.RotationDegrees,
        ClipKeyframeProperty.Opacity => clip.Opacity,
        ClipKeyframeProperty.Volume => clip.Volume,
        _ => 0
    };

    public static double Evaluate(TimelineClip clip, ClipKeyframeProperty property, double localTimeSeconds) =>
        Evaluate(clip.Keyframes, property, localTimeSeconds, StaticValue(clip, property));

    public static double Evaluate(
        IEnumerable<ClipKeyframe> keyframes,
        ClipKeyframeProperty property,
        double localTimeSeconds,
        double fallback)
    {
        var points = keyframes
            .Where(k => k.Property == property)
            .OrderBy(k => k.TimeSeconds)
            .ToArray();

        if (points.Length == 0)
        {
            return ClampValue(property, fallback);
        }

        if (points.Length == 1 || localTimeSeconds <= points[0].TimeSeconds)
        {
            return ClampValue(property, points[0].Value);
        }

        if (localTimeSeconds >= points[^1].TimeSeconds)
        {
            return ClampValue(property, points[^1].Value);
        }

        for (var i = 1; i < points.Length; i++)
        {
            var right = points[i];
            if (localTimeSeconds > right.TimeSeconds)
            {
                continue;
            }

            var left = points[i - 1];
            var span = Math.Max(1e-9, right.TimeSeconds - left.TimeSeconds);
            var t = Math.Clamp((localTimeSeconds - left.TimeSeconds) / span, 0, 1);
            var eased = ApplyEasing(t, right.Easing);
            return ClampValue(property, left.Value + (right.Value - left.Value) * eased);
        }

        return ClampValue(property, points[^1].Value);
    }

    public static double ApplyEasing(double t, ClipKeyframeEasing easing)
    {
        t = Math.Clamp(t, 0, 1);
        return easing switch
        {
            ClipKeyframeEasing.EaseIn => t * t,
            ClipKeyframeEasing.EaseOut => 1 - ((1 - t) * (1 - t)),
            ClipKeyframeEasing.EaseInOut => t < 0.5
                ? 2 * t * t
                : 1 - (2 * (1 - t) * (1 - t)),
            ClipKeyframeEasing.Hold => t >= 1 ? 1 : 0,
            _ => t
        };
    }
}
