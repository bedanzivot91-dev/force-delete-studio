from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def replace_once(rel, old, new):
    p = ROOT / rel
    text = p.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{rel}: expected one anchor, found {count}: {old[:120]!r}')
    p.write_text(text.replace(old, new, 1), encoding='utf-8')

# TimelineEditSession: real undo-safe edit API + Reverse/Freeze safety + deep-clone persistence.
replace_once(
    'src/NPVideoStudio.AI/TimelineEditSession.cs',
    '''    public void SetClipTransform(string clipId, ClipTransformSettings settings)\n    {''',
    '''    public bool SetClipStabilization(string clipId, bool enabled, int smoothingFrames, int accuracy, double zoomPercent)\n    {\n        var (_, clip) = FindClipWithTrack(clipId);\n        if (clip is null || clip.MediaAssetId is null)\n        {\n            return false;\n        }\n\n        if (enabled && (clip.IsReversed || clip.IsFreezeFrame))\n        {\n            return false;\n        }\n\n        var smoothing = Math.Clamp(smoothingFrames, 0, 120);\n        var clampedAccuracy = Math.Clamp(accuracy, 1, 15);\n        var zoom = Math.Clamp(zoomPercent, 0, 30);\n        if (clip.StabilizationEnabled == enabled &&\n            clip.StabilizationSmoothingFrames == smoothing &&\n            clip.StabilizationAccuracy == clampedAccuracy &&\n            Math.Abs(clip.StabilizationZoomPercent - zoom) < 1e-9)\n        {\n            return true;\n        }\n\n        SaveSnapshot();\n        var liveClip = FindClipWithTrack(clipId).Clip!;\n        liveClip.StabilizationEnabled = enabled;\n        liveClip.StabilizationSmoothingFrames = smoothing;\n        liveClip.StabilizationAccuracy = clampedAccuracy;\n        liveClip.StabilizationZoomPercent = zoom;\n        return true;\n    }\n\n    public void SetClipTransform(string clipId, ClipTransformSettings settings)\n    {''')

replace_once(
    'src/NPVideoStudio.AI/TimelineEditSession.cs',
    '''        if ((liveClip.IsReversed || liveClip.IsFreezeFrame) && SpeedCurveMath.HasCurve(liveClip))\n        {\n            // v1 does not silently fake reverse/freeze + velocity interaction. Switching either on returns\n            // the clip to deterministic constant timing; the user can reapply a curve after disabling it.\n            liveClip.SpeedCurvePreset = SpeedCurvePreset.None;\n            liveClip.SpeedCurvePoints.Clear();\n        }\n        liveClip.ChromaKeyEnabled = settings.ChromaKeyEnabled;''',
    '''        if ((liveClip.IsReversed || liveClip.IsFreezeFrame) && SpeedCurveMath.HasCurve(liveClip))\n        {\n            // v1 does not silently fake reverse/freeze + velocity interaction. Switching either on returns\n            // the clip to deterministic constant timing; the user can reapply a curve after disabling it.\n            liveClip.SpeedCurvePreset = SpeedCurvePreset.None;\n            liveClip.SpeedCurvePoints.Clear();\n        }\n        if (liveClip.IsReversed || liveClip.IsFreezeFrame)\n        {\n            // libvidstab first-pass vectors describe forward-moving source frames. Do not silently reuse\n            // them for Reverse/Freeze where they would no longer describe the rendered frame sequence.\n            liveClip.StabilizationEnabled = false;\n        }\n        liveClip.ChromaKeyEnabled = settings.ChromaKeyEnabled;''')

replace_once(
    'src/NPVideoStudio.AI/TimelineEditSession.cs',
    '''        SpeedCurvePoints = clip.SpeedCurvePoints.Select(point => new SpeedCurvePoint\n        {\n            Id = point.Id,\n            SourceTimeSeconds = point.SourceTimeSeconds,\n            SpeedMultiplier = point.SpeedMultiplier\n        }).ToList(),\n        RotationDegrees = clip.RotationDegrees,''',
    '''        SpeedCurvePoints = clip.SpeedCurvePoints.Select(point => new SpeedCurvePoint\n        {\n            Id = point.Id,\n            SourceTimeSeconds = point.SourceTimeSeconds,\n            SpeedMultiplier = point.SpeedMultiplier\n        }).ToList(),\n        StabilizationEnabled = clip.StabilizationEnabled,\n        StabilizationSmoothingFrames = clip.StabilizationSmoothingFrames,\n        StabilizationAccuracy = clip.StabilizationAccuracy,\n        StabilizationZoomPercent = clip.StabilizationZoomPercent,\n        RotationDegrees = clip.RotationDegrees,''')

# Clip VM: never mutate the live session object before the undo snapshot.
replace_once(
    'src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs',
    '''    private readonly Action<string, SpeedCurvePreset>? _onSpeedCurvePresetChanged;\n    private readonly Action<string, ClipTransformSettings>? _onTransformChanged;''',
    '''    private readonly Action<string, SpeedCurvePreset>? _onSpeedCurvePresetChanged;\n    private readonly Action<string, bool, int, int, double>? _onStabilizationChanged;\n    private readonly Action<string, ClipTransformSettings>? _onTransformChanged;''')

replace_once(
    'src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs',
    '''    private ClipTransformSettings CurrentTransform() => new(''',
    '''    public bool CanUseStabilization => HasSourceMedia && IsVideoClip && !Clip.IsReversed && !Clip.IsFreezeFrame;\n    public bool StabilizationEnabled\n    {\n        get => Clip.StabilizationEnabled;\n        set\n        {\n            if (Clip.StabilizationEnabled == value) return;\n            _onStabilizationChanged?.Invoke(Clip.Id, value, StabilizationSmoothingFrames, StabilizationAccuracy, StabilizationZoomPercent);\n        }\n    }\n    public int StabilizationSmoothingFrames\n    {\n        get => Clip.StabilizationSmoothingFrames;\n        set\n        {\n            if (Clip.StabilizationSmoothingFrames == value) return;\n            _onStabilizationChanged?.Invoke(Clip.Id, StabilizationEnabled, value, StabilizationAccuracy, StabilizationZoomPercent);\n        }\n    }\n    public int StabilizationAccuracy\n    {\n        get => Clip.StabilizationAccuracy;\n        set\n        {\n            if (Clip.StabilizationAccuracy == value) return;\n            _onStabilizationChanged?.Invoke(Clip.Id, StabilizationEnabled, StabilizationSmoothingFrames, value, StabilizationZoomPercent);\n        }\n    }\n    public double StabilizationZoomPercent\n    {\n        get => Clip.StabilizationZoomPercent;\n        set\n        {\n            if (Math.Abs(Clip.StabilizationZoomPercent - value) < 1e-6) return;\n            _onStabilizationChanged?.Invoke(Clip.Id, StabilizationEnabled, StabilizationSmoothingFrames, StabilizationAccuracy, value);\n        }\n    }\n\n    private ClipTransformSettings CurrentTransform() => new(''')

replace_once(
    'src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs',
    '''        Action<string, double>? onTrimOutChanged = null,\n        Action<string, SpeedCurvePreset>? onSpeedCurvePresetChanged = null)''',
    '''        Action<string, double>? onTrimOutChanged = null,\n        Action<string, SpeedCurvePreset>? onSpeedCurvePresetChanged = null,\n        Action<string, bool, int, int, double>? onStabilizationChanged = null)''')

replace_once(
    'src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs',
    '''        _onSpeedCurvePresetChanged = onSpeedCurvePresetChanged;\n        _onTransformChanged = onTransformChanged;''',
    '''        _onSpeedCurvePresetChanged = onSpeedCurvePresetChanged;\n        _onStabilizationChanged = onStabilizationChanged;\n        _onTransformChanged = onTransformChanged;''')

# Timeline VM wiring.
replace_once(
    'src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs',
    '''        void OnSpeedCurvePresetChanged(string clipId, SpeedCurvePreset preset)\n        {\n            _session.SetSpeedCurvePreset(clipId, preset);\n            RefreshFromSession();\n        }\n\n        void OnTransformChanged''',
    '''        void OnSpeedCurvePresetChanged(string clipId, SpeedCurvePreset preset)\n        {\n            _session.SetSpeedCurvePreset(clipId, preset);\n            RefreshFromSession();\n        }\n        void OnStabilizationChanged(string clipId, bool enabled, int smoothing, int accuracy, double zoom)\n        {\n            _session.SetClipStabilization(clipId, enabled, smoothing, accuracy, zoom);\n            RefreshFromSession();\n        }\n\n        void OnTransformChanged''')

replace_once(
    'src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs',
    '''            _getPlayhead, OnKeyframeUpsert, OnKeyframeRemove, OnTextFontChanged, sourceDurationSeconds, OnTrimInChanged, OnTrimOutChanged, OnSpeedCurvePresetChanged)''',
    '''            _getPlayhead, OnKeyframeUpsert, OnKeyframeRemove, OnTextFontChanged, sourceDurationSeconds, OnTrimInChanged, OnTrimOutChanged, OnSpeedCurvePresetChanged, OnStabilizationChanged)''')

# Active Studio 2026 inspector.
replace_once(
    'src/NPVideoStudio.App/Views/ModernInspectorView.axaml',
    '''                <StackPanel Spacing="4" IsVisible="{Binding IsVideoClip}">\n                  <TextBlock Text="Trajanje prelaza (s)" Classes="subtle"/>''',
    '''                <Border Name="ModernVideoStabilizationPanel" Classes="inspectorSection" IsVisible="{Binding CanUseStabilization}" Margin="0,4,0,0">\n                  <StackPanel Spacing="6">\n                    <TextBlock Text="Stabilizacija (2-pass)" Classes="section"/>\n                    <ToggleButton Name="ModernVideoStabilization" Content="Uključi stabilizaciju" IsChecked="{Binding StabilizationEnabled}" HorizontalAlignment="Left"/>\n                    <Grid ColumnDefinitions="*,*,*" IsVisible="{Binding StabilizationEnabled}">\n                      <StackPanel Spacing="4"><TextBlock Text="Smoothing" Classes="subtle"/><NumericUpDown Value="{Binding StabilizationSmoothingFrames}" Minimum="0" Maximum="120" Increment="1"/></StackPanel>\n                      <StackPanel Grid.Column="1" Spacing="4" Margin="8,0,0,0"><TextBlock Text="Accuracy" Classes="subtle"/><NumericUpDown Value="{Binding StabilizationAccuracy}" Minimum="1" Maximum="15" Increment="1"/></StackPanel>\n                      <StackPanel Grid.Column="2" Spacing="4" Margin="8,0,0,0"><TextBlock Text="Zoom %" Classes="subtle"/><NumericUpDown Value="{Binding StabilizationZoomPercent}" Minimum="0" Maximum="30" Increment="1"/></StackPanel>\n                    </Grid>\n                    <TextBlock Text="Export prvo analizira pomeranje kadrova (vidstabdetect), zatim primenjuje vidstabtransform. Reverse/Freeze automatski isključuju stabilizaciju." Classes="subtle" TextWrapping="Wrap"/>\n                  </StackPanel>\n                </Border>\n                <StackPanel Spacing="4" IsVisible="{Binding IsVideoClip}">\n                  <TextBlock Text="Trajanje prelaza (s)" Classes="subtle"/>''')

# FFmpeg graph: optional render-context transform map, base + overlay application, deep range clone.
replace_once(
    'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs',
    '''        Timeline timeline, IReadOnlyList<MediaAsset> mediaLibrary, int targetWidth = 1920, int targetHeight = 1080, double frameRate = 30)''',
    '''        Timeline timeline, IReadOnlyList<MediaAsset> mediaLibrary, int targetWidth = 1920, int targetHeight = 1080, double frameRate = 30,\n        IReadOnlyDictionary<string, string>? stabilizationTransforms = null)''')

replace_once(
    'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs',
    '''                $"[{inputIndex}:v]trim=start={clip.SourceTrimInSeconds}:end={clip.SourceTrimOutSeconds},setpts=PTS-STARTPTS"));\n            videoFilter.Append(BuildTemporalVideoFilters(clip, duration));''',
    '''                $"[{inputIndex}:v]trim=start={clip.SourceTrimInSeconds}:end={clip.SourceTrimOutSeconds},setpts=PTS-STARTPTS"));\n            videoFilter.Append(BuildStabilizationFilter(clip, stabilizationTransforms));\n            videoFilter.Append(BuildTemporalVideoFilters(clip, duration));''')

replace_once(
    'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs',
    '''            timeline, videoTrack, mediaLibrary, inputs, filterLines,\n            currentVideoLabel!, targetWidth, targetHeight, MapToRenderedTime);''',
    '''            timeline, videoTrack, mediaLibrary, inputs, filterLines,\n            currentVideoLabel!, targetWidth, targetHeight, MapToRenderedTime, stabilizationTransforms);''')

replace_once(
    'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs',
    '''        int targetHeight,\n        Func<double, double> mapToRenderedTime)''',
    '''        int targetHeight,\n        Func<double, double> mapToRenderedTime,\n        IReadOnlyDictionary<string, string>? stabilizationTransforms)''')

replace_once(
    'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs',
    '''                $"[{inputIndex}:v]trim=start={clip.SourceTrimInSeconds}:end={clip.SourceTrimOutSeconds},setpts=PTS-STARTPTS"));\n            // -1 keeps the source aspect ratio; the overlay is sized by width only.\n            prepared.Append(BuildTemporalVideoFilters''',
    '''                $"[{inputIndex}:v]trim=start={clip.SourceTrimInSeconds}:end={clip.SourceTrimOutSeconds},setpts=PTS-STARTPTS"));\n            // -1 keeps the source aspect ratio; the overlay is sized by width only.\n            prepared.Append(BuildStabilizationFilter(clip, stabilizationTransforms));\n            prepared.Append(BuildTemporalVideoFilters''')

replace_once(
    'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs',
    '''        SpeedCurvePoints = clip.SpeedCurvePoints.Select(point => new SpeedCurvePoint\n        {\n            Id = point.Id,\n            SourceTimeSeconds = point.SourceTimeSeconds,\n            SpeedMultiplier = point.SpeedMultiplier\n        }).ToList(),\n        RotationDegrees = clip.RotationDegrees,''',
    '''        SpeedCurvePoints = clip.SpeedCurvePoints.Select(point => new SpeedCurvePoint\n        {\n            Id = point.Id,\n            SourceTimeSeconds = point.SourceTimeSeconds,\n            SpeedMultiplier = point.SpeedMultiplier\n        }).ToList(),\n        StabilizationEnabled = clip.StabilizationEnabled,\n        StabilizationSmoothingFrames = clip.StabilizationSmoothingFrames,\n        StabilizationAccuracy = clip.StabilizationAccuracy,\n        StabilizationZoomPercent = clip.StabilizationZoomPercent,\n        RotationDegrees = clip.RotationDegrees,''')

replace_once(
    'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs',
    '''    private static (string VideoLabel, string AudioLabel, double Duration) AppendSegment(''',
    '''    public static string BuildStabilizationFilter(\n        TimelineClip clip,\n        IReadOnlyDictionary<string, string>? stabilizationTransforms)\n    {\n        if (!clip.StabilizationEnabled)\n        {\n            return string.Empty;\n        }\n        if (clip.IsReversed || clip.IsFreezeFrame)\n        {\n            throw new InvalidOperationException("Stabilizacija nije podržana zajedno sa Reverse/Freeze Frame.");\n        }\n        if (stabilizationTransforms is null ||\n            !stabilizationTransforms.TryGetValue(clip.Id, out var transformPath) ||\n            string.IsNullOrWhiteSpace(transformPath))\n        {\n            throw new InvalidOperationException(\n                $"Nedostaje vidstab motion analiza za stabilizovani klip {clip.Id}; render ne sme tiho da preskoči stabilizaciju.");\n        }\n\n        var escapedPath = EscapeFilterPath(transformPath);\n        var smoothing = Math.Clamp(clip.StabilizationSmoothingFrames, 0, 120);\n        var zoom = Math.Clamp(clip.StabilizationZoomPercent, 0, 30);\n        return FormattableString.Invariant(\n            $",vidstabtransform=input='{escapedPath}':smoothing={smoothing}:zoom={zoom}:optzoom=0:interpol=bicubic");\n    }\n\n    public static string EscapeFilterPath(string path)\n    {\n        ArgumentException.ThrowIfNullOrWhiteSpace(path);\n        return path.Replace("\\\\", "/", StringComparison.Ordinal)\n            .Replace(":", "\\\\:", StringComparison.Ordinal)\n            .Replace("'", "\\\\'", StringComparison.Ordinal);\n    }\n\n    private static (string VideoLabel, string AudioLabel, double Duration) AppendSegment(''')

# RenderService orchestrates actual first pass before graph creation and owns temp lifetime.
replace_once(
    'src/NPVideoStudio.Media/RenderService.cs',
    '''        var plan = FfmpegFilterGraphBuilder.Build(\n            project.Timeline, project.MediaLibrary, project.Format.Width, project.Format.Height, project.Format.Fps);''',
    '''        using var stabilization = await VideoStabilizationPrepass.PrepareAsync(project, _ffmpegPath, cancellationToken)\n            .ConfigureAwait(false);\n        var plan = FfmpegFilterGraphBuilder.Build(\n            project.Timeline, project.MediaLibrary, project.Format.Width, project.Format.Height, project.Format.Fps,\n            stabilization.TransformFiles);''')

print('Stabilization integration patches applied successfully.')
