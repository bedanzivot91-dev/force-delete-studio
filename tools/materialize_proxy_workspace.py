from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    if text.count(old) != 1:
        raise RuntimeError(f"Expected exactly one anchor in {path}, got {text.count(old)}: {old[:100]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")


# Workspace service injection.
path = "src/NPVideoStudio.App/ViewModels/WorkspaceViewModel.cs"
replace_once(path,
'''    private readonly IAiWorkerClient? _aiWorkerClient;\n    private readonly IRenderService _renderService;''',
'''    private readonly IAiWorkerClient? _aiWorkerClient;\n    private readonly IRenderService _renderService;\n    private readonly IProxyGeneratorService? _proxyGeneratorService;''')
replace_once(path,
'''    public WorkspaceViewModel(Project project, IProjectRepository projectRepository, IMediaProbeService mediaProbeService, IStorageService storageService, IFramePreviewService framePreviewService, ISubtitleGeneratorService subtitleGeneratorService, IRenderService renderService, ILogger logger, IAiWorkerClient? aiWorkerClient = null)''',
'''    public WorkspaceViewModel(Project project, IProjectRepository projectRepository, IMediaProbeService mediaProbeService, IStorageService storageService, IFramePreviewService framePreviewService, ISubtitleGeneratorService subtitleGeneratorService, IRenderService renderService, ILogger logger, IAiWorkerClient? aiWorkerClient = null, IProxyGeneratorService? proxyGeneratorService = null)''')
replace_once(path,
'''        _aiWorkerClient = aiWorkerClient;\n        _renderService = renderService;''',
'''        _aiWorkerClient = aiWorkerClient;\n        _renderService = renderService;\n        _proxyGeneratorService = proxyGeneratorService;''')

# Snapshot-frame preview uses proxy-aware media clones. Original project remains untouched.
replace_once(path,
'''        var request = TimelinePreviewResolver.Resolve(Timeline.CurrentTracks, Project.MediaLibrary, playheadSeconds);''',
'''        var request = TimelinePreviewResolver.Resolve(Timeline.CurrentTracks, BuildPreviewMediaLibrary(Project.MediaLibrary), playheadSeconds);''')
replace_once(path,
'''        UpdatePlayerAspectRatio(Project.MediaLibrary.FirstOrDefault(a =>\n            string.Equals(a.FilePath, request.Value.SourceFilePath, StringComparison.OrdinalIgnoreCase)));''',
'''        UpdatePlayerAspectRatio(Project.MediaLibrary.FirstOrDefault(a =>\n            string.Equals(a.FilePath, request.Value.SourceFilePath, StringComparison.OrdinalIgnoreCase) ||\n            string.Equals(a.ProxyFilePath, request.Value.SourceFilePath, StringComparison.OrdinalIgnoreCase)));''')

# Direct source playback uses proxy if ready.
replace_once(path,
'''        await RealPreview.LoadAndPlayAsync(asset.Asset.FilePath);''',
'''        await RealPreview.LoadAndPlayAsync(ResolvePreviewSourcePath(asset.Asset));''')

# Full real preview render uses proxy clones, final export path elsewhere still receives original Project.
replace_once(path,
'''            var outputPath = await _renderService.RenderAsync(Project, job);''',
'''            var outputPath = await _renderService.RenderAsync(CreatePreviewRenderProject(Project.Timeline), job);''')
replace_once(path,
'''        var rangeTimeline = FfmpegFilterGraphBuilder.ExtractRangeTimeline(Project.Timeline, rangeStart, rangeEnd);\n        var previewProject = new Project { Name = Project.Name, Format = Project.Format, MediaLibrary = Project.MediaLibrary, Timeline = rangeTimeline };''',
'''        var rangeTimeline = FfmpegFilterGraphBuilder.ExtractRangeTimeline(Project.Timeline, rangeStart, rangeEnd);\n        var previewProject = CreatePreviewRenderProject(rangeTimeline);''')

# Insert proxy helpers before CreateItemViewModel.
replace_once(path,
'''    private MediaAssetViewModel CreateItemViewModel(Domain.MediaAsset asset)\n    {''',
'''    /// <summary>Preview-only source resolver. A ready proxy is preferred only when its file is still\n    /// present. The original MediaAsset.FilePath is never mutated, so export remains full quality.</summary>\n    public static string ResolvePreviewSourcePath(MediaAsset asset) =>\n        asset.ProxyStatus == MediaProxyStatus.Ready &&\n        !string.IsNullOrWhiteSpace(asset.ProxyFilePath) &&\n        File.Exists(asset.ProxyFilePath)\n            ? asset.ProxyFilePath\n            : asset.FilePath;\n\n    /// <summary>Creates a preview-only media library with identical IDs/metadata but proxy-aware paths.\n    /// FfmpegFilterGraphBuilder therefore resolves timeline foreign keys normally while reading lower\n    /// resolution media only for preview. The real project media library is not modified.</summary>\n    public static IReadOnlyList<MediaAsset> BuildPreviewMediaLibrary(IEnumerable<MediaAsset> assets) => assets.Select(asset => new MediaAsset\n    {\n        Id = asset.Id,\n        FilePath = ResolvePreviewSourcePath(asset),\n        Kind = asset.Kind,\n        Duration = asset.Duration,\n        Width = asset.Width,\n        Height = asset.Height,\n        Fps = asset.Fps,\n        VideoCodec = asset.VideoCodec,\n        AudioCodec = asset.AudioCodec,\n        HasVideoStream = asset.HasVideoStream,\n        HasAudioStream = asset.HasAudioStream,\n        FileSizeBytes = asset.FileSizeBytes,\n        IsFavorite = asset.IsFavorite,\n        FolderTag = asset.FolderTag,\n        ImportedAt = asset.ImportedAt,\n        IsMissing = asset.IsMissing,\n        ProbeError = asset.ProbeError,\n        ProxyStatus = asset.ProxyStatus,\n        ProxyFilePath = asset.ProxyFilePath,\n        ProxyError = asset.ProxyError\n    }).ToList();\n\n    private Project CreatePreviewRenderProject(Timeline timeline) => new()\n    {\n        Id = Project.Id,\n        Name = Project.Name,\n        Format = Project.Format,\n        TargetPlatform = Project.TargetPlatform,\n        MediaLibrary = BuildPreviewMediaLibrary(Project.MediaLibrary).ToList(),\n        Timeline = timeline\n    };\n\n    private async Task PersistProxyStateAsync()\n    {\n        if (string.IsNullOrEmpty(Project.ProjectFilePath)) return;\n        Timeline.SaveToProject();\n        await _projectRepository.SaveAsync(Project, Project.ProjectFilePath);\n    }\n\n    private MediaAssetViewModel CreateItemViewModel(Domain.MediaAsset asset)\n    {''')

# Wire proxy commands while preserving safe-remove block.
replace_once(path,
'''        item.RemoveCommand = new RelayCommand(() =>''',
'''        item.GenerateProxyCommand = new AsyncRelayCommand(async () =>\n        {\n            if (_proxyGeneratorService is null)\n            {\n                StatusMessage = "Proxy servis nije dostupan u ovoj instalaciji.";\n                return;\n            }\n            if (!asset.HasVideoStream || !File.Exists(asset.FilePath))\n            {\n                StatusMessage = $"Proxy nije moguće napraviti za „{asset.FileName}“ jer originalni video nije dostupan.";\n                return;\n            }\n\n            Directory.CreateDirectory(AppSettings.ProxyCacheFolder());\n            var outputPath = Path.Combine(AppSettings.ProxyCacheFolder(), $"{Project.Id}-{asset.Id}.proxy.mp4");\n            asset.ProxyStatus = MediaProxyStatus.Generating;\n            asset.ProxyError = null;\n            item.NotifyAssetChanged();\n            StatusMessage = $"Generišem proxy za „{asset.FileName}“...";\n\n            try\n            {\n                asset.ProxyFilePath = await _proxyGeneratorService.GenerateProxyAsync(asset.FilePath, outputPath);\n                asset.ProxyStatus = MediaProxyStatus.Ready;\n                asset.ProxyError = null;\n                StatusMessage = $"Proxy za „{asset.FileName}“ je spreman. Preview sada koristi proxy, export i dalje koristi original.";\n                RefreshPreviewFrame(Player.CurrentTimeSeconds);\n            }\n            catch (OperationCanceledException)\n            {\n                asset.ProxyStatus = MediaProxyStatus.Original;\n                asset.ProxyError = null;\n                throw;\n            }\n            catch (Exception ex)\n            {\n                asset.ProxyStatus = MediaProxyStatus.Failed;\n                asset.ProxyError = ex.Message;\n                StatusMessage = $"Proxy za „{asset.FileName}“ nije napravljen: {ex.Message}";\n                _logger.Warning(ex, "Proxy generation failed for {Path}", asset.FilePath);\n            }\n            finally\n            {\n                item.NotifyAssetChanged();\n                await PersistProxyStateAsync();\n            }\n        });\n        item.RemoveProxyCommand = new AsyncRelayCommand(async () =>\n        {\n            if (!string.IsNullOrWhiteSpace(asset.ProxyFilePath) && File.Exists(asset.ProxyFilePath))\n            {\n                File.Delete(asset.ProxyFilePath);\n            }\n            asset.ProxyFilePath = null;\n            asset.ProxyStatus = MediaProxyStatus.Original;\n            asset.ProxyError = null;\n            item.NotifyAssetChanged();\n            await PersistProxyStateAsync();\n            RefreshPreviewFrame(Player.CurrentTimeSeconds);\n            StatusMessage = $"Proxy za „{asset.FileName}“ je uklonjen. Preview koristi original.";\n        });\n        item.OpenProxyFolderCommand = new RelayCommand(() =>\n        {\n            var folder = !string.IsNullOrWhiteSpace(asset.ProxyFilePath)\n                ? Path.GetDirectoryName(asset.ProxyFilePath)\n                : AppSettings.ProxyCacheFolder();\n            if (string.IsNullOrWhiteSpace(folder)) return;\n            Directory.CreateDirectory(folder);\n            try\n            {\n                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });\n            }\n            catch (Exception ex)\n            {\n                StatusMessage = $"Proxy folder nije moguće otvoriti: {ex.Message}";\n            }\n        });\n\n        item.RemoveCommand = new RelayCommand(() =>''')

# MainWindow composition root actually supplies the already-registered proxy service.
path = "src/NPVideoStudio.App/ViewModels/MainWindowViewModel.cs"
replace_once(path,
'''            _services.GetRequiredService<Serilog.ILogger>(),\n            _services.GetRequiredService<IAiWorkerClient>());''',
'''            _services.GetRequiredService<Serilog.ILogger>(),\n            _services.GetRequiredService<IAiWorkerClient>(),\n            _services.GetRequiredService<IProxyGeneratorService>());''')

# Media library: visible proxy status and controls, without touching inspector layout.
path = "src/NPVideoStudio.App/Views/WorkspaceView.axaml"
replace_once(path,
'''                    <Grid ColumnDefinitions="Auto,*,Auto,Auto,Auto,Auto,Auto">\n                      <TextBlock Text="{Binding KindLabel}" Width="60" FontWeight="Bold" VerticalAlignment="Center" />\n                      <TextBlock Grid.Column="1" Text="{Binding FileName}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />\n                      <TextBlock Grid.Column="2" Text="{Binding DurationLabel}" Classes="subtle" Margin="12,0" VerticalAlignment="Center" />\n                      <TextBlock Grid.Column="3" Text="{Binding ResolutionLabel}" Classes="subtle" Margin="12,0" VerticalAlignment="Center" />\n                      <TextBlock Grid.Column="4" Text="{Binding SizeLabel}" Classes="subtle" Margin="12,0" VerticalAlignment="Center" />\n                      <ToggleButton Grid.Column="5" Content="★" IsChecked="{Binding IsFavorite, Mode=OneWay}" Command="{Binding ToggleFavoriteCommand}" Margin="4,0" />\n                      <Button Grid.Column="6" Content="Ukloni" Command="{Binding RemoveCommand}" />\n                    </Grid>''',
'''                    <Grid ColumnDefinitions="Auto,*,Auto" RowDefinitions="Auto,Auto">\n                      <TextBlock Text="{Binding KindLabel}" Width="60" FontWeight="Bold" VerticalAlignment="Center" />\n                      <StackPanel Grid.Column="1" Spacing="2">\n                        <TextBlock Text="{Binding FileName}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />\n                        <TextBlock Text="{Binding ProxyStatusLabel}" Classes="subtle" TextWrapping="Wrap" />\n                        <TextBlock Text="{Binding DurationLabel}" Classes="subtle" />\n                      </StackPanel>\n                      <StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="4">\n                        <ToggleButton Content="★" IsChecked="{Binding IsFavorite, Mode=OneWay}" Command="{Binding ToggleFavoriteCommand}" />\n                        <Button Content="Proxy" Command="{Binding GenerateProxyCommand}" IsEnabled="{Binding CanGenerateProxy}" />\n                        <Button Content="Ukloni proxy" Command="{Binding RemoveProxyCommand}" IsVisible="{Binding HasReadyProxy}" />\n                        <Button Content="Folder" Command="{Binding OpenProxyFolderCommand}" />\n                        <Button Content="Ukloni" Command="{Binding RemoveCommand}" />\n                      </StackPanel>\n                    </Grid>''')

print("Proxy workspace production wiring materialized.")
