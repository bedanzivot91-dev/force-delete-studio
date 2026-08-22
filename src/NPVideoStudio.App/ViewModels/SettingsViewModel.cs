using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using NPVideoStudio.App.Services;
using Serilog;

namespace NPVideoStudio.App.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IStorageService _storageService;
    private readonly ILogger _logger;

    public IReadOnlyList<AppTheme> AvailableThemes { get; } = Enum.GetValues<AppTheme>();
    public string ThemeAvailabilityLabel => $"Dostupno je {AvailableThemes.Count} tema. Sve ponuđene teme koriste isti skup semantičkih UI resursa.";
    public IReadOnlyList<ToolUpdatePolicy> AvailableToolUpdatePolicies { get; } = Enum.GetValues<ToolUpdatePolicy>();

    [ObservableProperty]
    private AppTheme _theme;

    [ObservableProperty]
    private string _projectsFolder = string.Empty;

    [ObservableProperty]
    private string _cacheFolder = string.Empty;

    [ObservableProperty]
    private string? _ffmpegPath;

    [ObservableProperty]
    private string? _ffprobePath;

    [ObservableProperty]
    private string? _ytDlpPath;

    [ObservableProperty]
    private bool _autoSaveEnabled;

    [ObservableProperty]
    private int _autoSaveIntervalSeconds;

    [ObservableProperty]
    private int _logRetentionDays;

    [ObservableProperty]
    private ToolUpdatePolicy _toolUpdatePolicy;

    [ObservableProperty]
    private int _toolUpdateIntervalDays;

    [ObservableProperty]
    private string? _statusMessage;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasStatusMessage));

    public event Action<AppTheme>? ThemeChanged;

    public SettingsViewModel(ISettingsService settingsService, IStorageService storageService, ILogger logger)
    {
        _settingsService = settingsService;
        _storageService = storageService;
        _logger = logger.ForContext("SourceContext", nameof(SettingsViewModel));

        var current = _settingsService.Current;
        _theme = current.Theme;
        _projectsFolder = current.ProjectsFolder;
        _cacheFolder = current.CacheFolder;
        _ffmpegPath = current.FfmpegPath;
        _ffprobePath = current.FfprobePath;
        _ytDlpPath = current.YtDlpPath;
        _autoSaveEnabled = current.AutoSaveEnabled;
        _autoSaveIntervalSeconds = current.AutoSaveIntervalSeconds;
        _logRetentionDays = current.LogRetentionDays;
        _toolUpdatePolicy = current.ToolUpdatePolicy;
        _toolUpdateIntervalDays = current.ToolUpdateIntervalDays;
    }

    [RelayCommand]
    private async Task BrowseProjectsFolderAsync()
    {
        var folder = await _storageService.PickFolderAsync("Izaberite folder za projekte");
        if (folder is not null)
        {
            ProjectsFolder = folder;
        }
    }

    [RelayCommand]
    private async Task BrowseCacheFolderAsync()
    {
        var folder = await _storageService.PickFolderAsync("Izaberite cache folder");
        if (folder is not null)
        {
            CacheFolder = folder;
        }
    }

    [RelayCommand]
    private async Task BrowseFfmpegPathAsync()
    {
        var files = await _storageService.PickFilesAsync("Izaberite ffmpeg", Array.Empty<(string, string[])>(), allowMultiple: false);
        if (files.Count > 0)
        {
            FfmpegPath = files[0];
        }
    }

    [RelayCommand]
    private async Task BrowseFfprobePathAsync()
    {
        var files = await _storageService.PickFilesAsync("Izaberite ffprobe", Array.Empty<(string, string[])>(), allowMultiple: false);
        if (files.Count > 0)
        {
            FfprobePath = files[0];
        }
    }

    [RelayCommand]
    private async Task BrowseYtDlpPathAsync()
    {
        var files = await _storageService.PickFilesAsync("Izaberite yt-dlp", Array.Empty<(string, string[])>(), allowMultiple: false);
        if (files.Count > 0)
        {
            YtDlpPath = files[0];
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var current = _settingsService.Current;
        current.Theme = Theme;
        current.ProjectsFolder = ProjectsFolder;
        current.CacheFolder = CacheFolder;
        current.FfmpegPath = string.IsNullOrWhiteSpace(FfmpegPath) ? null : FfmpegPath;
        current.FfprobePath = string.IsNullOrWhiteSpace(FfprobePath) ? null : FfprobePath;
        current.YtDlpPath = string.IsNullOrWhiteSpace(YtDlpPath) ? null : YtDlpPath;
        current.AutoSaveEnabled = AutoSaveEnabled;
        current.AutoSaveIntervalSeconds = Math.Max(10, AutoSaveIntervalSeconds);
        current.LogRetentionDays = Math.Max(1, LogRetentionDays);
        current.ToolUpdatePolicy = ToolUpdatePolicy;
        current.ToolUpdateIntervalDays = Math.Clamp(ToolUpdateIntervalDays, 1, 90);

        await _settingsService.SaveAsync();
        ThemeChanged?.Invoke(Theme);
        StatusMessage = "Podešavanja su sačuvana.";
        _logger.Information("Podešavanja sačuvana");
    }

    [RelayCommand]
    private async Task ResetToDefaultsAsync()
    {
        await _settingsService.ResetToDefaultsAsync();
        var current = _settingsService.Current;
        Theme = current.Theme;
        ProjectsFolder = current.ProjectsFolder;
        CacheFolder = current.CacheFolder;
        FfmpegPath = current.FfmpegPath;
        FfprobePath = current.FfprobePath;
        YtDlpPath = current.YtDlpPath;
        AutoSaveEnabled = current.AutoSaveEnabled;
        AutoSaveIntervalSeconds = current.AutoSaveIntervalSeconds;
        LogRetentionDays = current.LogRetentionDays;
        ToolUpdatePolicy = current.ToolUpdatePolicy;
        ToolUpdateIntervalDays = current.ToolUpdateIntervalDays;
        ThemeChanged?.Invoke(Theme);
        StatusMessage = "Podešavanja su vraćena na podrazumevane vrednosti.";
    }
}
