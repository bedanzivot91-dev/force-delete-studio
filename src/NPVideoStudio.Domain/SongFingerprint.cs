namespace NPVideoStudio.Domain;

/// <summary>
/// One Chromaprint fingerprint window extracted from a fixed position in a track (spec Phase 4: start/
/// quarter/mid/three-quarter/end, each 5-15s). <see cref="Raw"/> is fpcalc's "-raw" comma-separated
/// uint32 list - kept as text so it round-trips through JSON/SQLite without a custom converter.
/// </summary>
public sealed class SongFingerprintWindow
{
    public required string Label { get; init; }
    public required double OffsetSeconds { get; init; }
    public required string Raw { get; init; }
}

public sealed class SongFingerprintResult
{
    public double DurationSeconds { get; init; }
    public IReadOnlyList<SongFingerprintWindow> Windows { get; init; } = Array.Empty<SongFingerprintWindow>();
}

/// <summary>
/// A candidate match against one library entry (spec Phase 4). Never auto-accept on a single agreeing
/// window or on confidence alone - <see cref="AutoAcceptEligible"/> also requires a sane duration ratio.
/// </summary>
public sealed class SongMatchCandidate
{
    public required Guid LibraryEntryId { get; init; }
    public required string Title { get; init; }
    public double Confidence { get; init; }
    public int AgreeingWindows { get; init; }
    public int ConflictingWindows { get; init; }
    public double DurationRatio { get; init; } = 1.0;
    public bool AutoAcceptEligible { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
