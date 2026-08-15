namespace NPVideoStudio.Domain;

/// <summary>One piece of speech transcribed from a video/song, with timing - the shape
/// <c>ISubtitleGeneratorService.TranscribeAsync</c> hands back when asked to transcribe into real
/// timeline caption clips (as opposed to writing a standalone .srt file).</summary>
public readonly record struct TranscribedCaptionSegment(TimeSpan Start, TimeSpan End, string Text);
