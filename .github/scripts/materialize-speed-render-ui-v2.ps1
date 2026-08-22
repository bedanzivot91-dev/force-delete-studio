$ErrorActionPreference = 'Stop'
function Replace-Exact([string]$Path, [string]$Old, [string]$New) {
    $text = [IO.File]::ReadAllText($Path)
    if (-not $text.Contains($Old)) { throw "Pattern not found in $Path`n---`n$Old" }
    [IO.File]::WriteAllText($Path, $text.Replace($Old, $New), [Text.UTF8Encoding]::new($false))
}

$renderer = 'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs'
Replace-Exact $renderer @'
                var sourceRate = clip.IsFreezeFrame ? 1.0 : Math.Clamp(clip.SpeedMultiplier, 0.25, 4);
                if (clip.IsFreezeFrame)
                {
                    var visibleDuration = Math.Max(0.05, overlapEnd - overlapStart);
                    if (clip.IsReversed)
                    {
                        newClip.SourceTrimOutSeconds = clip.SourceTrimOutSeconds;
                        newClip.SourceTrimInSeconds = Math.Max(clip.SourceTrimInSeconds, clip.SourceTrimOutSeconds - visibleDuration);
                    }
                    else
                    {
                        newClip.SourceTrimInSeconds = clip.SourceTrimInSeconds;
                        newClip.SourceTrimOutSeconds = Math.Min(clip.SourceTrimOutSeconds, clip.SourceTrimInSeconds + visibleDuration);
                    }
                }
                else if (clip.IsReversed)
                {
                    newClip.SourceTrimInSeconds = clip.SourceTrimInSeconds + trimmedFromEnd * sourceRate;
                    newClip.SourceTrimOutSeconds = clip.SourceTrimOutSeconds - trimmedFromStart * sourceRate;
                }
                else
                {
                    newClip.SourceTrimInSeconds = clip.SourceTrimInSeconds + trimmedFromStart * sourceRate;
                    newClip.SourceTrimOutSeconds = clip.SourceTrimOutSeconds - trimmedFromEnd * sourceRate;
                }
'@ @'
                var sourceRate = clip.IsFreezeFrame ? 1.0 : SpeedCurveMath.ClampSpeed(clip.SpeedMultiplier);
                if (clip.IsFreezeFrame)
                {
                    var visibleDuration = Math.Max(0.05, overlapEnd - overlapStart);
                    if (clip.IsReversed)
                    {
                        newClip.SourceTrimOutSeconds = clip.SourceTrimOutSeconds;
                        newClip.SourceTrimInSeconds = Math.Max(clip.SourceTrimInSeconds, clip.SourceTrimOutSeconds - visibleDuration);
                    }
                    else
                    {
                        newClip.SourceTrimInSeconds = clip.SourceTrimInSeconds;
                        newClip.SourceTrimOutSeconds = Math.Min(clip.SourceTrimOutSeconds, clip.SourceTrimInSeconds + visibleDuration);
                    }
                }
                else if (SpeedCurveMath.HasCurve(clip) && !clip.IsReversed)
                {
                    newClip.SourceTrimInSeconds = SpeedCurveMath.SourceTimeAtTimelineOffset(clip, trimmedFromStart);
                    newClip.SourceTrimOutSeconds = SpeedCurveMath.SourceTimeAtTimelineOffset(
                        clip, Math.Max(0, clip.TimelineDurationSeconds - trimmedFromEnd));
                }
                else if (clip.IsReversed)
                {
                    newClip.SourceTrimInSeconds = clip.SourceTrimInSeconds + trimmedFromEnd * sourceRate;
                    newClip.SourceTrimOutSeconds = clip.SourceTrimOutSeconds - trimmedFromStart * sourceRate;
                }
                else
                {
                    newClip.SourceTrimInSeconds = clip.SourceTrimInSeconds + trimmedFromStart * sourceRate;
                    newClip.SourceTrimOutSeconds = clip.SourceTrimOutSeconds - trimmedFromEnd * sourceRate;
                }
'@

Replace-Exact $renderer @'
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

Replace-Exact $renderer @'
    public static string BuildSpeedFilter(TimelineClip clip)
    {
        var speed = clip.IsFreezeFrame ? 1.0 : Math.Clamp(clip.SpeedMultiplier, 0.25, 4);
        if (Math.Abs(speed - 1) < 1e-6)
        {
            return string.Empty;
        }

        // Higher speed = shorter presentation timestamps, hence dividing by the multiplier.
        return FormattableString.Invariant($",setpts=PTS/{speed}");
    }

    /// <summary>FFmpeg audio-tempo chain matching <see cref="BuildSpeedFilter"/>. Chained 0.5..2.0
    /// stages work across the full UI range 0.25x..4x without pitch-shifting the audio.</summary>
    public static string BuildAudioSpeedFilter(TimelineClip clip)
    {
        if (clip.IsFreezeFrame)
        {
            return string.Empty;
        }

        var remaining = Math.Clamp(clip.SpeedMultiplier, 0.25, 4);
        if (Math.Abs(remaining - 1) < 1e-6)
        {
            return string.Empty;
        }

        var stages = new List<double>();
        while (remaining < 0.5 - 1e-9)
        {
            stages.Add(0.5);
            remaining /= 0.5;
        }
        while (remaining > 2.0 + 1e-9)
        {
            stages.Add(2.0);
            remaining /= 2.0;
        }
        if (Math.Abs(remaining - 1) > 1e-6)
        {
            stages.Add(remaining);
        }

        return stages.Count == 0
            ? string.Empty
            : "," + string.Join(",", stages.Select(s => FormattableString.Invariant($"atempo={s}")));
    }
'@ @'
    public static string BuildSpeedFilter(TimelineClip clip)
    {
        if (clip.IsFreezeFrame)
        {
            return string.Empty;
        }

        if (SpeedCurveMath.HasCurve(clip))
        {
            if (clip.IsReversed)
            {
                throw new InvalidOperationException("Velocity / Speed Curve ne može istovremeno sa Reverse. Isključite Reverse ili uklonite krivu brzine.");
            }

            var segments = SpeedCurveMath.BuildRenderSegments(clip);
            if (segments.Count == 0)
            {
                return string.Empty;
            }

            // trim+setpts before this filter makes PTS*TB clip-local source time. Each exact-duration
            // renderer cell maps that source interval to its cumulative output clock.
            string SegmentValue(SpeedCurveRenderSegment segment) =>
                $"({F(segment.OutputStartSeconds)}+((PTS*TB)-{F(segment.SourceStartSeconds - clip.SourceTrimInSeconds)})/{F(segment.SpeedMultiplier)})/TB";

            var expression = SegmentValue(segments[^1]);
            for (var i = segments.Count - 2; i >= 0; i--)
            {
                var segment = segments[i];
                var endLocal = segment.SourceEndSeconds - clip.SourceTrimInSeconds;
                expression = $"if(lt(PTS*TB,{F(endLocal)}),{SegmentValue(segment)},{expression})";
            }
            return $",setpts='{expression}'";
        }

        var speed = SpeedCurveMath.ClampSpeed(clip.SpeedMultiplier);
        if (Math.Abs(speed - 1) < 1e-6)
        {
            return string.Empty;
        }

        return FormattableString.Invariant($",setpts=PTS/{speed}");
    }

    /// <summary>Audio timing matching <see cref="BuildSpeedFilter"/>. Constant speed keeps the proven
    /// atempo chain. Velocity curves drive one named librubberband instance through asendcmd at the same
    /// source-time cell boundaries used by video, preserving pitch while tempo changes.</summary>
    public static string BuildAudioSpeedFilter(TimelineClip clip)
    {
        if (clip.IsFreezeFrame)
        {
            return string.Empty;
        }

        if (SpeedCurveMath.HasCurve(clip))
        {
            if (clip.IsReversed)
            {
                throw new InvalidOperationException("Velocity / Speed Curve ne može istovremeno sa Reverse. Isključite Reverse ili uklonite krivu brzine.");
            }

            var segments = SpeedCurveMath.BuildRenderSegments(clip);
            if (segments.Count == 0)
            {
                return string.Empty;
            }

            var initial = F(segments[0].SpeedMultiplier);
            if (segments.Count == 1)
            {
                return $",rubberband@npvsspeed=tempo={initial}";
            }

            var commands = segments.Skip(1).Select(segment =>
                $"{F(segment.SourceStartSeconds - clip.SourceTrimInSeconds)} rubberband@npvsspeed tempo {F(segment.SpeedMultiplier)}");
            return $",asendcmd=c='{string.Join(";", commands)}',rubberband@npvsspeed=tempo={initial}";
        }

        var remaining = SpeedCurveMath.ClampSpeed(clip.SpeedMultiplier);
        if (Math.Abs(remaining - 1) < 1e-6)
        {
            return string.Empty;
        }

        var stages = new List<double>();
        while (remaining < 0.5 - 1e-9)
        {
            stages.Add(0.5);
            remaining /= 0.5;
        }
        while (remaining > 2.0 + 1e-9)
        {
            stages.Add(2.0);
            remaining /= 2.0;
        }
        if (Math.Abs(remaining - 1) > 1e-6)
        {
            stages.Add(remaining);
        }

        return stages.Count == 0
            ? string.Empty
            : "," + string.Join(",", stages.Select(s => FormattableString.Invariant($"atempo={s}")));
    }
'@

$clipVm = 'src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs'
Replace-Exact $clipVm @'
    private readonly Action<string, ClipVideoEffect, double, double, double, double>? _onEffectsChanged;
    private readonly Action<string, ClipTransformSettings>? _onTransformChanged;
'@ @'
    private readonly Action<string, ClipVideoEffect, double, double, double, double>? _onEffectsChanged;
    private readonly Action<string, SpeedCurvePreset>? _onSpeedCurvePresetChanged;
    private readonly Action<string, ClipTransformSettings>? _onTransformChanged;
'@

Replace-Exact $clipVm @'
    /// <summary>0.5 = slow motion, 2 = double speed.</summary>
    public double SpeedMultiplier
    {
        get => Clip.SpeedMultiplier;
        set { if (Math.Abs(Clip.SpeedMultiplier - value) < 1e-6) return; _onEffectsChanged?.Invoke(Clip.Id, Effect, Brightness, Contrast, Saturation, value); }
    }

    private ClipTransformSettings CurrentTransform() => new(
'@ @'
    /// <summary>0.5 = slow motion, 2 = double speed. Changing it explicitly disables a velocity curve.</summary>
    public double SpeedMultiplier
    {
        get => Clip.SpeedMultiplier;
        set { if (Math.Abs(Clip.SpeedMultiplier - value) < 1e-6) return; _onEffectsChanged?.Invoke(Clip.Id, Effect, Brightness, Contrast, Saturation, value); }
    }

    public IReadOnlyList<SpeedCurvePreset> AvailableSpeedCurvePresets { get; } = Enum.GetValues<SpeedCurvePreset>();
    public bool CanUseSpeedCurve => HasSourceMedia && !IsTextClip && !Clip.IsReversed && !Clip.IsFreezeFrame;
    public SpeedCurvePreset SpeedCurvePreset
    {
        get => Clip.SpeedCurvePreset;
        set
        {
            if (Clip.SpeedCurvePreset == value) return;
            _onSpeedCurvePresetChanged?.Invoke(Clip.Id, value);
        }
    }

    private ClipTransformSettings CurrentTransform() => new(
'@

Replace-Exact $clipVm @'
        Action<string, double>? onTrimInChanged = null,
        Action<string, double>? onTrimOutChanged = null)
    {
        _onEffectsChanged = onEffectsChanged;
'@ @'
        Action<string, double>? onTrimInChanged = null,
        Action<string, double>? onTrimOutChanged = null,
        Action<string, SpeedCurvePreset>? onSpeedCurvePresetChanged = null)
    {
        _onEffectsChanged = onEffectsChanged;
        _onSpeedCurvePresetChanged = onSpeedCurvePresetChanged;
'@

$timelineVm = 'src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs'
Replace-Exact $timelineVm @'
        void OnEffectsChanged(string clipId, ClipVideoEffect effect, double brightness, double contrast, double saturation, double speed)
        {
            _session.SetClipEffects(clipId, effect, brightness, contrast, saturation, speed);
            RefreshFromSession();
        }

        void OnTransformChanged(string clipId, ClipTransformSettings settings)
'@ @'
        void OnEffectsChanged(string clipId, ClipVideoEffect effect, double brightness, double contrast, double saturation, double speed)
        {
            _session.SetClipEffects(clipId, effect, brightness, contrast, saturation, speed);
            RefreshFromSession();
        }
        void OnSpeedCurvePresetChanged(string clipId, SpeedCurvePreset preset)
        {
            _session.SetSpeedCurvePreset(clipId, preset);
            RefreshFromSession();
        }

        void OnTransformChanged(string clipId, ClipTransformSettings settings)
'@

Replace-Exact $timelineVm @'
            _getPlayhead, OnKeyframeUpsert, OnKeyframeRemove, OnTextFontChanged, sourceDurationSeconds, OnTrimInChanged, OnTrimOutChanged)
'@ @'
            _getPlayhead, OnKeyframeUpsert, OnKeyframeRemove, OnTextFontChanged, sourceDurationSeconds, OnTrimInChanged, OnTrimOutChanged, OnSpeedCurvePresetChanged)
'@

$xaml = 'src/NPVideoStudio.App/Views/WorkspaceView.axaml'
Replace-Exact $xaml @'
              <TextBlock Text="Brzina" Classes="subtle"/>
              <NumericUpDown Value="{Binding SpeedMultiplier}" Minimum="0.25" Maximum="4" Increment="0.25"/>
            </StackPanel>
            <StackPanel Spacing="8" IsVisible="{Binding IsPictureClip}">
'@ @'
              <TextBlock Text="Brzina" Classes="subtle"/>
              <NumericUpDown Value="{Binding SpeedMultiplier}" Minimum="0.25" Maximum="4" Increment="0.25"/>
              <StackPanel Name="InspectorAudioSpeedCurvePanel" Spacing="4" IsVisible="{Binding CanUseSpeedCurve}">
                <TextBlock Text="Velocity / Speed Curve" Classes="subtle"/>
                <ComboBox Name="InspectorAudioSpeedCurve" ItemsSource="{Binding AvailableSpeedCurvePresets}" SelectedItem="{Binding SpeedCurvePreset}"/>
                <TextBlock Text="Promena obične brzine vraća klip na konstantnu brzinu." Classes="subtle" TextWrapping="Wrap"/>
              </StackPanel>
            </StackPanel>
            <StackPanel Spacing="8" IsVisible="{Binding IsPictureClip}">
'@

Replace-Exact $xaml @'
              <TextBlock Text="Zasićenost" Classes="subtle"/><Slider Minimum="0" Maximum="3" Value="{Binding Saturation}"/>
              <TextBlock Text="Brzina" Classes="subtle"/><NumericUpDown Value="{Binding SpeedMultiplier}" Minimum="0.25" Maximum="4" Increment="0.25"/>
              <TextBlock Text="TRANSFORMACIJA" Classes="eyebrow" Margin="0,8,0,0" />
'@ @'
              <TextBlock Text="Zasićenost" Classes="subtle"/><Slider Minimum="0" Maximum="3" Value="{Binding Saturation}"/>
              <TextBlock Text="Brzina" Classes="subtle"/><NumericUpDown Value="{Binding SpeedMultiplier}" Minimum="0.25" Maximum="4" Increment="0.25"/>
              <StackPanel Name="InspectorVideoSpeedCurvePanel" Spacing="4" IsVisible="{Binding CanUseSpeedCurve}">
                <TextBlock Text="VELOCITY / SPEED CURVE" Classes="eyebrow" Margin="0,6,0,0"/>
                <ComboBox Name="InspectorVideoSpeedCurve" ItemsSource="{Binding AvailableSpeedCurvePresets}" SelectedItem="{Binding SpeedCurvePreset}"/>
                <TextBlock Text="Montage / Hero / Bullet / JumpCut / FlashIn / FlashOut menjaju stvarni FFmpeg video i audio timing. Ručna Brzina iznad isključuje krivu." Classes="subtle" TextWrapping="Wrap"/>
              </StackPanel>
              <TextBlock Text="TRANSFORMACIJA" Classes="eyebrow" Margin="0,8,0,0" />
'@

Remove-Item '.github/scripts/materialize-speed-render-ui-v2.ps1' -Force
Remove-Item '.github/workflows/materialize-speed-render-ui-v2.yml' -Force

git config user.name 'github-actions[bot]'
git config user.email '41898282+github-actions[bot]@users.noreply.github.com'
git add src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs src/NPVideoStudio.App/Views/WorkspaceView.axaml .github/scripts/materialize-speed-render-ui-v2.ps1 .github/workflows/materialize-speed-render-ui-v2.yml
git commit -m 'Render velocity curves and expose them in the inspector'
git push origin HEAD:agent/velocity-speed-curves-v2
