using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;

namespace NPVideoStudio.UnitTests;

/// <summary>In-memory stand-in so ViewModel tests don't touch the real shared SQLite database.</summary>
public sealed class FakeSongLibraryRepository : ISongLibraryRepository
{
    public List<SongLibraryEntry> Entries { get; } = new();

    public Task<IReadOnlyList<SongLibraryEntry>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SongLibraryEntry>>(Entries.ToList());

    public Task<SongLibraryEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Entries.FirstOrDefault(e => e.Id == id));

    public Task AddAsync(SongLibraryEntry entry, CancellationToken cancellationToken = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(SongLibraryEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DeleteAsync(Guid id, bool deleteAudioFile, CancellationToken cancellationToken = default)
    {
        Entries.RemoveAll(e => e.Id == id);
        return Task.CompletedTask;
    }
}

/// <summary>Canned fingerprint/match results so ViewModel tests don't need real ffmpeg/fpcalc processes.</summary>
public sealed class FakeSongRecognitionService : ISongRecognitionService
{
    public SongFingerprintResult FingerprintToReturn { get; set; } =
        new() { DurationSeconds = 10, Windows = Array.Empty<SongFingerprintWindow>() };

    public IReadOnlyList<SongMatchCandidate> MatchesToReturn { get; set; } = Array.Empty<SongMatchCandidate>();

    public Task<SongFingerprintResult> ComputeFingerprintAsync(string audioFilePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(FingerprintToReturn);

    public IReadOnlyList<SongMatchCandidate> FindMatches(SongFingerprintResult candidate, IReadOnlyList<SongLibraryEntry> library) =>
        MatchesToReturn;
}
