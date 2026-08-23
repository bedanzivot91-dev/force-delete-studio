using System.Text.Json;
using NPVideoStudio.Domain;

namespace NPVideoStudio.Media;

/// <summary>
/// Builds an isolated, render-only copy of the project. Rendering can take minutes; it must not read a
/// live object graph while the UI is simultaneously editing it, and hidden/muted track state must be
/// resolved before stabilization and FFmpeg graph construction see the timeline.
/// </summary>
internal static class RenderProjectSnapshot
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static Project Create(Project source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var snapshot = JsonSerializer.Deserialize<Project>(json, JsonOptions)
            ?? throw new InvalidDataException("Nije moguće napraviti stabilan snapshot projekta za render.");

        // Hidden means absent from final output, not only absent from preview. Removing it here also keeps
        // stabilization/pre-analysis from failing on a source file that the user intentionally hid.
        snapshot.Timeline.Tracks.RemoveAll(track => track.IsHidden);

        // Embedded audio of the base video is generated from clip state by the graph builder. Translate
        // the track-level mute into that existing per-clip contract on the private snapshot only.
        var baseVideoTrack = snapshot.Timeline.Tracks
            .FirstOrDefault(track => track.Kind == TimelineTrackKind.Video && track.Clips.Count > 0);
        if (baseVideoTrack?.IsMuted == true)
        {
            foreach (var clip in baseVideoTrack.Clips)
            {
                clip.IsMuted = true;
            }
        }

        return snapshot;
    }

    /// <summary>
    /// Every drawtext payload produced by the timeline is user-authored literal text. FFmpeg's default
    /// drawtext expansion interprets percent expressions such as %{pts}; disabling expansion makes titles
    /// like "100%" and arbitrary user text deterministic and prevents accidental expression evaluation.
    /// </summary>
    public static FfmpegRenderPlan MakeLiteralTextSafe(FfmpegRenderPlan plan)
    {
        const string marker = "drawtext=text=";
        if (!plan.FilterComplexArgument.Contains(marker, StringComparison.Ordinal))
        {
            return plan;
        }

        return new FfmpegRenderPlan
        {
            InputFilePaths = plan.InputFilePaths,
            FilterComplexArgument = plan.FilterComplexArgument.Replace(
                marker,
                "drawtext=expansion=none:text=",
                StringComparison.Ordinal),
            VideoMapLabel = plan.VideoMapLabel,
            AudioMapLabel = plan.AudioMapLabel,
            TotalDurationSeconds = plan.TotalDurationSeconds
        };
    }
}
