using NPVideoStudio.Domain;

namespace NPVideoStudio.Core.Services;

/// <summary>Reads and writes .npvsproject files. Saves are atomic (write to temp file, then replace) to avoid corrupting a project on crash.</summary>
public interface IProjectRepository
{
    Task<Project> LoadAsync(string projectFilePath, CancellationToken cancellationToken = default);

    Task SaveAsync(Project project, string projectFilePath, CancellationToken cancellationToken = default);

    /// <summary>Creates a timestamped backup copy of the project file, keeping at most <paramref name="maxBackups"/> per project.</summary>
    Task BackupAsync(string projectFilePath, int maxBackups = 10, CancellationToken cancellationToken = default);
}
