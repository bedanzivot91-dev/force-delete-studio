namespace NPVideoStudio.Domain;

/// <summary>Entry in the "recent projects" list stored in the local database.</summary>
public sealed class RecentProjectEntry
{
    public required string ProjectFilePath { get; set; }
    public required string ProjectName { get; set; }
    public DateTimeOffset LastOpenedAt { get; set; } = DateTimeOffset.Now;
    public string AspectRatioLabel { get; set; } = string.Empty;

    /// <summary>True when the file no longer exists on disk (moved/deleted outside the app).</summary>
    public bool IsMissing { get; set; }
}
