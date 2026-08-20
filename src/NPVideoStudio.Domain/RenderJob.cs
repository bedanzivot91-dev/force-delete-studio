namespace NPVideoStudio.Domain;

/// <summary>Video codecs the real FFmpeg render pipeline can target. Hardware H.264 encoders automatically
/// fall back to libx264 when unavailable; software codecs fail honestly instead of silently changing format.</summary>
public enum VideoCodec
{
    Libx264,
    H264Nvenc,
    H264Qsv,
    H264Amf,
    Libx265,
    LibvpxVp9,
    LibaomAv1
}

/// <summary>Container/output choices exposed by the export UI. M4A/MP3/WAV/FLAC are audio-only.</summary>
public enum ExportFormat
{
    Mp4,
    Mov,
    WebM,
    M4a,
    Mp3,
    Wav,
    Flac
}

public static class ExportFormatInfo
{
    public static bool IsAudioOnly(this ExportFormat format) => format is
        ExportFormat.M4a or ExportFormat.Mp3 or ExportFormat.Wav or ExportFormat.Flac;

    public static string Extension(this ExportFormat format) => format switch
    {
        ExportFormat.Mp4 => ".mp4",
        ExportFormat.Mov => ".mov",
        ExportFormat.WebM => ".webm",
        ExportFormat.M4a => ".m4a",
        ExportFormat.Mp3 => ".mp3",
        ExportFormat.Wav => ".wav",
        ExportFormat.Flac => ".flac",
        _ => ".mp4"
    };

    public static bool SupportsVideoCodec(this ExportFormat format, VideoCodec codec) => format switch
    {
        ExportFormat.Mp4 => codec is VideoCodec.Libx264 or VideoCodec.H264Nvenc or VideoCodec.H264Qsv or
            VideoCodec.H264Amf or VideoCodec.Libx265 or VideoCodec.LibaomAv1,
        ExportFormat.Mov => codec is VideoCodec.Libx264 or VideoCodec.H264Nvenc or VideoCodec.H264Qsv or
            VideoCodec.H264Amf or VideoCodec.Libx265,
        ExportFormat.WebM => codec is VideoCodec.LibvpxVp9 or VideoCodec.LibaomAv1,
        _ => false
    };
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
/// Persisted settings for one export. Defaults preserve the original reliable MP4/H.264/AAC path while
/// allowing professional container/codec and audio-only choices through the same render service.
/// </summary>
public sealed class RenderSettings
{
    public ExportFormat Format { get; set; } = ExportFormat.Mp4;
    public VideoCodec Codec { get; set; } = VideoCodec.Libx264;
    public int Crf { get; set; } = 18;
    public string Preset { get; set; } = "medium";
    public int AudioBitrateKbps { get; set; } = 192;
    public required string OutputFilePath { get; set; }
    public bool OverwriteConfirmed { get; set; }
}

/// <summary>One queued/running/finished export job.</summary>
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
