using NPVideoStudio.Domain;

namespace NPVideoStudio.Core.Services;

public interface IRecentProjectsService
{
    Task<IReadOnlyList<RecentProjectEntry>> GetRecentAsync(CancellationToken cancellationToken = default);

    Task RegisterOpenedAsync(Project project, CancellationToken cancellationToken = default);

    Task RemoveAsync(string projectFilePath, CancellationToken cancellationToken = default);
}
