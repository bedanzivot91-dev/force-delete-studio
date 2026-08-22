using NPVideoStudio.Domain;

namespace NPVideoStudio.Core.Services;

/// <summary>Analyzes media files via ffprobe and returns metadata used by the media library and import UI.</summary>
public interface IMediaProbeService
{
    Task<MediaAsset> ProbeAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>True when ffprobe was found and responded to a version query.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}
