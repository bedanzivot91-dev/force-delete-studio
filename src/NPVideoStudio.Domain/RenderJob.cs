namespace NPVideoStudio.Domain;

/// <summary>Video codecs the render pipeline can target (spec Phase 9), with automatic fallback to Libx264 if a hardware encoder isn't available/fails.</summary>
public enum VideoCodec
{
    Libx264,
    H264Nvenc,
    H264Qsv,
    H264Amf
}

public enum RenderJobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Render/export settings (spec Phase 9 defaults): MP4/H.264/CRF 18/preset medium/AAC 192kbps, keep
/// original resolution/fps/rotation, +faststart, keep original audio unless the user changes it.
/// </summary>
public sealed class RenderSettings
{
    public VideoCodec Codec { get; set; } = VideoCodec.Libx264;
    public int Crf { get; set; } = 18;
    public string Preset { get; set; } = "medium";
    public int AudioBitrateKbps { get; set; } = 192;
    public required string OutputFilePath { get; set; }
    public bool OverwriteConfirmed { get; set; }
}

/// <summary>One queued/running/finished export job (spec Phase 9: "multiple queued export jobs").</summary>
public sealed class RenderJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string ProjectName { get; set; }
    public required RenderSettings Settings { get; set; }
    public RenderJobStatus Status { get; set; } = RenderJobStatus.Queued;
    public double ProgressPercent { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>The real ffmpeg command actually run, for the log - never contains secrets since every argument here is a local file path or a plain encoding setting.</summary>
    public string? FfmpegCommandLogged { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
