using System.Text.Json;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;

namespace NPVideoStudio.Infrastructure.Persistence;

public sealed class ProjectRepository : IProjectRepository, IProjectSnapshotRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<Project> LoadAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(projectFilePath))
        {
            throw new FileNotFoundException($"Projekat nije pronađen na putanji: {projectFilePath}", projectFilePath);
        }

        await using var stream = File.OpenRead(projectFilePath);
        var project = await JsonSerializer.DeserializeAsync<Project>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Fajl projekta je oštećen ili prazan: {projectFilePath}");

        project.ProjectFilePath = projectFilePath;
        return project;
    }

    public async Task SaveAsync(Project project, string projectFilePath, CancellationToken cancellationToken = default)
    {
        project.LastModifiedAt = DateTimeOffset.Now;
        await WriteProjectFileAsync(project, projectFilePath, cancellationToken).ConfigureAwait(false);
        project.ProjectFilePath = projectFilePath;
    }

    public Task SaveSnapshotAsync(Project project, string snapshotFilePath, CancellationToken cancellationToken = default) =>
        WriteProjectFileAsync(project, snapshotFilePath, cancellationToken);

    private static async Task WriteProjectFileAsync(
        Project project,
        string projectFilePath,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(projectFilePath)
            ?? throw new ArgumentException("Neispravna putanja projekta.", nameof(projectFilePath));
        Directory.CreateDirectory(directory);

        // Use a unique sibling temp file. A fixed "path.tmp" lets two legitimate concurrent saves race
        // on the same temp path and can corrupt/fail both operations before the atomic replace is reached.
        var tempFilePath = Path.Combine(
            directory,
            $".{Path.GetFileName(projectFilePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                             tempFilePath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, project, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(projectFilePath))
            {
                File.Replace(tempFilePath, projectFilePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempFilePath, projectFilePath);
            }
        }
        finally
        {
            // Cancellation/serialization/I/O failure must not leave stale temp files in the project or
            // autosave folder. If the move/replace succeeded this path no longer exists, so this is cheap.
            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch
            {
                // Cleanup failure must not hide the original save exception.
            }
        }
    }

    public Task BackupAsync(string projectFilePath, int maxBackups = 10, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(projectFilePath))
        {
            return Task.CompletedTask;
        }

        var directory = Path.GetDirectoryName(projectFilePath)!;
        var backupDir = Path.Combine(directory, "Backups");
        Directory.CreateDirectory(backupDir);

        var name = Path.GetFileNameWithoutExtension(projectFilePath);
        var ext = Path.GetExtension(projectFilePath);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupPath = Path.Combine(backupDir, $"{name}_{timestamp}{ext}");

        File.Copy(projectFilePath, backupPath, overwrite: true);

        var existing = Directory.GetFiles(backupDir, $"{name}_*{ext}")
            .OrderByDescending(f => f)
            .Skip(maxBackups);

        foreach (var old in existing)
        {
            File.Delete(old);
        }

        return Task.CompletedTask;
    }
}
