using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Layout;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.AI;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using NPVideoStudio.App.Services;
using Serilog;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// Project workspace: media import/library, and (since Phase 8) the non-destructive timeline + player.
/// Phase 11 follow-up: also resolves and fetches the player's real preview frame (<see cref="TimelinePreviewResolver"/>
/// + <see cref="IFramePreviewService"/>) on every seek/step/play-tick/timeline-edit, so
/// <see cref="PlayerViewModel.CurrentFrameBitmap"/> shows an actual picture instead of nothing.
/// </summary>
public sealed partial class WorkspaceViewModel : ViewModelBase, IDisposable
{
    private readonly IProjectRepository _projectRepository;
    private readonly IMediaProbeService _mediaProbeService;
    private readonly IStorageService _storageService;
    private readonly IFramePreviewService _framePreviewService;
    private readonly ISubtitleGeneratorService _subtitleGeneratorService;
    private readonly IAiWorkerClient? _aiWorkerClient;
    private readonly IRenderService _renderService;
    private readonly IProxyGeneratorService? _proxyGeneratorService;
    private readonly IMotionTrackingService? _motionTrackingService;

    private readonly ILogger _logger;
    private CancellationTokenSource? _framePreviewCts;
    private CancellationTokenSource? _captionGenerationCts;
    private CancellationTokenSource? _motionTrackingCts;

    public Project Project { get; }

    public ObservableCollection<MediaAssetViewModel> MediaLibrary { get; } = new();

    public bool HasMedia => MediaLibrary.Count > 0;

    public TimelineViewModel Timeline { get; }
    public PlayerViewModel Player { get; }

    /// <summary>Real, continuous audio+video playback of a rendered preview - see
    /// <see cref="RealPreviewViewModel"/> and <see cref="RenderRealPreviewAsync"/> for what actually
    /// drives it. Kept separate from <see cref="Player"/> (the always-available frame-snapshot preview).</summary>
    public RealPreviewViewModel RealPreview { get; } = new();

    /// <summary>
    /// True when the real, continuous player is what is on screen, rather than the frame-snapshot
    /// preview. The screen shows ONE player, so the transport buttons have to know which engine the
    /// picture is currently coming from.
    /// </summary>
    public bool IsShowingContinuousVideo => RealPreview.HasLoadedFile;

    /// <summary>Whether whichever engine is currently driving the picture is playing.</summary>
    public bool IsPlayerPlaying => IsShowingContinuousVideo ? RealPreview.IsPlaying : Player.IsPlaying;

    /// <summary>
    /// The single Play button. Routing lives here rather than in the view because the user should not
    /// have to know, or care, which of the two decode paths is behind the picture - that distinction is
    /// what made this screen look like it had several players.
    /// </summary>
    [RelayCommand]
    private void PlayerPlay()
    {
        if (IsShowingContinuousVideo)
        {
            if (!RealPreview.IsPlaying)
            {
                RealPreview.TogglePlayPauseCommand.Execute(null);
            }
        }
        else
        {
            Player.PlayCommand.Execute(null);
        }

        RaisePlayerTransportChanged();
    }

    [RelayCommand]
    private void PlayerPause()
    {
        if (IsShowingContinuousVideo)
        {
            if (RealPreview.IsPlaying)
            {
                RealPreview.TogglePlayPauseCommand.Execute(null);
            }
        }
        else
        {
            Player.PauseCommand.Execute(null);
        }

        RaisePlayerTransportChanged();
    }

    [RelayCommand]
    private void PlayerStop()
    {
        if (IsShowingContinuousVideo)
        {
            RealPreview.StopCommand.Execute(null);
        }
        else
        {
            Player.StopCommand.Execute(null);
        }

        RaisePlayerTransportChanged();
    }

    private void RaisePlayerTransportChanged()
    {
        OnPropertyChanged(nameof(IsPlayerPlaying));
        OnPropertyChanged(nameof(IsShowingContinuousVideo));
    }

    [ObservableProperty]
    private bool _isRenderingRealPreview;

    [ObservableProperty]
    private string? _realPreviewStatusMessage;

    public event Action? ExportRequested;
    public event Action? CaptionStyleGalleryRequested;

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isGeneratingCaptions;

    [ObservableProperty]
    private string? _captionsStatusMessage;

    [ObservableProperty]
    private string _verifiedLyricsText = string.Empty;

    [ObservableProperty]
    private string? _verifiedLyricsFileName;

    private static readonly (string Name, string[] Extensions) LyricsFilter =
        ("Tekst pesme", new[] { "txt", "rtf" });

    /// <summary>Bindable mirror of <c>Project.Format</c>'s summary text - <see cref="Project"/> and
    /// <see cref="Domain.ProjectFormat"/> are plain (non-observable) domain classes, so mutating
    /// <c>Project.Format.Width</c> etc. in place (see <see cref="TryAdjustProjectFormatToMatch"/>) doesn't
    /// notify the UI on its own; this property is what the header actually binds to, refreshed explicitly
    /// wherever Format changes, instead of relying on nested-property-path change propagation that this
    /// domain model doesn't support.</summary>
    [ObservableProperty]
    private string _formatSummaryLabel = string.Empty;

    /// <summary>
    /// Shape of the video currently selected/playing. This is intentionally NOT permanently tied to
    /// Project.Format: one project can contain both Shorts (9:16) and landscape (16:9) sources, and the
    /// source monitor must change shape when the user switches files.
    /// </summary>
    [ObservableProperty]
    private double _playerAspectRatio = 16.0 / 9.0;

    /// <summary>Preview-only platform safe-area guide. It is never burned into export; it only shows the
    /// usable rectangle from SafeAreaPreset over the player so text/logos stay clear of Shorts/Reels/
    /// TikTok chrome.</summary>
    [ObservableProperty]
    private bool _showSafeArea;

    public SafeAreaPreset CurrentSafeAreaPreset => SafeAreaPreset.ForFrame(Project.Format.Width, Project.Format.Height);
    public string SafeAreaGuideLabel => $"SAFE AREA {CurrentSafeAreaPreset.FormatLabel}";
    public bool IsVerticalSafeArea => CurrentSafeAreaPreset == SafeAreaPreset.Vertical9By16;
    public bool IsSquareSafeArea => CurrentSafeAreaPreset == SafeAreaPreset.Square1By1;
    public bool IsHorizontalSafeArea => !IsVerticalSafeArea && !IsSquareSafeArea;

    public string PreviewCaptionText => Timeline.SelectedClip is { IsTextClip: true } clip ? clip.TextContent : string.Empty;
    public bool IsPreviewCaptionVisible => !string.IsNullOrWhiteSpace(PreviewCaptionText);
    public double PreviewCaptionFontSize => Timeline.SelectedClip is { IsTextClip: true } clip
        ? Math.Clamp(clip.FontSizePx * 0.75, 12, 96)
        : 27;
    public IBrush PreviewCaptionBrush
    {
        get
        {
            var color = Timeline.SelectedClip is { IsTextClip: true } clip ? clip.TextColor : "#FFFFFF";
            try { return Brush.Parse(color); }
            catch { return Brushes.White; }
        }
    }
    public VerticalAlignment PreviewCaptionVerticalAlignment => Timeline.SelectedClip?.TextPosition switch
    {
        CaptionTextPosition.Top => VerticalAlignment.Top,
        CaptionTextPosition.Middle => VerticalAlignment.Center,
        _ => VerticalAlignment.Bottom
    };
    public HorizontalAlignment PreviewCaptionHorizontalAlignment => Timeline.SelectedClip?.HorizontalAlign switch
    {
        TextHorizontalAlign.Left => HorizontalAlignment.Left,
        TextHorizontalAlign.Right => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Center
    };

    /// <summary>How far back from the playhead the "render just a window" quick preview starts, and how
    /// wide that window is - see <see cref="RenderRealPreviewAroundPlayheadAsync"/>.</summary>
    private const double RangePreviewLeadInSeconds = 2;
    private const double RangePreviewWindowSeconds = 15;

    private static readonly (string Name, string[] Extensions) VideoFilter = ("Video", new[] { "mp4", "mov", "mkv", "avi", "webm", "m4v", "mpeg", "mpg" });
    private static readonly (string Name, string[] Extensions) AudioFilter = ("Audio", new[] { "mp3", "wav", "aac", "m4a", "flac", "ogg", "wma" });
    private static readonly (string Name, string[] Extensions) ImageFilter = ("Slike", new[] { "jpg", "jpeg", "png", "webp", "bmp", "gif", "tiff", "tif" });

    public WorkspaceViewModel(Project project, IProjectRepository projectRepository, IMediaProbeService mediaProbeService, IStorageService storageService, IFramePreviewService framePreviewService, ISubtitleGeneratorService subtitleGeneratorService, IRenderService renderService, ILogger logger, IAiWorkerClient? aiWorkerClient = null, IProxyGeneratorService? proxyGeneratorService = null, IMotionTrackingService? motionTrackingService = null)
    {
        Project = project;
        _projectRepository = projectRepository;
        _mediaProbeService = mediaProbeService;
        _storageService = storageService;
        _framePreviewService = framePreviewService;
        _subtitleGeneratorService = subtitleGeneratorService;
        _aiWorkerClient = aiWorkerClient;
        _renderService = renderService;
        _proxyGeneratorService = proxyGeneratorService;
        _motionTrackingService = motionTrackingService;
        _logger = logger.ForContext("SourceContext", nameof(WorkspaceViewModel));
        RefreshFormatSummaryLabel();
        PlayerAspectRatio = ProjectAspectRatio;

        foreach (var asset in project.MediaLibrary)
        {
            MediaLibrary.Add(CreateItemViewModel(asset));
        }

        MediaLibrary.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMedia));

        Player = new PlayerViewModel(totalDurationSeconds: ComputeInitialDuration(project));
        Timeline = new TimelineViewModel(project, MediaLibrary, () => Player.CurrentTimeSeconds);
        Timeline.MotionTrackingRequested += (clipId, region) => _ = TrackMotionAndEnableReframeAsync(clipId, region);
        Timeline.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TimelineViewModel.SelectedMediaAsset))
            {
                UpdatePlayerAspectRatio(Timeline.SelectedMediaAsset?.Asset);
            }
            if (e.PropertyName is nameof(TimelineViewModel.SelectedClip) or nameof(TimelineViewModel.SelectedClipId))
            {
                RaiseCaptionPreviewChanged();
            }
        };
        Timeline.TimelineChanged += () =>
        {
            Player.Retarget(Timeline.TotalDurationSeconds);
            RefreshPreviewFrame(Player.CurrentTimeSeconds);
            RaiseCaptionPreviewChanged();
        };
        Player.TimeChanged += RefreshPreviewFrame;
    }

    private void RaiseCaptionPreviewChanged()
    {
        OnPropertyChanged(nameof(PreviewCaptionText));
        OnPropertyChanged(nameof(IsPreviewCaptionVisible));
        OnPropertyChanged(nameof(PreviewCaptionFontSize));
        OnPropertyChanged(nameof(PreviewCaptionBrush));
        OnPropertyChanged(nameof(PreviewCaptionVerticalAlignment));
        OnPropertyChanged(nameof(PreviewCaptionHorizontalAlignment));
    }

    private void RefreshPreviewFrame(double playheadSeconds)
    {
        _framePreviewCts?.Cancel();
        _framePreviewCts?.Dispose();
        var cts = new CancellationTokenSource();
        _framePreviewCts = cts;

        var request = TimelinePreviewResolver.Resolve(Timeline.CurrentTracks, BuildPreviewMediaLibrary(Project.MediaLibrary), playheadSeconds);
        if (request is null)
        {
            Player.CurrentFrameBitmap = null;
            Player.PreviewStatusMessage = "Nema kadra za prikaz - dodajte klip na video traku i postavite plejhed na njega.";
            return;
        }

        UpdatePlayerAspectRatio(Project.MediaLibrary.FirstOrDefault(a =>
            string.Equals(a.FilePath, request.Value.SourceFilePath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.ProxyFilePath, request.Value.SourceFilePath, StringComparison.OrdinalIgnoreCase)));

        _ = ExtractAndApplyFrameAsync(request.Value, cts.Token);
    }

    private void UpdatePlayerAspectRatio(MediaAsset? asset)
    {
        if (asset is not { Width: > 0, Height: > 0 } || !asset.HasVideoStream)
        {
            return;
        }

        PlayerAspectRatio = (double)asset.Width / asset.Height;
    }

    private async Task ExtractAndApplyFrameAsync(TimelinePreviewResolver.PreviewFrameRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _framePreviewService.ExtractFrameAsync(request.SourceFilePath, request.SourceTimestampSeconds, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (bytes is null)
            {
                Player.CurrentFrameBitmap = null;
                // A clip genuinely exists under the playhead here (TimelinePreviewResolver already found
                // one) - unlike the "no clip yet" case above, this means extraction itself failed, most
                // likely ffmpeg isn't installed/found. Say so explicitly instead of repeating the same
                // "add a clip" message the user already did.
                Player.PreviewStatusMessage = "Nije moguće prikazati kadar - proverite da li je FFmpeg instaliran i pronađen (Podešavanja → Alati, ili pokrenite Dijagnostiku), ili da li je izvorni fajl oštećen/premešten.";
                return;
            }

            Player.CurrentFrameBitmap = new Bitmap(new MemoryStream(bytes));
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer seek/step/tick before this one finished decoding - expected during
            // fast scrubbing, not an error.
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Osvežavanje prikaza kadra u plejeru nije uspelo za vreme {Time}s", request.SourceTimestampSeconds);
        }
    }

    private static double ComputeInitialDuration(Project project)
    {
        var fromTimeline = project.Timeline.Tracks.SelectMany(t => t.Clips)
            .Select(c => (double?)c.TimelineEndSeconds).DefaultIfEmpty(0).Max() ?? 0;
        if (fromTimeline > 0)
        {
            return fromTimeline;
        }

        var fromMedia = project.MediaLibrary.Where(a => a.Duration > TimeSpan.Zero)
            .Select(a => (double?)a.Duration.TotalSeconds).DefaultIfEmpty(0).Max() ?? 0;
        return fromMedia;
    }

    private async Task TrackMotionAndEnableReframeAsync(string clipId, MotionTrackingRegion region)
    {
        if (_motionTrackingService is null)
        {
            StatusMessage = "Motion Tracking servis nije dostupan.";
            return;
        }

        var clip = Timeline.CurrentTracks.SelectMany(track => track.Clips).FirstOrDefault(item => item.Id == clipId);
        var asset = clip?.MediaAssetId is null ? null : Project.MediaLibrary.FirstOrDefault(item => item.Id == clip.MediaAssetId);
        if (clip is null || asset is null || !asset.HasVideoStream)
        {
            StatusMessage = "Izabrani klip nema validan video izvor za Motion Tracking.";
            return;
        }

        _motionTrackingCts?.Cancel();
        _motionTrackingCts?.Dispose();
        _motionTrackingCts = new CancellationTokenSource();
        var token = _motionTrackingCts.Token;
        StatusMessage = "Motion Tracking: pratim izabrani objekat kroz klip...";

        try
        {
            var progress = new Progress<double>(value => StatusMessage = $"Motion Tracking: {value:0}%");
            var points = await _motionTrackingService.TrackAsync(new MotionTrackingRequest
            {
                MediaFilePath = asset.FilePath,
                SourceStartSeconds = clip.SourceTrimInSeconds,
                SourceEndSeconds = clip.SourceTrimOutSeconds,
                InitialRegion = region,
                SampleIntervalSeconds = 0.1
            }, progress, token);

            if (!Timeline.ApplyMotionTrackingResult(clipId, region, points))
                throw new InvalidOperationException("Tracking rezultat nije mogao bezbedno da se primeni na klip.");

            Timeline.SaveToProject();
            Project.LastModifiedAt = DateTimeOffset.Now;
            if (!string.IsNullOrWhiteSpace(Project.ProjectFilePath))
                await _projectRepository.SaveAsync(Project, Project.ProjectFilePath, token);
            RefreshPreviewFrame(Player.CurrentTimeSeconds);
            StatusMessage = $"Motion Tracking završen: {points.Count} tačaka. Auto Reframe je uključen.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            StatusMessage = "Motion Tracking je otkazan.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Motion Tracking nije uspeo: {ex.Message}";
            _logger.Warning(ex, "Motion Tracking nije uspeo za klip {ClipId}", clipId);
        }
    }

    public void Dispose()
    {
        _framePreviewCts?.Cancel();
        _framePreviewCts?.Dispose();
        _captionGenerationCts?.Cancel();
        _captionGenerationCts?.Dispose();
        _motionTrackingCts?.Cancel();
        _motionTrackingCts?.Dispose();
        Player.Dispose();
        RealPreview.Dispose();
    }

    /// <summary>
    /// "Pravi pregled sa zvukom" - real, continuous audio+video playback, answering the real user request
    /// that the frame-snapshot <see cref="Player"/> alone can't (no audio, one still frame at a time, no
    /// continuous motion). Deliberately reuses the exact same <see cref="IRenderService"/>/
    /// <see cref="FfmpegFilterGraphBuilder"/> pipeline "Izvezi video" uses - not a separate, simplified
    /// preview path that could drift from what actually exports - just with a fast/low-quality preset
    /// (ultrafast, CRF 28) so the wait before playback starts is reasonable instead of a full-quality
    /// export's minutes. The real, disclosed cost: a render has to finish before anything plays (unlike
    /// scrubbing the snapshot preview, which is instant), and <see cref="RealPreviewViewModel.IsAvailable"/>
    /// can be false if libvlc's native library isn't present on this machine.
    /// </summary>
    [RelayCommand]
    private async Task RenderRealPreviewAsync() => await RenderRealPreviewCoreAsync();

    /// <summary>
    /// Plays an imported source file directly through the real (libvlc) player - video and sound, right
    /// away, with no render step at all.
    ///
    /// Real user report that motivated this: "NE PUŠTA SE VIDEO, NE ČUJE SE ZVUK". Until now the only way
    /// to hear anything was <see cref="RenderRealPreviewAsync"/>, which renders the whole timeline through
    /// ffmpeg first - correct for previewing edits, but a bad answer to "I just imported a video, play
    /// it". libvlc plays mp4/mp3/etc. natively, so for looking at the source material there is nothing to
    /// render; this is the source-monitor half that every editor has and this one was missing.
    /// </summary>
    [RelayCommand]
    private async Task PlaySelectedSourceAsync()
    {
        var asset = Timeline.SelectedMediaAsset ?? MediaLibrary.FirstOrDefault();
        if (asset is null)
        {
            RealPreviewStatusMessage = "Prvo dodajte video ili audio fajl (dugme \"Dodaj medije\").";
            return;
        }

        if (!File.Exists(asset.Asset.FilePath))
        {
            RealPreviewStatusMessage = $"Fajl više ne postoji na disku: {asset.Asset.FilePath}";
            return;
        }

        if (!RealPreview.IsAvailable)
        {
            RealPreviewStatusMessage = RealPreview.UnavailableReason ?? "Plejer nije dostupan na ovom računaru.";
            return;
        }

        // Plays HERE, in the one player panel, instead of opening a separate window.
        //
        // It used to open a standalone PlayerWindow, and for a real reason at the time: the embedded
        // picture was LibVLCSharp's VideoView, a NativeControlHost, which does not get a native window
        // handle inside a UserControl (AvaloniaUI/Avalonia#6237, VideoLAN/LibVLCSharp#525) - this screen
        // is a UserControl, so an embedded player had nowhere to draw and a window was the only way to
        // see anything. VideoSurface has no native window at all, so that constraint is gone, and with
        // it the reason this screen had several players instead of one.
        UpdatePlayerAspectRatio(asset.Asset);
        await RealPreview.LoadAndPlayAsync(ResolvePreviewSourcePath(asset.Asset));

        RaisePlayerTransportChanged();

        RealPreviewStatusMessage = RealPreview.HasLoadedFile
            ? $"Pušta se: {asset.FileName}"
            : "Plejer nije mogao da pusti ovaj fajl.";

        _logger.Information("Plejer pokrenut za {Path}, uspeh={Ok}", asset.Asset.FilePath, RealPreview.HasLoadedFile);
    }

    /// <summary>
    /// Opens the selected file in whatever video player Windows already uses.
    ///
    /// Exists as a separate, first-class button rather than only as a fallback inside the player window,
    /// because the failure it guards against kills the app outright: when libvlc fails to attach its
    /// native window handle it takes the whole process down from native code, so the user never reaches
    /// any fallback offered inside that window. This path never touches libvlc at all - it is a plain
    /// ShellExecute, so it cannot crash this program, and it gives full size, sound and full screen using
    /// the player the user already has.
    /// </summary>
    [RelayCommand]
    private void OpenInSystemPlayer()
    {
        var asset = Timeline.SelectedMediaAsset ?? MediaLibrary.FirstOrDefault();
        if (asset is null)
        {
            RealPreviewStatusMessage = "Prvo dodajte video ili audio fajl (dugme \"Dodaj medije\").";
            return;
        }

        if (!File.Exists(asset.Asset.FilePath))
        {
            RealPreviewStatusMessage = $"Fajl više ne postoji na disku: {asset.Asset.FilePath}";
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(asset.Asset.FilePath)
            {
                UseShellExecute = true
            });

            RealPreviewStatusMessage = $"Otvoreno u vašem plejeru: {asset.FileName}";
            _logger.Information("Fajl otvoren u sistemskom plejeru: {Path}", asset.Asset.FilePath);
        }
        catch (Exception ex)
        {
            RealPreviewStatusMessage = $"Nije moguće otvoriti fajl: {ex.Message}";
            _logger.Error(ex, "Otvaranje u sistemskom plejeru nije uspelo");
        }
    }

    private async Task RenderRealPreviewCoreAsync()
    {
        if (!Timeline.CurrentTracks.Any(t => t.Clips.Count > 0))
        {
            RealPreviewStatusMessage = "Dodajte bar jedan klip na vremensku traku pre renderovanja pravog pregleda.";
            return;
        }

        if (!RealPreview.IsAvailable)
        {
            RealPreviewStatusMessage = RealPreview.UnavailableReason ?? "Pravi plejer nije dostupan na ovom računaru.";
            return;
        }

        Timeline.SaveToProject();
        IsRenderingRealPreview = true;
        RealPreviewStatusMessage = "Renderovanje pravog pregleda u toku...";

        var previewPath = Path.Combine(AppSettings.PreviewCacheFolder(), $"{Project.Id}-preview.mp4");
        var job = new RenderJob
        {
            ProjectName = Project.Name,
            Settings = new RenderSettings
            {
                OutputFilePath = previewPath,
                OverwriteConfirmed = true,
                Preset = "ultrafast",
                Crf = 28
            }
        };

        try
        {
            var outputPath = await _renderService.RenderAsync(CreatePreviewRenderProject(Project.Timeline), job);
            PlayerAspectRatio = ProjectAspectRatio;
            await RealPreview.LoadAndPlayAsync(outputPath);
            RealPreviewStatusMessage = "Pravi pregled je spreman i pušta se, sa zvukom.";
            _logger.Information("Pravi pregled renderovan i pušten: {Path}", outputPath);
        }
        catch (Exception ex)
        {
            RealPreviewStatusMessage = $"Renderovanje pravog pregleda nije uspelo: {ex.Message}";
            _logger.Error(ex, "Renderovanje pravog pregleda nije uspelo");
        }
        finally
        {
            IsRenderingRealPreview = false;
        }
    }

    /// <summary>
    /// "Renderuj deo oko plejhed-a (brzo)" - real, researched answer to "rendering the whole timeline
    /// before anything plays is too slow" for a long project: instead of chasing true live compositing
    /// (researched via a real comparable open-source project - FramePFX, github.com/AngryCarrot789/FramePFX,
    /// a non-linear editor on this exact C#/Avalonia stack - whose own docs describe live full-timeline
    /// compositing as a still-unsolved performance problem even for a project built specifically for it),
    /// this renders only a short window (<see cref="RangePreviewWindowSeconds"/>) around the current
    /// playhead via <see cref="FfmpegFilterGraphBuilder.ExtractRangeTimeline"/>, so previewing a change deep
    /// into a 30-minute project takes seconds instead of however long the whole project takes to encode.
    /// Reuses the exact same real render pipeline as the full-timeline command above - a temporary
    /// in-memory <see cref="Project"/> wrapping the range-extracted timeline, same media library/format.
    /// </summary>
    [RelayCommand]
    private async Task RenderRealPreviewAroundPlayheadAsync()
    {
        if (!Timeline.CurrentTracks.Any(t => t.Clips.Count > 0))
        {
            RealPreviewStatusMessage = "Dodajte bar jedan klip na vremensku traku pre renderovanja pregleda.";
            return;
        }

        if (!RealPreview.IsAvailable)
        {
            RealPreviewStatusMessage = RealPreview.UnavailableReason ?? "Pravi plejer nije dostupan na ovom računaru.";
            return;
        }

        Timeline.SaveToProject();

        var rangeStart = Math.Max(0, Player.CurrentTimeSeconds - RangePreviewLeadInSeconds);
        var rangeEnd = Math.Min(Timeline.TotalDurationSeconds, rangeStart + RangePreviewWindowSeconds);
        if (rangeEnd <= rangeStart)
        {
            RealPreviewStatusMessage = "Nema ničega na trenutnoj poziciji plejhed-a za renderovanje.";
            return;
        }

        IsRenderingRealPreview = true;
        RealPreviewStatusMessage = FormattableString.Invariant($"Renderovanje dela pregleda ({rangeStart:0.0}s-{rangeEnd:0.0}s) u toku...");

        var rangeTimeline = FfmpegFilterGraphBuilder.ExtractRangeTimeline(Project.Timeline, rangeStart, rangeEnd);
        var previewProject = CreatePreviewRenderProject(rangeTimeline);
        var previewPath = Path.Combine(AppSettings.PreviewCacheFolder(), $"{Project.Id}-range-preview.mp4");
        var job = new RenderJob
        {
            ProjectName = Project.Name,
            Settings = new RenderSettings
            {
                OutputFilePath = previewPath,
                OverwriteConfirmed = true,
                Preset = "ultrafast",
                Crf = 28
            }
        };

        try
        {
            var outputPath = await _renderService.RenderAsync(previewProject, job);
            PlayerAspectRatio = ProjectAspectRatio;
            await RealPreview.LoadAndPlayAsync(outputPath);
            RealPreviewStatusMessage = FormattableString.Invariant($"Deo pregleda ({rangeStart:0.0}s-{rangeEnd:0.0}s) je spreman i pušta se.");
            _logger.Information("Deo pravog pregleda renderovan i pušten: {Path} ({Start}s-{End}s)", outputPath, rangeStart, rangeEnd);
        }
        catch (Exception ex)
        {
            RealPreviewStatusMessage = $"Renderovanje dela pregleda nije uspelo: {ex.Message}";
            _logger.Error(ex, "Renderovanje dela pravog pregleda (oko plejhed-a) nije uspelo");
        }
        finally
        {
            IsRenderingRealPreview = false;
        }
    }

    /// <summary>Preview-only source resolver. A ready proxy is preferred only when its file is still
    /// present. The original MediaAsset.FilePath is never mutated, so export remains full quality.</summary>
    public static string ResolvePreviewSourcePath(MediaAsset asset) =>
        asset.ProxyStatus == MediaProxyStatus.Ready &&
        !string.IsNullOrWhiteSpace(asset.ProxyFilePath) &&
        File.Exists(asset.ProxyFilePath)
            ? asset.ProxyFilePath
            : asset.FilePath;

    /// <summary>Creates a preview-only media library with identical IDs/metadata but proxy-aware paths.
    /// FfmpegFilterGraphBuilder therefore resolves timeline foreign keys normally while reading lower
    /// resolution media only for preview. The real project media library is not modified.</summary>
    public static IReadOnlyList<MediaAsset> BuildPreviewMediaLibrary(IEnumerable<MediaAsset> assets) => assets.Select(asset => new MediaAsset
    {
        Id = asset.Id,
        FilePath = ResolvePreviewSourcePath(asset),
        Kind = asset.Kind,
        Duration = asset.Duration,
        Width = asset.Width,
        Height = asset.Height,
        Fps = asset.Fps,
        VideoCodec = asset.VideoCodec,
        AudioCodec = asset.AudioCodec,
        HasVideoStream = asset.HasVideoStream,
        HasAudioStream = asset.HasAudioStream,
        FileSizeBytes = asset.FileSizeBytes,
        IsFavorite = asset.IsFavorite,
        FolderTag = asset.FolderTag,
        ImportedAt = asset.ImportedAt,
        IsMissing = asset.IsMissing,
        ProbeError = asset.ProbeError,
        ProxyStatus = asset.ProxyStatus,
        ProxyFilePath = asset.ProxyFilePath,
        ProxyError = asset.ProxyError
    }).ToList();

    private Project CreatePreviewRenderProject(Timeline timeline) => new()
    {
        Id = Project.Id,
        Name = Project.Name,
        Format = Project.Format,
        TargetPlatform = Project.TargetPlatform,
        MediaLibrary = BuildPreviewMediaLibrary(Project.MediaLibrary).ToList(),
        Timeline = timeline
    };

    private async Task PersistProxyStateAsync()
    {
        if (string.IsNullOrEmpty(Project.ProjectFilePath)) return;
        Timeline.SaveToProject();
        await _projectRepository.SaveAsync(Project, Project.ProjectFilePath);
    }

    private MediaAssetViewModel CreateItemViewModel(Domain.MediaAsset asset)
    {
        var item = new MediaAssetViewModel(asset);
        item.ToggleFavoriteCommand = new RelayCommand(() =>
        {
            asset.IsFavorite = !asset.IsFavorite;
            item.NotifyAssetChanged();
        });
        item.GenerateProxyCommand = new AsyncRelayCommand(async () =>
        {
            if (_proxyGeneratorService is null)
            {
                StatusMessage = "Proxy servis nije dostupan u ovoj instalaciji.";
                return;
            }
            if (!asset.HasVideoStream || !File.Exists(asset.FilePath))
            {
                StatusMessage = $"Proxy nije moguće napraviti za „{asset.FileName}“ jer originalni video nije dostupan.";
                return;
            }

            Directory.CreateDirectory(AppSettings.ProxyCacheFolder());
            var outputPath = Path.Combine(AppSettings.ProxyCacheFolder(), $"{Project.Id}-{asset.Id}.proxy.mp4");
            asset.ProxyStatus = MediaProxyStatus.Generating;
            asset.ProxyError = null;
            item.NotifyAssetChanged();
            StatusMessage = $"Generišem proxy za „{asset.FileName}“...";

            try
            {
                asset.ProxyFilePath = await _proxyGeneratorService.GenerateProxyAsync(asset.FilePath, outputPath);
                asset.ProxyStatus = MediaProxyStatus.Ready;
                asset.ProxyError = null;
                StatusMessage = $"Proxy za „{asset.FileName}“ je spreman. Preview sada koristi proxy, export i dalje koristi original.";
                RefreshPreviewFrame(Player.CurrentTimeSeconds);
            }
            catch (OperationCanceledException)
            {
                asset.ProxyStatus = MediaProxyStatus.Original;
                asset.ProxyError = null;
                throw;
            }
            catch (Exception ex)
            {
                asset.ProxyStatus = MediaProxyStatus.Failed;
                asset.ProxyError = ex.Message;
                StatusMessage = $"Proxy za „{asset.FileName}“ nije napravljen: {ex.Message}";
                _logger.Warning(ex, "Proxy generation failed for {Path}", asset.FilePath);
            }
            finally
            {
                item.NotifyAssetChanged();
                await PersistProxyStateAsync();
            }
        });
        item.RemoveProxyCommand = new AsyncRelayCommand(async () =>
        {
            if (!string.IsNullOrWhiteSpace(asset.ProxyFilePath) && File.Exists(asset.ProxyFilePath))
            {
                File.Delete(asset.ProxyFilePath);
            }
            asset.ProxyFilePath = null;
            asset.ProxyStatus = MediaProxyStatus.Original;
            asset.ProxyError = null;
            item.NotifyAssetChanged();
            await PersistProxyStateAsync();
            RefreshPreviewFrame(Player.CurrentTimeSeconds);
            StatusMessage = $"Proxy za „{asset.FileName}“ je uklonjen. Preview koristi original.";
        });
        item.OpenProxyFolderCommand = new RelayCommand(() =>
        {
            var folder = !string.IsNullOrWhiteSpace(asset.ProxyFilePath)
                ? Path.GetDirectoryName(asset.ProxyFilePath)
                : AppSettings.ProxyCacheFolder();
            if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Proxy folder nije moguće otvoriti: {ex.Message}";
            }
        });

        item.RemoveCommand = new RelayCommand(() =>
        {
            // MediaAssetId is the persisted foreign key used by timeline clips. Removing an asset while
            // a clip still references it makes the project internally invalid and the renderer later has
            // no source file to resolve. Block that destructive state at the UI boundary instead.
            var isUsedOnTimeline = Timeline.CurrentTracks
                .SelectMany(track => track.Clips)
                .Any(clip => string.Equals(clip.MediaAssetId, asset.Id, StringComparison.Ordinal));

            if (isUsedOnTimeline)
            {
                StatusMessage = $"Medij „{asset.FileName}“ se koristi na vremenskoj traci. Prvo uklonite sve njegove klipove sa timeline-a, pa ga onda uklonite iz biblioteke.";
                return;
            }

            Project.MediaLibrary.Remove(asset);
            MediaLibrary.Remove(item);
            StatusMessage = $"Medij „{asset.FileName}“ je uklonjen iz projekta.";
        });
        return item;
    }

    [RelayCommand]
    private void OpenCaptionStyleGallery()
    {
        if (Timeline.SelectedClip is not { IsTextClip: true })
        {
            StatusMessage = "Izaberite titl ili tekst klip na timeline-u, pa ponovo otvorite Stilove titlova.";
            return;
        }

        // Keep every live timeline edit in the project model before navigating away from the workspace.
        Timeline.SaveToProject();
        CaptionStyleGalleryRequested?.Invoke();
    }

    public async Task<string> ApplyCaptionStylePresetAsync(CaptionStylePreset preset)
    {
        if (!Timeline.ApplyCaptionStylePresetToSelected(preset))
        {
            return "Preset nije primenjen: izabrani klip više nije titl/tekst klip.";
        }

        Timeline.SaveToProject();
        if (!string.IsNullOrEmpty(Project.ProjectFilePath))
        {
            await _projectRepository.SaveAsync(Project, Project.ProjectFilePath);
        }

        RefreshPreviewFrame(Player.CurrentTimeSeconds);
        RaiseCaptionPreviewChanged();

        var message = $"Stil „{preset.Name}“ je primenjen i sačuvan. Boja/kontura-senka/panel ulaze u finalni FFmpeg render. " +
                      $"Deklarisana animacija {preset.Animation} i granularnost {preset.Granularity} nisu lažno označene kao primenjene dok njihov renderer ne bude dodat.";
        StatusMessage = message;
        return message;
    }

    [RelayCommand]
    private async Task ImportMediaAsync()
    {
        var filters = new[] { VideoFilter, AudioFilter, ImageFilter };
        var files = await _storageService.PickFilesAsync("Dodaj medije u projekat", filters, allowMultiple: true);
        await ImportFilesAsync(files);
    }

    /// <summary>
    /// Entry point for the home screen's "Dodaj tekst u video" shortcut - opens straight into a fresh
    /// project's workspace and immediately does the two steps a user would otherwise have to discover on
    /// their own (import a video, then find and click "+ Tekst traka" + "+ Klip" in the timeline): prompts
    /// for the video file, imports and auto-places it (reusing the exact same tested
    /// <see cref="ImportFilesAsync"/> path everything else already goes through, including the orientation
    /// auto-fix), then adds a Text track with one starter clip so the "Tekst:"/font/size/color/position
    /// controls in <c>WorkspaceView.axaml</c> are immediately visible and ready to use. A no-op (stays on
    /// an empty workspace) if the user cancels the file picker.
    /// </summary>
    public async Task StartAddTextToVideoFlowAsync()
    {
        var files = await _storageService.PickFilesAsync("Izaberi video za dodavanje teksta", new[] { VideoFilter }, allowMultiple: false);
        if (files.Count == 0)
        {
            return;
        }

        await ImportFilesAsync(files);

        if (!Timeline.CurrentTracks.Any(t => t.Kind == TimelineTrackKind.Video && t.Clips.Count > 0))
        {
            return;
        }

        Timeline.AddTextTrackCommand.Execute(null);
        var textTrack = Timeline.Tracks.LastOrDefault(t => t.Track.Kind == TimelineTrackKind.Text);
        textTrack?.AddClipAtPlayheadCommand.Execute(null);

        StatusMessage = "Video je dodat i tekst traka je spremna - kliknite na novi tekst klip u vremenskoj traci da unesete i stilizujete tekst.";
    }

    public async Task ImportFilesAsync(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0)
        {
            return;
        }

        IsImporting = true;
        var imported = 0;
        var failed = 0;
        (int Width, int Height)? formatAdjustedTo = null;

        try
        {
            foreach (var path in filePaths)
            {
                var asset = await _mediaProbeService.ProbeAsync(path);
                Project.MediaLibrary.Add(asset);
                MediaLibrary.Add(CreateItemViewModel(asset));

                if (Timeline.AutoPlaceFirstImportOnEmptyTimeline(asset) && TryAdjustProjectFormatToMatch(asset))
                {
                    formatAdjustedTo = (asset.Width, asset.Height);
                }

                if (asset.ProbeError is null)
                {
                    imported++;
                }
                else
                {
                    failed++;
                    _logger.Warning("Analiza medija nije uspela za {Path}: {Error}", path, asset.ProbeError);
                }
            }

            if (!string.IsNullOrEmpty(Project.ProjectFilePath))
            {
                Timeline.SaveToProject();
                await _projectRepository.SaveAsync(Project, Project.ProjectFilePath);
            }

            var importSummary = failed == 0
                ? $"Uvezeno {imported} fajl(ova)."
                : $"Uvezeno {imported} fajl(ova), {failed} nije uspelo (pogledajte logove).";
            StatusMessage = formatAdjustedTo is { } size
                ? $"{importSummary} Format projekta je prilagođen ovom videu ({size.Width}x{size.Height}) da se ne prikazuje sa crnim trakama."
                : importSummary;
        }
        finally
        {
            IsImporting = false;
        }
    }

    /// <summary>
    /// Real fix for a real, reported point of confusion: a portrait (e.g. 1080x1920) video imported into a
    /// project created with a mismatched (e.g. 1920x1080 horizontal) canvas showed up tiny, pillarboxed
    /// with black bars - correct given the mismatch, but a non-technical user has no reason to expect
    /// "make a new project" and "the video's own orientation" are two independent choices they both have to
    /// get right. Since this only ever runs right after <see cref="TimelineViewModel.AutoPlaceFirstImportOnEmptyTimeline"/>
    /// succeeds (the very first video on a still-empty timeline), resizing the canvas here can never
    /// surprise an edit already in progress - there's nothing on the timeline yet to reflow. Deliberately
    /// scoped to a genuine orientation mismatch (portrait vs. landscape) only, not "any dimension
    /// difference" - a video imported at a different resolution but the same orientation just scales, which
    /// is normal and not what the user is confused by.
    /// </summary>
    private bool TryAdjustProjectFormatToMatch(MediaAsset asset)
    {
        if (asset.Width <= 0 || asset.Height <= 0)
        {
            return false;
        }

        var projectIsPortrait = Project.Format.Height > Project.Format.Width;
        var projectIsSquare = Project.Format.Width == Project.Format.Height;
        var assetIsPortrait = asset.Height > asset.Width;
        var assetIsSquare = asset.Width == asset.Height;

        if (projectIsSquare || assetIsSquare || projectIsPortrait == assetIsPortrait)
        {
            return false;
        }

        Project.Format.Width = asset.Width;
        Project.Format.Height = asset.Height;
        Project.Format.AspectRatio = AspectRatioPreset.Custom;
        RefreshFormatSummaryLabel();
        _logger.Information("Format projekta automatski prilagođen prvom uvezenom videu: {Width}x{Height}", asset.Width, asset.Height);
        return true;
    }

    private void RefreshFormatSummaryLabel()
    {
        FormatSummaryLabel = $"{Project.Format.Width}×{Project.Format.Height}  ·  {Project.Format.Fps:0.##} fps  ·  {Project.Format.Orientation}";
        OnPropertyChanged(nameof(ProjectAspectRatio));
        OnPropertyChanged(nameof(CurrentSafeAreaPreset));
        OnPropertyChanged(nameof(SafeAreaGuideLabel));
        OnPropertyChanged(nameof(IsVerticalSafeArea));
        OnPropertyChanged(nameof(IsSquareSafeArea));
        OnPropertyChanged(nameof(IsHorizontalSafeArea));
    }

    /// <summary>
    /// The project's width:height, which the player panel takes as its own shape. A Shorts project makes
    /// the player tall and narrow instead of showing a sliver of picture between two huge black bars in a
    /// landscape box - the "ako je video u vertikalnom položaju hoću i da plejer bude u vertikalnom
    /// položaju" request. Guarded against a zero-sized format so the layout can never divide by zero.
    /// </summary>
    public double ProjectAspectRatio =>
        Project.Format.Height > 0 && Project.Format.Width > 0
            ? (double)Project.Format.Width / Project.Format.Height
            : 16.0 / 9.0;

    /// <summary>
    /// "Automatski dodaj titlove iz videa" - runs the same local Whisper transcription the standalone
    /// "Generiši titlove (SRT)" tool uses, but places the result directly onto the project's timeline as
    /// real caption clips (see <see cref="TimelineViewModel.AddGeneratedCaptions"/>) instead of only
    /// writing a standalone .srt file the user would otherwise have to import by hand. Deliberately does
    /// NOT auto-download the Whisper model here - that stays a one-time, explicit consent click in the
    /// "Generiši titlove (SRT)" tool (spec: never download without asking), so this command just tells
    /// the user to do that first if the model isn't ready yet.
    /// </summary>
    [RelayCommand]
    private Task GenerateCaptionsForVideoAsync() =>
        _aiWorkerClient is null ? GenerateCaptionsCoreAsync(wordLevel: false) : GenerateSongLyricsAsync();

    [RelayCommand]
    private async Task LoadVerifiedLyricsAsync()
    {
        var files = await _storageService.PickFilesAsync(
            "Izaberite tačan tekst pesme", new[] { LyricsFilter }, allowMultiple: false);
        if (files.Count == 0) return;

        try
        {
            VerifiedLyricsText = await LyricsDocumentReader.ReadAsync(files[0]);
            VerifiedLyricsFileName = Path.GetFileName(files[0]);
            CaptionsStatusMessage = string.IsNullOrWhiteSpace(VerifiedLyricsText)
                ? "Izabrani fajl ne sadrži čitljiv tekst."
                : $"Učitan je provereni tekst: {VerifiedLyricsFileName}. Kliknite „SINHRONIZUJ TAČAN TEKST“.";
        }
        catch (Exception ex)
        {
            CaptionsStatusMessage = $"Tekst pesme nije moguće učitati: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SyncVerifiedLyricsAsync()
    {
        if (string.IsNullOrWhiteSpace(VerifiedLyricsText))
        {
            CaptionsStatusMessage = "Prvo učitajte .txt/.rtf fajl ili nalepite kompletan tekst pesme.";
            return;
        }
        await GenerateSongLyricsAsync();
    }

    /// <summary>
    /// "Automatski dodaj karaoke titlove (reč po reč)" - same pipeline as the line-level command above,
    /// but each transcribed WORD becomes its own short-lived clip on the caption track (via
    /// <see cref="ISubtitleGeneratorService.TranscribeWordsAsync"/>, which uses whisper.cpp's own word-
    /// splitting so the timing is real, not guessed) - so on export/preview, words appear on screen one at
    /// a time exactly when spoken, the "karaoke" style short-form editors (CapCut etc.) use, as opposed to
    /// highlighting one word inside an otherwise-static full sentence (which would need per-character
    /// glyph-width measurement ffmpeg's drawtext doesn't expose - not attempted here).
    /// </summary>
    [RelayCommand]
    private Task GenerateKaraokeCaptionsForVideoAsync() => GenerateCaptionsCoreAsync(wordLevel: true);

    /// <summary>
    /// Song-specific pipeline. Unlike the lightweight speech button, this calls the Python worker that
    /// separates vocals with Demucs and runs a substantially larger faster-whisper model. Returned words
    /// are grouped into editable subtitle lines so the timeline is not flooded with one editor per word.
    /// </summary>
    [RelayCommand]
    private async Task GenerateSongLyricsAsync()
    {
        var videoFilePath = ResolvePrimaryVideoFilePath();
        if (videoFilePath is null)
        {
            CaptionsStatusMessage = "Dodajte MP3 pesmu ili video pre sinhronizacije teksta.";
            return;
        }

        if (_aiWorkerClient is null)
        {
            CaptionsStatusMessage = "AI worker nije dostupan u ovoj instalaciji.";
            return;
        }

        _captionGenerationCts?.Dispose();
        _captionGenerationCts = new CancellationTokenSource();
        var cancellationToken = _captionGenerationCts.Token;
        IsGeneratingCaptions = true;
        CancelCaptionGenerationCommand.NotifyCanExecuteChanged();
        CaptionsStatusMessage = "Proveravam AI alate za pesmu...";

        try
        {
            var capabilities = await _aiWorkerClient.CheckCapabilitiesAsync(cancellationToken);
            if (!capabilities.WorkerReachable || !capabilities.FasterWhisperAvailable)
            {
                CaptionsStatusMessage =
                    "AI za pesme nije instaliran. Otvorite Podešavanja → Alati i instalirajte Python AI paket " +
                    "(faster-whisper + Demucs). Obični mali Whisper model nije dovoljan za pevanje.";
                return;
            }

            var words = new List<AiWorkerWord>();
            string? workerError = null;
            await foreach (var evt in _aiWorkerClient.RunAsync(new AiWorkerRequest
            {
                Profile = AiProcessingProfile.MostAccurate,
                AudioFilePath = Path.GetFullPath(videoFilePath),
                LanguageHint = "sr",
                JobKind = string.IsNullOrWhiteSpace(VerifiedLyricsText)
                    ? AiWorkerJobKind.UnknownSongTranscription
                    : AiWorkerJobKind.KnownSongAlignment,
                VerifiedLyrics = string.IsNullOrWhiteSpace(VerifiedLyricsText) ? null : VerifiedLyricsText
            }, cancellationToken))
            {
                if (!string.IsNullOrWhiteSpace(evt.Message) && evt.Type is AiWorkerEventType.Progress or AiWorkerEventType.Warning)
                {
                    CaptionsStatusMessage = evt.Message;
                }
                if (evt.Type == AiWorkerEventType.Result && evt.Words is not null)
                {
                    words.AddRange(evt.Words);
                }
                if (evt.Type == AiWorkerEventType.Error)
                {
                    workerError = evt.Message;
                }
            }

            if (workerError is not null)
            {
                throw new InvalidOperationException(workerError);
            }

            var lines = GroupSongWordsIntoCaptionLines(words);
            Timeline.AddGeneratedCaptions(lines);
            CaptionsStatusMessage = lines.Count == 0
                ? "AI nije pouzdano prepoznao nijedan stih. Pokušajte sa čistijim zvukom ili unesite provereni tekst ručno."
                : string.IsNullOrWhiteSpace(VerifiedLyricsText)
                    ? $"Dodato {lines.Count} prepoznatih stihova. Obavezno ih proverite."
                    : $"Sinhronizovano je {lines.Count} redova vašeg tačnog teksta. Kliknite red za ispravku vremena ili izgleda.";
        }
        catch (OperationCanceledException)
        {
            CaptionsStatusMessage = "Prepoznavanje pesme je prekinuto.";
        }
        catch (Exception ex)
        {
            CaptionsStatusMessage = $"Prepoznavanje pesme nije uspelo: {ex.Message}";
            _logger.Error(ex, "AI prepoznavanje pesme nije uspelo za {File}", videoFilePath);
        }
        finally
        {
            IsGeneratingCaptions = false;
            CancelCaptionGenerationCommand.NotifyCanExecuteChanged();
        }
    }

    public static IReadOnlyList<TranscribedCaptionSegment> GroupSongWordsIntoCaptionLines(
        IReadOnlyList<AiWorkerWord> words, int maximumWordsPerLine = 6)
    {
        var result = new List<TranscribedCaptionSegment>();
        var line = new List<AiWorkerWord>();

        void Flush()
        {
            if (line.Count == 0) return;
            result.Add(new TranscribedCaptionSegment(
                line[0].Start,
                line[^1].End > line[0].Start ? line[^1].End : line[0].Start + TimeSpan.FromMilliseconds(250),
                string.Join(' ', line.Select(w => w.Text.Trim()))));
            line.Clear();
        }

        foreach (var word in words.Where(w => !string.IsNullOrWhiteSpace(w.Text)).OrderBy(w => w.Start))
        {
            // Known-lyrics alignment returns one already verified lyric LINE per item. Do not merge up
            // to six of those lines as if they were individual Whisper words.
            if (word.Text.Trim().Contains(' '))
            {
                Flush();
                result.Add(new TranscribedCaptionSegment(
                    word.Start,
                    word.End > word.Start ? word.End : word.Start + TimeSpan.FromMilliseconds(250),
                    word.Text.Trim()));
                continue;
            }
            if (line.Count > 0 && (word.Start - line[^1].End > TimeSpan.FromMilliseconds(850) || line.Count >= maximumWordsPerLine))
            {
                Flush();
            }
            line.Add(word);
            if (word.Text.EndsWith('.') || word.Text.EndsWith('!') || word.Text.EndsWith('?'))
            {
                Flush();
            }
        }
        Flush();
        return result;
    }

    [RelayCommand(CanExecute = nameof(IsGeneratingCaptions))]
    private void CancelCaptionGeneration() => _captionGenerationCts?.Cancel();

    private async Task GenerateCaptionsCoreAsync(bool wordLevel)
    {
        var videoFilePath = ResolvePrimaryVideoFilePath();
        if (videoFilePath is null)
        {
            CaptionsStatusMessage = "Dodajte video na video traku pre generisanja titlova.";
            return;
        }

        if (!_subtitleGeneratorService.IsModelReady)
        {
            CaptionsStatusMessage = "Model za prepoznavanje govora nije preuzet - otvorite alat \"Generiši titlove (SRT)\" i preuzmite ga (~75 MB, jednom).";
            return;
        }

        _captionGenerationCts?.Dispose();
        _captionGenerationCts = new CancellationTokenSource();
        var cancellationToken = _captionGenerationCts.Token;
        IsGeneratingCaptions = true;
        CancelCaptionGenerationCommand.NotifyCanExecuteChanged();
        CaptionsStatusMessage = "Pripremam zvuk za prepoznavanje...";

        try
        {
            // Whisper is CPU-heavy. Running it on the UI thread made the window appear frozen at
            // "Prepoznavanje govora u toku...", even while recognition was still working. Keep it on
            // a worker thread and emit a heartbeat so the user can distinguish progress from a hang.
            var startedAt = DateTimeOffset.UtcNow;
            var transcription = Task.Run(
                () => wordLevel
                    ? _subtitleGeneratorService.TranscribeWordsAsync(videoFilePath, cancellationToken)
                    : _subtitleGeneratorService.TranscribeAsync(videoFilePath, cancellationToken),
                cancellationToken);

            while (!transcription.IsCompleted)
            {
                var heartbeat = Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                if (await Task.WhenAny(transcription, heartbeat) == transcription)
                {
                    break;
                }

                var elapsed = DateTimeOffset.UtcNow - startedAt;
                CaptionsStatusMessage = $"Prepoznavanje govora je u toku ({elapsed.Minutes:00}:{elapsed.Seconds:00})...";
            }

            var segments = await transcription;
            Timeline.AddGeneratedCaptions(segments);
            CaptionsStatusMessage = segments.Count == 0
                ? "Nije prepoznat nijedan izgovoren tekst u ovom videu."
                : wordLevel
                    ? $"Dodato {segments.Count} reč(i) na vremensku traku (karaoke)."
                    : $"Dodato {segments.Count} titl(ova) na vremensku traku.";
            _logger.Information("Automatski generisani titlovi dodati na traku: {Count} segmenata iz {File} (karaoke={WordLevel})", segments.Count, videoFilePath, wordLevel);
        }
        catch (OperationCanceledException)
        {
            CaptionsStatusMessage = "Prepoznavanje govora je prekinuto. Nijedan titl nije dodat.";
            _logger.Information("Automatsko generisanje titlova je prekinuto za {File}", videoFilePath);
        }
        catch (Exception ex)
        {
            CaptionsStatusMessage = $"Generisanje titlova nije uspelo: {ex.Message}";
            _logger.Error(ex, "Automatsko generisanje titlova nije uspelo za {File}", videoFilePath);
        }
        finally
        {
            IsGeneratingCaptions = false;
            CancelCaptionGenerationCommand.NotifyCanExecuteChanged();
        }
    }

    private string? ResolvePrimaryVideoFilePath()
    {
        var clip = Timeline.CurrentTracks
            .Where(t => t.Kind is TimelineTrackKind.Video or TimelineTrackKind.Audio)
            .SelectMany(t => t.Clips)
            .OrderBy(c => c.TimelineStartSeconds)
            .FirstOrDefault(c => c.MediaAssetId is not null);

        if (clip is not null)
        {
            return Project.MediaLibrary.FirstOrDefault(a => a.Id == clip.MediaAssetId)?.FilePath;
        }

        // Known-lyrics synchronization must also work when the user imported a standalone MP3 but has
        // not yet created an audio track. Prefer the explicit media selection, then the first real
        // audio/video asset in the library.
        return Timeline.SelectedMediaAsset?.Asset.FilePath
            ?? Project.MediaLibrary.FirstOrDefault(a => a.HasAudioStream || a.HasVideoStream)?.FilePath;
    }

    /// <summary>
    /// The render queue reads straight off <see cref="Project"/>.Timeline.Tracks, which is only ever
    /// updated by <see cref="TimelineViewModel.SaveToProject"/> - without calling it here first, an
    /// export triggered right after editing (before the next auto-save-on-import or manual "Sačuvaj
    /// projekat" click) would silently render whatever was last saved instead of what's actually on
    /// screen. A real bug, not a hypothetical: found by reading how RenderQueueViewModel is constructed.
    /// </summary>
    [RelayCommand]
    private void ExportVideo()
    {
        Timeline.SaveToProject();
        ExportRequested?.Invoke();
    }

    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        if (string.IsNullOrEmpty(Project.ProjectFilePath))
        {
            return;
        }

        Timeline.SaveToProject();
        await _projectRepository.SaveAsync(Project, Project.ProjectFilePath);
        StatusMessage = "Projekat je sačuvan.";
        _logger.Information("Projekat {ProjectName} sačuvan ručno", Project.Name);
    }
}
