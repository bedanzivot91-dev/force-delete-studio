using NPVideoStudio.Domain;

namespace NPVideoStudio.Core.Services;

/// <summary>Renders a project's timeline to a final video file (spec Phase 9). Mutates <paramref name="job"/>'s Status/ProgressPercent/FfmpegCommandLogged/ErrorMessage/timestamps in place as it runs, so a queue can observe one job's live state without a separate event channel.</summary>
public interface IRenderService
{
    Task<string> RenderAsync(Project project, RenderJob job, CancellationToken cancellationToken = default);
}
