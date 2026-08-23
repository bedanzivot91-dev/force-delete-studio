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

/// <summary>
/// Optional repository capability for writing a recovery/snapshot copy without changing the identity of
/// the currently open <see cref="Project"/>. Autosave must use this contract instead of normal
/// <see cref="IProjectRepository.SaveAsync"/>, because a recovery file is not the user's active project file.
/// </summary>
public interface IProjectSnapshotRepository
{
    Task SaveSnapshotAsync(Project project, string snapshotFilePath, CancellationToken cancellationToken = default);
}
