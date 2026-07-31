namespace NPVideoStudio.Domain;

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
}
