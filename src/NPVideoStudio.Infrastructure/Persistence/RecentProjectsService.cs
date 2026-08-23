using Microsoft.Data.Sqlite;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;

namespace NPVideoStudio.Infrastructure.Persistence;

public sealed class RecentProjectsService : IRecentProjectsService
{
    private readonly AppDatabase _database;

    public RecentProjectsService(AppDatabase database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<RecentProjectEntry>> GetRecentAsync(CancellationToken cancellationToken = default)
    {
        await _database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ProjectFilePath, ProjectName, LastOpenedAt, AspectRatioLabel FROM RecentProjects ORDER BY LastOpenedAt DESC;";

        var results = new List<RecentProjectEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var path = reader.GetString(0);
            results.Add(new RecentProjectEntry
            {
                ProjectFilePath = path,
                ProjectName = reader.GetString(1),
                LastOpenedAt = DateTimeOffset.Parse(reader.GetString(2)),
                AspectRatioLabel = reader.GetString(3),
                IsMissing = !File.Exists(path)
            });
        }

        return results;
    }

    public async Task RegisterOpenedAsync(Project project, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(project.ProjectFilePath))
        {
            throw new InvalidOperationException("Projekat mora prvo da bude sačuvan pre nego što se doda u nedavne.");
        }

        await _database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RecentProjects (ProjectFilePath, ProjectName, LastOpenedAt, AspectRatioLabel)
            VALUES ($path, $name, $openedAt, $aspect)
            ON CONFLICT(ProjectFilePath) DO UPDATE SET
                ProjectName = excluded.ProjectName,
                LastOpenedAt = excluded.LastOpenedAt,
                AspectRatioLabel = excluded.AspectRatioLabel;
            """;
        command.Parameters.AddWithValue("$path", project.ProjectFilePath);
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$openedAt", DateTimeOffset.Now.ToString("O"));
        command.Parameters.AddWithValue("$aspect", $"{project.Format.Width}x{project.Format.Height} ({project.Format.Orientation})");

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        await _database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RecentProjects WHERE ProjectFilePath = $path;";
        command.Parameters.AddWithValue("$path", projectFilePath);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
