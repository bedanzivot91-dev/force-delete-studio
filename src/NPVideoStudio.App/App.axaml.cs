using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Microsoft.Extensions.DependencyInjection;
using NPVideoStudio.AI;
using NPVideoStudio.Core.Diagnostics;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Diagnostics;
using NPVideoStudio.Domain;
using NPVideoStudio.Infrastructure.Logging;
using NPVideoStudio.Infrastructure.Persistence;
using NPVideoStudio.Media;
using NPVideoStudio.App.Services;
using NPVideoStudio.App.ViewModels;
using NPVideoStudio.App.Views;
using Serilog;

namespace NPVideoStudio.App;

public partial class App : Avalonia.Application
{
    private IServiceProvider? _services;
    private ResourceInclude? _currentThemeDictionary;
    private ILogger? _logger;
    private IAutoSaveService? _autoSaveService;

    /// <summary>Exposed for headless UI smoke tests, which need the composition root without a real desktop lifetime.</summary>
    public IServiceProvider? Services => _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var settingsService = new SettingsService();
        // Runs the load on a threadpool thread with no captured context (Task.Run), not just
        // ConfigureAwait(false) inside SettingsService - blocking on a dispatcher-affine thread
        // deadlocked here under Avalonia's headless test dispatcher otherwise.
        Task.Run(() => settingsService.LoadAsync()).GetAwaiter().GetResult();

        _logger = AppLogging.CreateLogger(settingsService.Current.LogRetentionDays);
        Log.Logger = _logger;
        _logger.Information("NP Video Studio se pokreće (verzija {Version})", ThisAssemblyVersion());

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            _logger.Fatal(e.ExceptionObject as Exception, "Neuhvaćen izuzetak na nivou aplikacije");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _logger.Error(e.Exception, "Neuhvaćen izuzetak u pozadinskom zadatku");
            e.SetObserved();
        };

        var services = new ServiceCollection();
        services.AddSingleton(_logger);
        services.AddSingleton<ISettingsService>(settingsService);
        services.AddSingleton<AppDatabase>();
        services.AddSingleton<IProjectRepository, ProjectRepository>();
        services.AddSingleton<IRecentProjectsService, RecentProjectsService>();
        services.AddSingleton<IAutoSaveService, AutoSaveService>();
        services.AddSingleton<IMediaProbeService>(_ => new FfprobeService(settingsService.Current.FfprobePath));
        services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
        services.AddSingleton<IStorageService>(sp => new StorageService(() => (Current as App)?.MainWindowRef));
        services.AddSingleton<ISongHighlightService>(sp =>
            new SongHighlightService(sp.GetRequiredService<IMediaProbeService>(), settingsService.Current.FfmpegPath));
        services.AddSingleton<ILyricSearchService>(_ => new LyricSearchService(settingsService.Current.FfmpegPath));
        services.AddSingleton<IYouTubeDownloadService>(_ =>
            new YouTubeDownloadService(settingsService.Current.FfmpegPath, settingsService.Current.YtDlpPath));
        services.AddSingleton<ISubtitleGeneratorService>(_ => new SubtitleGeneratorService(settingsService.Current.FfmpegPath));
        services.AddSingleton<ISongLibraryRepository, SongLibraryRepository>();
        services.AddSingleton<ISongRecognitionService>(sp =>
            new SongRecognitionService(sp.GetRequiredService<IMediaProbeService>(), settingsService.Current.FfmpegPath));
        services.AddSingleton<IAiWorkerClient, AiWorkerClient>();
        services.AddSingleton<IDependencyManagerService, DependencyManagerService>();

        services.AddTransient<StartScreenViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<DiagnosticsViewModel>();
        services.AddTransient<SongHighlightsViewModel>();
        services.AddTransient<LyricSearchViewModel>();
        services.AddTransient<YouTubeDownloadViewModel>();
        services.AddTransient<SubtitleGeneratorViewModel>();
        services.AddTransient<DependencyManagerViewModel>();
        services.AddTransient<MySongsViewModel>();
        services.AddTransient<CaptionEditorViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        _services = services.BuildServiceProvider();

        ApplyTheme(settingsService.Current.Theme);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindowViewModel = _services.GetRequiredService<MainWindowViewModel>();
            mainWindowViewModel.ThemeChanged += ApplyTheme;

            var window = new MainWindow { DataContext = mainWindowViewModel };
            MainWindowRef = window;
            desktop.MainWindow = window;

            _autoSaveService = _services.GetRequiredService<IAutoSaveService>();

            desktop.ShutdownRequested += (_, _) =>
            {
                Task.Run(() => _autoSaveService.MarkCleanShutdownAsync()).GetAwaiter().GetResult();
                _logger.Information("NP Video Studio se zatvara čisto");
            };

            _ = mainWindowViewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    internal Window? MainWindowRef { get; private set; }

    private void ApplyTheme(AppTheme theme)
    {
        var fileName = theme switch
        {
            AppTheme.DarkCinematic => "DarkCinematic",
            AppTheme.MinimalLight => "MinimalLight",
            AppTheme.ProfessionalStudio => "ProfessionalStudio",
            AppTheme.ObsidianNeon => "ObsidianNeon",
            AppTheme.ArcticGlass => "ArcticGlass",
            AppTheme.CrimsonCyber => "CrimsonCyber",
            AppTheme.MidnightPro => "MidnightPro",
            AppTheme.OceanGlass => "OceanGlass",
            _ => "DarkCinematic"
        };

        var uri = new Uri($"avares://NPVideoStudio/Themes/{fileName}.axaml");
        var themeDictionary = new ResourceInclude(uri) { Source = uri };

        if (_currentThemeDictionary is not null)
        {
            Resources.MergedDictionaries.Remove(_currentThemeDictionary);
        }

        Resources.MergedDictionaries.Add(themeDictionary);
        _currentThemeDictionary = themeDictionary;

        RequestedThemeVariant = theme is AppTheme.MinimalLight or AppTheme.ArcticGlass or AppTheme.OceanGlass
            ? Avalonia.Styling.ThemeVariant.Light
            : Avalonia.Styling.ThemeVariant.Dark;
    }

    private static string ThisAssemblyVersion()
    {
        var version = typeof(App).Assembly.GetName().Version;
        // Only major.minor.build - the SDK-generated 4th component (revision) isn't meaningful here
        // and would make this drift from installer/NPVideoStudio.iss's three-part MyAppVersion.
        return version is null ? "0.1.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
