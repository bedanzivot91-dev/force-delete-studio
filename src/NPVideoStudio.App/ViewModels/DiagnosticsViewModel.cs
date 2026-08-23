using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Core.Diagnostics;
using NPVideoStudio.App.Services;
using Serilog;

namespace NPVideoStudio.App.ViewModels;

public sealed partial class DiagnosticsViewModel : ViewModelBase
{
    private readonly IDiagnosticsService _diagnosticsService;
    private readonly IStorageService _storageService;
    private readonly ILogger _logger;

    public ObservableCollection<DiagnosticCheckItemViewModel> Checks { get; } = new();

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string? _statusMessage;

    public DiagnosticsViewModel(IDiagnosticsService diagnosticsService, IStorageService storageService, ILogger logger)
    {
        _diagnosticsService = diagnosticsService;
        _storageService = storageService;
        _logger = logger.ForContext("SourceContext", nameof(DiagnosticsViewModel));
    }

    [RelayCommand]
    public async Task RunChecksAsync()
    {
        IsRunning = true;
        StatusMessage = null;
        try
        {
            var results = await _diagnosticsService.RunAllChecksAsync();
            Checks.Clear();
            foreach (var result in results)
            {
                Checks.Add(new DiagnosticCheckItemViewModel(result, _diagnosticsService));
            }
            _logger.Information("Dijagnostika pokrenuta: {OkCount} OK, {WarnCount} upozorenja, {ErrCount} grešaka",
                results.Count(r => r.Status == DiagnosticStatus.Ok),
                results.Count(r => r.Status == DiagnosticStatus.Warning),
                results.Count(r => r.Status == DiagnosticStatus.Error));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Dijagnostika nije mogla da se izvrši: {ex.Message}";
            _logger.Error(ex, "Dijagnostika nije uspela");
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task CreateSupportPackageAsync()
    {
        var folder = await _storageService.PickFolderAsync("Izaberite folder za paket podrške");
        if (folder is null)
        {
            return;
        }

        try
        {
            var path = await _diagnosticsService.CreateSupportPackageAsync(folder);
            StatusMessage = $"Paket za podršku je sačuvan: {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pravljenje paketa nije uspelo: {ex.Message}";
            _logger.Error(ex, "Pravljenje paketa za podršku nije uspelo");
        }
    }
}
