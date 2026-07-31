using NPVideoStudio.Domain;

namespace NPVideoStudio.Core.Services;

public interface IAutoSaveService
{
    void Start(Func<Project?> getCurrentProject);

    void Stop();

    /// <summary>Forces an immediate autosave of the current project, if any.</summary>
    Task TriggerNowAsync(CancellationToken cancellationToken = default);

    /// <summary>Checks whether an autosave newer than its source project exists (i.e. the app likely crashed) and returns its path.</summary>
    Task<string?> FindRecoverableAutoSaveAsync(string projectFilePath, CancellationToken cancellationToken = default);

    /// <summary>Marks the current session as cleanly shut down, so no recovery prompt is shown next launch.</summary>
    Task MarkCleanShutdownAsync(CancellationToken cancellationToken = default);
}
