using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.AI;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
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
    private readonly ILogger _logger;
    private CancellationTokenSource? _framePreviewCts;

    public Project Project { get; }

    public ObservableCollection<MediaAssetViewModel> MediaLibrary { get; } = new();

    public bool HasMedia => MediaLibrary.Count > 0;

    public TimelineViewModel Timeline { get; }
    public PlayerViewModel Player { get; }

    public event Action? ExportRequested;

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isGeneratingCaptions;

    [ObservableProperty]
    private string? _captionsStatusMessage;

    private static readonly (string Name, string[] Extensions) VideoFilter = ("Video", new[] { "mp4", "mov", "mkv", "avi", "webm", "m4v", "mpeg", "mpg" });
    private static readonly (string Name, string[] Extensions) AudioFilter = ("Audio", new[] { "mp3", "wav", "aac", "m4a", "flac", "ogg", "wma" });
    private static readonly (string Name, string[] Extensions) ImageFilter = ("Slike", new[] { "jpg", "jpeg", "png", "webp", "bmp", "gif", "tiff", "tif" });

    public WorkspaceViewModel(Project project, IProjectRepository projectRepository, IMediaProbeService mediaProbeService, IStorageService storageService, IFramePreviewService framePreviewService, ISubtitleGeneratorService subtitleGeneratorService, ILogger logger)
    {
        Project = project;
        _projectRepository = projectRepository;
        _mediaProbeService = mediaProbeService;
        _storageService = storageService;
        _framePreviewService = framePreviewService;
        _subtitleGeneratorService = subtitleGeneratorService;
        _logger = logger.ForContext("SourceContext", nameof(WorkspaceViewModel));

        foreach (var asset in project.MediaLibrary)
        {
            MediaLibrary.Add(CreateItemViewModel(asset));
        }

        MediaLibrary.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMedia));

        Player = new PlayerViewModel(totalDurationSeconds: ComputeInitialDuration(project));
        Timeline = new TimelineViewModel(project, MediaLibrary, () => Player.CurrentTimeSeconds);
        Timeline.TimelineChanged += () =>
        {
            Player.Retarget(Timeline.TotalDurationSeconds);
            RefreshPreviewFrame(Player.CurrentTimeSeconds);
        };
        Player.TimeChanged += RefreshPreviewFrame;
    }

    private void RefreshPreviewFrame(double playheadSeconds)
    {
        _framePreviewCts?.Cancel();
        _framePreviewCts?.Dispose();
        var cts = new CancellationTokenSource();
        _framePreviewCts = cts;

        var request = TimelinePreviewResolver.Resolve(Timeline.CurrentTracks, Project.MediaLibrary, playheadSeconds);
        if (request is null)
        {
            Player.CurrentFrameBitmap = null;
            Player.PreviewStatusMessage = "Nema kadra za prikaz - dodajte klip na video traku i postavite plejhed na njega.";
            return;
        }

        _ = ExtractAndApplyFrameAsync(request.Value, cts.Token);
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

    public void Dispose()
    {
        _framePreviewCts?.Cancel();
        _framePreviewCts?.Dispose();
        Player.Dispose();
    }

    private MediaAssetViewModel CreateItemViewModel(Domain.MediaAsset asset)
    {
        var item = new MediaAssetViewModel(asset);
        item.ToggleFavoriteCommand = new RelayCommand(() =>
        {
            asset.IsFavorite = !asset.IsFavorite;
            item.NotifyAssetChanged();
        });
        item.RemoveCommand = new RelayCommand(() =>
        {
            Project.MediaLibrary.Remove(asset);
            MediaLibrary.Remove(item);
        });
        return item;
    }

    [RelayCommand]
    private async Task ImportMediaAsync()
    {
        var filters = new[] { VideoFilter, AudioFilter, ImageFilter };
        var files = await _storageService.PickFilesAsync("Dodaj medije u projekat", filters, allowMultiple: true);
        await ImportFilesAsync(files);
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

        try
        {
            foreach (var path in filePaths)
            {
                var asset = await _mediaProbeService.ProbeAsync(path);
                Project.MediaLibrary.Add(asset);
                MediaLibrary.Add(CreateItemViewModel(asset));
                Timeline.AutoPlaceFirstImportOnEmptyTimeline(asset);

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

            StatusMessage = failed == 0
                ? $"Uvezeno {imported} fajl(ova)."
                : $"Uvezeno {imported} fajl(ova), {failed} nije uspelo (pogledajte logove).";
        }
        finally
        {
            IsImporting = false;
        }
    }

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
    private Task GenerateCaptionsForVideoAsync() => GenerateCaptionsCoreAsync(wordLevel: false);

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

        IsGeneratingCaptions = true;
        CaptionsStatusMessage = "Prepoznavanje govora u toku...";

        try
        {
            var segments = wordLevel
                ? await _subtitleGeneratorService.TranscribeWordsAsync(videoFilePath)
                : await _subtitleGeneratorService.TranscribeAsync(videoFilePath);
            Timeline.AddGeneratedCaptions(segments);
            CaptionsStatusMessage = segments.Count == 0
                ? "Nije prepoznat nijedan izgovoren tekst u ovom videu."
                : wordLevel
                    ? $"Dodato {segments.Count} reč(i) na vremensku traku (karaoke)."
                    : $"Dodato {segments.Count} titl(ova) na vremensku traku.";
            _logger.Information("Automatski generisani titlovi dodati na traku: {Count} segmenata iz {File} (karaoke={WordLevel})", segments.Count, videoFilePath, wordLevel);
        }
        catch (Exception ex)
        {
            CaptionsStatusMessage = $"Generisanje titlova nije uspelo: {ex.Message}";
            _logger.Error(ex, "Automatsko generisanje titlova nije uspelo za {File}", videoFilePath);
        }
        finally
        {
            IsGeneratingCaptions = false;
        }
    }

    private string? ResolvePrimaryVideoFilePath()
    {
        var clip = Timeline.CurrentTracks
            .Where(t => t.Kind == TimelineTrackKind.Video)
            .SelectMany(t => t.Clips)
            .OrderBy(c => c.TimelineStartSeconds)
            .FirstOrDefault(c => c.MediaAssetId is not null);

        if (clip is null)
        {
            return null;
        }

        return Project.MediaLibrary.FirstOrDefault(a => a.Id == clip.MediaAssetId)?.FilePath;
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
