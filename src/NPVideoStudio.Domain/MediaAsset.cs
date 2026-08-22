namespace NPVideoStudio.Domain;

public enum MediaProxyStatus
{
    Original,
    Generating,
    Ready,
    Failed
}

/// <summary>A media file imported into a project's media library, with metadata read via ffprobe.</summary>
public sealed class MediaAsset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string FilePath { get; set; }
    public string FileName => Path.GetFileName(FilePath);
    public MediaKind Kind { get; set; } = MediaKind.Unknown;

    public TimeSpan Duration { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double Fps { get; set; }
    public string? VideoCodec { get; set; }
    public string? AudioCodec { get; set; }
    public bool HasVideoStream { get; set; }
    public bool HasAudioStream { get; set; }
    public long FileSizeBytes { get; set; }

    public bool IsFavorite { get; set; }
    public string? FolderTag { get; set; }
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>True when the file could not be found at <see cref="FilePath"/> on last check (offline/moved media).</summary>
    public bool IsMissing { get; set; }

    /// <summary>Error captured from ffprobe when analysis failed, shown to the user instead of silently ignoring the file.</summary>
    public string? ProbeError { get; set; }

    /// <summary>State of the optional lower-resolution editing proxy. The original <see cref="FilePath"/>
    /// is never replaced: final export therefore always retains the full-quality source.</summary>
    public MediaProxyStatus ProxyStatus { get; set; } = MediaProxyStatus.Original;

    /// <summary>App-owned proxy file used only for preview/playback when <see cref="ProxyStatus"/> is Ready.</summary>
    public string? ProxyFilePath { get; set; }

    /// <summary>Last proxy-generation failure shown in the media library. Null on Original/Ready.</summary>
    public string? ProxyError { get; set; }
}
