$ErrorActionPreference = 'Stop'

function Replace-Exact([string]$Path, [string]$Old, [string]$New) {
    $text = [IO.File]::ReadAllText($Path)
    if (-not $text.Contains($Old)) { throw "Pattern not found in $Path`n---`n$Old" }
    $text = $text.Replace($Old, $New)
    [IO.File]::WriteAllText($Path, $text, [Text.UTF8Encoding]::new($false))
}

$timeline = 'src/NPVideoStudio.Domain/Timeline.cs'
Replace-Exact $timeline @'
    public double TimelineStartSeconds { get; set; }
    public double TimelineDurationSeconds => Math.Max(0, SourceTrimOutSeconds - SourceTrimInSeconds) / (IsFreezeFrame ? 1.0 : Math.Clamp(SpeedMultiplier, 0.25, 4));
    public double TimelineEndSeconds => TimelineStartSeconds + TimelineDurationSeconds;
'@ @'
    public double TimelineStartSeconds { get; set; }
    public double TimelineDurationSeconds => IsFreezeFrame
        ? Math.Max(0, SourceTrimOutSeconds - SourceTrimInSeconds)
        : SpeedCurveMath.OutputDuration(this);
    public double TimelineEndSeconds => TimelineStartSeconds + TimelineDurationSeconds;
'@

Replace-Exact $timeline @'
    /// <summary>Playback speed, 0.25..4. 1 = normal, 0.5 = slow motion, 2 = double speed.</summary>
    public double SpeedMultiplier { get; set; } = 1.0;
'@ @'
    /// <summary>Playback speed, 0.25..4. Used when no velocity curve is active.</summary>
    public double SpeedMultiplier { get; set; } = 1.0;

    /// <summary>Optional CapCut-style variable velocity preset. None keeps constant speed.</summary>
    public SpeedCurvePreset SpeedCurvePreset { get; set; } = SpeedCurvePreset.None;

    /// <summary>Absolute source-time control points for the active velocity curve.</summary>
    public List<SpeedCurvePoint> SpeedCurvePoints { get; set; } = new();
'@

$session = 'src/NPVideoStudio.AI/TimelineEditSession.cs'
Replace-Exact $session @'
        var offsetIntoClip = atTimelineSeconds - clip.TimelineStartSeconds;
        var splitSourcePoint = clip.SourceTrimInSeconds + offsetIntoClip * (clip.IsFreezeFrame ? 1.0 : Math.Clamp(clip.SpeedMultiplier, 0.25, 4));
'@ @'
        var offsetIntoClip = atTimelineSeconds - clip.TimelineStartSeconds;
        var splitSourcePoint = clip.IsFreezeFrame
            ? clip.SourceTrimInSeconds + offsetIntoClip
            : SpeedCurveMath.SourceTimeAtTimelineOffset(clip, offsetIntoClip);
'@

Replace-Exact $session @'
        SaveSnapshot();
        var (_, liveClip) = FindClipWithTrack(clipId);
        var timelineDelta = delta / (liveClip!.IsFreezeFrame ? 1.0 : Math.Clamp(liveClip.SpeedMultiplier, 0.25, 4));
        TrimKeyframesAtStart(liveClip, timelineDelta);
        liveClip.SourceTrimInSeconds = clamped;
        liveClip.TimelineStartSeconds = Math.Max(0, liveClip.TimelineStartSeconds + timelineDelta);
'@ @'
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
'@

Replace-Exact $session @'
        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        var speed = liveClip.IsFreezeFrame ? 1.0 : Math.Clamp(liveClip.SpeedMultiplier, 0.25, 4);
        var newDuration = Math.Max(0, clamped - liveClip.SourceTrimInSeconds) / speed;
        TrimKeyframesAtEnd(liveClip, newDuration);
        liveClip.SourceTrimOutSeconds = clamped;
'@ @'
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
'@

Replace-Exact $session @'
        liveClip.Effect = effect;
        liveClip.Brightness = Math.Clamp(brightness, -1, 1);
        liveClip.Contrast = Math.Clamp(contrast, 0, 3);
        liveClip.Saturation = Math.Clamp(saturation, 0, 3);
        liveClip.SpeedMultiplier = Math.Clamp(speed, 0.25, 4);
        RescaleKeyframesForDurationChange(liveClip, previousTimelineDuration);
    }

    public void SetClipTransform(string clipId, ClipTransformSettings settings)
'@ @'
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

    public void SetClipTransform(string clipId, ClipTransformSettings settings)
'@

Replace-Exact $session @'
        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        liveClip.RotationDegrees = Math.Clamp(settings.RotationDegrees, -360, 360);
'@ @'
        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        var previousTimelineDuration = liveClip.TimelineDurationSeconds;
        liveClip.RotationDegrees = Math.Clamp(settings.RotationDegrees, -360, 360);
'@

Replace-Exact $session @'
        liveClip.IsReversed = settings.IsReversed;
        liveClip.IsFreezeFrame = settings.IsFreezeFrame;
        liveClip.ChromaKeyEnabled = settings.ChromaKeyEnabled;
        liveClip.ChromaKeyColor = string.IsNullOrWhiteSpace(settings.ChromaKeyColor) ? "#00FF00" : settings.ChromaKeyColor;
        liveClip.ChromaKeySimilarity = Math.Clamp(settings.ChromaKeySimilarity, 0.01, 1.0);
        liveClip.ChromaKeyBlend = Math.Clamp(settings.ChromaKeyBlend, 0, 1.0);
    }
'@ @'
        liveClip.IsReversed = settings.IsReversed;
        liveClip.IsFreezeFrame = settings.IsFreezeFrame;
        if ((liveClip.IsReversed || liveClip.IsFreezeFrame) && SpeedCurveMath.HasCurve(liveClip))
        {
            // v1 does not silently fake reverse/freeze + velocity interaction. Switching either on returns
            // the clip to deterministic constant timing; the user can reapply a curve after disabling it.
            liveClip.SpeedCurvePreset = SpeedCurvePreset.None;
            liveClip.SpeedCurvePoints.Clear();
        }
        liveClip.ChromaKeyEnabled = settings.ChromaKeyEnabled;
        liveClip.ChromaKeyColor = string.IsNullOrWhiteSpace(settings.ChromaKeyColor) ? "#00FF00" : settings.ChromaKeyColor;
        liveClip.ChromaKeySimilarity = Math.Clamp(settings.ChromaKeySimilarity, 0.01, 1.0);
        liveClip.ChromaKeyBlend = Math.Clamp(settings.ChromaKeyBlend, 0, 1.0);
        RescaleKeyframesForDurationChange(liveClip, previousTimelineDuration);
    }
'@

Replace-Exact $session @'
        Saturation = clip.Saturation,
        SpeedMultiplier = clip.SpeedMultiplier,
        RotationDegrees = clip.RotationDegrees,
'@ @'
        Saturation = clip.Saturation,
        SpeedMultiplier = clip.SpeedMultiplier,
        SpeedCurvePreset = clip.SpeedCurvePreset,
        SpeedCurvePoints = clip.SpeedCurvePoints.Select(point => new SpeedCurvePoint
        {
            Id = point.Id,
            SourceTimeSeconds = point.SourceTimeSeconds,
            SpeedMultiplier = point.SpeedMultiplier
        }).ToList(),
        RotationDegrees = clip.RotationDegrees,
'@

# This helper is intentionally temporary and must not enter the feature diff.
Remove-Item '.github/scripts/materialize-speed-v2.ps1' -Force
Remove-Item '.github/workflows/materialize-speed-v2.yml' -Force

git config user.name 'github-actions[bot]'
git config user.email '41898282+github-actions[bot]@users.noreply.github.com'
git add src/NPVideoStudio.Domain/Timeline.cs src/NPVideoStudio.AI/TimelineEditSession.cs .github/scripts/materialize-speed-v2.ps1 .github/workflows/materialize-speed-v2.yml
git commit -m 'Integrate velocity curves with timeline editing'
git push origin HEAD:agent/velocity-speed-curves-v2
