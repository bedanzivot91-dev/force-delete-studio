namespace NPVideoStudio.Core.Services;

/// <summary>
/// Extracts a single real decoded video frame at an exact timestamp (spec: the workspace player needs a
/// real picture, not just transport state) - deliberately a single-frame-per-call design rather than a
/// continuous streaming decoder: the workspace player calls this on every seek/step/play-tick to refresh
/// what's currently showing, which is honest "scrubbing preview" behavior for an editor rather than
/// smooth real-time video playback (a much larger, separate piece of work - see PHASE_STATUS.md).
/// </summary>
public interface IFramePreviewService
{
    /// <summary>Returns PNG-encoded frame bytes, or null if the file/timestamp couldn't produce a frame
    /// (e.g. past the end of the file, or ffmpeg genuinely not found).</summary>
    Task<byte[]?> ExtractFrameAsync(string sourceFilePath, double timestampSeconds, CancellationToken cancellationToken = default);
}
