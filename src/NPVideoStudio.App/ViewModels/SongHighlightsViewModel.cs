using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Core.Services;
using NPVideoStudio.App.Services;
using Serilog;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// "Isečci iz pesme" tool: load a song, find its loudest non-overlapping stretches, export each as
/// a standalone clip to use as raw material for Shorts/Reels song-announcement teasers. This does not
/// build a video - it hands the user ready-to-use audio clips, since video assembly around them
/// (text, cover art, transitions) is timeline work that lands in a later phase.
/// </summary>
public sealed partial class SongHighlightsViewModel : ViewModelBase
{
    private readonly ISongHighlightService _highlightService;
    private readonly IStorageService _storageService;
    private readonly ILogger _logger;

    private static readonly (string Name, string[] Extensions) AudioFilter =
        ("Audio", new[] { "mp3", "wav", "aac", "m4a", "flac", "ogg", "wma" });

    [ObservableProperty]
    private string? _selectedFilePath;

    [ObservableProperty]
    private string? _selectedFileName;

    [ObservableProperty]
    private int _minDurationSeconds = 30;

    [ObservableProperty]
    private int _maxDurationSeconds = 50;

    [ObservableProperty]
    private int _clipCount = 3;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isExportingAll;

    public ObservableCollection<SongHighlightItemViewModel> Highlights { get; } = new();

    public bool HasHighlights => Highlights.Count > 0;
    public bool HasSelectedFile => !string.IsNullOrEmpty(SelectedFilePath);

    public SongHighlightsViewModel(ISongHighlightService highlightService, IStorageService storageService, ILogger logger)
    {
        _highlightService = highlightService;
        _storageService = storageService;
        _logger = logger.ForContext("SourceContext", nameof(SongHighlightsViewModel));
        Highlights.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasHighlights));
    }

    partial void OnSelectedFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasSelectedFile));
        AnalyzeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Preloads a file handed off from another tool (e.g. a song just downloaded from YouTube).</summary>
    public void LoadFile(string filePath)
    {
        SelectedFilePath = filePath;
        SelectedFileName = Path.GetFileName(filePath);
        Highlights.Clear();
        StatusMessage = null;
    }

    [RelayCommand]
    private async Task PickSongAsync()
    {
        var files = await _storageService.PickFilesAsync("Izaberite pesmu", new[] { AudioFilter }, allowMultiple: false);
        if (files.Count == 0)
        {
            return;
        }

        SelectedFilePath = files[0];
        SelectedFileName = Path.GetFileName(files[0]);
        Highlights.Clear();
        StatusMessage = null;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedFile))]
    private async Task AnalyzeAsync()
    {
        if (SelectedFilePath is null)
        {
            return;
        }

        if (MinDurationSeconds < 10 || MaxDurationSeconds < MinDurationSeconds)
        {
            StatusMessage = "Neispravno podešeno trajanje isečka.";
            return;
        }

        IsAnalyzing = true;
        StatusMessage = null;
        Highlights.Clear();

        try
        {
            var results = await _highlightService.FindHighlightsAsync(
                SelectedFilePath,
                ClipCount,
                TimeSpan.FromSeconds(MinDurationSeconds),
                TimeSpan.FromSeconds(MaxDurationSeconds));

            var index = 1;
            foreach (var highlight in results)
            {
                Highlights.Add(new SongHighlightItemViewModel(highlight, index++, SelectedFilePath, _highlightService, _logger));
            }

            StatusMessage = results.Count == 0
                ? "Nije pronađen nijedan predlog - pesma je možda prekratka za izabrano trajanje."
                : $"Pronađeno {results.Count} predloga. Ovo su najglasniji delovi pesme, ne garantovano refren - proverite ih pre upotrebe.";

            _logger.Information("Analiza pesme {File} pronašla {Count} predloga", SelectedFileName, results.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Analiza nije uspela: {ex.Message}";
            _logger.Error(ex, "Analiza pesme nije uspela za {File}", SelectedFilePath);
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    [RelayCommand]
    private async Task ExportAllAsync()
    {
        if (SelectedFilePath is null || Highlights.Count == 0)
        {
            return;
        }

        var folder = await _storageService.PickFolderAsync("Izaberite folder za isečke");
        if (folder is null)
        {
            return;
        }

        IsExportingAll = true;
        try
        {
            var baseName = Path.GetFileNameWithoutExtension(SelectedFilePath);
            foreach (var item in Highlights)
            {
                var outputPath = Path.Combine(folder, $"{baseName}_isecak{item.Index}.mp3");
                await item.ExportAsync(outputPath);
            }

            StatusMessage = $"Svi isečci su sačuvani u: {folder}";
        }
        finally
        {
            IsExportingAll = false;
        }
    }
}
