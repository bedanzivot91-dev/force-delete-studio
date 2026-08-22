namespace NPVideoStudio.Domain;

/// <summary>
/// Root object serialized into a .npvsproject file: project identity, format, media library, and (since
/// Phase 8) the non-destructive timeline. A project saved before Phase 8 simply deserializes with an
/// empty <see cref="Timeline"/> (new List, defaults) since System.Text.Json tolerates a missing property
/// - no explicit migration step was needed for this addition, unlike AppDatabase's SQLite schema
/// migrations in NPVideoStudio.Infrastructure.
/// </summary>
public sealed class Project
{
    /// <summary>Bumped whenever the on-disk schema changes, so the loader can migrate old files instead of failing.</summary>
    public int ProjectFormatVersion { get; set; } = 2;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string Name { get; set; }
    public ProjectFormat Format { get; set; } = ProjectFormat.FromPresets(AspectRatioPreset.Widescreen16x9, ResolutionPreset.FullHd1080, FrameRatePreset.Fps30);
    public TargetPlatform? TargetPlatform { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset LastModifiedAt { get; set; } = DateTimeOffset.Now;

    public List<MediaAsset> MediaLibrary { get; set; } = new();

    public Timeline Timeline { get; set; } = new();

    /// <summary>Absolute path to the .npvsproject file this project was loaded from / saved to. Not serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ProjectFilePath { get; set; }
}
