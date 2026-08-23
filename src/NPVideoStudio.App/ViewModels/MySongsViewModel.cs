using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using NPVideoStudio.App.Services;
using Serilog;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// "Moje pesme" library screen (spec Phase 4): import audio, compute a Chromaprint fingerprint, check
/// for existing duplicates - never auto-picked, the top matches are shown and the user decides or adds
/// it as new anyway - then store title/artist/lyrics/fingerprint locally. Deleting a record never
/// deletes the underlying audio file unless the user explicitly asks for that too.
/// </summary>
public sealed partial class MySongsViewModel : ViewModelBase
{
    private static readonly (string Name, string[] Extensions) AudioFilter =
        ("Audio", new[] { "mp3", "wav", "flac", "m4a", "aac", "ogg" });

    private readonly ISongLibraryRepository _repository;
    private readonly ISongRecognitionService _recognitionService;
    private readonly IStorageService _storageService;
    private readonly ILogger _logger;

    private SongFingerprintResult? _pendingFingerprint;

    public ObservableCollection<SongLibraryItemViewModel> Songs { get; } = new();
    public ObservableCollection<SongMatchCandidate> DuplicateCandidates { get; } = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _pendingImportFilePath;

    [ObservableProperty]
    private string _pendingImportTitle = string.Empty;

    [ObservableProperty]
    private string _pendingImportArtist = string.Empty;

    public bool HasPendingImport => PendingImportFilePath is not null;
    public bool HasDuplicateCandidates => DuplicateCandidates.Count > 0;
    public bool HasSongs => Songs.Count > 0;

    public MySongsViewModel(
        ISongLibraryRepository repository, ISongRecognitionService recognitionService, IStorageService storageService, ILogger logger)
    {
        _repository = repository;
        _recognitionService = recognitionService;
        _storageService = storageService;
        _logger = logger.ForContext("SourceContext", nameof(MySongsViewModel));
        Songs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSongs));
        DuplicateCandidates.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDuplicateCandidates));
    }

    partial void OnPendingImportFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasPendingImport));
        ConfirmAddNewCommand.NotifyCanExecuteChanged();
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            var entries = await _repository.GetAllAsync();
            Songs.Clear();
            foreach (var entry in entries)
            {
                Songs.Add(CreateItem(entry));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Učitavanje biblioteke nije uspelo: {ex.Message}";
            _logger.Error(ex, "Učitavanje biblioteke pesama nije uspelo");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private SongLibraryItemViewModel CreateItem(SongLibraryEntry entry)
    {
        var reanalyze = new AsyncRelayCommand(() => ReanalyzeAsync(entry));
        var deleteRecordOnly = new AsyncRelayCommand(() => DeleteAsync(entry, deleteAudioFile: false));
        var deleteRecordAndFile = new AsyncRelayCommand(() => DeleteAsync(entry, deleteAudioFile: true));
        return new SongLibraryItemViewModel(entry, reanalyze, deleteRecordOnly, deleteRecordAndFile);
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var files = await _storageService.PickFilesAsync("Izaberite audio fajl pesme", new[] { AudioFilter }, allowMultiple: false);
        if (files.Count == 0)
        {
            return;
        }

        var filePath = files[0];
        IsImporting = true;
        StatusMessage = null;
        DuplicateCandidates.Clear();

        try
        {
            _pendingFingerprint = await _recognitionService.ComputeFingerprintAsync(filePath);
            PendingImportFilePath = filePath;
            PendingImportTitle = Path.GetFileNameWithoutExtension(filePath);
            PendingImportArtist = string.Empty;

            var matches = _recognitionService.FindMatches(_pendingFingerprint, Songs.Select(s => s.Entry).ToList());
            foreach (var match in matches)
            {
                DuplicateCandidates.Add(match);
            }

            StatusMessage = DuplicateCandidates.Count > 0
                ? "Moguće je da je ova pesma već u biblioteci - proverite spisak ispod pre dodavanja."
                : "Nema podudaranja u biblioteci - može se dodati kao nova pesma.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Analiza pesme nije uspela: {ex.Message}";
            _logger.Error(ex, "Analiza otiska nije uspela za {File}", filePath);
            _pendingFingerprint = null;
            PendingImportFilePath = null;
        }
        finally
        {
            IsImporting = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasPendingImport))]
    private async Task ConfirmAddNewAsync()
    {
        if (_pendingFingerprint is null || PendingImportFilePath is null)
        {
            return;
        }

        var entry = new SongLibraryEntry
        {
            Title = string.IsNullOrWhiteSpace(PendingImportTitle) ? Path.GetFileNameWithoutExtension(PendingImportFilePath) : PendingImportTitle,
            Artist = PendingImportArtist,
            OriginalAudioPath = PendingImportFilePath,
            Duration = TimeSpan.FromSeconds(_pendingFingerprint.DurationSeconds),
            Fingerprint = JsonSerializer.Serialize(_pendingFingerprint)
        };

        await _repository.AddAsync(entry);
        Songs.Insert(0, CreateItem(entry));
        _logger.Information("Dodata pesma u biblioteku: {Title}", entry.Title);

        CancelImport();
    }

    [RelayCommand]
    private void CancelImport()
    {
        PendingImportFilePath = null;
        PendingImportTitle = string.Empty;
        PendingImportArtist = string.Empty;
        _pendingFingerprint = null;
        DuplicateCandidates.Clear();
        StatusMessage = null;
    }

    private async Task ReanalyzeAsync(SongLibraryEntry entry)
    {
        try
        {
            var fingerprint = await _recognitionService.ComputeFingerprintAsync(entry.OriginalAudioPath);
            entry.Fingerprint = JsonSerializer.Serialize(fingerprint);
            entry.Duration = TimeSpan.FromSeconds(fingerprint.DurationSeconds);
            await _repository.UpdateAsync(entry);
            Songs.FirstOrDefault(s => s.Entry.Id == entry.Id)?.RaiseAllChanged();
            StatusMessage = $"Otisak ponovo izračunat za „{entry.Title}“.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ponovna analiza nije uspela: {ex.Message}";
            _logger.Error(ex, "Ponovna analiza otiska nije uspela za {Title}", entry.Title);
        }
    }

    private async Task DeleteAsync(SongLibraryEntry entry, bool deleteAudioFile)
    {
        await _repository.DeleteAsync(entry.Id, deleteAudioFile);
        var item = Songs.FirstOrDefault(s => s.Entry.Id == entry.Id);
        if (item is not null)
        {
            Songs.Remove(item);
        }
    }
}
