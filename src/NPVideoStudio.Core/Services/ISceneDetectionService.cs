namespace NPVideoStudio.Core.Services;

/// <summary>One detected visual scene boundary expressed on the original media source clock.</summary>
public readonly record struct SceneChange(double SourceTimeSeconds, double Score);

/// <summary>Detects visual scene boundaries in a bounded portion of a real video source.</summary>
public interface ISceneDetectionService
{
    Task<IReadOnlyList<SceneChange>> DetectAsync(
        string sourceFilePath,
        double sourceStartSeconds,
        double sourceEndSeconds,
        double thresholdPercent = 10.0,
        double minimumSpacingSeconds = 0.35,
        CancellationToken cancellationToken = default);
}
