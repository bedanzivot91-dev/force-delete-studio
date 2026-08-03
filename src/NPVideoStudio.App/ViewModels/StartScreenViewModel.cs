using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using NPVideoStudio.App.Services;
using Serilog;

namespace NPVideoStudio.App.ViewModels;

public sealed partial class StartScreenViewModel : ViewModelBase
{
    private readonly IRecentProjectsService _recentProjectsService;
    private readonly IProjectRepository _projectRepository;
    private readonly IAutoSaveService _autoSaveService;
    private readonly IStorageService _storageService;
    private readonly ILogger _logger;

    public ObservableCollection<RecentProjectItemViewModel> RecentProjects { get; } = new();

    public bool HasRecentProjects => RecentProjects.Count > 0;

    /// <summary>Features listed in the spec that are not implemented yet. Shown disabled with a clear
    /// "uskoro" label instead of a dead button that looks functional (spec §53).</summary>
    public IReadOnlyList<string> PlannedFeatures { get; } = new[]
    {
        "Kreiraj video iz šablona",
        "Brzi video od slike i pesme",
        "Automatski video sa utisnutim titlovima (na slici)",
        "Upravljanje šablonima",
        "Upravljanje fontovima",
        "Upravljanje efektima"
    };

    [ObservableProperty]
    private string? _recoveryMessage;

    public bool HasRecoveryMessage => !string.IsNullOrEmpty(RecoveryMessage);

    partial void OnRecoveryMessageChanged(string? value) => OnPropertyChanged(nameof(HasRecoveryMessage));

    private string? _recoveryProjectPath;
    private string? _recoveryAutoSavePath;

    public event Action<Project>? ProjectOpened;
    public event Action<TargetPlatform?>? NewProjectRequested;
    public event Action? SettingsRequested;
    public event Action? DiagnosticsRequested;
    public event Action? SongHighlightsRequested;
    public event Action? LyricSearchRequested;
    public event Action? YouTubeDownloadRequested;
    public event Action? SubtitleGeneratorRequested;
    public event Action? DependencyManagerRequested;
    public event Action? MySongsRequested;
    public event Action? CaptionEditorRequested;
    public event Action? CaptionStyleGalleryRequested;
    public event Action? VideoLayoutAnalyzerRequested;

    public StartScreenViewModel(IRecentProjectsService recentProjectsService, IProjectRepository projectRepository,
        IAutoSaveService autoSaveService, IStorageService storageService, ILogger logger)
    {
        _recentProjectsService = recentProjectsService;
        _projectRepository = projectRepository;
        _autoSaveService = autoSaveService;
        _storageService = storageService;
        _logger = logger.ForContext("SourceContext", nameof(StartScreenViewModel));
        RecentProjects.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentProjects));
    }

    public async Task InitializeAsync()
    {
        var recent = await _recentProjectsService.GetRecentAsync();
        RecentProjects.Clear();
        foreach (var entry in recent)
        {
            var openCommand = new AsyncRelayCommand(() => OpenProjectFromPathAsync(entry.ProjectFilePath), () => !entry.IsMissing);
            var removeCommand = new AsyncRelayCommand(async () =>
            {
                await _recentProjectsService.RemoveAsync(entry.ProjectFilePath);
                var toRemove = RecentProjects.FirstOrDefault(i => i.ProjectFilePath == entry.ProjectFilePath);
                if (toRemove is not null)
                {
                    RecentProjects.Remove(toRemove);
                }
            });
            RecentProjects.Add(new RecentProjectItemViewModel(entry, openCommand, removeCommand));
        }

        foreach (var entry in recent.Where(e => !e.IsMissing))
        {
            var autoSave = await _autoSaveService.FindRecoverableAutoSaveAsync(entry.ProjectFilePath);
            if (autoSave is not null)
            {
                _recoveryProjectPath = entry.ProjectFilePath;
                _recoveryAutoSavePath = autoSave;
                RecoveryMessage = $"Pronađena je automatski sačuvana verzija projekta „{entry.ProjectName}“ novija od poslednjeg ručnog čuvanja. Program se verovatno neočekivano zatvorio.";
                break;
            }
        }
    }

    [RelayCommand]
    private void NewProject() => NewProjectRequested?.Invoke(null);

    [RelayCommand]
    private void NewYouTubeVideo() => NewProjectRequested?.Invoke(TargetPlatform.YouTube);

    [RelayCommand]
    private void NewYouTubeShorts() => NewProjectRequested?.Invoke(TargetPlatform.YouTubeShorts);

    [RelayCommand]
    private void NewTikTok() => NewProjectRequested?.Invoke(TargetPlatform.TikTok);

    [RelayCommand]
    private void NewInstagramReel() => NewProjectRequested?.Invoke(TargetPlatform.InstagramReel);

    [RelayCommand]
    private void NewFacebookReel() => NewProjectRequested?.Invoke(TargetPlatform.FacebookReel);

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        var files = await _storageService.PickFilesAsync(
            "Otvori projekat",
            new[] { ("NP Video Studio projekat", new[] { "npvsproject" }) },
            allowMultiple: false);

        if (files.Count == 0)
        {
            return;
        }

        await OpenProjectFromPathAsync(files[0]);
    }

    [RelayCommand]
    private async Task RecoverAutoSaveAsync()
    {
        if (_recoveryAutoSavePath is null)
        {
            return;
        }

        await OpenProjectFromPathAsync(_recoveryAutoSavePath, originalPathOverride: _recoveryProjectPath);
        RecoveryMessage = null;
    }

    [RelayCommand]
    private void DismissRecovery() => RecoveryMessage = null;

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke();

    [RelayCommand]
    private void OpenDiagnostics() => DiagnosticsRequested?.Invoke();

    [RelayCommand]
    private void OpenDependencyManager() => DependencyManagerRequested?.Invoke();

    [RelayCommand]
    private void OpenSongHighlights() => SongHighlightsRequested?.Invoke();

    [RelayCommand]
    private void OpenLyricSearch() => LyricSearchRequested?.Invoke();

    [RelayCommand]
    private void OpenYouTubeDownload() => YouTubeDownloadRequested?.Invoke();

    [RelayCommand]
    private void OpenSubtitleGenerator() => SubtitleGeneratorRequested?.Invoke();

    [RelayCommand]
    private void OpenMySongs() => MySongsRequested?.Invoke();

    [RelayCommand]
    private void OpenCaptionEditor() => CaptionEditorRequested?.Invoke();

    [RelayCommand]
    private void OpenCaptionStyleGallery() => CaptionStyleGalleryRequested?.Invoke();

    [RelayCommand]
    private void OpenVideoLayoutAnalyzer() => VideoLayoutAnalyzerRequested?.Invoke();

    private async Task OpenProjectFromPathAsync(string path, string? originalPathOverride = null)
    {
        try
        {
            var project = await _projectRepository.LoadAsync(path);
            if (originalPathOverride is not null)
            {
                project.ProjectFilePath = originalPathOverride;
            }
            await _recentProjectsService.RegisterOpenedAsync(project);
            _logger.Information("Otvoren projekat {ProjectName} sa {Path}", project.Name, path);
            ProjectOpened?.Invoke(project);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Otvaranje projekta nije uspelo za putanju {Path}", path);
            RecoveryMessage = $"Otvaranje projekta nije uspelo: {ex.Message}";
        }
    }
}
