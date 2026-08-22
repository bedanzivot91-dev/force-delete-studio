namespace NPVideoStudio.Domain;

/// <summary>
/// A starter track layout for a new project (spec Phase 10: "Kreiraj video iz šablona"). Deliberately
/// small and honest scope: a template is just a named starting set of empty <see cref="TimelineTrack"/>
/// kinds added to a freshly created project - not a separate user-authored/CRUD-able template system
/// (there is nothing else to "manage" beyond this fixed list, which is why the separate "Upravljanje
/// šablonima" planned tile was removed rather than implemented - see PHASE_STATUS.md).
/// </summary>
public sealed class ProjectTemplate
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<TimelineTrackKind> StarterTrackKinds { get; init; }

    public static IReadOnlyList<ProjectTemplate> BuiltIn { get; } = new[]
    {
        new ProjectTemplate
        {
            Name = "Prazan projekat",
            Description = "Bez unapred dodatih traka - potpuno prazna vremenska traka.",
            StarterTrackKinds = Array.Empty<TimelineTrackKind>()
        },
        new ProjectTemplate
        {
            Name = "Govor sa titlovima",
            Description = "Video traka + traka za titlove - za vlogove, podkaste i tutorijale.",
            StarterTrackKinds = new[] { TimelineTrackKind.Video, TimelineTrackKind.Caption }
        },
        new ProjectTemplate
        {
            Name = "Muzički spot",
            Description = "Video, audio i titl traka - za spotove i pesme sa tekstom na ekranu.",
            StarterTrackKinds = new[] { TimelineTrackKind.Video, TimelineTrackKind.Audio, TimelineTrackKind.Caption }
        },
        new ProjectTemplate
        {
            Name = "Slike i tekst",
            Description = "Video traka, traka za tekst i traka za slike (overlay) - za prezentacije i najave.",
            StarterTrackKinds = new[] { TimelineTrackKind.Video, TimelineTrackKind.Text, TimelineTrackKind.ImageOverlay }
        }
    };
}
