from pathlib import Path

root = Path(__file__).resolve().parents[1]

def replace_once(path, old, new):
    p = root / path
    text = p.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{path}: expected one anchor, found {count}')
    p.write_text(text.replace(old, new, 1), encoding='utf-8')

# Clip inspector ViewModel callbacks and properties.
replace_once('src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs',
'''    private readonly Action<string, bool, int, int, double>? _onStabilizationChanged;\n    private readonly Action<string, ClipTransformSettings>? _onTransformChanged;''',
'''    private readonly Action<string, bool, int, int, double>? _onStabilizationChanged;\n    private readonly Action<string, MotionTrackingRegion>? _onTrackingRegionChanged;\n    private readonly Action<string, MotionTrackingRegion>? _onMotionTrackingRequested;\n    private readonly Action<string, bool>? _onAutoReframeChanged;\n    private readonly Action<string, ClipTransformSettings>? _onTransformChanged;''')

replace_once('src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs',
'''    private ClipTransformSettings CurrentTransform() => new(''',
'''    public bool CanUseMotionTracking => HasSourceMedia && IsVideoClip && !Clip.IsReversed && !Clip.IsFreezeFrame;\n    public bool HasMotionTracking => Clip.MotionTrackingPoints.Count >= 2;\n    public string MotionTrackingSummary => HasMotionTracking\n        ? $"Praćenje: {Clip.MotionTrackingPoints.Count} tačaka"\n        : "Praćenje još nije izračunato";\n\n    private MotionTrackingRegion CurrentTrackingRegion() => new(\n        Clip.TrackingRegionCenterX, Clip.TrackingRegionCenterY,\n        Clip.TrackingRegionWidth, Clip.TrackingRegionHeight);\n\n    private void PushTrackingRegion(Func<MotionTrackingRegion, MotionTrackingRegion> mutate) =>\n        _onTrackingRegionChanged?.Invoke(Clip.Id, mutate(CurrentTrackingRegion()).Clamp());\n\n    public double TrackingCenterXPercent\n    {\n        get => Clip.TrackingRegionCenterX * 100;\n        set { var normalized = Math.Clamp(value, 0, 100) / 100.0; if (Math.Abs(normalized - Clip.TrackingRegionCenterX) < 1e-6) return; PushTrackingRegion(r => r with { CenterX = normalized }); }\n    }\n    public double TrackingCenterYPercent\n    {\n        get => Clip.TrackingRegionCenterY * 100;\n        set { var normalized = Math.Clamp(value, 0, 100) / 100.0; if (Math.Abs(normalized - Clip.TrackingRegionCenterY) < 1e-6) return; PushTrackingRegion(r => r with { CenterY = normalized }); }\n    }\n    public double TrackingWidthPercent\n    {\n        get => Clip.TrackingRegionWidth * 100;\n        set { var normalized = Math.Clamp(value, 2, 100) / 100.0; if (Math.Abs(normalized - Clip.TrackingRegionWidth) < 1e-6) return; PushTrackingRegion(r => r with { Width = normalized }); }\n    }\n    public double TrackingHeightPercent\n    {\n        get => Clip.TrackingRegionHeight * 100;\n        set { var normalized = Math.Clamp(value, 2, 100) / 100.0; if (Math.Abs(normalized - Clip.TrackingRegionHeight) < 1e-6) return; PushTrackingRegion(r => r with { Height = normalized }); }\n    }\n    public bool AutoReframeEnabled\n    {\n        get => Clip.AutoReframeEnabled;\n        set { if (Clip.AutoReframeEnabled == value) return; _onAutoReframeChanged?.Invoke(Clip.Id, value); }\n    }\n    public ICommand TrackMotionCommand { get; }\n\n    private ClipTransformSettings CurrentTransform() => new(''')

replace_once('src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs',
'''        Action<string, SpeedCurvePreset>? onSpeedCurvePresetChanged = null,\n        Action<string, bool, int, int, double>? onStabilizationChanged = null)''',
'''        Action<string, SpeedCurvePreset>? onSpeedCurvePresetChanged = null,\n        Action<string, bool, int, int, double>? onStabilizationChanged = null,\n        Action<string, MotionTrackingRegion>? onTrackingRegionChanged = null,\n        Action<string, MotionTrackingRegion>? onMotionTrackingRequested = null,\n        Action<string, bool>? onAutoReframeChanged = null)''')

replace_once('src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs',
'''        _onStabilizationChanged = onStabilizationChanged;\n        _onTransformChanged = onTransformChanged;''',
'''        _onStabilizationChanged = onStabilizationChanged;\n        _onTrackingRegionChanged = onTrackingRegionChanged;\n        _onMotionTrackingRequested = onMotionTrackingRequested;\n        _onAutoReframeChanged = onAutoReframeChanged;\n        _onTransformChanged = onTransformChanged;''')

replace_once('src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs',
'''        RemoveKeyframeAtPlayheadCommand = new RelayCommand(RemoveKeyframeAtPlayhead);\n    }''',
'''        RemoveKeyframeAtPlayheadCommand = new RelayCommand(RemoveKeyframeAtPlayhead);\n        TrackMotionCommand = new RelayCommand(() => _onMotionTrackingRequested?.Invoke(Clip.Id, CurrentTrackingRegion()));\n    }''')

# Timeline emits requests to Workspace and owns all undo-safe model application.
replace_once('src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs',
'''    public event Action? TimelineChanged;\n\n    public TimelineViewModel''',
'''    public event Action? TimelineChanged;\n    public event Action<string, MotionTrackingRegion>? MotionTrackingRequested;\n\n    public bool ApplyMotionTrackingResult(string clipId, MotionTrackingRegion region, IReadOnlyList<MotionTrackingPoint> points)\n    {\n        if (!_session.ApplyMotionTrackingResult(clipId, region, points)) return false;\n        RefreshFromSession();\n        SelectedClipId = clipId;\n        return true;\n    }\n\n    public TimelineViewModel''')

replace_once('src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs',
'''        void OnStabilizationChanged(string clipId, bool enabled, int smoothing, int accuracy, double zoom)\n        {\n            _session.SetClipStabilization(clipId, enabled, smoothing, accuracy, zoom);\n            RefreshFromSession();\n        }\n\n        void OnTransformChanged''',
'''        void OnStabilizationChanged(string clipId, bool enabled, int smoothing, int accuracy, double zoom)\n        {\n            _session.SetClipStabilization(clipId, enabled, smoothing, accuracy, zoom);\n            RefreshFromSession();\n        }\n        void OnTrackingRegionChanged(string clipId, MotionTrackingRegion region)\n        {\n            _session.SetMotionTrackingRegion(clipId, region);\n            RefreshFromSession();\n        }\n        void OnMotionTrackingRequested(string clipId, MotionTrackingRegion region) =>\n            MotionTrackingRequested?.Invoke(clipId, region);\n        void OnAutoReframeChanged(string clipId, bool enabled)\n        {\n            _session.SetAutoReframeEnabled(clipId, enabled);\n            RefreshFromSession();\n        }\n\n        void OnTransformChanged''')

replace_once('src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs',
'''            _getPlayhead, OnKeyframeUpsert, OnKeyframeRemove, OnTextFontChanged, sourceDurationSeconds, OnTrimInChanged, OnTrimOutChanged, OnSpeedCurvePresetChanged, OnStabilizationChanged)''',
'''            _getPlayhead, OnKeyframeUpsert, OnKeyframeRemove, OnTextFontChanged, sourceDurationSeconds, OnTrimInChanged, OnTrimOutChanged, OnSpeedCurvePresetChanged, OnStabilizationChanged,\n            OnTrackingRegionChanged, OnMotionTrackingRequested, OnAutoReframeChanged)''')

# Workspace orchestrates the async tracker and saves only a successful result.
replace_once('src/NPVideoStudio.App/ViewModels/WorkspaceViewModel.cs',
'''    private readonly IProxyGeneratorService? _proxyGeneratorService;\n\n    private readonly ILogger _logger;''',
'''    private readonly IProxyGeneratorService? _proxyGeneratorService;\n    private readonly IMotionTrackingService? _motionTrackingService;\n\n    private readonly ILogger _logger;''')
replace_once('src/NPVideoStudio.App/ViewModels/WorkspaceViewModel.cs',
'''    private CancellationTokenSource? _captionGenerationCts;''',
'''    private CancellationTokenSource? _captionGenerationCts;\n    private CancellationTokenSource? _motionTrackingCts;''')
replace_once('src/NPVideoStudio.App/ViewModels/WorkspaceViewModel.cs',
'''    public WorkspaceViewModel(Project project, IProjectRepository projectRepository, IMediaProbeService mediaProbeService, IStorageService storageService, IFramePreviewService framePreviewService, ISubtitleGeneratorService subtitleGeneratorService, IRenderService renderService, ILogger logger, IAiWorkerClient? aiWorkerClient = null, IProxyGeneratorService? proxyGeneratorService = null)''',
'''    public WorkspaceViewModel(Project project, IProjectRepository projectRepository, IMediaProbeService mediaProbeService, IStorageService storageService, IFramePreviewService framePreviewService, ISubtitleGeneratorService subtitleGeneratorService, IRenderService renderService, ILogger logger, IAiWorkerClient? aiWorkerClient = null, IProxyGeneratorService? proxyGeneratorService = null, IMotionTrackingService? motionTrackingService = null)''')
replace_once('src/NPVideoStudio.App/ViewModels/WorkspaceViewModel.cs',
'''        _proxyGeneratorService = proxyGeneratorService;\n        _logger = logger.ForContext''',
'''        _proxyGeneratorService = proxyGeneratorService;\n        _motionTrackingService = motionTrackingService;\n        _logger = logger.ForContext''')
replace_once('src/NPVideoStudio.App/ViewModels/WorkspaceViewModel.cs',
'''        Timeline = new TimelineViewModel(project, MediaLibrary, () => Player.CurrentTimeSeconds);\n        Timeline.PropertyChanged''',
'''        Timeline = new TimelineViewModel(project, MediaLibrary, () => Player.CurrentTimeSeconds);\n        Timeline.MotionTrackingRequested += (clipId, region) => _ = TrackMotionAndEnableReframeAsync(clipId, region);\n        Timeline.PropertyChanged''')
replace_once('src/NPVideoStudio.App/ViewModels/WorkspaceViewModel.cs',
'''        _captionGenerationCts?.Dispose();\n        Player.Dispose();''',
'''        _captionGenerationCts?.Dispose();\n        _motionTrackingCts?.Cancel();\n        _motionTrackingCts?.Dispose();\n        Player.Dispose();''')

# Insert async workflow before Dispose where all needed fields are already initialized.
replace_once('src/NPVideoStudio.App/ViewModels/WorkspaceViewModel.cs',
'''    public void Dispose()\n    {''',
'''    private async Task TrackMotionAndEnableReframeAsync(string clipId, MotionTrackingRegion region)\n    {\n        if (_motionTrackingService is null)\n        {\n            StatusMessage = "Motion Tracking servis nije dostupan.";\n            return;\n        }\n\n        var clip = Timeline.CurrentTracks.SelectMany(track => track.Clips).FirstOrDefault(item => item.Id == clipId);\n        var asset = clip?.MediaAssetId is null ? null : Project.MediaLibrary.FirstOrDefault(item => item.Id == clip.MediaAssetId);\n        if (clip is null || asset is null || !asset.HasVideoStream)\n        {\n            StatusMessage = "Izabrani klip nema validan video izvor za Motion Tracking.";\n            return;\n        }\n\n        _motionTrackingCts?.Cancel();\n        _motionTrackingCts?.Dispose();\n        _motionTrackingCts = new CancellationTokenSource();\n        var token = _motionTrackingCts.Token;\n        StatusMessage = "Motion Tracking: pratim izabrani objekat kroz klip...";\n\n        try\n        {\n            var progress = new Progress<double>(value => StatusMessage = $"Motion Tracking: {value:0}%");\n            var points = await _motionTrackingService.TrackAsync(new MotionTrackingRequest\n            {\n                MediaFilePath = asset.FilePath,\n                SourceStartSeconds = clip.SourceTrimInSeconds,\n                SourceEndSeconds = clip.SourceTrimOutSeconds,\n                InitialRegion = region,\n                SampleIntervalSeconds = 0.1\n            }, progress, token);\n\n            if (!Timeline.ApplyMotionTrackingResult(clipId, region, points))\n                throw new InvalidOperationException("Tracking rezultat nije mogao bezbedno da se primeni na klip.");\n\n            Timeline.SaveToProject();\n            Project.LastModifiedAt = DateTimeOffset.Now;\n            if (!string.IsNullOrWhiteSpace(Project.ProjectFilePath))\n                await _projectRepository.SaveAsync(Project, Project.ProjectFilePath, token);\n            RefreshPreviewFrame(Player.CurrentTimeSeconds);\n            StatusMessage = $"Motion Tracking završen: {points.Count} tačaka. Auto Reframe je uključen.";\n        }\n        catch (OperationCanceledException) when (token.IsCancellationRequested)\n        {\n            StatusMessage = "Motion Tracking je otkazan.";\n        }\n        catch (Exception ex)\n        {\n            StatusMessage = $"Motion Tracking nije uspeo: {ex.Message}";\n            _logger.Warning(ex, "Motion Tracking nije uspeo za klip {ClipId}", clipId);\n        }\n    }\n\n    public void Dispose()\n    {''')

# DI registration.
replace_once('src/NPVideoStudio.App/App.axaml.cs',
'''        services.AddSingleton<IAiWorkerClient, AiWorkerClient>();\n        services.AddSingleton<IVideoLayoutAnalysisService>''',
'''        services.AddSingleton<IAiWorkerClient, AiWorkerClient>();\n        services.AddSingleton<IMotionTrackingService, MotionTrackingService>();\n        services.AddSingleton<IVideoLayoutAnalysisService>''')

# Modern Studio 2026 inspector.
replace_once('src/NPVideoStudio.App/Views/ModernInspectorView.axaml',
'''                <StackPanel Spacing="4" IsVisible="{Binding IsVideoClip}">\n                  <TextBlock Text="Trajanje prelaza (s)"''',
'''                <Border Name="ModernMotionTrackingPanel" Classes="inspectorSection" IsVisible="{Binding CanUseMotionTracking}" Margin="0,4,0,0">\n                  <StackPanel Spacing="6">\n                    <TextBlock Text="Motion Tracking / Auto Reframe" Classes="section"/>\n                    <TextBlock Text="Početni region objekta (%)" Classes="subtle"/>\n                    <Grid ColumnDefinitions="*,*,*,*">\n                      <NumericUpDown Value="{Binding TrackingCenterXPercent}" Minimum="0" Maximum="100" Increment="1" ToolTip.Tip="Centar X"/>\n                      <NumericUpDown Grid.Column="1" Margin="6,0,0,0" Value="{Binding TrackingCenterYPercent}" Minimum="0" Maximum="100" Increment="1" ToolTip.Tip="Centar Y"/>\n                      <NumericUpDown Grid.Column="2" Margin="6,0,0,0" Value="{Binding TrackingWidthPercent}" Minimum="2" Maximum="100" Increment="1" ToolTip.Tip="Širina"/>\n                      <NumericUpDown Grid.Column="3" Margin="6,0,0,0" Value="{Binding TrackingHeightPercent}" Minimum="2" Maximum="100" Increment="1" ToolTip.Tip="Visina"/>\n                    </Grid>\n                    <WrapPanel>\n                      <Button Name="ModernTrackMotionButton" Classes="cta" Content="Prati objekat" Command="{Binding TrackMotionCommand}" Margin="0,0,8,4"/>\n                      <ToggleButton Content="Auto Reframe" IsChecked="{Binding AutoReframeEnabled}" IsEnabled="{Binding HasMotionTracking}" Margin="0,0,8,4"/>\n                    </WrapPanel>\n                    <TextBlock Text="{Binding MotionTrackingSummary}" Classes="subtle"/>\n                    <TextBlock Text="CSRT radi lokalno. Ako tracker izgubi objekat, operacija prekida umesto da izmisli ostatak putanje. Auto Reframe koristi putanju u finalnom FFmpeg exportu." Classes="subtle" TextWrapping="Wrap"/>\n                  </StackPanel>\n                </Border>\n                <StackPanel Spacing="4" IsVisible="{Binding IsVideoClip}">\n                  <TextBlock Text="Trajanje prelaza (s)"''')

print('Tracking UI/service orchestration materialized.')
