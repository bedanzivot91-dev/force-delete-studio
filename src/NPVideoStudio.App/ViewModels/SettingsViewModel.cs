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

    [ObservableProperty]
    private AppTheme _theme;

    [ObservableProperty]
    private string _projectsFolder = string.Empty;

    [ObservableProperty]
    private string _cacheFolder = string.Empty;

    [ObservableProperty]
    private bool _autoSaveEnabled;

    [ObservableProperty]
    private int _autoSaveIntervalSeconds;

    [ObservableProperty]
    private int _logRetentionDays;

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
        _autoSaveEnabled = current.AutoSaveEnabled;
        _autoSaveIntervalSeconds = current.AutoSaveIntervalSeconds;
        _logRetentionDays = current.LogRetentionDays;
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
    private async Task SaveAsync()
    {
        var current = _settingsService.Current;
        current.Theme = Theme;
        current.ProjectsFolder = ProjectsFolder;
        current.CacheFolder = CacheFolder;
        current.AutoSaveEnabled = AutoSaveEnabled;
        current.AutoSaveIntervalSeconds = Math.Max(10, AutoSaveIntervalSeconds);
        current.LogRetentionDays = Math.Max(1, LogRetentionDays);

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
        AutoSaveEnabled = current.AutoSaveEnabled;
        AutoSaveIntervalSeconds = current.AutoSaveIntervalSeconds;
        LogRetentionDays = current.LogRetentionDays;
        ThemeChanged?.Invoke(Theme);
        StatusMessage = "Podešavanja su vraćena na podrazumevane vrednosti.";
    }
}
