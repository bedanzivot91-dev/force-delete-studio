using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using NPVideoStudio.App.Services;
using Serilog;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// Project workspace: media import/library, and (since Phase 8) the non-destructive timeline + player.
/// </summary>
public sealed partial class WorkspaceViewModel : ViewModelBase, IDisposable
{
    private readonly IProjectRepository _projectRepository;
    private readonly IMediaProbeService _mediaProbeService;
    private readonly IStorageService _storageService;
    private readonly ILogger _logger;

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

    private static readonly (string Name, string[] Extensions) VideoFilter = ("Video", new[] { "mp4", "mov", "mkv", "avi", "webm", "m4v", "mpeg", "mpg" });
    private static readonly (string Name, string[] Extensions) AudioFilter = ("Audio", new[] { "mp3", "wav", "aac", "m4a", "flac", "ogg", "wma" });
    private static readonly (string Name, string[] Extensions) ImageFilter = ("Slike", new[] { "jpg", "jpeg", "png", "webp", "bmp", "gif", "tiff", "tif" });

    public WorkspaceViewModel(Project project, IProjectRepository projectRepository, IMediaProbeService mediaProbeService, IStorageService storageService, ILogger logger)
    {
        Project = project;
        _projectRepository = projectRepository;
        _mediaProbeService = mediaProbeService;
        _storageService = storageService;
        _logger = logger.ForContext("SourceContext", nameof(WorkspaceViewModel));

        foreach (var asset in project.MediaLibrary)
        {
            MediaLibrary.Add(CreateItemViewModel(asset));
        }

        MediaLibrary.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMedia));

        Player = new PlayerViewModel(totalDurationSeconds: ComputeInitialDuration(project));
        Timeline = new TimelineViewModel(project, MediaLibrary, () => Player.CurrentTimeSeconds);
        Timeline.TimelineChanged += () => Player.Retarget(Timeline.TotalDurationSeconds);
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

    public void Dispose() => Player.Dispose();

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

    [RelayCommand]
    private void ExportVideo() => ExportRequested?.Invoke();

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
