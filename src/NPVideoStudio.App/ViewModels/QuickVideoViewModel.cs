using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Core.Services;
using NPVideoStudio.App.Services;
using Serilog;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// "Brzi video od slike i pesme" / "Automatski video sa utisnutim titlovima (na slici)" (spec Phase 10) -
/// one screen serving both former planned-feature tiles, since they are the same underlying wizard at two
/// capability levels (auto-captions on or off, either way toggleable). Runs <see cref="IQuickVideoService"/>
/// directly against picked files - deliberately not project/timeline-based, since a single image+song
/// video has no need for the general timeline model.
/// </summary>
public sealed partial class QuickVideoViewModel : ViewModelBase
{
    private static readonly (string Name, string[] Extensions) ImageFilter = ("Slike", new[] { "jpg", "jpeg", "png", "webp", "bmp" });
    private static readonly (string Name, string[] Extensions) AudioFilter = ("Audio", new[] { "mp3", "wav", "aac", "m4a", "flac", "ogg", "wma" });

    private readonly IQuickVideoService _quickVideoService;
    private readonly ISubtitleGeneratorService _subtitleGeneratorService;
    private readonly IMediaProbeService _mediaProbeService;
    private readonly IStorageService _storageService;
    private readonly ILogger _logger;

    private double _songDurationSeconds;
    private bool _outputPathConfirmedForOverwrite;

    [ObservableProperty]
    private string? _imageFilePath;

    [ObservableProperty]
    private string? _songFilePath;

    [ObservableProperty]
    private string? _outputFilePath;

    partial void OnOutputFilePathChanged(string? value) => _outputPathConfirmedForOverwrite = false;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _autoCaptions;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isDownloadingModel;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _modelStatusMessage;

    public bool IsModelReady => _subtitleGeneratorService.IsModelReady;
    public string ModelSizeLabel => _subtitleGeneratorService.ModelSizeLabel;
    public bool NeedsModelDownload => AutoCaptions && !IsModelReady;

    partial void OnAutoCaptionsChanged(bool value) => OnPropertyChanged(nameof(NeedsModelDownload));

    public event Action? BackRequested;

    public QuickVideoViewModel(
        IQuickVideoService quickVideoService, ISubtitleGeneratorService subtitleGeneratorService,
        IMediaProbeService mediaProbeService, IStorageService storageService, ILogger logger, bool initialAutoCaptions)
    {
        _quickVideoService = quickVideoService;
        _subtitleGeneratorService = subtitleGeneratorService;
        _mediaProbeService = mediaProbeService;
        _storageService = storageService;
        _logger = logger.ForContext("SourceContext", nameof(QuickVideoViewModel));
        AutoCaptions = initialAutoCaptions;
    }

    [RelayCommand]
    private async Task PickImageAsync()
    {
        var files = await _storageService.PickFilesAsync("Izaberite sliku", new[] { ImageFilter }, allowMultiple: false);
        if (files.Count > 0)
        {
            ImageFilePath = files[0];
        }
    }

    [RelayCommand]
    private async Task PickSongAsync()
    {
        var files = await _storageService.PickFilesAsync("Izaberite pesmu", new[] { AudioFilter }, allowMultiple: false);
        if (files.Count == 0)
        {
            return;
        }

        SongFilePath = files[0];
        StatusMessage = null;

        try
        {
            var asset = await _mediaProbeService.ProbeAsync(files[0]);
            _songDurationSeconds = asset.Duration.TotalSeconds;
        }
        catch (Exception ex)
        {
            _songDurationSeconds = 0;
            _logger.Warning(ex, "Analiza trajanja pesme nije uspela za {Path}", files[0]);
        }
    }

    [RelayCommand]
    private async Task PickOutputFileAsync()
    {
        var suggested = SongFilePath is null ? "video.mp4" : $"{Path.GetFileNameWithoutExtension(SongFilePath)}.mp4";
        var path = await _storageService.PickSaveFileAsync("Sačuvaj video kao", suggested, new[] { ("MP4 video", new[] { "mp4" }) });
        if (path is null)
        {
            return;
        }

        OutputFilePath = path;
        _outputPathConfirmedForOverwrite = true;
    }

    [RelayCommand]
    private async Task DownloadModelAsync()
    {
        IsDownloadingModel = true;
        ModelStatusMessage = null;
        try
        {
            var progress = new Progress<string>(message => ModelStatusMessage = message);
            await _subtitleGeneratorService.DownloadModelAsync(progress);
            OnPropertyChanged(nameof(IsModelReady));
            OnPropertyChanged(nameof(NeedsModelDownload));
            StartCommand.NotifyCanExecuteChanged();
            _logger.Information("Model za prepoznavanje govora je preuzet (brzi video)");
        }
        catch (Exception ex)
        {
            ModelStatusMessage = ex.Message;
            _logger.Error(ex, "Preuzimanje modela za prepoznavanje govora nije uspelo (brzi video)");
        }
        finally
        {
            IsDownloadingModel = false;
        }
    }

    private bool CanStart() => !IsRunning && !string.IsNullOrEmpty(ImageFilePath) && !string.IsNullOrEmpty(SongFilePath)
        && !string.IsNullOrEmpty(OutputFilePath) && (!AutoCaptions || IsModelReady);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (ImageFilePath is null || SongFilePath is null || OutputFilePath is null)
        {
            return;
        }

        if (File.Exists(OutputFilePath) && !_outputPathConfirmedForOverwrite)
        {
            StatusMessage = "Fajl na toj putanji već postoji - koristite dugme „Sačuvaj kao“ da potvrdite prepisivanje, ili unesite drugo ime.";
            return;
        }

        var overwriteConfirmed = _outputPathConfirmedForOverwrite || !File.Exists(OutputFilePath);
        _outputPathConfirmedForOverwrite = false;

        IsRunning = true;
        ProgressPercent = 0;
        StatusMessage = null;
        string? srtPath = null;

        try
        {
            if (AutoCaptions)
            {
                StatusMessage = "Prepoznavanje govora u pesmi u toku...";
                srtPath = Path.Combine(Path.GetTempPath(), $"npvs_quickvideo_{Guid.NewGuid():N}.srt");
                await _subtitleGeneratorService.GenerateSrtAsync(SongFilePath, srtPath);
            }

            StatusMessage = "Izvoz videa u toku...";
            var progress = new Progress<double>(p => ProgressPercent = p);
            var outputPath = await _quickVideoService.CreateAsync(
                ImageFilePath, SongFilePath, _songDurationSeconds, OutputFilePath, overwriteConfirmed,
                subtitleSrtPath: srtPath, progress: progress);

            StatusMessage = $"Video je napravljen: {outputPath}";
            _logger.Information("Brzi video napravljen: {OutputPath} (titlovi: {AutoCaptions})", outputPath, AutoCaptions);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pravljenje videa nije uspelo: {ex.Message}";
            _logger.Error(ex, "Pravljenje brzog videa nije uspelo");
        }
        finally
        {
            if (srtPath is not null && File.Exists(srtPath))
            {
                try { File.Delete(srtPath); } catch (IOException) { }
            }

            IsRunning = false;
        }
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke();
}
