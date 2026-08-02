using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using NPVideoStudio.App.Services;
using Serilog;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// "Preuzmi sa YouTube-a" tool: paste a link to your own video (Suno song posted to your own channel),
/// confirm ownership, and get the full audio back as a file ready for the highlight-cutter or
/// lyric-search tools. Restricted to YouTube URLs; the ownership checkbox is a required precondition,
/// not decoration - DownloadAsync refuses to run without it (spec-style consent gate).
/// </summary>
public sealed partial class YouTubeDownloadViewModel : ViewModelBase
{
    private readonly IYouTubeDownloadService _downloadService;
    private readonly IStorageService _storageService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger _logger;

    [ObservableProperty]
    private string _videoUrl = string.Empty;

    [ObservableProperty]
    private bool _isFetchingInfo;

    [ObservableProperty]
    private YouTubeVideoInfo? _videoInfo;

    [ObservableProperty]
    private bool _ownershipConfirmed;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _downloadedFilePath;

    public bool HasVideoInfo => VideoInfo is not null;
    public bool HasDownloadedFile => !string.IsNullOrEmpty(DownloadedFilePath);
    public string? VideoTitle => VideoInfo?.Title;
    public string? VideoUploader => VideoInfo?.Uploader;
    public string? VideoDurationLabel => VideoInfo is null ? null : $"{VideoInfo.Duration:mm\\:ss}";
    public bool CanFetchInfo => !string.IsNullOrWhiteSpace(VideoUrl) && !IsFetchingInfo;
    public bool CanDownload => HasVideoInfo && OwnershipConfirmed && !IsDownloading;

    public event Action<string>? OpenInHighlightsRequested;
    public event Action<string>? OpenInLyricSearchRequested;

    public YouTubeDownloadViewModel(
        IYouTubeDownloadService downloadService, IStorageService storageService, ISettingsService settingsService, ILogger logger)
    {
        _downloadService = downloadService;
        _storageService = storageService;
        _settingsService = settingsService;
        _logger = logger.ForContext("SourceContext", nameof(YouTubeDownloadViewModel));
    }

    partial void OnVideoUrlChanged(string value)
    {
        OnPropertyChanged(nameof(CanFetchInfo));
        FetchInfoCommand.NotifyCanExecuteChanged();
        VideoInfo = null;
        DownloadedFilePath = null;
        StatusMessage = null;
    }

    partial void OnIsFetchingInfoChanged(bool value)
    {
        OnPropertyChanged(nameof(CanFetchInfo));
        FetchInfoCommand.NotifyCanExecuteChanged();
    }

    partial void OnVideoInfoChanged(YouTubeVideoInfo? value)
    {
        OnPropertyChanged(nameof(HasVideoInfo));
        OnPropertyChanged(nameof(VideoTitle));
        OnPropertyChanged(nameof(VideoUploader));
        OnPropertyChanged(nameof(VideoDurationLabel));
        OnPropertyChanged(nameof(CanDownload));
        DownloadCommand.NotifyCanExecuteChanged();
    }

    partial void OnOwnershipConfirmedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanDownload));
        DownloadCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsDownloadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanDownload));
        DownloadCommand.NotifyCanExecuteChanged();
    }

    partial void OnDownloadedFilePathChanged(string? value) => OnPropertyChanged(nameof(HasDownloadedFile));

    [RelayCommand(CanExecute = nameof(CanFetchInfo))]
    private async Task FetchInfoAsync()
    {
        IsFetchingInfo = true;
        StatusMessage = null;
        VideoInfo = null;
        OwnershipConfirmed = false;

        try
        {
            VideoInfo = await _downloadService.GetVideoInfoAsync(VideoUrl.Trim());
        }
        catch (Exception ex)
        {
            StatusMessage = $"Učitavanje podataka nije uspelo: {ex.Message}";
            _logger.Error(ex, "Učitavanje podataka o YouTube videu nije uspelo za {Url}", VideoUrl);
        }
        finally
        {
            IsFetchingInfo = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        if (VideoInfo is null)
        {
            return;
        }

        IsDownloading = true;
        StatusMessage = null;
        DownloadedFilePath = null;

        try
        {
            var outputDirectory = Path.Combine(_settingsService.Current.CacheFolder, "Preuzeto sa YouTube-a");
            var progress = new Progress<string>(message => StatusMessage = message);

            DownloadedFilePath = await _downloadService.DownloadAudioAsync(
                VideoUrl.Trim(), outputDirectory, OwnershipConfirmed, progress);

            StatusMessage = $"Preuzeto: {DownloadedFilePath}";
            _logger.Information("Preuzeta pesma sa YouTube-a: {Title} -> {Path}", VideoInfo.Title, DownloadedFilePath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Preuzimanje nije uspelo: {ex.Message}";
            _logger.Error(ex, "Preuzimanje sa YouTube-a nije uspelo za {Url}", VideoUrl);
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void OpenInHighlights()
    {
        if (DownloadedFilePath is not null)
        {
            OpenInHighlightsRequested?.Invoke(DownloadedFilePath);
        }
    }

    [RelayCommand]
    private void OpenInLyricSearch()
    {
        if (DownloadedFilePath is not null)
        {
            OpenInLyricSearchRequested?.Invoke(DownloadedFilePath);
        }
    }
}
