namespace NPVideoStudio.Domain;

/// <summary>
/// A place in a song where the local speech-recognition model heard something matching a phrase the
/// user typed. Singing recognition is much less reliable than spoken speech, so every match carries
/// a confidence score and the exact recognized text, rather than a bare "found it" claim.
/// </summary>
public sealed class LyricMatch
{
    public required TimeSpan Start { get; init; }
    public required TimeSpan Duration { get; init; }
    public required string RecognizedText { get; init; }

    /// <summary>1.0 for an exact substring match, lower for a fuzzy/approximate match.</summary>
    public required double Confidence { get; init; }

    public TimeSpan End => Start + Duration;

    public string? ExportedFilePath { get; set; }
}
