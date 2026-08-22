from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def replace_once(rel, old, new):
    p = ROOT / rel
    text = p.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{rel}: expected one anchor, found {count}: {old[:140]!r}')
    p.write_text(text.replace(old, new, 1), encoding='utf-8')

# Persisted model.
replace_once(
    'src/NPVideoStudio.Domain/Timeline.cs',
    '''public enum ClipVideoEffect\n{''',
    '''public enum OpticalFlowQuality\n{\n    Fast,\n    Balanced,\n    High\n}\n\npublic enum ClipVideoEffect\n{''')

replace_once(
    'src/NPVideoStudio.Domain/Timeline.cs',
    '''    public double StabilizationZoomPercent { get; set; }\n    // --- Transform / temporal / green-screen''',
    '''    public double StabilizationZoomPercent { get; set; }\n\n    // --- Optical-flow motion interpolation ------------------------------------------------------\n    public bool OpticalFlowEnabled { get; set; }\n    public OpticalFlowQuality OpticalFlowQuality { get; set; } = OpticalFlowQuality.Balanced;\n    // --- Transform / temporal / green-screen''')

# Undo-safe session edit + freeze safety + clone.
replace_once(
    'src/NPVideoStudio.AI/TimelineEditSession.cs',
    '''    public bool SetClipStabilization(string clipId, bool enabled, int smoothingFrames, int accuracy, double zoomPercent)''',
    '''    public bool SetClipOpticalFlow(string clipId, bool enabled, OpticalFlowQuality quality)\n    {\n        var (_, clip) = FindClipWithTrack(clipId);\n        if (clip is null || clip.MediaAssetId is null || (enabled && clip.IsFreezeFrame))\n        {\n            return false;\n        }\n        if (clip.OpticalFlowEnabled == enabled && clip.OpticalFlowQuality == quality)\n        {\n            return true;\n        }\n\n        SaveSnapshot();\n        var liveClip = FindClipWithTrack(clipId).Clip!;\n        liveClip.OpticalFlowEnabled = enabled;\n        liveClip.OpticalFlowQuality = quality;\n        return true;\n    }\n\n    public bool SetClipStabilization(string clipId, bool enabled, int smoothingFrames, int accuracy, double zoomPercent)''')

replace_once(
    'src/NPVideoStudio.AI/TimelineEditSession.cs',
    '''        if (liveClip.IsReversed || liveClip.IsFreezeFrame)\n        {\n            // libvidstab first-pass vectors describe forward-moving source frames. Do not silently reuse\n            // them for Reverse/Freeze where they would no longer describe the rendered frame sequence.\n            liveClip.StabilizationEnabled = false;\n        }\n        liveClip.ChromaKeyEnabled''',
    '''        if (liveClip.IsReversed || liveClip.IsFreezeFrame)\n        {\n            // libvidstab first-pass vectors describe forward-moving source frames. Do not silently reuse\n            // them for Reverse/Freeze where they would no longer describe the rendered frame sequence.\n            liveClip.StabilizationEnabled = false;\n        }\n        if (liveClip.IsFreezeFrame)\n        {\n            // Interpolating a frozen still is meaningless and wastes a very expensive motion-estimation pass.\n            liveClip.OpticalFlowEnabled = false;\n        }\n        liveClip.ChromaKeyEnabled''')

replace_once(
    'src/NPVideoStudio.AI/TimelineEditSession.cs',
    '''        StabilizationZoomPercent = clip.StabilizationZoomPercent,\n        RotationDegrees = clip.RotationDegrees,''',
    '''        StabilizationZoomPercent = clip.StabilizationZoomPercent,\n        OpticalFlowEnabled = clip.OpticalFlowEnabled,\n        OpticalFlowQuality = clip.OpticalFlowQuality,\n        RotationDegrees = clip.RotationDegrees,''')

# ViewModel callback + properties.
replace_once(
    'src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs',
    '''    private readonly Action<string, bool, int, int, double>? _onStabilizationChanged;\n    private readonly Action<string, ClipTransformSettings>? _onTransformChanged;''',
    '''    private readonly Action<string, bool, int, int, double>? _onStabilizationChanged;\n    private readonly Action<string, bool, OpticalFlowQuality>? _onOpticalFlowChanged;\n    private readonly Action<string, ClipTransformSettings>? _onTransformChanged;''')

replace_once(
    'src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs',
    '''    public bool CanUseStabilization => HasSourceMedia && IsVideoClip && !Clip.IsReversed && !Clip.IsFreezeFrame;''',
    '''    public IReadOnlyList<OpticalFlowQuality> AvailableOpticalFlowQualities { get; } = Enum.GetValues<OpticalFlowQuality>();\n    public bool CanUseOpticalFlow => HasSourceMedia && IsVideoClip && !Clip.IsFreezeFrame;\n    public bool OpticalFlowEnabled\n    {\n        get => Clip.OpticalFlowEnabled;\n        set\n        {\n            if (Clip.OpticalFlowEnabled == value) return;\n            _onOpticalFlowChanged?.Invoke(Clip.Id, value, OpticalFlowQuality);\n        }\n    }\n    public OpticalFlowQuality OpticalFlowQuality\n    {\n        get => Clip.OpticalFlowQuality;\n        set\n        {\n            if (Clip.OpticalFlowQuality == value) return;\n            _onOpticalFlowChanged?.Invoke(Clip.Id, OpticalFlowEnabled, value);\n        }\n    }\n\n    public bool CanUseStabilization => HasSourceMedia && IsVideoClip && !Clip.IsReversed && !Clip.IsFreezeFrame;''')

replace_once(
    'src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs',
    '''        Action<string, SpeedCurvePreset>? onSpeedCurvePresetChanged = null,\n        Action<string, bool, int, int, double>? onStabilizationChanged = null)''',
    '''        Action<string, SpeedCurvePreset>? onSpeedCurvePresetChanged = null,\n        Action<string, bool, int, int, double>? onStabilizationChanged = null,\n        Action<string, bool, OpticalFlowQuality>? onOpticalFlowChanged = null)''')

replace_once(
    'src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs',
    '''        _onStabilizationChanged = onStabilizationChanged;\n        _onTransformChanged = onTransformChanged;''',
    '''        _onStabilizationChanged = onStabilizationChanged;\n        _onOpticalFlowChanged = onOpticalFlowChanged;\n        _onTransformChanged = onTransformChanged;''')

# Timeline wiring.
replace_once(
    'src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs',
    '''        void OnStabilizationChanged(string clipId, bool enabled, int smoothing, int accuracy, double zoom)\n        {\n            _session.SetClipStabilization(clipId, enabled, smoothing, accuracy, zoom);\n            RefreshFromSession();\n        }\n\n        void OnTransformChanged''',
    '''        void OnStabilizationChanged(string clipId, bool enabled, int smoothing, int accuracy, double zoom)\n        {\n            _session.SetClipStabilization(clipId, enabled, smoothing, accuracy, zoom);\n            RefreshFromSession();\n        }\n        void OnOpticalFlowChanged(string clipId, bool enabled, OpticalFlowQuality quality)\n        {\n            _session.SetClipOpticalFlow(clipId, enabled, quality);\n            RefreshFromSession();\n        }\n\n        void OnTransformChanged''')

replace_once(
    'src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs',
    '''            _getPlayhead, OnKeyframeUpsert, OnKeyframeRemove, OnTextFontChanged, sourceDurationSeconds, OnTrimInChanged, OnTrimOutChanged, OnSpeedCurvePresetChanged, OnStabilizationChanged)''',
    '''            _getPlayhead, OnKeyframeUpsert, OnKeyframeRemove, OnTextFontChanged, sourceDurationSeconds, OnTrimInChanged, OnTrimOutChanged, OnSpeedCurvePresetChanged, OnStabilizationChanged, OnOpticalFlowChanged)''')

# Active inspector.
replace_once(
    'src/NPVideoStudio.App/Views/ModernInspectorView.axaml',
    '''                <Border Name="ModernVideoStabilizationPanel"''',
    '''                <Border Name="ModernVideoOpticalFlowPanel" Classes="inspectorSection" IsVisible="{Binding CanUseOpticalFlow}" Margin="0,4,0,0">\n                  <StackPanel Spacing="6">\n                    <TextBlock Text="Optical Flow / Smooth Slow Motion" Classes="section"/>\n                    <ToggleButton Name="ModernVideoOpticalFlow" Content="Generiši međukadrove" IsChecked="{Binding OpticalFlowEnabled}" HorizontalAlignment="Left"/>\n                    <ComboBox Name="ModernVideoOpticalFlowQuality" ItemsSource="{Binding AvailableOpticalFlowQualities}" SelectedItem="{Binding OpticalFlowQuality}" IsVisible="{Binding OpticalFlowEnabled}"/>\n                    <TextBlock Text="FFmpeg minterpolate radi posle Brzine/Velocity krive i stvarno generiše nove kadrove na FPS projekta. High je najsporiji i najkvalitetniji." Classes="subtle" TextWrapping="Wrap"/>\n                  </StackPanel>\n                </Border>\n                <Border Name="ModernVideoStabilizationPanel"''')

# Renderer base and overlay chains. Optical flow follows timing changes and precedes geometric transforms.
replace_once(
    'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs',
    '''            videoFilter.Append(BuildSpeedFilter(clip));\n            videoFilter.Append(BuildTransformFilters(clip));''',
    '''            videoFilter.Append(BuildSpeedFilter(clip));\n            videoFilter.Append(BuildOpticalFlowFilter(clip, frameRate));\n            videoFilter.Append(BuildTransformFilters(clip));''')

replace_once(
    'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs',
    '''            currentVideoLabel!, targetWidth, targetHeight, MapToRenderedTime, stabilizationTransforms);''',
    '''            currentVideoLabel!, targetWidth, targetHeight, MapToRenderedTime, stabilizationTransforms, frameRate);''')

replace_once(
    'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs',
    '''        Func<double, double> mapToRenderedTime,\n        IReadOnlyDictionary<string, string>? stabilizationTransforms)''',
    '''        Func<double, double> mapToRenderedTime,\n        IReadOnlyDictionary<string, string>? stabilizationTransforms,\n        double frameRate)''')

replace_once(
    'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs',
    '''            prepared.Append(BuildSpeedFilter(clip));\n            prepared.Append(BuildTransformFilters(clip));''',
    '''            prepared.Append(BuildSpeedFilter(clip));\n            prepared.Append(BuildOpticalFlowFilter(clip, frameRate));\n            prepared.Append(BuildTransformFilters(clip));''')

replace_once(
    'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs',
    '''        StabilizationZoomPercent = clip.StabilizationZoomPercent,\n        RotationDegrees = clip.RotationDegrees,''',
    '''        StabilizationZoomPercent = clip.StabilizationZoomPercent,\n        OpticalFlowEnabled = clip.OpticalFlowEnabled,\n        OpticalFlowQuality = clip.OpticalFlowQuality,\n        RotationDegrees = clip.RotationDegrees,''')

replace_once(
    'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs',
    '''    public static string BuildStabilizationFilter(''',
    '''    public static string BuildOpticalFlowFilter(TimelineClip clip, double frameRate)\n    {\n        if (!clip.OpticalFlowEnabled)\n        {\n            return string.Empty;\n        }\n        if (clip.IsFreezeFrame)\n        {\n            throw new InvalidOperationException("Optical Flow nema smisla na Freeze Frame klipu.");\n        }\n\n        var fps = Math.Clamp(frameRate, 12, 120).ToString("0.###", CultureInfo.InvariantCulture);\n        var options = clip.OpticalFlowQuality switch\n        {\n            OpticalFlowQuality.Fast => "mc_mode=obmc:me_mode=bidir:me=epzs:vsbmc=0",\n            OpticalFlowQuality.High => "mc_mode=aobmc:me_mode=bidir:me=umh:vsbmc=1",\n            _ => "mc_mode=aobmc:me_mode=bidir:me=epzs:vsbmc=1"\n        };\n        return $",minterpolate=fps={fps}:mi_mode=mci:{options}";\n    }\n\n    public static string BuildStabilizationFilter(''')

print('Optical flow integration patches applied successfully.')
