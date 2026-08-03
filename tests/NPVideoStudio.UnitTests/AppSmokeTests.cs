using System.Threading;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NPVideoStudio.App.ViewModels;
using NPVideoStudio.App.Views;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Boots the real application composition root (DI, theme resources, all XAML views) headlessly.
/// This is what catches runtime-only failures - a DynamicResource that doesn't exist, a binding that
/// throws, an unhandled exception on the start screen - that a plain `dotnet build` cannot see.
/// </summary>
public class AppSmokeTests
{
    [AvaloniaFact]
    public void MainWindow_ShowsStartScreen_WithoutThrowing()
    {
        var app = (NPVideoStudio.App.App)Application.Current!;
        var services = app.Services ?? throw new InvalidOperationException("DI kontejner nije inicijalizovan.");

        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        Task.Run(() => viewModel.InitializeAsync()).GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        Assert.IsType<StartScreenViewModel>(viewModel.CurrentPage);
    }

    [AvaloniaFact]
    public void Navigating_ToSettingsAndDiagnostics_DoesNotThrow()
    {
        var app = (NPVideoStudio.App.App)Application.Current!;
        var services = app.Services ?? throw new InvalidOperationException("DI kontejner nije inicijalizovan.");

        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Task.Run(() => viewModel.InitializeAsync()).GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        viewModel.GoToSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<SettingsViewModel>(viewModel.CurrentPage);

        viewModel.GoToDiagnosticsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<DiagnosticsViewModel>(viewModel.CurrentPage);
    }

    [AvaloniaFact]
    public void OpeningDependencyManager_LoadsRealDependencyStatusesWithoutThrowing()
    {
        var app = (NPVideoStudio.App.App)Application.Current!;
        var services = app.Services ?? throw new InvalidOperationException("DI kontejner nije inicijalizovan.");

        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Task.Run(() => viewModel.InitializeAsync()).GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        var startScreen = (StartScreenViewModel)viewModel.CurrentPage!;
        startScreen.OpenDependencyManagerCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var dependencyManager = Assert.IsType<DependencyManagerViewModel>(viewModel.CurrentPage);

        // The page's own fire-and-forget InitializeAsync (real production navigation pattern - not
        // awaited by the caller, matching how a real desktop app's dispatcher keeps pumping on its own)
        // needs the same headless dispatcher pump AppSmokeTests already relies on elsewhere. This check
        // genuinely launches FFmpeg/FFprobe/yt-dlp as real processes one after another (real -version/
        // --version calls), so a generous budget is needed on a loaded CI runner, not just the near-
        // instant case where a tool is absent and fails fast.
        for (var i = 0; i < 300 && dependencyManager.IsLoading; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(50);
        }

        Assert.False(dependencyManager.IsLoading);
        Assert.Null(dependencyManager.StatusMessage);
        Assert.Equal(7, dependencyManager.Dependencies.Count);
        Assert.Contains(dependencyManager.Dependencies, d => d.Name == "FFmpeg" && d.IsInstalled);
        Assert.Contains(dependencyManager.Dependencies, d => d.Name == "FFprobe" && d.IsInstalled);
    }

    [AvaloniaFact]
    public void Navigating_ToMySongs_LoadsRealLibraryWithoutThrowing()
    {
        var app = (NPVideoStudio.App.App)Application.Current!;
        var services = app.Services ?? throw new InvalidOperationException("DI kontejner nije inicijalizovan.");

        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Task.Run(() => viewModel.InitializeAsync()).GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        var startScreen = (StartScreenViewModel)viewModel.CurrentPage!;
        startScreen.OpenMySongsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var mySongs = Assert.IsType<MySongsViewModel>(viewModel.CurrentPage);

        // Same fire-and-forget InitializeAsync pattern as the dependency-manager page above - this one
        // only touches the (already shared-across-tests, per Phase 0/1 precedent) real SQLite database,
        // no external process, so a short budget is enough.
        for (var i = 0; i < 100 && mySongs.IsLoading; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }

        Assert.False(mySongs.IsLoading);
    }

    [AvaloniaFact]
    public void Navigating_ToCaptionEditor_StartsEmptyAndAllowsARealEditRoundTrip()
    {
        var app = (NPVideoStudio.App.App)Application.Current!;
        var services = app.Services ?? throw new InvalidOperationException("DI kontejner nije inicijalizovan.");

        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Task.Run(() => viewModel.InitializeAsync()).GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        var startScreen = (StartScreenViewModel)viewModel.CurrentPage!;
        startScreen.OpenCaptionEditorCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var editor = Assert.IsType<CaptionEditorViewModel>(viewModel.CurrentPage);
        Assert.False(editor.HasDocument);

        editor.NewDocumentCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(editor.HasDocument);
        Assert.Empty(editor.Words);

        editor.AddWordCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(editor.Words);
        Assert.True(editor.UndoCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Navigating_ToCaptionStyleGallery_LoadsAllTwentyFourPresetsWithRealBrushes()
    {
        var app = (NPVideoStudio.App.App)Application.Current!;
        var services = app.Services ?? throw new InvalidOperationException("DI kontejner nije inicijalizovan.");

        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Task.Run(() => viewModel.InitializeAsync()).GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        var startScreen = (StartScreenViewModel)viewModel.CurrentPage!;
        startScreen.OpenCaptionStyleGalleryCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var gallery = Assert.IsType<CaptionStyleGalleryViewModel>(viewModel.CurrentPage);

        Assert.Equal(24, gallery.Presets.Count);
        Assert.All(gallery.Presets, p => Assert.NotNull(p.TextBrush));

        gallery.SelectedThemeFilter = NPVideoStudio.Domain.AppTheme.ObsidianNeon;
        Dispatcher.UIThread.RunJobs();

        Assert.True(gallery.Presets.Count >= 3);
        Assert.All(gallery.Presets, p => Assert.Equal(NPVideoStudio.Domain.AppTheme.ObsidianNeon, p.Preset.Theme));
    }

    [AvaloniaFact]
    public void Navigating_ToVideoLayoutAnalyzer_StartsWithNoFileSelectedWithoutThrowing()
    {
        var app = (NPVideoStudio.App.App)Application.Current!;
        var services = app.Services ?? throw new InvalidOperationException("DI kontejner nije inicijalizovan.");

        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Task.Run(() => viewModel.InitializeAsync()).GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        var startScreen = (StartScreenViewModel)viewModel.CurrentPage!;
        startScreen.OpenVideoLayoutAnalyzerCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var analyzer = Assert.IsType<VideoLayoutAnalyzerViewModel>(viewModel.CurrentPage);

        Assert.False(analyzer.HasSelectedFile);
        Assert.False(analyzer.CanAnalyze);
        Assert.False(analyzer.HasResult);
    }

    [AvaloniaFact]
    public void Navigating_ToTemplateGallery_SelectingOneOpensNewProjectWithStarterTracksPreselected()
    {
        var app = (NPVideoStudio.App.App)Application.Current!;
        var services = app.Services ?? throw new InvalidOperationException("DI kontejner nije inicijalizovan.");

        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Task.Run(() => viewModel.InitializeAsync()).GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        var startScreen = (StartScreenViewModel)viewModel.CurrentPage!;
        startScreen.OpenTemplateGalleryCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var gallery = Assert.IsType<TemplateGalleryViewModel>(viewModel.CurrentPage);
        Assert.True(gallery.Templates.Count >= 3);

        var template = gallery.Templates.First(t => t.StarterTrackKinds.Count > 0);
        gallery.SelectTemplateCommand.Execute(template);
        Dispatcher.UIThread.RunJobs();

        var newProject = Assert.IsType<NewProjectViewModel>(viewModel.CurrentPage);
        Assert.NotNull(newProject.TemplateInfoLabel);
        Assert.Contains(template.Name, newProject.TemplateInfoLabel);
    }

    [AvaloniaFact]
    public void Navigating_ToQuickVideo_BothPlannedTilesOpenWithExpectedInitialAutoCaptionsState()
    {
        var app = (NPVideoStudio.App.App)Application.Current!;
        var services = app.Services ?? throw new InvalidOperationException("DI kontejner nije inicijalizovan.");

        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Task.Run(() => viewModel.InitializeAsync()).GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        var startScreen = (StartScreenViewModel)viewModel.CurrentPage!;
        startScreen.OpenQuickVideoCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var plainQuickVideo = Assert.IsType<QuickVideoViewModel>(viewModel.CurrentPage);
        Assert.False(plainQuickVideo.AutoCaptions);
        Assert.False(plainQuickVideo.StartCommand.CanExecute(null));

        plainQuickVideo.BackCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<StartScreenViewModel>(viewModel.CurrentPage);

        var startScreenAgain = (StartScreenViewModel)viewModel.CurrentPage!;
        startScreenAgain.OpenQuickVideoWithCaptionsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var captionedQuickVideo = Assert.IsType<QuickVideoViewModel>(viewModel.CurrentPage);
        Assert.True(captionedQuickVideo.AutoCaptions);
    }

    [AvaloniaFact]
    public void AllEightThemes_LoadAsRealAvaloniaResourceDictionaries()
    {
        // Real avares:// resolution + real XAML parsing via Avalonia's own asset loader - the same
        // ResourceInclude class App.axaml.cs: ApplyTheme uses. A typo in a file name or a malformed
        // color would throw here, not just fail an XML well-formedness check.
        var themeFiles = new[]
        {
            "DarkCinematic", "MinimalLight", "ProfessionalStudio",
            "ObsidianNeon", "ArcticGlass", "CrimsonCyber", "MidnightPro", "OceanGlass"
        };

        foreach (var name in themeFiles)
        {
            var uri = new Uri($"avares://NPVideoStudio/Themes/{name}.axaml");
            var include = new Avalonia.Markup.Xaml.Styling.ResourceInclude(uri) { Source = uri };

            var found = include.Loaded.TryGetResource("ThemeAccentBrush", null, out var resource);

            Assert.True(found, $"{name}: ThemeAccentBrush nije pronađen posle stvarnog XAML parsiranja.");
            Assert.NotNull(resource);
        }
    }
}
