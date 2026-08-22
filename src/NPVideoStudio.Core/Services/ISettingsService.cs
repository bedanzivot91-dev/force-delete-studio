using NPVideoStudio.Domain;

namespace NPVideoStudio.Core.Services;

public interface ISettingsService
{
    AppSettings Current { get; }

    Task LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>Resets settings to defaults and persists them. Does not touch projects.</summary>
    Task ResetToDefaultsAsync(CancellationToken cancellationToken = default);
}
