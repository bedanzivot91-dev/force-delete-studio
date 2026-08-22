using NPVideoStudio.Domain;

namespace NPVideoStudio.App.ViewModels;

public sealed partial class TimelineViewModel
{
    /// <summary>
    /// Splits the currently selected real-media clip at absolute source timestamps returned by scene
    /// analysis. Timestamps are converted through the clip's real speed curve, so a cut found at source
    /// second 5 still lands on the correct timeline frame after variable Velocity editing.
    /// </summary>
    /// <returns>The number of scene cuts actually applied.</returns>
    public int AutoCutSelectedAtSourceTimes(IEnumerable<double> sourceTimes)
    {
        var selectedId = SelectedClipId;
        if (selectedId is null)
        {
            return 0;
        }

        var originalTrack = _session.Tracks.FirstOrDefault(t => t.Clips.Any(c => c.Id == selectedId));
        var original = originalTrack?.Clips.FirstOrDefault(c => c.Id == selectedId);
        if (originalTrack is null || original is null || original.MediaAssetId is null || original.IsFreezeFrame || original.IsReversed)
        {
            return 0;
        }

        var sourceIn = original.SourceTrimInSeconds;
        var sourceOut = original.SourceTrimOutSeconds;
        var cuts = sourceTimes
            .Where(t => double.IsFinite(t) && t > sourceIn + 0.05 && t < sourceOut - 0.05)
            .DistinctBy(t => Math.Round(t, 4))
            .OrderBy(t => t)
            .ToArray();
        if (cuts.Length == 0)
        {
            return 0;
        }

        // Compute all absolute timeline positions against the untouched original clip before the first
        // split. SplitClip then works on the live segment containing each point.
        var timelineCuts = cuts.Select(sourceTime => new
        {
            SourceTime = sourceTime,
            TimelineTime = original.TimelineStartSeconds + SpeedCurveMath.OutputDuration(
                sourceIn,
                sourceTime,
                original.SpeedMultiplier,
                original.SpeedCurvePoints,
                SpeedCurveMath.HasCurve(original))
        }).ToArray();

        var applied = 0;
        foreach (var cut in timelineCuts)
        {
            var liveTrack = _session.Tracks.FirstOrDefault(t => t.Id == originalTrack.Id);
            if (liveTrack is null)
            {
                break;
            }

            var segment = liveTrack.Clips.FirstOrDefault(c =>
                string.Equals(c.MediaAssetId, original.MediaAssetId, StringComparison.Ordinal) &&
                cut.SourceTime > c.SourceTrimInSeconds + 0.05 &&
                cut.SourceTime < c.SourceTrimOutSeconds - 0.05 &&
                cut.TimelineTime > c.TimelineStartSeconds + 0.05 &&
                cut.TimelineTime < c.TimelineEndSeconds - 0.05);
            if (segment is null)
            {
                continue;
            }

            var before = liveTrack.Clips.Count;
            _session.SplitClip(segment.Id, cut.TimelineTime);
            if (liveTrack.Clips.Count > before)
            {
                applied++;
            }
        }

        if (applied > 0)
        {
            RefreshFromSession();
            SelectedClipId = selectedId; // keep inspector on the first resulting segment.
        }

        return applied;
    }
}
