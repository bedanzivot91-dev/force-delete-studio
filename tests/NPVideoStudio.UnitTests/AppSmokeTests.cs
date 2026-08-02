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
        Assert.Equal(4, dependencyManager.Dependencies.Count);
        Assert.Contains(dependencyManager.Dependencies, d => d.Name == "FFmpeg" && d.IsInstalled);
        Assert.Contains(dependencyManager.Dependencies, d => d.Name == "FFprobe" && d.IsInstalled);
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
