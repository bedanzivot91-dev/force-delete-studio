namespace NPVideoStudio.Domain;

public enum TimelineTrackKind
{
    Video,
    Audio,
    Caption,
    Text,
    ImageOverlay
}

/// <summary>
/// One clip placed on a timeline track. Non-destructive: <see cref="SourceTrimInSeconds"/>/
/// <see cref="SourceTrimOutSeconds"/> only change which slice of the original source plays - the
/// underlying <see cref="MediaAssetId"/> file is never modified (spec Phase 8: "non-destructive").
/// </summary>
public sealed class TimelineClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Links to a <see cref="Project.MediaLibrary"/> entry via <see cref="MediaAsset.Id"/> - null for a Text-overlay clip, which has no underlying media file.</summary>
    public string? MediaAssetId { get; set; }

    /// <summary>Plain text for a Text-overlay clip - null for every other track kind.</summary>
    public string? TextContent { get; set; }

    public double SourceTrimInSeconds { get; set; }
    public double SourceTrimOutSeconds { get; set; }

    public double TimelineStartSeconds { get; set; }
    public double TimelineDurationSeconds => Math.Max(0, SourceTrimOutSeconds - SourceTrimInSeconds);
    public double TimelineEndSeconds => TimelineStartSeconds + TimelineDurationSeconds;

    public double FadeInSeconds { get; set; }
    public double FadeOutSeconds { get; set; }

    public bool IsMuted { get; set; }
    public double Volume { get; set; } = 1.0;
}

/// <summary>One track (a lane of non-overlapping clips) in the timeline.</summary>
public sealed class TimelineTrack
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required TimelineTrackKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;

    public List<TimelineClip> Clips { get; set; } = new();

    public bool IsLocked { get; set; }
    public bool IsHidden { get; set; }
    public bool IsMuted { get; set; }
    public bool IsSolo { get; set; }
    public double Volume { get; set; } = 1.0;
}

/// <summary>
/// The project's non-destructive timeline (spec Phase 8) - persisted as part of <see cref="Project"/>,
/// not just transient UI-control state. Playhead/zoom are session UI state that happens to be worth
/// persisting too (so re-opening a project restores where you left off), not editing data.
/// </summary>
public sealed class Timeline
{
    public List<TimelineTrack> Tracks { get; set; } = new();
    public double PlayheadSeconds { get; set; }
    public double ZoomPixelsPerSecond { get; set; } = 40;
}
