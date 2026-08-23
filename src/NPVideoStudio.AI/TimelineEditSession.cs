using NPVideoStudio.Domain;

namespace NPVideoStudio.AI;

/// <summary>
/// Pure, testable editing operations over a timeline's tracks/clips, with full undo/redo (spec Phase 8:
/// split/trim-in/trim-out/move/delete/duplicate/mute/volume/fade/lock/hide/solo/undo/redo). Same whole-
/// state-snapshot undo/redo approach as <see cref="CaptionEditSession"/>, for the same reason: simple to
/// reason about correctly, no hand-written inverse needed per operation. Holds no persistence/UI state -
/// callers own building the initial track list from (and writing the result back to) <see cref="Domain.Timeline"/>.
///
/// Deliberately does not enforce "clips on a track never overlap" - real editors commonly allow
/// overlapping layers (especially on caption/text/image-overlay tracks), so this doesn't guess at a
/// constraint the spec never actually states.
/// </summary>
public sealed class TimelineEditSession
{
    private const double MinClipDurationSeconds = 0.05;

    private readonly List<List<TimelineTrack>> _undoStack = new();
    private readonly List<List<TimelineTrack>> _redoStack = new();
    private List<TimelineTrack> _tracks;

    public TimelineEditSession(IEnumerable<TimelineTrack> tracks)
    {
        _tracks = CloneTracks(tracks);
    }

    public IReadOnlyList<TimelineTrack> Tracks => _tracks;
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        _redoStack.Add(CloneTracks(_tracks));
        _tracks = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
    }

    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        _undoStack.Add(CloneTracks(_tracks));
        _tracks = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
    }

    public void AddTrack(TimelineTrack track)
    {
        SaveSnapshot();
        _tracks.Add(track);
    }

    public void RemoveTrack(string trackId)
    {
        if (FindTrack(trackId) is null)
        {
            return;
        }

        SaveSnapshot();
        _tracks.RemoveAll(t => t.Id == trackId);
    }

    public void AddClip(string trackId, TimelineClip clip)
    {
        var track = FindTrack(trackId);
        if (track is null)
        {
            return;
        }

        SaveSnapshot();
        FindTrack(trackId)!.Clips.Add(clip);
    }

    public void DeleteClips(IEnumerable<string> clipIds)
    {
        var idSet = clipIds.ToHashSet();
        if (!_tracks.Any(t => t.Clips.Any(c => idSet.Contains(c.Id))))
        {
            return;
        }

        SaveSnapshot();
        foreach (var track in _tracks)
        {
            track.Clips.RemoveAll(c => idSet.Contains(c.Id));
        }
    }

    /// <summary>Removes a transcript-selected time range from picture/audio tracks and closes the gap.</summary>
    public void RippleDeleteMediaRange(double startSeconds, double endSeconds, string? transcriptClipId = null)
    {
        startSeconds = Math.Max(0, startSeconds);
        if (endSeconds - startSeconds < MinClipDurationSeconds) return;
        var affected = _tracks.Where(t => t.Kind is TimelineTrackKind.Video or TimelineTrackKind.Audio)
            .SelectMany(t => t.Clips).Any(c => c.TimelineEndSeconds > startSeconds && c.TimelineStartSeconds < endSeconds);
        if (!affected) return;
        SaveSnapshot();
        var gap = endSeconds - startSeconds;
        foreach (var track in _tracks.Where(t => t.Kind is TimelineTrackKind.Video or TimelineTrackKind.Audio))
        {
            foreach (var clip in track.Clips.ToArray())
            {
                var clipStart = clip.TimelineStartSeconds; var clipEnd = clip.TimelineEndSeconds;
                if (clipEnd <= startSeconds) continue;
                if (clipStart >= endSeconds) { clip.TimelineStartSeconds -= gap; continue; }
                if (clipStart >= startSeconds && clipEnd <= endSeconds) { track.Clips.Remove(clip); continue; }
                if (clipStart < startSeconds && clipEnd > endSeconds)
                {
                    var right = Clone(clip); right.Id = Guid.NewGuid().ToString("N");
                    var leftOffset = startSeconds - clipStart; var rightOffset = endSeconds - clipStart;
                    clip.SourceTrimOutSeconds = SpeedCurveMath.SourceTimeAtTimelineOffset(clip, leftOffset);
                    right.SourceTrimInSeconds = SpeedCurveMath.SourceTimeAtTimelineOffset(right, rightOffset);
                    right.TimelineStartSeconds = startSeconds; track.Clips.Add(right); continue;
                }
                if (clipStart < startSeconds)
                    clip.SourceTrimOutSeconds = SpeedCurveMath.SourceTimeAtTimelineOffset(clip, startSeconds - clipStart);
                else
                {
                    clip.SourceTrimInSeconds = SpeedCurveMath.SourceTimeAtTimelineOffset(clip, endSeconds - clipStart);
                    clip.TimelineStartSeconds = startSeconds;
                }
            }
        }
        if (transcriptClipId is not null)
            foreach (var track in _tracks) track.Clips.RemoveAll(c => c.Id == transcriptClipId);
    }

    public void DuplicateClip(string clipId)
    {
        var (track, clip) = FindClipWithTrack(clipId);
        if (track is null || clip is null)
        {
            return;
        }

        SaveSnapshot();
        var (liveTrack, liveClip) = FindClipWithTrack(clipId);
        var duplicate = Clone(liveClip!);
        duplicate.Id = Guid.NewGuid().ToString("N");
        duplicate.TimelineStartSeconds = liveClip!.TimelineEndSeconds;
        liveTrack!.Clips.Add(duplicate);
    }

    /// <summary>Moves a clip to a new timeline position, optionally onto a different track.</summary>
    public void MoveClip(string clipId, double newTimelineStartSeconds, string? newTrackId = null)
    {
        var (track, clip) = FindClipWithTrack(clipId);
        if (track is null || clip is null)
        {
            return;
        }

        var targetTrack = newTrackId is null ? track : FindTrack(newTrackId);
        if (targetTrack is null)
        {
            return;
        }

        SaveSnapshot();
        var (liveTrack, liveClip) = FindClipWithTrack(clipId);
        liveClip!.TimelineStartSeconds = Math.Max(0, newTimelineStartSeconds);

        if (newTrackId is not null && liveTrack!.Id != newTrackId)
        {
            liveTrack.Clips.Remove(liveClip);
            FindTrack(newTrackId)!.Clips.Add(liveClip);
        }
    }

    /// <summary>Splits one clip into two at an absolute timeline position - the spec's "split" operation.</summary>
    public void SplitClip(string clipId, double atTimelineSeconds)
    {
        var (track, clip) = FindClipWithTrack(clipId);
        if (track is null || clip is null)
        {
            return;
        }

        if (atTimelineSeconds <= clip.TimelineStartSeconds + MinClipDurationSeconds ||
            atTimelineSeconds >= clip.TimelineEndSeconds - MinClipDurationSeconds)
        {
            return; // Split point too close to either edge to leave two valid clips.
        }

        var offsetIntoClip = atTimelineSeconds - clip.TimelineStartSeconds;
        var splitSourcePoint = clip.IsFreezeFrame
            ? clip.SourceTrimInSeconds + offsetIntoClip
            : SpeedCurveMath.SourceTimeAtTimelineOffset(clip, offsetIntoClip);

        SaveSnapshot();
        var (liveTrack, liveClip) = FindClipWithTrack(clipId);
        var second = Clone(liveClip!);
        second.Id = Guid.NewGuid().ToString("N");
        SplitKeyframesAt(liveClip!, second, offsetIntoClip);
        second.SourceTrimInSeconds = splitSourcePoint;
        second.TimelineStartSeconds = atTimelineSeconds;
        second.FadeInSeconds = 0; // The new leading edge is a fresh cut, not the original fade-in point.

        liveClip!.SourceTrimOutSeconds = splitSourcePoint;
        liveClip.FadeOutSeconds = 0; // Same reasoning for the original clip's new trailing edge.

        liveTrack!.Clips.Add(second);
    }

    public void TrimIn(string clipId, double newSourceTrimInSeconds)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        var clamped = Math.Clamp(newSourceTrimInSeconds, 0, clip.SourceTrimOutSeconds - MinClipDurationSeconds);
        var delta = clamped - clip.SourceTrimInSeconds;
        if (Math.Abs(delta) < 1e-9)
        {
            return;
        }

        SaveSnapshot();
        var (_, liveClip) = FindClipWithTrack(clipId);
        var oldTrimIn = liveClip!.SourceTrimInSeconds;
        var timelineDelta = liveClip.IsFreezeFrame
            ? delta
            : Math.Sign(delta) * SpeedCurveMath.OutputDuration(
                Math.Min(oldTrimIn, clamped),
                Math.Max(oldTrimIn, clamped),
                liveClip.SpeedMultiplier,
                liveClip.SpeedCurvePoints,
                SpeedCurveMath.HasCurve(liveClip));
        TrimKeyframesAtStart(liveClip, timelineDelta);
        liveClip.SourceTrimInSeconds = clamped;
        liveClip.TimelineStartSeconds = Math.Max(0, liveClip.TimelineStartSeconds + timelineDelta);
    }

    public void TrimOut(string clipId, double newSourceTrimOutSeconds)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        var clamped = Math.Max(newSourceTrimOutSeconds, clip.SourceTrimInSeconds + MinClipDurationSeconds);
        if (Math.Abs(clamped - clip.SourceTrimOutSeconds) < 1e-9)
        {
            return;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        var newDuration = liveClip.IsFreezeFrame
            ? Math.Max(0, clamped - liveClip.SourceTrimInSeconds)
            : SpeedCurveMath.OutputDuration(
                liveClip.SourceTrimInSeconds,
                clamped,
                liveClip.SpeedMultiplier,
                liveClip.SpeedCurvePoints,
                SpeedCurveMath.HasCurve(liveClip));
        TrimKeyframesAtEnd(liveClip, newDuration);
        liveClip.SourceTrimOutSeconds = clamped;
    }

    public void SetFade(string clipId, double fadeInSeconds, double fadeOutSeconds)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        SaveSnapshot();
        var (_, liveClip) = FindClipWithTrack(clipId);
        liveClip!.FadeInSeconds = Math.Max(0, fadeInSeconds);
        liveClip.FadeOutSeconds = Math.Max(0, fadeOutSeconds);
    }

    /// <summary>Lets the user correct a Caption/Text clip's own words - most importantly, what
    /// auto-generated speech-to-text captions actually got right or wrong, since Whisper is never
    /// guaranteed accurate (especially on singing/music) and there was previously no way to fix a
    /// misheard word short of deleting the whole clip and retyping it from scratch on a Text track.</summary>
    public void SetTextContent(string clipId, string textContent)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null || clip.TextContent is null)
        {
            return;
        }

        SaveSnapshot();
        FindClipWithTrack(clipId).Clip!.TextContent = textContent;
    }

    public void SetTransition(string clipId, ClipTransitionType type, double durationSeconds)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        liveClip.TransitionInType = type;
        liveClip.TransitionInDurationSeconds = Math.Max(0.05, durationSeconds);
    }

    /// <summary>
    /// Sets where an overlay clip sits over the video underneath it - its size, its centre position and
    /// how see-through it is (the CapCut-style picture-in-picture / sticker / logo placement rendered by
    /// <c>FfmpegFilterGraphBuilder.AppendOverlayLayers</c>). Goes through the session, like every other
    /// edit here, so one undo takes the whole placement change back.
    ///
    /// Values are clamped to what the renderer can actually honour rather than trusted: a scale of 0 or a
    /// negative opacity would produce a filter graph ffmpeg rejects outright, failing the whole export for
    /// what is really just a slider dragged to its end.
    /// </summary>
    public void SetLayerPlacement(string clipId, double scalePercent, double positionXPercent, double positionYPercent, double opacity)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        liveClip.ScalePercent = Math.Clamp(scalePercent, 1, 1000);
        liveClip.PositionXPercent = Math.Clamp(positionXPercent, 0, 100);
        liveClip.PositionYPercent = Math.Clamp(positionYPercent, 0, 100);
        liveClip.Opacity = Math.Clamp(opacity, 0, 1);
    }

    /// <summary>Adds or replaces one keyframe at a clip-local time. This is a normal session edit:
    /// it is persisted, undoable and never mutates the caller's pre-snapshot object first.</summary>
    public void UpsertKeyframe(
        string clipId,
        ClipKeyframeProperty property,
        double localTimeSeconds,
        double value,
        ClipKeyframeEasing easing)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        var time = Math.Clamp(localTimeSeconds, 0, Math.Max(0, clip.TimelineDurationSeconds));
        var clampedValue = ClipKeyframeEvaluator.ClampValue(property, value);
        var existing = clip.Keyframes.FirstOrDefault(k =>
            k.Property == property && Math.Abs(k.TimeSeconds - time) <= 0.001);

        if (existing is not null &&
            Math.Abs(existing.Value - clampedValue) <= 1e-9 &&
            existing.Easing == easing)
        {
            return;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        var liveExisting = liveClip.Keyframes.FirstOrDefault(k =>
            k.Property == property && Math.Abs(k.TimeSeconds - time) <= 0.001);
        if (liveExisting is null)
        {
            liveClip.Keyframes.Add(new ClipKeyframe
            {
                Property = property,
                TimeSeconds = time,
                Value = clampedValue,
                Easing = easing
            });
        }
        else
        {
            liveExisting.TimeSeconds = time;
            liveExisting.Value = clampedValue;
            liveExisting.Easing = easing;
        }

        liveClip.Keyframes = liveClip.Keyframes
            .OrderBy(k => k.Property)
            .ThenBy(k => k.TimeSeconds)
            .ToList();
    }

    public void RemoveKeyframe(
        string clipId,
        ClipKeyframeProperty property,
        double localTimeSeconds,
        double toleranceSeconds = 0.08)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        var nearest = clip.Keyframes
            .Where(k => k.Property == property)
            .OrderBy(k => Math.Abs(k.TimeSeconds - localTimeSeconds))
            .FirstOrDefault();
        if (nearest is null || Math.Abs(nearest.TimeSeconds - localTimeSeconds) > Math.Max(0.001, toleranceSeconds))
        {
            return;
        }

        SaveSnapshot();
        FindClipWithTrack(clipId).Clip!.Keyframes.RemoveAll(k => k.Id == nearest.Id);
    }

    public void RemoveAllKeyframes(string clipId)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null || clip.Keyframes.Count == 0)
        {
            return;
        }

        SaveSnapshot();
        FindClipWithTrack(clipId).Clip!.Keyframes.Clear();
    }

    /// <summary>
    /// Sets a clip's picture look and playback speed (rendered by
    /// <c>FfmpegFilterGraphBuilder.BuildEffectFilters</c>/<c>BuildSpeedFilter</c>). Values are clamped to
    /// what ffmpeg accepts rather than trusted, so a slider dragged to its end can't produce a filter
    /// graph that fails the whole export.
    /// </summary>
    public void SetClipEffects(string clipId, ClipVideoEffect effect, double brightness, double contrast, double saturation, double speed)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        var previousTimelineDuration = liveClip.TimelineDurationSeconds;
        liveClip.Effect = effect;
        liveClip.Brightness = Math.Clamp(brightness, -1, 1);
        liveClip.Contrast = Math.Clamp(contrast, 0, 3);
        liveClip.Saturation = Math.Clamp(saturation, 0, 3);
        var newSpeed = SpeedCurveMath.ClampSpeed(speed);
        if (Math.Abs(newSpeed - liveClip.SpeedMultiplier) > 1e-9)
        {
            // The constant-speed slider is an explicit switch back from a velocity ramp.
            liveClip.SpeedCurvePreset = SpeedCurvePreset.None;
            liveClip.SpeedCurvePoints.Clear();
        }
        liveClip.SpeedMultiplier = newSpeed;
        RescaleKeyframesForDurationChange(liveClip, previousTimelineDuration);
    }

    public void SetColorGrading(string clipId, ClipColorGradingSettings settings)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null) return;

        var exposure = Math.Clamp(settings.ExposureStops, -3, 3);
        var highlights = Math.Clamp(settings.Highlights, -1, 1);
        var shadows = Math.Clamp(settings.Shadows, -1, 1);
        var temperature = Math.Clamp(settings.Temperature, -1, 1);
        var tint = Math.Clamp(settings.Tint, -1, 1);
        var hue = Math.Clamp(settings.HueDegrees, -180, 180);
        var vibrance = Math.Clamp(settings.Vibrance, -1, 1);
        var sr = Math.Clamp(settings.ShadowRed, -1, 1);
        var sg = Math.Clamp(settings.ShadowGreen, -1, 1);
        var sb = Math.Clamp(settings.ShadowBlue, -1, 1);
        var mr = Math.Clamp(settings.MidtoneRed, -1, 1);
        var mg = Math.Clamp(settings.MidtoneGreen, -1, 1);
        var mb = Math.Clamp(settings.MidtoneBlue, -1, 1);
        var hr = Math.Clamp(settings.HighlightRed, -1, 1);
        var hg = Math.Clamp(settings.HighlightGreen, -1, 1);
        var hb = Math.Clamp(settings.HighlightBlue, -1, 1);
        if (Math.Abs(clip.ExposureStops - exposure) < 1e-9 &&
            Math.Abs(clip.Highlights - highlights) < 1e-9 &&
            Math.Abs(clip.Shadows - shadows) < 1e-9 &&
            Math.Abs(clip.Temperature - temperature) < 1e-9 &&
            Math.Abs(clip.Tint - tint) < 1e-9 &&
            Math.Abs(clip.HueDegrees - hue) < 1e-9 &&
            Math.Abs(clip.Vibrance - vibrance) < 1e-9 &&
            Math.Abs(clip.ShadowRed - sr) < 1e-9 && Math.Abs(clip.ShadowGreen - sg) < 1e-9 && Math.Abs(clip.ShadowBlue - sb) < 1e-9 &&
            Math.Abs(clip.MidtoneRed - mr) < 1e-9 && Math.Abs(clip.MidtoneGreen - mg) < 1e-9 && Math.Abs(clip.MidtoneBlue - mb) < 1e-9 &&
            Math.Abs(clip.HighlightRed - hr) < 1e-9 && Math.Abs(clip.HighlightGreen - hg) < 1e-9 && Math.Abs(clip.HighlightBlue - hb) < 1e-9) return;

        SaveSnapshot();
        var live = FindClipWithTrack(clipId).Clip!;
        live.ExposureStops = exposure;
        live.Highlights = highlights;
        live.Shadows = shadows;
        live.Temperature = temperature;
        live.Tint = tint;
        live.HueDegrees = hue;
        live.Vibrance = vibrance;
        live.ShadowRed = sr; live.ShadowGreen = sg; live.ShadowBlue = sb;
        live.MidtoneRed = mr; live.MidtoneGreen = mg; live.MidtoneBlue = mb;
        live.HighlightRed = hr; live.HighlightGreen = hg; live.HighlightBlue = hb;
    }

    public void SetClipLut(string clipId, string? lutFilePath)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null) return;
        var normalized = string.IsNullOrWhiteSpace(lutFilePath) ? null : Path.GetFullPath(lutFilePath);
        if (string.Equals(clip.LutFilePath, normalized, StringComparison.OrdinalIgnoreCase)) return;
        SaveSnapshot();
        FindClipWithTrack(clipId).Clip!.LutFilePath = normalized;
    }

    public void SetClipAudioEnhancement(string clipId, ClipAudioEnhancementSettings settings)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null || clip.MediaAssetId is null) return;

        var strength = Math.Clamp(settings.NoiseReductionStrength, 0, 1);
        if (clip.AudioNoiseReductionEnabled == settings.NoiseReductionEnabled &&
            Math.Abs(clip.AudioNoiseReductionStrength - strength) < 1e-9 &&
            clip.AudioEnhanceVoiceEnabled == settings.EnhanceVoiceEnabled &&
            clip.AudioLoudnessNormalizationEnabled == settings.LoudnessNormalizationEnabled)
            return;

        SaveSnapshot();
        var live = FindClipWithTrack(clipId).Clip!;
        live.AudioNoiseReductionEnabled = settings.NoiseReductionEnabled;
        live.AudioNoiseReductionStrength = strength;
        live.AudioEnhanceVoiceEnabled = settings.EnhanceVoiceEnabled;
        live.AudioLoudnessNormalizationEnabled = settings.LoudnessNormalizationEnabled;
    }

    public void SetSpeedCurvePreset(string clipId, SpeedCurvePreset preset)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null || clip.MediaAssetId is null || clip.IsFreezeFrame || clip.IsReversed)
        {
            return;
        }

        if (preset != SpeedCurvePreset.None && clip.SourceTrimOutSeconds - clip.SourceTrimInSeconds <= MinClipDurationSeconds)
        {
            return;
        }

        if (clip.SpeedCurvePreset == preset &&
            (preset == SpeedCurvePreset.None || clip.SpeedCurvePoints.Count >= 2))
        {
            return;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        var previousTimelineDuration = liveClip.TimelineDurationSeconds;
        liveClip.SpeedCurvePreset = preset;
        liveClip.SpeedCurvePoints = SpeedCurveMath.CreatePreset(preset, liveClip.SourceTrimInSeconds, liveClip.SourceTrimOutSeconds);
        if (preset != SpeedCurvePreset.None)
        {
            // Preset points own the timing while active; 1x is the deterministic fallback at malformed edges.
            liveClip.SpeedMultiplier = 1.0;
        }
        RescaleKeyframesForDurationChange(liveClip, previousTimelineDuration);
    }

    public bool SetClipStabilization(string clipId, bool enabled, int smoothingFrames, int accuracy, double zoomPercent)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null || clip.MediaAssetId is null)
        {
            return false;
        }

        if (enabled && (clip.IsReversed || clip.IsFreezeFrame))
        {
            return false;
        }

        var smoothing = Math.Clamp(smoothingFrames, 0, 120);
        var clampedAccuracy = Math.Clamp(accuracy, 1, 15);
        var zoom = Math.Clamp(zoomPercent, 0, 30);
        if (clip.StabilizationEnabled == enabled &&
            clip.StabilizationSmoothing == smoothing &&
            clip.StabilizationAccuracy == clampedAccuracy &&
            Math.Abs(clip.StabilizationZoomPercent - zoom) < 1e-9)
        {
            return true;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        liveClip.StabilizationEnabled = enabled;
        liveClip.StabilizationSmoothing = smoothing;
        liveClip.StabilizationAccuracy = clampedAccuracy;
        liveClip.StabilizationZoomPercent = zoom;
        return true;
    }

    public void SetMotionTrackingRegion(string clipId, MotionTrackingRegion region)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null || clip.MediaAssetId is null) return;
        var clamped = region.Clamp();
        if (Math.Abs(clip.TrackingRegionCenterX - clamped.CenterX) < 1e-9 &&
            Math.Abs(clip.TrackingRegionCenterY - clamped.CenterY) < 1e-9 &&
            Math.Abs(clip.TrackingRegionWidth - clamped.Width) < 1e-9 &&
            Math.Abs(clip.TrackingRegionHeight - clamped.Height) < 1e-9) return;

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        liveClip.TrackingRegionCenterX = clamped.CenterX;
        liveClip.TrackingRegionCenterY = clamped.CenterY;
        liveClip.TrackingRegionWidth = clamped.Width;
        liveClip.TrackingRegionHeight = clamped.Height;
        // A changed starting box invalidates an old path; never silently render stale tracking data.
        liveClip.MotionTrackingPoints.Clear();
        liveClip.AutoReframeEnabled = false;
    }

    public bool ApplyMotionTrackingResult(string clipId, MotionTrackingRegion region, IReadOnlyList<MotionTrackingPoint> points)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null || clip.MediaAssetId is null || clip.IsReversed || clip.IsFreezeFrame || points.Count < 2)
            return false;

        var ordered = points
            .Where(p => p.SourceTimeSeconds >= clip.SourceTrimInSeconds - 0.01 &&
                        p.SourceTimeSeconds <= clip.SourceTrimOutSeconds + 0.01)
            .OrderBy(p => p.SourceTimeSeconds)
            .Select(CloneTrackingPoint)
            .ToList();
        if (ordered.Count < 2) return false;
        const double endpointToleranceSeconds = 0.05;
        if (ordered[0].SourceTimeSeconds > clip.SourceTrimInSeconds + endpointToleranceSeconds ||
            ordered[^1].SourceTimeSeconds < clip.SourceTrimOutSeconds - endpointToleranceSeconds)
        {
            return false;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        var clamped = region.Clamp();
        liveClip.TrackingRegionCenterX = clamped.CenterX;
        liveClip.TrackingRegionCenterY = clamped.CenterY;
        liveClip.TrackingRegionWidth = clamped.Width;
        liveClip.TrackingRegionHeight = clamped.Height;
        liveClip.MotionTrackingPoints = ordered;
        liveClip.AutoReframeEnabled = true;
        return true;
    }

    public void SetAutoReframeEnabled(string clipId, bool enabled)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null || clip.AutoReframeEnabled == enabled) return;
        if (enabled && (clip.IsReversed || clip.IsFreezeFrame || clip.MotionTrackingPoints.Count < 2 ||
            clip.MotionTrackingPoints.Min(p => p.SourceTimeSeconds) > clip.SourceTrimInSeconds + 0.05 ||
            clip.MotionTrackingPoints.Max(p => p.SourceTimeSeconds) < clip.SourceTrimOutSeconds - 0.05)) return;
        SaveSnapshot();
        FindClipWithTrack(clipId).Clip!.AutoReframeEnabled = enabled;
    }

    private static MotionTrackingPoint CloneTrackingPoint(MotionTrackingPoint point) => new()
    {
        SourceTimeSeconds = point.SourceTimeSeconds,
        CenterX = Math.Clamp(point.CenterX, 0, 1),
        CenterY = Math.Clamp(point.CenterY, 0, 1),
        Width = Math.Clamp(point.Width, 0.001, 1),
        Height = Math.Clamp(point.Height, 0.001, 1),
        Confidence = Math.Clamp(point.Confidence, 0, 1)
    };

    public void SetClipTransform(string clipId, ClipTransformSettings settings)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        var previousTimelineDuration = liveClip.TimelineDurationSeconds;
        liveClip.RotationDegrees = Math.Clamp(settings.RotationDegrees, -360, 360);
        liveClip.FlipHorizontal = settings.FlipHorizontal;
        liveClip.FlipVertical = settings.FlipVertical;
        liveClip.CropLeftPercent = Math.Clamp(settings.CropLeftPercent, 0, 45);
        liveClip.CropTopPercent = Math.Clamp(settings.CropTopPercent, 0, 45);
        liveClip.CropRightPercent = Math.Clamp(settings.CropRightPercent, 0, 45);
        liveClip.CropBottomPercent = Math.Clamp(settings.CropBottomPercent, 0, 45);
        liveClip.IsReversed = settings.IsReversed;
        liveClip.IsFreezeFrame = settings.IsFreezeFrame;
        if ((liveClip.IsReversed || liveClip.IsFreezeFrame) && SpeedCurveMath.HasCurve(liveClip))
        {
            // v1 does not silently fake reverse/freeze + velocity interaction. Switching either on returns
            // the clip to deterministic constant timing; the user can reapply a curve after disabling it.
            liveClip.SpeedCurvePreset = SpeedCurvePreset.None;
            liveClip.SpeedCurvePoints.Clear();
        }
        if (liveClip.IsReversed || liveClip.IsFreezeFrame)
        {
            // libvidstab vectors and source-time tracking paths both describe forward-moving source frames.
            // Keep authored tracking points for later, but disable consumers that would otherwise interpret
            // them on the wrong temporal axis.
            liveClip.StabilizationEnabled = false;
            liveClip.AutoReframeEnabled = false;
        }
        liveClip.ChromaKeyEnabled = settings.ChromaKeyEnabled;
        liveClip.ChromaKeyColor = string.IsNullOrWhiteSpace(settings.ChromaKeyColor) ? "#00FF00" : settings.ChromaKeyColor;
        liveClip.ChromaKeySimilarity = Math.Clamp(settings.ChromaKeySimilarity, 0.01, 1.0);
        liveClip.ChromaKeyBlend = Math.Clamp(settings.ChromaKeyBlend, 0, 1.0);
        RescaleKeyframesForDurationChange(liveClip, previousTimelineDuration);
    }
    public void SetClipCompositing(string clipId, ClipCompositingSettings settings)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        liveClip.MaskType = settings.MaskType;
        liveClip.MaskCenterXPercent = Math.Clamp(settings.MaskCenterXPercent, 0, 100);
        liveClip.MaskCenterYPercent = Math.Clamp(settings.MaskCenterYPercent, 0, 100);
        liveClip.MaskWidthPercent = Math.Clamp(settings.MaskWidthPercent, 1, 100);
        liveClip.MaskHeightPercent = Math.Clamp(settings.MaskHeightPercent, 1, 100);
        liveClip.MaskFeatherPercent = Math.Clamp(settings.MaskFeatherPercent, 0, 50);
        liveClip.MaskRotationDegrees = Math.Clamp(settings.MaskRotationDegrees, -180, 180);
        liveClip.MaskInvert = settings.MaskInvert;
        liveClip.BlendMode = settings.BlendMode;
    }
    public void SetClipMute(string clipId, bool muted)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null || clip.IsMuted == muted)
        {
            return;
        }

        SaveSnapshot();
        FindClipWithTrack(clipId).Clip!.IsMuted = muted;
    }

    public void SetClipVolume(string clipId, double volume)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        var clamped = Math.Clamp(volume, 0, 2.0);
        if (clip is null || Math.Abs(clip.Volume - clamped) < 1e-9)
        {
            return;
        }

        SaveSnapshot();
        FindClipWithTrack(clipId).Clip!.Volume = clamped;
    }

    public void SetTextStyle(string clipId, CaptionFontChoice fontChoice, int fontSizePx, string textColor, CaptionTextPosition position)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        liveClip.FontChoice = fontChoice;
        liveClip.FontSizePx = Math.Clamp(fontSizePx, 8, 200);
        liveClip.TextColor = textColor;
        liveClip.TextPosition = position;
    }

    /// <summary>Selects either a legacy preset or a real installed font file. This is its own undo step
    /// and deliberately keeps family + path so projects can be moved to another Windows installation.</summary>
    public void SetTextFont(string clipId, CaptionFontChoice legacyChoice, string? installedFamilyName, string? installedFilePath)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null || clip.TextContent is null)
        {
            return;
        }

        var family = string.IsNullOrWhiteSpace(installedFamilyName) ? null : installedFamilyName.Trim();
        var path = string.IsNullOrWhiteSpace(installedFilePath) ? null : installedFilePath.Trim();
        if (clip.FontChoice == legacyChoice &&
            string.Equals(clip.TextFontFamilyName, family, StringComparison.Ordinal) &&
            string.Equals(clip.TextFontFilePath, path, StringComparison.Ordinal))
        {
            return;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        liveClip.FontChoice = legacyChoice;
        liveClip.TextFontFamilyName = family;
        liveClip.TextFontFilePath = path;
    }

    /// <summary>The extra text style knobs beyond font/size/color/position - outline, shadow, background
    /// on/off/color/opacity, horizontal alignment, bold/italic, case transform, line spacing.</summary>
    public void SetTextAdvancedStyle(string clipId, TextAdvancedStyle style)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        liveClip.TextOutlineColor = style.OutlineColor;
        liveClip.TextOutlineWidthPx = Math.Max(0, style.OutlineWidthPx);
        liveClip.TextShadowColor = style.ShadowColor;
        liveClip.TextShadowOffsetPx = Math.Max(0, style.ShadowOffsetPx);
        liveClip.HasTextBackground = style.HasBackground;
        liveClip.TextBackgroundColor = style.BackgroundColor;
        liveClip.TextBackgroundOpacity = Math.Clamp(style.BackgroundOpacity, 0, 1);
        liveClip.TextHorizontalAlign = style.HorizontalAlign;
        liveClip.IsTextBold = style.IsBold;
        liveClip.IsTextItalic = style.IsItalic;
        liveClip.TextCase = style.TextCase;
        liveClip.LineSpacingPx = Math.Max(0, style.LineSpacingPx);
    }

    /// <summary>
    /// Applies the renderable part of a caption-style gallery preset to one real Caption/Text clip in a
    /// single undo step. The gallery catalog also describes granularity and named animation ideas; those
    /// are intentionally not faked here. This method changes only fields that the current FFmpeg renderer
    /// actually consumes: text color, outline/shadow and optional panel background.
    /// </summary>
    public bool ApplyCaptionStylePreset(string clipId, CaptionStylePreset preset)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null || clip.TextContent is null)
        {
            return false;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        liveClip.TextColor = preset.TextColorHex;
        liveClip.CaptionAnimation = preset.Animation;
        liveClip.CaptionGranularity = preset.Granularity;
        liveClip.CaptionAccentColor = preset.AccentColorHex;
        liveClip.TextOutlineWidthPx = Math.Max(2, liveClip.TextOutlineWidthPx);
        liveClip.TextShadowOffsetPx = Math.Max(2, liveClip.TextShadowOffsetPx);

        if (preset.Animation == CaptionAnimationKind.Shadow)
        {
            liveClip.TextOutlineColor = null;
            liveClip.TextShadowColor = preset.OutlineOrShadowColorHex;
        }
        else if (preset.Animation == CaptionAnimationKind.Glow)
        {
            liveClip.TextOutlineColor = preset.OutlineOrShadowColorHex;
            liveClip.TextShadowColor = preset.AccentColorHex;
        }
        else
        {
            // Outline is also the safe static fallback for Glow/Pop/Slide/etc. until their temporal
            // animation engines exist; unlike the old gallery this still produces a visible exported change.
            liveClip.TextOutlineColor = preset.OutlineOrShadowColorHex;
            liveClip.TextShadowColor = null;
        }

        if (!string.IsNullOrWhiteSpace(preset.PanelColorHex))
        {
            liveClip.HasTextBackground = true;
            var panel = preset.PanelColorHex!;
            if (panel.Length == 9 && panel[0] == '#')
            {
                // Avalonia catalog colors use #AARRGGBB. FFmpeg drawtext expects RGB plus a separate
                // opacity, so split the alpha instead of passing an invalid 8-digit color through.
                liveClip.TextBackgroundOpacity = Math.Clamp(Convert.ToInt32(panel.Substring(1, 2), 16) / 255.0, 0, 1);
                liveClip.TextBackgroundColor = "#" + panel.Substring(3, 6);
            }
            else
            {
                liveClip.TextBackgroundColor = panel;
                liveClip.TextBackgroundOpacity = Math.Clamp(liveClip.TextBackgroundOpacity, 0.15, 1);
            }
        }
        else
        {
            liveClip.HasTextBackground = false;
        }

        return true;
    }

    /// <summary>
    /// "Primeni na sve titlove na ovoj traci" - copies every text style field (font/size/color/position
    /// plus everything <see cref="SetTextAdvancedStyle"/> covers) from one clip onto every other Caption/
    /// Text clip on the same track, so styling a batch of auto-generated captions doesn't mean re-clicking
    /// the same font/size/color/outline/etc. on every single clip by hand. Never touches
    /// <see cref="TimelineClip.TextContent"/> itself - only the styling around it.
    /// </summary>
    public void ApplyTextStyleToAllClipsOnTrack(string trackId, string sourceClipId)
    {
        var track = FindTrack(trackId);
        var source = track?.Clips.FirstOrDefault(c => c.Id == sourceClipId);
        if (track is null || source is null || source.TextContent is null)
        {
            return;
        }

        var targets = track.Clips.Where(c => c.Id != sourceClipId && c.TextContent is not null).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        SaveSnapshot();
        foreach (var target in track.Clips.Where(c => c.Id != sourceClipId && c.TextContent is not null))
        {
            target.FontChoice = source.FontChoice;
            target.TextFontFamilyName = source.TextFontFamilyName;
            target.TextFontFilePath = source.TextFontFilePath;
            target.FontSizePx = source.FontSizePx;
            target.TextColor = source.TextColor;
            target.TextPosition = source.TextPosition;
            target.TextHorizontalAlign = source.TextHorizontalAlign;
            target.TextOutlineColor = source.TextOutlineColor;
            target.TextOutlineWidthPx = source.TextOutlineWidthPx;
            target.TextShadowColor = source.TextShadowColor;
            target.TextShadowOffsetPx = source.TextShadowOffsetPx;
            target.HasTextBackground = source.HasTextBackground;
            target.TextBackgroundColor = source.TextBackgroundColor;
            target.TextBackgroundOpacity = source.TextBackgroundOpacity;
            target.IsTextBold = source.IsTextBold;
            target.IsTextItalic = source.IsTextItalic;
            target.TextCase = source.TextCase;
            target.LineSpacingPx = source.LineSpacingPx;
            target.CaptionAnimation = source.CaptionAnimation;
            target.CaptionGranularity = source.CaptionGranularity;
            target.CaptionAccentColor = source.CaptionAccentColor;
        }
    }

    private static void SplitKeyframesAt(TimelineClip first, TimelineClip second, double splitSeconds)
    {
        if (first.Keyframes.Count == 0)
        {
            return;
        }

        var original = first.Keyframes.Select(CloneKeyframe).ToList();
        var left = original.Where(k => k.TimeSeconds < splitSeconds - 0.001).Select(CloneKeyframe).ToList();
        var right = original.Where(k => k.TimeSeconds > splitSeconds + 0.001).Select(k =>
        {
            var clone = CloneKeyframe(k);
            clone.TimeSeconds -= splitSeconds;
            return clone;
        }).ToList();

        foreach (var property in original.Select(k => k.Property).Distinct())
        {
            var fallback = ClipKeyframeEvaluator.StaticValue(first, property);
            var boundary = ClipKeyframeEvaluator.Evaluate(original, property, splitSeconds, fallback);
            var easingIntoBoundary = original
                .Where(k => k.Property == property && k.TimeSeconds >= splitSeconds)
                .OrderBy(k => k.TimeSeconds)
                .Select(k => k.Easing)
                .FirstOrDefault();

            left.Add(new ClipKeyframe
            {
                Property = property,
                TimeSeconds = splitSeconds,
                Value = boundary,
                Easing = easingIntoBoundary
            });
            right.Add(new ClipKeyframe
            {
                Property = property,
                TimeSeconds = 0,
                Value = boundary,
                Easing = ClipKeyframeEasing.Linear
            });
        }

        first.Keyframes = left.OrderBy(k => k.Property).ThenBy(k => k.TimeSeconds).ToList();
        second.Keyframes = right.OrderBy(k => k.Property).ThenBy(k => k.TimeSeconds).ToList();
    }

    private static void TrimKeyframesAtStart(TimelineClip clip, double timelineDelta)
    {
        if (clip.Keyframes.Count == 0 || Math.Abs(timelineDelta) <= 1e-9)
        {
            return;
        }

        if (timelineDelta < 0)
        {
            foreach (var keyframe in clip.Keyframes)
            {
                keyframe.TimeSeconds -= timelineDelta;
            }
            return;
        }

        var original = clip.Keyframes.Select(CloneKeyframe).ToList();
        var shifted = original.Where(k => k.TimeSeconds > timelineDelta + 0.001).Select(k =>
        {
            var clone = CloneKeyframe(k);
            clone.TimeSeconds -= timelineDelta;
            return clone;
        }).ToList();

        foreach (var property in original.Select(k => k.Property).Distinct())
        {
            shifted.Add(new ClipKeyframe
            {
                Property = property,
                TimeSeconds = 0,
                Value = ClipKeyframeEvaluator.Evaluate(original, property, timelineDelta, ClipKeyframeEvaluator.StaticValue(clip, property)),
                Easing = ClipKeyframeEasing.Linear
            });
        }

        clip.Keyframes = shifted.OrderBy(k => k.Property).ThenBy(k => k.TimeSeconds).ToList();
    }

    private static void TrimKeyframesAtEnd(TimelineClip clip, double newDuration)
    {
        if (clip.Keyframes.Count == 0)
        {
            return;
        }

        var original = clip.Keyframes.Select(CloneKeyframe).ToList();
        var kept = original.Where(k => k.TimeSeconds < newDuration - 0.001).Select(CloneKeyframe).ToList();
        foreach (var property in original.Select(k => k.Property).Distinct())
        {
            kept.Add(new ClipKeyframe
            {
                Property = property,
                TimeSeconds = newDuration,
                Value = ClipKeyframeEvaluator.Evaluate(original, property, newDuration, ClipKeyframeEvaluator.StaticValue(clip, property)),
                Easing = original.Where(k => k.Property == property && k.TimeSeconds >= newDuration)
                    .OrderBy(k => k.TimeSeconds)
                    .Select(k => k.Easing)
                    .FirstOrDefault()
            });
        }

        clip.Keyframes = kept.OrderBy(k => k.Property).ThenBy(k => k.TimeSeconds).ToList();
    }

    private static void RescaleKeyframesForDurationChange(TimelineClip clip, double previousDuration)
    {
        var duration = Math.Max(0, clip.TimelineDurationSeconds);
        if (clip.Keyframes.Count == 0)
        {
            return;
        }

        if (previousDuration <= 1e-9)
        {
            foreach (var keyframe in clip.Keyframes)
            {
                keyframe.TimeSeconds = Math.Clamp(keyframe.TimeSeconds, 0, duration);
            }
        }
        else
        {
            var timeScale = duration / previousDuration;
            foreach (var keyframe in clip.Keyframes)
            {
                keyframe.TimeSeconds = Math.Clamp(keyframe.TimeSeconds * timeScale, 0, duration);
            }
        }

        clip.Keyframes = clip.Keyframes.OrderBy(k => k.Property).ThenBy(k => k.TimeSeconds).ToList();
    }

    private static ClipKeyframe CloneKeyframe(ClipKeyframe keyframe) => new()
    {
        Id = keyframe.Id,
        Property = keyframe.Property,
        TimeSeconds = keyframe.TimeSeconds,
        Value = keyframe.Value,
        Easing = keyframe.Easing
    };

    public void SetTrackLocked(string trackId, bool locked) => SetTrackFlag(trackId, t => t.IsLocked, (t, v) => t.IsLocked = v, locked);
    public void SetTrackHidden(string trackId, bool hidden) => SetTrackFlag(trackId, t => t.IsHidden, (t, v) => t.IsHidden = v, hidden);
    public void SetTrackMuted(string trackId, bool muted) => SetTrackFlag(trackId, t => t.IsMuted, (t, v) => t.IsMuted = v, muted);
    public void SetTrackSolo(string trackId, bool solo) => SetTrackFlag(trackId, t => t.IsSolo, (t, v) => t.IsSolo = v, solo);

    public void SetTrackVolume(string trackId, double volume)
    {
        var track = FindTrack(trackId);
        var clamped = Math.Clamp(volume, 0, 2.0);
        if (track is null || Math.Abs(track.Volume - clamped) < 1e-9)
        {
            return;
        }

        SaveSnapshot();
        FindTrack(trackId)!.Volume = clamped;
    }

    /// <summary>Returns the nearest value in <paramref name="candidates"/> within <paramref name="thresholdSeconds"/>, or the original position if nothing is close enough (spec's "snap").</summary>
    public static double SnapToNearest(double seconds, IEnumerable<double> candidates, double thresholdSeconds)
    {
        var best = seconds;
        var bestDelta = thresholdSeconds;
        foreach (var candidate in candidates)
        {
            var delta = Math.Abs(candidate - seconds);
            if (delta <= bestDelta)
            {
                bestDelta = delta;
                best = candidate;
            }
        }

        return best;
    }

    private void SetTrackFlag(string trackId, Func<TimelineTrack, bool> getter, Action<TimelineTrack, bool> setter, bool value)
    {
        var track = FindTrack(trackId);
        if (track is null || getter(track) == value)
        {
            return;
        }

        SaveSnapshot();
        setter(FindTrack(trackId)!, value);
    }

    private TimelineTrack? FindTrack(string trackId) => _tracks.FirstOrDefault(t => t.Id == trackId);

    private (TimelineTrack? Track, TimelineClip? Clip) FindClipWithTrack(string clipId)
    {
        foreach (var track in _tracks)
        {
            var clip = track.Clips.FirstOrDefault(c => c.Id == clipId);
            if (clip is not null)
            {
                return (track, clip);
            }
        }

        return (null, null);
    }

    private void SaveSnapshot()
    {
        _undoStack.Add(CloneTracks(_tracks));
        _redoStack.Clear();
    }

    private static List<TimelineTrack> CloneTracks(IEnumerable<TimelineTrack> tracks) => tracks.Select(Clone).ToList();

    private static TimelineTrack Clone(TimelineTrack track) => new()
    {
        Id = track.Id,
        Kind = track.Kind,
        Name = track.Name,
        Clips = track.Clips.Select(Clone).ToList(),
        IsLocked = track.IsLocked,
        IsHidden = track.IsHidden,
        IsMuted = track.IsMuted,
        IsSolo = track.IsSolo,
        Volume = track.Volume
    };

    /// <summary>
    /// Real bug found and fixed while adding <see cref="ApplyTextStyleToAllClipsOnTrack"/>'s test: this
    /// explicit field list had silently fallen behind <see cref="TimelineClip"/> itself over several past
    /// sessions - FontChoice/FontSizePx/TextColor/TextPosition/TransitionInType/TransitionInDurationSeconds
    /// were all missing, meaning every undo snapshot (every <see cref="SaveSnapshot"/> call, i.e. every
    /// single edit) silently reset a clip's text style and transition back to their type defaults. Existing
    /// tests never caught this because they all happened to undo from a non-default style back to the
    /// still-default starting state, where the bug is invisible - a real style-A -> style-B -> undo
    /// sequence would have incorrectly landed on hardcoded defaults instead of style A. Every field on
    /// <see cref="TimelineClip"/> is now listed explicitly here on purpose (no reflection/serialization
    /// shortcut) so a future field addition has to be a deliberate, visible one-line change instead of a
    /// silent gap like this one.
    /// </summary>
    private static TimelineClip Clone(TimelineClip clip) => new()
    {
        Id = clip.Id,
        MediaAssetId = clip.MediaAssetId,
        TextContent = clip.TextContent,
        FontChoice = clip.FontChoice,
        TextFontFamilyName = clip.TextFontFamilyName,
        TextFontFilePath = clip.TextFontFilePath,
        FontSizePx = clip.FontSizePx,
        TextColor = clip.TextColor,
        TextPosition = clip.TextPosition,
        TextHorizontalAlign = clip.TextHorizontalAlign,
        TextOutlineColor = clip.TextOutlineColor,
        TextOutlineWidthPx = clip.TextOutlineWidthPx,
        TextShadowColor = clip.TextShadowColor,
        TextShadowOffsetPx = clip.TextShadowOffsetPx,
        HasTextBackground = clip.HasTextBackground,
        TextBackgroundColor = clip.TextBackgroundColor,
        TextBackgroundOpacity = clip.TextBackgroundOpacity,
        IsTextBold = clip.IsTextBold,
        IsTextItalic = clip.IsTextItalic,
        TextCase = clip.TextCase,
        LineSpacingPx = clip.LineSpacingPx,
        CaptionAnimation = clip.CaptionAnimation,
        CaptionGranularity = clip.CaptionGranularity,
        CaptionAccentColor = clip.CaptionAccentColor,
        SourceTrimInSeconds = clip.SourceTrimInSeconds,
        SourceTrimOutSeconds = clip.SourceTrimOutSeconds,
        TimelineStartSeconds = clip.TimelineStartSeconds,
        FadeInSeconds = clip.FadeInSeconds,
        FadeOutSeconds = clip.FadeOutSeconds,
        TransitionInType = clip.TransitionInType,
        TransitionInDurationSeconds = clip.TransitionInDurationSeconds,
        IsMuted = clip.IsMuted,
        Volume = clip.Volume,
        AudioNoiseReductionEnabled = clip.AudioNoiseReductionEnabled,
        AudioNoiseReductionStrength = clip.AudioNoiseReductionStrength,
        AudioEnhanceVoiceEnabled = clip.AudioEnhanceVoiceEnabled,
        AudioLoudnessNormalizationEnabled = clip.AudioLoudnessNormalizationEnabled,
        ScalePercent = clip.ScalePercent,
        PositionXPercent = clip.PositionXPercent,
        PositionYPercent = clip.PositionYPercent,
        Opacity = clip.Opacity,
        Effect = clip.Effect,
        Brightness = clip.Brightness,
        Contrast = clip.Contrast,
        Saturation = clip.Saturation,
        ExposureStops = clip.ExposureStops,
        Highlights = clip.Highlights,
        Shadows = clip.Shadows,
        Temperature = clip.Temperature,
        Tint = clip.Tint,
        HueDegrees = clip.HueDegrees,
        Vibrance = clip.Vibrance,
        ShadowRed = clip.ShadowRed,
        ShadowGreen = clip.ShadowGreen,
        ShadowBlue = clip.ShadowBlue,
        MidtoneRed = clip.MidtoneRed,
        MidtoneGreen = clip.MidtoneGreen,
        MidtoneBlue = clip.MidtoneBlue,
        HighlightRed = clip.HighlightRed,
        HighlightGreen = clip.HighlightGreen,
        HighlightBlue = clip.HighlightBlue,
        LutFilePath = clip.LutFilePath,
        SpeedMultiplier = clip.SpeedMultiplier,
        SpeedCurvePreset = clip.SpeedCurvePreset,
        SpeedCurvePoints = clip.SpeedCurvePoints.Select(point => new SpeedCurvePoint
        {
            Id = point.Id,
            SourceTimeSeconds = point.SourceTimeSeconds,
            SpeedMultiplier = point.SpeedMultiplier
        }).ToList(),
        StabilizationEnabled = clip.StabilizationEnabled,
        StabilizationShakiness = clip.StabilizationShakiness,
        StabilizationAccuracy = clip.StabilizationAccuracy,
        StabilizationSmoothing = clip.StabilizationSmoothing,
        StabilizationZoomPercent = clip.StabilizationZoomPercent,
        StabilizationOptimalZoom = clip.StabilizationOptimalZoom,
        TrackingRegionCenterX = clip.TrackingRegionCenterX,
        TrackingRegionCenterY = clip.TrackingRegionCenterY,
        TrackingRegionWidth = clip.TrackingRegionWidth,
        TrackingRegionHeight = clip.TrackingRegionHeight,
        MotionTrackingPoints = clip.MotionTrackingPoints.Select(CloneTrackingPoint).ToList(),
        AutoReframeEnabled = clip.AutoReframeEnabled,
        RotationDegrees = clip.RotationDegrees,
        FlipHorizontal = clip.FlipHorizontal,
        FlipVertical = clip.FlipVertical,
        CropLeftPercent = clip.CropLeftPercent,
        CropTopPercent = clip.CropTopPercent,
        CropRightPercent = clip.CropRightPercent,
        CropBottomPercent = clip.CropBottomPercent,
        IsReversed = clip.IsReversed,
        IsFreezeFrame = clip.IsFreezeFrame,
        ChromaKeyEnabled = clip.ChromaKeyEnabled,
        ChromaKeyColor = clip.ChromaKeyColor,
        ChromaKeySimilarity = clip.ChromaKeySimilarity,
        ChromaKeyBlend = clip.ChromaKeyBlend,
        MaskType = clip.MaskType,
        MaskCenterXPercent = clip.MaskCenterXPercent,
        MaskCenterYPercent = clip.MaskCenterYPercent,
        MaskWidthPercent = clip.MaskWidthPercent,
        MaskHeightPercent = clip.MaskHeightPercent,
        MaskFeatherPercent = clip.MaskFeatherPercent,
        MaskRotationDegrees = clip.MaskRotationDegrees,
        MaskInvert = clip.MaskInvert,
        BlendMode = clip.BlendMode,
        Keyframes = clip.Keyframes.Select(CloneKeyframe).ToList()
    };
}
