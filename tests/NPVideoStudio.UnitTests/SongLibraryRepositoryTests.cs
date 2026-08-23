using NPVideoStudio.Domain;
using NPVideoStudio.Infrastructure.Persistence;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Real SQLite CRUD + schema-migration tests for the "Moje pesme" library table (spec Phase 4).</summary>
public class SongLibraryRepositoryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"npvs_songlib_test_{Guid.NewGuid():N}");
    private readonly string _dbPath;
    private readonly AppDatabase _database;
    private readonly SongLibraryRepository _repository;

    public SongLibraryRepositoryTests()
    {
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "app.db");
        _database = new AppDatabase(_dbPath);
        _repository = new SongLibraryRepository(_database);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private static SongLibraryEntry MakeEntry(string title = "Test pesma") => new()
    {
        Title = title,
        Artist = "Test izvođač",
        OriginalAudioPath = "/tmp/does-not-matter.mp3",
        Duration = TimeSpan.FromSeconds(123),
        Fingerprint = """{"DurationSeconds":123,"Windows":[]}""",
        FullLyrics = "Neki tekst pesme",
        VerificationStatus = SongVerificationStatus.NotVerified
    };

    [Fact]
    public async Task AddAsync_ThenGetAllAsync_RoundTripsAllFields()
    {
        var entry = MakeEntry();

        await _repository.AddAsync(entry);
        var all = await _repository.GetAllAsync();

        var loaded = Assert.Single(all);
        Assert.Equal(entry.Id, loaded.Id);
        Assert.Equal(entry.Title, loaded.Title);
        Assert.Equal(entry.Artist, loaded.Artist);
        Assert.Equal(entry.OriginalAudioPath, loaded.OriginalAudioPath);
        Assert.Equal(entry.Duration, loaded.Duration);
        Assert.Equal(entry.Fingerprint, loaded.Fingerprint);
        Assert.Equal(entry.FullLyrics, loaded.FullLyrics);
        Assert.Equal(entry.VerificationStatus, loaded.VerificationStatus);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var result = await _repository.GetByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_PersistsChanges()
    {
        var entry = MakeEntry();
        await _repository.AddAsync(entry);

        entry.Title = "Izmenjen naslov";
        entry.VerificationStatus = SongVerificationStatus.Verified;
        await _repository.UpdateAsync(entry);

        var reloaded = await _repository.GetByIdAsync(entry.Id);
        Assert.Equal("Izmenjen naslov", reloaded!.Title);
        Assert.Equal(SongVerificationStatus.Verified, reloaded.VerificationStatus);
    }

    [Fact]
    public async Task DeleteAsync_RecordOnly_RemovesRecordButKeepsAudioFile()
    {
        var audioPath = Path.Combine(_tempDir, "song.mp3");
        await File.WriteAllTextAsync(audioPath, "fake audio bytes");
        var entry = MakeEntry();
        entry.OriginalAudioPath = audioPath;
        await _repository.AddAsync(entry);

        await _repository.DeleteAsync(entry.Id, deleteAudioFile: false);

        Assert.Null(await _repository.GetByIdAsync(entry.Id));
        Assert.True(File.Exists(audioPath));
    }

    [Fact]
    public async Task DeleteAsync_WithDeleteAudioFile_RemovesRecordAndFile()
    {
        var audioPath = Path.Combine(_tempDir, "song.mp3");
        await File.WriteAllTextAsync(audioPath, "fake audio bytes");
        var entry = MakeEntry();
        entry.OriginalAudioPath = audioPath;
        await _repository.AddAsync(entry);

        await _repository.DeleteAsync(entry.Id, deleteAudioFile: true);

        Assert.Null(await _repository.GetByIdAsync(entry.Id));
        Assert.False(File.Exists(audioPath));
    }

    [Fact]
    public async Task EnsureCreatedAsync_MigratingFromV1_BacksUpDatabaseFileFirst()
    {
        // Simulate a real pre-Phase-4 database: only the v1 schema, no SongLibrary table yet.
        await using (var connection = _database.OpenConnection())
        {
            await using var create = connection.CreateCommand();
            create.CommandText = """
                CREATE TABLE IF NOT EXISTS RecentProjects (
                    ProjectFilePath TEXT PRIMARY KEY, ProjectName TEXT NOT NULL,
                    LastOpenedAt TEXT NOT NULL, AspectRatioLabel TEXT NOT NULL
                );
                PRAGMA user_version = 1;
                """;
            await create.ExecuteNonQueryAsync();
        }

        // Any repository call runs EnsureCreatedAsync, which must back up the v1 file before adding
        // the SongLibrary table (spec Phase 4: "migration + backup-before-migration").
        await _repository.GetAllAsync();

        var backupDir = Path.Combine(_tempDir, "Backups");
        Assert.True(Directory.Exists(backupDir));
        var backups = Directory.GetFiles(backupDir, "*pre-migration-v2*");
        Assert.Single(backups);

        // And the migration itself actually happened - SongLibrary is now usable.
        await _repository.AddAsync(MakeEntry());
        Assert.Single(await _repository.GetAllAsync());
    }
}
