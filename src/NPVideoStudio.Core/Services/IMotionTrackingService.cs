using NPVideoStudio.Domain;

namespace NPVideoStudio.Core.Services;

/// <summary>Tracks one user-selected object region through a source-video range using the local AI runtime.
/// Implementations must fail explicitly if the tracker/runtime is unavailable; no guessed/fabricated path.</summary>
public interface IMotionTrackingService
{
    Task<IReadOnlyList<MotionTrackingPoint>> TrackAsync(
        MotionTrackingRequest request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
