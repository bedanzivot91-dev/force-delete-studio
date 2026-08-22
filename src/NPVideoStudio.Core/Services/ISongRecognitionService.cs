using NPVideoStudio.Domain;

namespace NPVideoStudio.Core.Services;

/// <summary>
/// Chromaprint/fpcalc-based multi-window fingerprinting and library matching (spec Phase 4). Matching
/// never auto-accepts on a single agreeing window or on confidence alone - see
/// <see cref="SongMatchCandidate.AutoAcceptEligible"/>.
/// </summary>
public interface ISongRecognitionService
{
    Task<SongFingerprintResult> ComputeFingerprintAsync(string audioFilePath, CancellationToken cancellationToken = default);

    /// <summary>Ranks <paramref name="library"/> against <paramref name="candidate"/>, best match first,
    /// top 3 only - the UI must show these as choices ("this one" / "none of these"), never auto-pick.</summary>
    IReadOnlyList<SongMatchCandidate> FindMatches(SongFingerprintResult candidate, IReadOnlyList<SongLibraryEntry> library);
}
