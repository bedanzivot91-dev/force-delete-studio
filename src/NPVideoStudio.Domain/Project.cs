namespace NPVideoStudio.Domain;

/// <summary>
/// Root object serialized into a .npvsproject file. This is the current, minimal Phase 1 shape:
/// project identity, format and a media library. Timeline/track/clip data is added in a later phase
/// and must not break loading of projects saved by this version (see ProjectFormatVersion).
/// </summary>
public sealed class Project
{
    /// <summary>Bumped whenever the on-disk schema changes, so the loader can migrate old files instead of failing.</summary>
    public int ProjectFormatVersion { get; set; } = 1;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string Name { get; set; }
    public ProjectFormat Format { get; set; } = ProjectFormat.FromPresets(AspectRatioPreset.Widescreen16x9, ResolutionPreset.FullHd1080, FrameRatePreset.Fps30);
    public TargetPlatform? TargetPlatform { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset LastModifiedAt { get; set; } = DateTimeOffset.Now;

    public List<MediaAsset> MediaLibrary { get; set; } = new();

    /// <summary>Absolute path to the .npvsproject file this project was loaded from / saved to. Not serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ProjectFilePath { get; set; }
}
