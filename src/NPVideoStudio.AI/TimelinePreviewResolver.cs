using NPVideoStudio.Domain;

namespace NPVideoStudio.AI;

/// <summary>
/// Pure logic (no ffmpeg, no process) - given the timeline and playhead, figures out which real source
/// file and which exact timestamp within that source file the player should show right now. Separated
/// from <see cref="FfmpegFilterGraphBuilder"/> (that one plans a whole-timeline render graph) since this
/// is a much smaller, single-point-in-time question the player asks on every seek/step/play-tick.
/// </summary>
public static class TimelinePreviewResolver
{
    /// <summary>The real source file and the exact timestamp within it to decode a preview frame from.</summary>
    public readonly record struct PreviewFrameRequest(string SourceFilePath, double SourceTimestampSeconds);

    /// <summary>
    /// Finds the first non-hidden Video track and the clip active at <paramref name="playheadSeconds"/>,
    /// then maps that timeline position back to a real point in the clip's own source file via
    /// <see cref="TimelineClip.SourceTrimInSeconds"/> - null if there's nothing to show (no video track,
    /// no clip under the playhead, a Text clip with no underlying media, or a clip whose asset isn't in
    /// the library).
    /// </summary>
    public static PreviewFrameRequest? Resolve(IReadOnlyList<TimelineTrack> tracks, IReadOnlyList<MediaAsset> mediaLibrary, double playheadSeconds)
    {
        var videoTrack = tracks.FirstOrDefault(t => t.Kind == TimelineTrackKind.Video && !t.IsHidden);
        if (videoTrack is null)
        {
            return null;
        }

        var clip = videoTrack.Clips.FirstOrDefault(c => playheadSeconds >= c.TimelineStartSeconds && playheadSeconds < c.TimelineEndSeconds);
        if (clip?.MediaAssetId is null)
        {
            return null;
        }

        var asset = mediaLibrary.FirstOrDefault(a => a.Id == clip.MediaAssetId);
        if (asset is null)
        {
            return null;
        }

        var offsetIntoClip = playheadSeconds - clip.TimelineStartSeconds;
        var sourceTimestamp = clip.SourceTrimInSeconds + offsetIntoClip;
        return new PreviewFrameRequest(asset.FilePath, sourceTimestamp);
    }
}
