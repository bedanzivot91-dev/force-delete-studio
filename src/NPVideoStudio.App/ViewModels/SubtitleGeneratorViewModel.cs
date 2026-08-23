using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.AI;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using NPVideoStudio.App.Services;
using Serilog;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// "Generiši titlove (SRT)" tool: transcribe an audio/video file locally with Whisper and export a
/// standard .srt file - usable on YouTube/TikTok/Reels uploads or in any other editor today, ahead of
/// this app's own timeline with burned-in captions (spec §53: don't claim more than what's built).
/// </summary>
public sealed partial class SubtitleGeneratorViewModel : ViewModelBase
{
    private readonly ISubtitleGeneratorService _subtitleService;
    private readonly IStorageService _storageService;
    private readonly ILogger _logger;

    private static readonly (string Name, string[] Extensions) MediaFilter = ("Audio/Video", new[]
    {
        "mp3", "wav", "aac", "m4a", "flac", "ogg", "wma", "mp4", "mov", "mkv", "avi", "webm", "m4v", "mpeg", "mpg"
    });

    [ObservableProperty]
    private string? _selectedFilePath;

    [ObservableProperty]
    private string? _selectedFileName;

    [ObservableProperty]
    private bool _isDownloadingModel;

    [ObservableProperty]
    private bool _isGenerating;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _modelStatusMessage;

    [ObservableProperty]
    private string? _generatedSrtPath;

    public bool IsModelReady => _subtitleService.IsModelReady;
    public string ModelSizeLabel => _subtitleService.ModelSizeLabel;
    public bool HasSelectedFile => !string.IsNullOrEmpty(SelectedFilePath);
    public bool HasGeneratedSrt => !string.IsNullOrEmpty(GeneratedSrtPath);
    public bool CanGenerate => IsModelReady && HasSelectedFile && !IsGenerating;

    public event Action<IReadOnlyList<CaptionWord>>? OpenInCaptionEditorRequested;

    public SubtitleGeneratorViewModel(ISubtitleGeneratorService subtitleService, IStorageService storageService, ILogger logger)
    {
        _subtitleService = subtitleService;
        _storageService = storageService;
        _logger = logger.ForContext("SourceContext", nameof(SubtitleGeneratorViewModel));
    }

    partial void OnSelectedFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasSelectedFile));
        OnPropertyChanged(nameof(CanGenerate));
        GenerateCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsGeneratingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanGenerate));
        GenerateCommand.NotifyCanExecuteChanged();
    }

    partial void OnGeneratedSrtPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasGeneratedSrt));
        OpenInCaptionEditorCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Preloads a file handed off from another tool (e.g. a song just downloaded from YouTube).</summary>
    public void LoadFile(string filePath)
    {
        SelectedFilePath = filePath;
        SelectedFileName = Path.GetFileName(filePath);
        GeneratedSrtPath = null;
        StatusMessage = null;
    }

    [RelayCommand]
    private async Task PickFileAsync()
    {
        var files = await _storageService.PickFilesAsync("Izaberite audio ili video fajl", new[] { MediaFilter }, allowMultiple: false);
        if (files.Count == 0)
        {
            return;
        }

        LoadFile(files[0]);
    }

    [RelayCommand]
    private async Task DownloadModelAsync()
    {
        // The button itself is the user's explicit consent to download (~75 MB, spec §38) - no
        // separate dialog needed on top of a labeled, deliberate click.
        IsDownloadingModel = true;
        ModelStatusMessage = null;
        try
        {
            var progress = new Progress<string>(message => ModelStatusMessage = message);
            await _subtitleService.DownloadModelAsync(progress);
            OnPropertyChanged(nameof(IsModelReady));
            OnPropertyChanged(nameof(CanGenerate));
            GenerateCommand.NotifyCanExecuteChanged();
            _logger.Information("Model za prepoznavanje govora je preuzet (titlovi)");
        }
        catch (Exception ex)
        {
            ModelStatusMessage = ex.Message;
            _logger.Error(ex, "Preuzimanje modela za prepoznavanje govora nije uspelo (titlovi)");
        }
        finally
        {
            IsDownloadingModel = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (SelectedFilePath is null)
        {
            return;
        }

        var outputPath = await _storageService.PickSaveFileAsync(
            "Sačuvaj titlove kao",
            Path.GetFileNameWithoutExtension(SelectedFilePath) + ".srt",
            new[] { ("Titlovi (SRT)", new[] { "srt" }) });

        if (outputPath is null)
        {
            return;
        }

        IsGenerating = true;
        StatusMessage = null;
        GeneratedSrtPath = null;

        try
        {
            GeneratedSrtPath = await _subtitleService.GenerateSrtAsync(SelectedFilePath, outputPath);
            StatusMessage = $"Titlovi su sačuvani: {GeneratedSrtPath}";
            _logger.Information("Generisani titlovi za {File} -> {Output}", SelectedFileName, GeneratedSrtPath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Generisanje titlova nije uspelo: {ex.Message}";
            _logger.Error(ex, "Generisanje titlova nije uspelo za {File}", SelectedFilePath);
        }
        finally
        {
            IsGenerating = false;
        }
    }

    [RelayCommand]
    private void OpenGeneratedSrt()
    {
        if (GeneratedSrtPath is null || !File.Exists(GeneratedSrtPath))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = GeneratedSrtPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Otvaranje fajla titlova nije uspelo: {Path}", GeneratedSrtPath);
        }
    }

    [RelayCommand(CanExecute = nameof(HasGeneratedSrt))]
    private void OpenInCaptionEditor()
    {
        if (GeneratedSrtPath is null || !File.Exists(GeneratedSrtPath))
        {
            return;
        }

        try
        {
            var words = CaptionFormatConverter.FromSrt(File.ReadAllText(GeneratedSrtPath));
            OpenInCaptionEditorRequested?.Invoke(words);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Otvaranje u uređivaču titlova nije uspelo: {ex.Message}";
            _logger.Error(ex, "Otvaranje generisanih titlova u uređivaču titlova nije uspelo: {Path}", GeneratedSrtPath);
        }
    }
}
