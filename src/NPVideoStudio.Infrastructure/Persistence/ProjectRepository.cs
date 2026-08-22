using System.Text.Json;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;

namespace NPVideoStudio.Infrastructure.Persistence;

public sealed class ProjectRepository : IProjectRepository
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
        var directory = Path.GetDirectoryName(projectFilePath)
            ?? throw new ArgumentException("Neispravna putanja projekta.", nameof(projectFilePath));
        Directory.CreateDirectory(directory);

        project.LastModifiedAt = DateTimeOffset.Now;

        // Atomic save: write to a temp file first, then replace the target, so a crash mid-write
        // never leaves a truncated/corrupt .npvsproject file on disk.
        var tempFilePath = projectFilePath + ".tmp";
        await using (var stream = File.Create(tempFilePath))
        {
            await JsonSerializer.SerializeAsync(stream, project, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(projectFilePath))
        {
            File.Replace(tempFilePath, projectFilePath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempFilePath, projectFilePath);
        }

        project.ProjectFilePath = projectFilePath;
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
