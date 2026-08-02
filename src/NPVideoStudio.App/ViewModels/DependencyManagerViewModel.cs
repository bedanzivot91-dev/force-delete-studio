using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Core.Diagnostics;
using Serilog;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// "Alati i modeli" screen: real status (found + version, via an actual version-command exit code, not
/// just file existence) for FFmpeg, FFprobe, yt-dlp and the local Whisper model. The Whisper download is
/// the first cancellable long-running operation in the app - everything else that shells out to a
/// process today has no Cancel button anywhere in the UI (a real, documented gap).
/// </summary>
public sealed partial class DependencyManagerViewModel : ViewModelBase
{
    private readonly IDependencyManagerService _service;
    private readonly ILogger _logger;
    private CancellationTokenSource? _downloadCts;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isDownloadingModel;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _modelStatusMessage;

    public ObservableCollection<DependencyItemViewModel> Dependencies { get; } = new();

    public bool HasDownloadableWhisperModel => Dependencies.Any(d => d.CanDownload);

    public DependencyManagerViewModel(IDependencyManagerService service, ILogger logger)
    {
        _service = service;
        _logger = logger.ForContext("SourceContext", nameof(DependencyManagerViewModel));
    }

    partial void OnIsDownloadingModelChanged(bool value) => CancelDownloadCommand.NotifyCanExecuteChanged();

    public Task InitializeAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        StatusMessage = null;
        try
        {
            var results = await _service.GetDependenciesAsync();
            Dependencies.Clear();
            foreach (var info in results)
            {
                Dependencies.Add(new DependencyItemViewModel(info, _service));
            }

            OnPropertyChanged(nameof(HasDownloadableWhisperModel));
            DownloadWhisperModelCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Provera alata nije uspela: {ex.Message}";
            _logger.Error(ex, "Provera zavisnosti nije uspela");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasDownloadableWhisperModel))]
    private async Task DownloadWhisperModelAsync()
    {
        // The button itself is the user's explicit consent to download (~75 MB, spec §38).
        _downloadCts = new CancellationTokenSource();
        IsDownloadingModel = true;
        ModelStatusMessage = null;

        try
        {
            var progress = new Progress<string>(message => ModelStatusMessage = message);
            await _service.DownloadWhisperModelAsync(progress, _downloadCts.Token);
            _logger.Information("Whisper model preuzet preko ekrana Alati i modeli");
            await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
            ModelStatusMessage = "Preuzimanje je otkazano.";
        }
        catch (Exception ex)
        {
            ModelStatusMessage = ex.Message;
            _logger.Error(ex, "Preuzimanje Whisper modela nije uspelo (Alati i modeli)");
        }
        finally
        {
            IsDownloadingModel = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(IsDownloadingModel))]
    private void CancelDownload() => _downloadCts?.Cancel();
}
