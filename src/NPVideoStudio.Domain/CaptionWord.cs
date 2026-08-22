namespace NPVideoStudio.Domain;

/// <summary>
/// Where a caption word's text/timing came from (spec Phase 6). Never collapsed into a single
/// "verified" boolean - the caption editor and any future auto-placement logic need to know exactly
/// how much to trust a given word.
/// </summary>
public enum CaptionWordSource
{
    VerifiedLyrics,
    Lrc,
    Whisper,
    WhisperX,
    FuzzyAligned,
    Interpolated,
    Manual
}

public enum CaptionVerificationStatus
{
    NotVerified,
    Verified,
    NeedsReview
}

/// <summary>
/// One word in a caption transcript. This is the whole granularity model: "line"/"sentence" groupings
/// are not a separate parent object - a line is just a run of words ending at the first word (reading
/// forward) with <see cref="LineBreakAfter"/> set. This keeps the model exactly word-level (spec: "word-
/// level model: original text, normalized text, start, end, confidence, source, verification status")
/// while still letting the editor and block-based export formats (SRT/VTT/ASS) group words into lines -
/// without a second parallel data structure that could drift out of sync with the words it groups.
/// </summary>
public sealed class CaptionWord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OriginalText { get; set; }
    public string NormalizedText { get; set; } = string.Empty;
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }

    /// <summary>1.0 for verified/manual text, lower for anything ASR-derived (spec: never invent confidence).</summary>
    public double Confidence { get; set; } = 1.0;

    public CaptionWordSource Source { get; set; } = CaptionWordSource.Manual;
    public CaptionVerificationStatus VerificationStatus { get; set; } = CaptionVerificationStatus.NotVerified;

    public bool LineBreakAfter { get; set; }
}
