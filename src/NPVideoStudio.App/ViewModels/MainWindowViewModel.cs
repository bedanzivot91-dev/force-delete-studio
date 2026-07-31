using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;

namespace NPVideoStudio.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;
    private readonly IAutoSaveService _autoSaveService;

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private Project? _currentProject;

    public event Action<AppTheme>? ThemeChanged;

    public MainWindowViewModel(IServiceProvider services, IAutoSaveService autoSaveService)
    {
        _services = services;
        _autoSaveService = autoSaveService;
        _autoSaveService.Start(() => CurrentProject);
    }

    public async Task InitializeAsync()
    {
        await ShowStartScreenAsync();
    }

    public async Task ShowStartScreenAsync()
    {
        CurrentProject = null;
        var vm = _services.GetRequiredService<StartScreenViewModel>();
        vm.ProjectOpened += project => CurrentPage = OpenWorkspace(project);
        vm.NewProjectRequested += platform => CurrentPage = CreateNewProjectPage(platform);
        vm.SettingsRequested += () => CurrentPage = CreateSettingsPage();
        vm.DiagnosticsRequested += () => CurrentPage = CreateDiagnosticsPage();
        vm.SongHighlightsRequested += () => CurrentPage = _services.GetRequiredService<SongHighlightsViewModel>();
        vm.LyricSearchRequested += () => CurrentPage = _services.GetRequiredService<LyricSearchViewModel>();

        CurrentPage = vm;
        await vm.InitializeAsync();
    }

    private ViewModelBase CreateNewProjectPage(TargetPlatform? platform)
    {
        var vm = new NewProjectViewModel(
            _services.GetRequiredService<IProjectRepository>(),
            _services.GetRequiredService<IRecentProjectsService>(),
            _services.GetRequiredService<ISettingsService>(),
            _services.GetRequiredService<Serilog.ILogger>(),
            platform);
        vm.ProjectCreated += project => CurrentPage = OpenWorkspace(project);
        return vm;
    }

    private WorkspaceViewModel OpenWorkspace(Project project)
    {
        CurrentProject = project;
        return new WorkspaceViewModel(
            project,
            _services.GetRequiredService<IProjectRepository>(),
            _services.GetRequiredService<IMediaProbeService>(),
            _services.GetRequiredService<Services.IStorageService>(),
            _services.GetRequiredService<Serilog.ILogger>());
    }

    private SettingsViewModel CreateSettingsPage()
    {
        var vm = _services.GetRequiredService<SettingsViewModel>();
        vm.ThemeChanged += theme => ThemeChanged?.Invoke(theme);
        return vm;
    }

    private DiagnosticsViewModel CreateDiagnosticsPage()
    {
        var vm = _services.GetRequiredService<DiagnosticsViewModel>();
        _ = vm.RunChecksAsync();
        return vm;
    }

    [RelayCommand]
    private async Task GoHomeAsync() => await ShowStartScreenAsync();

    [RelayCommand]
    private void GoToSettings() => CurrentPage = CreateSettingsPage();

    [RelayCommand]
    private void GoToDiagnostics() => CurrentPage = CreateDiagnosticsPage();
}
