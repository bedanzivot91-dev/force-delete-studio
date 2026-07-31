using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Core.Services;
using NPVideoStudio.App.Services;
using Serilog;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// "Pronađi tekst u pesmi" tool: type a lyric phrase, the local speech-recognition model transcribes
/// the song and reports every place it thinks that phrase is sung, with a confidence score - never a
/// bare "found it", since singing recognition is far less reliable than spoken speech.
/// </summary>
public sealed partial class LyricSearchViewModel : ViewModelBase
{
    private readonly ILyricSearchService _lyricSearchService;
    private readonly IStorageService _storageService;
    private readonly ILogger _logger;

    private static readonly (string Name, string[] Extensions) AudioFilter =
        ("Audio", new[] { "mp3", "wav", "aac", "m4a", "flac", "ogg", "wma" });

    [ObservableProperty]
    private string? _selectedFilePath;

    [ObservableProperty]
    private string? _selectedFileName;

    [ObservableProperty]
    private string _searchPhrase = string.Empty;

    [ObservableProperty]
    private bool _isDownloadingModel;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _modelStatusMessage;

    public ObservableCollection<LyricMatchItemViewModel> Matches { get; } = new();

    public bool IsModelReady => _lyricSearchService.IsModelReady;
    public string ModelSizeLabel => _lyricSearchService.ModelSizeLabel;
    public bool HasSelectedFile => !string.IsNullOrEmpty(SelectedFilePath);
    public bool HasMatches => Matches.Count > 0;
    public bool CanSearch => IsModelReady && HasSelectedFile && !string.IsNullOrWhiteSpace(SearchPhrase) && !IsSearching;

    public LyricSearchViewModel(ILyricSearchService lyricSearchService, IStorageService storageService, ILogger logger)
    {
        _lyricSearchService = lyricSearchService;
        _storageService = storageService;
        _logger = logger.ForContext("SourceContext", nameof(LyricSearchViewModel));
        Matches.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMatches));
    }

    partial void OnSelectedFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasSelectedFile));
        OnPropertyChanged(nameof(CanSearch));
        SearchCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchPhraseChanged(string value)
    {
        OnPropertyChanged(nameof(CanSearch));
        SearchCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSearchingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSearch));
        SearchCommand.NotifyCanExecuteChanged();
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
        Matches.Clear();
        StatusMessage = null;
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
            await _lyricSearchService.DownloadModelAsync(progress);
            OnPropertyChanged(nameof(IsModelReady));
            OnPropertyChanged(nameof(CanSearch));
            SearchCommand.NotifyCanExecuteChanged();
            _logger.Information("Model za prepoznavanje govora je preuzet");
        }
        catch (Exception ex)
        {
            ModelStatusMessage = ex.Message;
            _logger.Error(ex, "Preuzimanje modela za prepoznavanje govora nije uspelo");
        }
        finally
        {
            IsDownloadingModel = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        if (SelectedFilePath is null)
        {
            return;
        }

        IsSearching = true;
        StatusMessage = null;
        Matches.Clear();

        try
        {
            var results = await _lyricSearchService.FindPhraseInSongAsync(SelectedFilePath, SearchPhrase);

            var index = 1;
            foreach (var match in results)
            {
                Matches.Add(new LyricMatchItemViewModel(match, index++, SelectedFilePath, _lyricSearchService, _logger));
            }

            StatusMessage = results.Count == 0
                ? "Tekst nije pronađen u pesmi. Prepoznavanje pevanja je manje pouzdano od govora - pokušajte kraću ili prepoznatljiviju frazu."
                : $"Pronađeno {results.Count} mogućih mesta. Ovo je prepoznavanje pevanja, ne garantovano tačno - proverite pre upotrebe.";

            _logger.Information("Pretraga teksta '{Phrase}' u {File} pronašla {Count} rezultata", SearchPhrase, SelectedFileName, results.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pretraga nije uspela: {ex.Message}";
            _logger.Error(ex, "Pretraga teksta u pesmi nije uspela");
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task ExportAllAsync()
    {
        if (SelectedFilePath is null || Matches.Count == 0)
        {
            return;
        }

        var folder = await _storageService.PickFolderAsync("Izaberite folder za isečke");
        if (folder is null)
        {
            return;
        }

        var baseName = Path.GetFileNameWithoutExtension(SelectedFilePath);
        foreach (var item in Matches)
        {
            var outputPath = Path.Combine(folder, $"{baseName}_tekst{item.Index}.mp3");
            await item.ExportAsync(outputPath);
        }

        StatusMessage = $"Svi isečci su sačuvani u: {folder}";
    }
}
