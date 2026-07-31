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
}
