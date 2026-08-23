using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Domain;
using Serilog;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Drives MySongsViewModel's real commands (import -> duplicate check -> confirm/cancel, delete) against
/// fakes for the repository and recognition service - plain MVVM object, no Window/Application needed,
/// same pattern as SongHighlightsViewModelTests. Real fingerprinting/matching is already covered directly
/// by SongRecognitionServiceTests/FingerprintMatcherTests; this exercises the ViewModel's own decision
/// flow (never auto-adding a duplicate, letting the user confirm or cancel).
/// </summary>
public class MySongsViewModelTests
{
    private readonly FakeSongLibraryRepository _repository = new();
    private readonly FakeSongRecognitionService _recognitionService = new();
    private readonly FakeStorageService _storageService = new();
    private readonly MySongsViewModel _viewModel;

    public MySongsViewModelTests()
    {
        _viewModel = new MySongsViewModel(_repository, _recognitionService, _storageService, new LoggerConfiguration().CreateLogger());
    }

    [Fact]
    public async Task InitializeAsync_LoadsExistingEntriesFromRepository()
    {
        _repository.Entries.Add(new SongLibraryEntry { Title = "Postojeća", OriginalAudioPath = "/a.mp3" });

        await _viewModel.InitializeAsync();

        Assert.Single(_viewModel.Songs);
        Assert.Equal("Postojeća", _viewModel.Songs[0].Title);
        Assert.True(_viewModel.HasSongs);
    }

    [Fact]
    public async Task ImportCommand_NoDuplicates_SetsPendingImportWithoutAddingYet()
    {
        _storageService.FilesToReturn = new[] { "/tmp/new-song.mp3" };
        _recognitionService.MatchesToReturn = Array.Empty<SongMatchCandidate>();

        await _viewModel.ImportCommand.ExecuteAsync(null);

        Assert.True(_viewModel.HasPendingImport);
        Assert.False(_viewModel.HasDuplicateCandidates);
        Assert.Empty(_repository.Entries); // not added until confirmed
        Assert.Equal("new-song", _viewModel.PendingImportTitle);
    }

    [Fact]
    public async Task ImportCommand_WithDuplicateCandidate_PopulatesDuplicateCandidatesForUserToReview()
    {
        _storageService.FilesToReturn = new[] { "/tmp/new-song.mp3" };
        _recognitionService.MatchesToReturn = new[]
        {
            new SongMatchCandidate { LibraryEntryId = Guid.NewGuid(), Title = "Slična pesma", Confidence = 0.9, AgreeingWindows = 3, AutoAcceptEligible = true }
        };

        await _viewModel.ImportCommand.ExecuteAsync(null);

        Assert.True(_viewModel.HasDuplicateCandidates);
        Assert.Single(_viewModel.DuplicateCandidates);
        // Never auto-added on the app's own initiative, even when a match is AutoAcceptEligible - the
        // user must still explicitly confirm (spec Phase 4: never guess).
        Assert.Empty(_repository.Entries);
    }

    [Fact]
    public async Task ConfirmAddNewCommand_AddsEntryToRepositoryAndSongsList()
    {
        _storageService.FilesToReturn = new[] { "/tmp/new-song.mp3" };
        await _viewModel.ImportCommand.ExecuteAsync(null);
        _viewModel.PendingImportTitle = "Moja nova pesma";
        _viewModel.PendingImportArtist = "Ja";

        await _viewModel.ConfirmAddNewCommand.ExecuteAsync(null);

        var added = Assert.Single(_repository.Entries);
        Assert.Equal("Moja nova pesma", added.Title);
        Assert.Equal("Ja", added.Artist);
        Assert.Single(_viewModel.Songs);
        Assert.False(_viewModel.HasPendingImport);
    }

    [Fact]
    public async Task CancelImportCommand_ClearsPendingStateWithoutAdding()
    {
        _storageService.FilesToReturn = new[] { "/tmp/new-song.mp3" };
        await _viewModel.ImportCommand.ExecuteAsync(null);

        _viewModel.CancelImportCommand.Execute(null);

        Assert.False(_viewModel.HasPendingImport);
        Assert.Empty(_viewModel.DuplicateCandidates);
        Assert.Empty(_repository.Entries);
    }

    [Fact]
    public async Task DeleteRecordOnlyCommand_OnLoadedItem_RemovesFromRepositoryAndList()
    {
        var entry = new SongLibraryEntry { Title = "Za brisanje", OriginalAudioPath = "/a.mp3" };
        _repository.Entries.Add(entry);
        await _viewModel.InitializeAsync();
        var item = _viewModel.Songs[0];

        await ((IAsyncRelayCommand)item.DeleteRecordOnlyCommand).ExecuteAsync(null);

        Assert.Empty(_viewModel.Songs);
        Assert.Empty(_repository.Entries);
    }
}
