using Microsoft.Data.Sqlite;
using NPVideoStudio.Domain;

namespace NPVideoStudio.Infrastructure.Persistence;

/// <summary>Owns the local SQLite database (recent projects, and future local metadata). Handles schema creation and migration.</summary>
public sealed class AppDatabase
{
    private readonly string _databasePath;
    private readonly string _connectionString;

    public AppDatabase(string? databasePath = null)
    {
        _databasePath = databasePath ?? AppSettings.DatabasePath();
        var dir = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Pooling off: this is a low-concurrency local desktop database, and keeping pooled native
        // connections around after Dispose() holds a Windows file lock on the .db file - which broke
        // both test cleanup and any real "delete/recreate the database" diagnostics repair.
        _connectionString = new SqliteConnectionStringBuilder { DataSource = _databasePath, Pooling = false }.ToString();
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = await ReadUserVersionAsync(cancellationToken).ConfigureAwait(false);

        // Back up the whole .db file before any real schema change to an existing (already-versioned)
        // database - never before the very first creation, where there's nothing yet worth backing up
        // (spec Phase 4: "migration + backup-before-migration").
        if (currentVersion is >= 1 and < 2 && File.Exists(_databasePath))
        {
            BackupDatabaseFile("pre-migration-v2");
        }

        await using var connection = OpenConnection();

        if (currentVersion < 1)
        {
            await using var create = connection.CreateCommand();
            create.CommandText = """
                CREATE TABLE IF NOT EXISTS RecentProjects (
                    ProjectFilePath TEXT PRIMARY KEY,
                    ProjectName TEXT NOT NULL,
                    LastOpenedAt TEXT NOT NULL,
                    AspectRatioLabel TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS SchemaInfo (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );

                PRAGMA user_version = 1;
                """;
            await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (currentVersion < 2)
        {
            await using var create = connection.CreateCommand();
            create.CommandText = """
                CREATE TABLE IF NOT EXISTS SongLibrary (
                    Id TEXT PRIMARY KEY,
                    Title TEXT NOT NULL,
                    Artist TEXT NOT NULL,
                    Album TEXT,
                    OriginalAudioPath TEXT NOT NULL,
                    DurationSeconds REAL NOT NULL,
                    Fingerprint TEXT NOT NULL,
                    FullLyrics TEXT,
                    LrcPath TEXT,
                    AddedAt TEXT NOT NULL,
                    VerificationStatus TEXT NOT NULL,
                    Notes TEXT
                );

                PRAGMA user_version = 2;
                """;
            await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Future schema migrations append here as `if (currentVersion < N) { ... PRAGMA user_version = N; }`
        // so upgrading the app never loses a user's existing recent-projects list (spec §33/40).
    }

    private async Task<int> ReadUserVersionAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_databasePath))
        {
            return 0;
        }

        await using var connection = OpenConnection();
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await pragma.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
    }

    private void BackupDatabaseFile(string reasonTag)
    {
        var dir = Path.GetDirectoryName(_databasePath);
        if (string.IsNullOrEmpty(dir))
        {
            return;
        }

        var backupDir = Path.Combine(dir, "Backups");
        Directory.CreateDirectory(backupDir);

        var name = Path.GetFileNameWithoutExtension(_databasePath);
        var ext = Path.GetExtension(_databasePath);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupPath = Path.Combine(backupDir, $"{name}_{reasonTag}_{timestamp}{ext}");

        File.Copy(_databasePath, backupPath, overwrite: true);
    }

    /// <summary>Runs PRAGMA integrity_check and returns "ok" or the list of problems found.</summary>
    public async Task<string> CheckIntegrityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = OpenConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result?.ToString() ?? "unknown";
        }
        catch (Exception ex)
        {
            return $"error: {ex.Message}";
        }
    }
}
