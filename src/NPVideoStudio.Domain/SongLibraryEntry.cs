namespace NPVideoStudio.Domain;

public enum SongVerificationStatus
{
    NotVerified,
    Verified,
    NeedsReview
}

/// <summary>
/// A song in the user's local library: original audio, verified lyrics, and a Chromaprint fingerprint
/// used to auto-recognize the same song later (e.g. after a fresh YouTube download or a re-import), so
/// lyrics/verification work never has to be redone for a song already in the library.
/// </summary>
public sealed class SongLibraryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public string Artist { get; set; } = string.Empty;
    public string? Album { get; set; }
    public required string OriginalAudioPath { get; set; }
    public TimeSpan Duration { get; set; }

    /// <summary>JSON-serialized <see cref="SongFingerprintResult"/> (multi-window Chromaprint fingerprints).</summary>
    public string Fingerprint { get; set; } = string.Empty;

    public string? FullLyrics { get; set; }
    public string? LrcPath { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
    public SongVerificationStatus VerificationStatus { get; set; } = SongVerificationStatus.NotVerified;
    public string? Notes { get; set; }
}
