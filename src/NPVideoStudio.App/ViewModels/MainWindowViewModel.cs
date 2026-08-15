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

    /// <summary>
    /// Nothing called Dispose() on navigation before this - an open WorkspaceViewModel's
    /// DispatcherTimer/frame-preview CancellationTokenSource, or a RenderQueueViewModel's polling timer,
    /// kept running in the background after the user left the page (via "Početni ekran", "Podešavanja",
    /// or "Dijagnostika" in the persistent top navbar - all reachable from any page, not just via each
    /// page's own in-context "Nazad" button). The one deliberate exception: Workspace -> RenderQueue is
    /// not an abandonment, it's a "Nazad" hands the exact same live workspace back.
    /// </summary>
    partial void OnCurrentPageChanging(ViewModelBase? oldValue, ViewModelBase? newValue)
    {
        if (oldValue is IDisposable disposable && !(oldValue is WorkspaceViewModel && newValue is RenderQueueViewModel))
        {
            disposable.Dispose();
        }
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
        vm.YouTubeDownloadRequested += () => CurrentPage = CreateYouTubeDownloadPage();
        vm.SubtitleGeneratorRequested += () => CurrentPage = CreateSubtitleGeneratorPage();
        vm.DependencyManagerRequested += () => CurrentPage = CreateDependencyManagerPage();
        vm.MySongsRequested += () => CurrentPage = CreateMySongsPage();
        vm.CaptionEditorRequested += () => CurrentPage = CreateCaptionEditorPage();
        vm.CaptionStyleGalleryRequested += () => CurrentPage = _services.GetRequiredService<CaptionStyleGalleryViewModel>();
        vm.VideoLayoutAnalyzerRequested += () => CurrentPage = _services.GetRequiredService<VideoLayoutAnalyzerViewModel>();
        vm.TemplateGalleryRequested += () => CurrentPage = CreateTemplateGalleryPage();
        vm.QuickVideoRequested += () => CurrentPage = CreateQuickVideoPage(initialAutoCaptions: false);
        vm.QuickVideoWithCaptionsRequested += () => CurrentPage = CreateQuickVideoPage(initialAutoCaptions: true);

        CurrentPage = vm;
        await vm.InitializeAsync();
    }

    private DependencyManagerViewModel CreateDependencyManagerPage()
    {
        var vm = _services.GetRequiredService<DependencyManagerViewModel>();
        _ = vm.InitializeAsync();
        return vm;
    }

    private MySongsViewModel CreateMySongsPage()
    {
        var vm = _services.GetRequiredService<MySongsViewModel>();
        _ = vm.InitializeAsync();
        return vm;
    }

    private YouTubeDownloadViewModel CreateYouTubeDownloadPage()
    {
        var vm = _services.GetRequiredService<YouTubeDownloadViewModel>();
        vm.OpenInHighlightsRequested += path => CurrentPage = CreateSongHighlightsPage(path);
        vm.OpenInLyricSearchRequested += path => CurrentPage = CreateLyricSearchPage(path);
        vm.OpenInSubtitleGeneratorRequested += path => CurrentPage = CreateSubtitleGeneratorPage(path);
        return vm;
    }

    private SongHighlightsViewModel CreateSongHighlightsPage(string preloadedFilePath)
    {
        var vm = _services.GetRequiredService<SongHighlightsViewModel>();
        vm.LoadFile(preloadedFilePath);
        return vm;
    }

    private LyricSearchViewModel CreateLyricSearchPage(string preloadedFilePath)
    {
        var vm = _services.GetRequiredService<LyricSearchViewModel>();
        vm.LoadFile(preloadedFilePath);
        return vm;
    }

    private SubtitleGeneratorViewModel CreateSubtitleGeneratorPage(string? preloadedFilePath = null)
    {
        var vm = _services.GetRequiredService<SubtitleGeneratorViewModel>();
        if (preloadedFilePath is not null)
        {
            vm.LoadFile(preloadedFilePath);
        }
        vm.OpenInCaptionEditorRequested += words => CurrentPage = CreateCaptionEditorPage(words, "Generiši titlove (SRT)");
        return vm;
    }

    private CaptionEditorViewModel CreateCaptionEditorPage(IEnumerable<CaptionWord>? preloadedWords = null, string? sourceLabel = null)
    {
        var vm = _services.GetRequiredService<CaptionEditorViewModel>();
        if (preloadedWords is not null)
        {
            vm.LoadWords(preloadedWords, sourceLabel);
        }
        return vm;
    }

    private ViewModelBase CreateNewProjectPage(TargetPlatform? platform, ProjectTemplate? template = null)
    {
        var vm = new NewProjectViewModel(
            _services.GetRequiredService<IProjectRepository>(),
            _services.GetRequiredService<IRecentProjectsService>(),
            _services.GetRequiredService<ISettingsService>(),
            _services.GetRequiredService<Serilog.ILogger>(),
            platform,
            template);
        vm.ProjectCreated += project => CurrentPage = OpenWorkspace(project);
        return vm;
    }

    private TemplateGalleryViewModel CreateTemplateGalleryPage()
    {
        var vm = _services.GetRequiredService<TemplateGalleryViewModel>();
        vm.TemplateSelected += template => CurrentPage = CreateNewProjectPage(platform: null, template: template);
        return vm;
    }

    private QuickVideoViewModel CreateQuickVideoPage(bool initialAutoCaptions)
    {
        var vm = new QuickVideoViewModel(
            _services.GetRequiredService<IQuickVideoService>(),
            _services.GetRequiredService<ISubtitleGeneratorService>(),
            _services.GetRequiredService<IMediaProbeService>(),
            _services.GetRequiredService<Services.IStorageService>(),
            _services.GetRequiredService<Serilog.ILogger>(),
            initialAutoCaptions);
        vm.BackRequested += async () => await ShowStartScreenAsync();
        return vm;
    }

    private WorkspaceViewModel OpenWorkspace(Project project)
    {
        CurrentProject = project;
        var workspace = new WorkspaceViewModel(
            project,
            _services.GetRequiredService<IProjectRepository>(),
            _services.GetRequiredService<IMediaProbeService>(),
            _services.GetRequiredService<Services.IStorageService>(),
            _services.GetRequiredService<IFramePreviewService>(),
            _services.GetRequiredService<ISubtitleGeneratorService>(),
            _services.GetRequiredService<Serilog.ILogger>());
        workspace.ExportRequested += () => CurrentPage = CreateRenderQueuePage(workspace);
        return workspace;
    }

    private RenderQueueViewModel CreateRenderQueuePage(WorkspaceViewModel workspace)
    {
        var vm = new RenderQueueViewModel(
            workspace.Project,
            _services.GetRequiredService<IRenderService>(),
            _services.GetRequiredService<Services.IStorageService>(),
            _services.GetRequiredService<Serilog.ILogger>());
        // OnCurrentPageChanging disposes vm here automatically (and specifically skips disposing
        // workspace on this transition, since it's the exact same live instance being handed back).
        vm.BackRequested += () => CurrentPage = workspace;
        return vm;
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
