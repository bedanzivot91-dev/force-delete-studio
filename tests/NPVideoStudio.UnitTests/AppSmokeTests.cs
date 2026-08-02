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
        // needs the same headless dispatcher pump AppSmokeTests already relies on elsewhere.
        for (var i = 0; i < 50 && dependencyManager.IsLoading; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }

        Assert.False(dependencyManager.IsLoading);
        Assert.Null(dependencyManager.StatusMessage);
        Assert.Equal(4, dependencyManager.Dependencies.Count);
        Assert.Contains(dependencyManager.Dependencies, d => d.Name == "FFmpeg" && d.IsInstalled);
        Assert.Contains(dependencyManager.Dependencies, d => d.Name == "FFprobe" && d.IsInstalled);
    }
}
