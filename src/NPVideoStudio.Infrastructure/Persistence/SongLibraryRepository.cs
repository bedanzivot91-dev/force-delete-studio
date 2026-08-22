using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;

namespace NPVideoStudio.Infrastructure.Persistence;

public sealed class SongLibraryRepository : ISongLibraryRepository
{
    private readonly AppDatabase _database;

    public SongLibraryRepository(AppDatabase database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<SongLibraryEntry>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Title, Artist, Album, OriginalAudioPath, DurationSeconds, Fingerprint,
                   FullLyrics, LrcPath, AddedAt, VerificationStatus, Notes
            FROM SongLibrary ORDER BY AddedAt DESC;
            """;

        var results = new List<SongLibraryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadEntry(reader));
        }

        return results;
    }

    public async Task<SongLibraryEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Title, Artist, Album, OriginalAudioPath, DurationSeconds, Fingerprint,
                   FullLyrics, LrcPath, AddedAt, VerificationStatus, Notes
            FROM SongLibrary WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadEntry(reader) : null;
    }

    public async Task AddAsync(SongLibraryEntry entry, CancellationToken cancellationToken = default)
    {
        await _database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SongLibrary
                (Id, Title, Artist, Album, OriginalAudioPath, DurationSeconds, Fingerprint,
                 FullLyrics, LrcPath, AddedAt, VerificationStatus, Notes)
            VALUES
                ($id, $title, $artist, $album, $audioPath, $duration, $fingerprint,
                 $lyrics, $lrc, $addedAt, $status, $notes);
            """;
        BindParameters(command, entry);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(SongLibraryEntry entry, CancellationToken cancellationToken = default)
    {
        await _database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SongLibrary SET
                Title = $title, Artist = $artist, Album = $album, OriginalAudioPath = $audioPath,
                DurationSeconds = $duration, Fingerprint = $fingerprint, FullLyrics = $lyrics,
                LrcPath = $lrc, VerificationStatus = $status, Notes = $notes
            WHERE Id = $id;
            """;
        BindParameters(command, entry);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, bool deleteAudioFile, CancellationToken cancellationToken = default)
    {
        await _database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        string? audioPath = null;
        if (deleteAudioFile)
        {
            var entry = await GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
            audioPath = entry?.OriginalAudioPath;
        }

        await using (var connection = _database.OpenConnection())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM SongLibrary WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (deleteAudioFile && audioPath is not null && File.Exists(audioPath))
        {
            File.Delete(audioPath);
        }
    }

    private static void BindParameters(Microsoft.Data.Sqlite.SqliteCommand command, SongLibraryEntry entry)
    {
        command.Parameters.AddWithValue("$id", entry.Id.ToString());
        command.Parameters.AddWithValue("$title", entry.Title);
        command.Parameters.AddWithValue("$artist", entry.Artist);
        command.Parameters.AddWithValue("$album", (object?)entry.Album ?? DBNull.Value);
        command.Parameters.AddWithValue("$audioPath", entry.OriginalAudioPath);
        command.Parameters.AddWithValue("$duration", entry.Duration.TotalSeconds);
        command.Parameters.AddWithValue("$fingerprint", entry.Fingerprint);
        command.Parameters.AddWithValue("$lyrics", (object?)entry.FullLyrics ?? DBNull.Value);
        command.Parameters.AddWithValue("$lrc", (object?)entry.LrcPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$addedAt", entry.AddedAt.ToString("O"));
        command.Parameters.AddWithValue("$status", entry.VerificationStatus.ToString());
        command.Parameters.AddWithValue("$notes", (object?)entry.Notes ?? DBNull.Value);
    }

    private static SongLibraryEntry ReadEntry(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(0)),
        Title = reader.GetString(1),
        Artist = reader.GetString(2),
        Album = reader.IsDBNull(3) ? null : reader.GetString(3),
        OriginalAudioPath = reader.GetString(4),
        Duration = TimeSpan.FromSeconds(reader.GetDouble(5)),
        Fingerprint = reader.GetString(6),
        FullLyrics = reader.IsDBNull(7) ? null : reader.GetString(7),
        LrcPath = reader.IsDBNull(8) ? null : reader.GetString(8),
        AddedAt = DateTimeOffset.Parse(reader.GetString(9)),
        VerificationStatus = Enum.Parse<SongVerificationStatus>(reader.GetString(10)),
        Notes = reader.IsDBNull(11) ? null : reader.GetString(11)
    };
}
