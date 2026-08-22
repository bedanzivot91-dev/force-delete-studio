using NPVideoStudio.Domain;

namespace NPVideoStudio.Core.Services;

/// <summary>CRUD for the local "Moje pesme" song library (SQLite-backed, spec Phase 4).</summary>
public interface ISongLibraryRepository
{
    Task<IReadOnlyList<SongLibraryEntry>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SongLibraryEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(SongLibraryEntry entry, CancellationToken cancellationToken = default);

    Task UpdateAsync(SongLibraryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Removes the library record. Deleting the underlying audio file is a separate, explicit
    /// opt-in (<paramref name="deleteAudioFile"/>) - never implied by removing the record (spec Phase 4:
    /// "delete-record-without-deleting-file").</summary>
    Task DeleteAsync(Guid id, bool deleteAudioFile, CancellationToken cancellationToken = default);
}
