using Microsoft.Data.Sqlite;
using NPVideoStudio.Domain;

namespace NPVideoStudio.Infrastructure.Persistence;

/// <summary>Owns the local SQLite database (recent projects, and future local metadata). Handles schema creation and migration.</summary>
public sealed class AppDatabase
{
    private readonly string _connectionString;

    public AppDatabase(string? databasePath = null)
    {
        var path = databasePath ?? AppSettings.DatabasePath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Pooling off: this is a low-concurrency local desktop database, and keeping pooled native
        // connections around after Dispose() holds a Windows file lock on the .db file - which broke
        // both test cleanup and any real "delete/recreate the database" diagnostics repair.
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();

        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA user_version;";
        var currentVersion = Convert.ToInt32(await pragma.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);

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

        // Future schema migrations append here as `if (currentVersion < N) { ... PRAGMA user_version = N; }`
        // so upgrading the app never loses a user's existing recent-projects list (spec §33/40).
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
