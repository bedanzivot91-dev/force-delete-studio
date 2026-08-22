using NPVideoStudio.Domain;

namespace NPVideoStudio.AI;

public enum CaptionProblemKind
{
    /// <summary>End is at or before start - the caption can never be shown.</summary>
    InvalidRange,

    /// <summary>No text at all.</summary>
    EmptyText,

    /// <summary>Runs past the end of the video/song.</summary>
    OutOfBounds,

    /// <summary>Starts before the previous caption has finished.</summary>
    Overlap,

    /// <summary>Legal, but the gap to the next caption is too short to register as a change.</summary>
    ShortGap
}

/// <summary>One problem found in a caption track. <see cref="IsError"/> separates "this is broken" from
/// "this is legal but will look bad", so the UI can block on one and merely warn on the other.</summary>
public sealed record CaptionProblem(CaptionProblemKind Kind, int Index, string Message)
{
    public bool IsError => Kind is not CaptionProblemKind.ShortGap;
}

public sealed record CaptionTrackReport(IReadOnlyList<CaptionProblem> Problems, int CaptionCount)
{
    public bool IsValid => !Problems.Any(p => p.IsError);
    public IEnumerable<CaptionProblem> Errors => Problems.Where(p => p.IsError);
    public IEnumerable<CaptionProblem> Warnings => Problems.Where(p => !p.IsError);
}

/// <summary>
/// Structural checking and repair for a caption track, ported from the user's other project
/// (bedanzivot91-dev/PROGRAM-ZA-TEKST-U-VIDEO, <c>text-video-tools.js</c>: <c>validateCaptionTrack</c>,
/// <c>normalizeCaptionTrack</c>, <c>splitLongCaptionCue</c>) at their request.
///
/// This is the check that was missing here: this app could already generate captions from speech and
/// align them to known lyrics, but nothing verified the *result* was structurally sound before it went
/// into a render. Overlapping captions in particular are easy to produce from ASR output and show up in
/// the exported video as two lines stacked on top of each other.
/// </summary>
public static class CaptionTrackValidator
{
    public static readonly TimeSpan DefaultMinimumDuration = TimeSpan.FromMilliseconds(80);

    /// <summary>
    /// Reports every structural problem, in caption order. <paramref name="totalDuration"/> is optional -
    /// pass it to also catch captions running past the end of the media; pass null when the media length
    /// genuinely isn't known rather than guessing one.
    /// </summary>
    public static CaptionTrackReport Validate(
        IReadOnlyList<CaptionWord> captions,
        TimeSpan? totalDuration = null,
        TimeSpan? minimumGap = null)
    {
        var ordered = (captions ?? Array.Empty<CaptionWord>())
            .OrderBy(c => c.Start)
            .ToList();

        var gap = minimumGap ?? TimeSpan.Zero;
        var problems = new List<CaptionProblem>();

        for (var i = 0; i < ordered.Count; i++)
        {
            var caption = ordered[i];

            if (caption.End <= caption.Start)
            {
                problems.Add(new CaptionProblem(
                    CaptionProblemKind.InvalidRange, i, "Kraj titla mora biti posle početka."));
            }

            if (string.IsNullOrWhiteSpace(caption.OriginalText))
            {
                problems.Add(new CaptionProblem(CaptionProblemKind.EmptyText, i, "Titl nema tekst."));
            }

            if (totalDuration is { } total && caption.End > total)
            {
                problems.Add(new CaptionProblem(
                    CaptionProblemKind.OutOfBounds, i, "Titl prelazi kraj videa."));
            }

            if (i + 1 < ordered.Count)
            {
                var next = ordered[i + 1];
                var distance = next.Start - caption.End;

                if (distance < TimeSpan.Zero)
                {
                    problems.Add(new CaptionProblem(
                        CaptionProblemKind.Overlap, i, "Titlovi se preklapaju - prikazaće se jedan preko drugog."));
                }
                else if (distance < gap)
                {
                    problems.Add(new CaptionProblem(
                        CaptionProblemKind.ShortGap, i,
                        $"Razmak do sledećeg titla je vrlo kratak ({distance.TotalMilliseconds:0} ms)."));
                }
            }
        }

        return new CaptionTrackReport(problems, ordered.Count);
    }

    /// <summary>
    /// Returns a repaired copy: sorted, blank captions dropped, every caption at least
    /// <paramref name="minimumDuration"/> long, clamped to <paramref name="totalDuration"/> when given,
    /// and overlaps resolved by trimming the *earlier* caption's end (never by moving the later one's
    /// start, which would desynchronize it from the audio it belongs to).
    ///
    /// Deliberately non-destructive - the input list is not modified, so a caller can show the user a
    /// before/after and let them decide.
    /// </summary>
    public static IReadOnlyList<CaptionWord> Normalize(
        IReadOnlyList<CaptionWord> captions,
        TimeSpan? totalDuration = null,
        TimeSpan? minimumDuration = null)
    {
        var floor = minimumDuration ?? DefaultMinimumDuration;

        var result = (captions ?? Array.Empty<CaptionWord>())
            .Where(c => !string.IsNullOrWhiteSpace(c.OriginalText))
            .OrderBy(c => c.Start)
            .Select(c => CloneCaption(c))
            .ToList();

        foreach (var caption in result)
        {
            if (caption.Start < TimeSpan.Zero)
            {
                caption.Start = TimeSpan.Zero;
            }

            if (caption.End < caption.Start + floor)
            {
                caption.End = caption.Start + floor;
            }

            if (totalDuration is { } total && caption.End > total)
            {
                caption.End = total > caption.Start ? total : caption.Start + floor;
            }

            caption.OriginalText = caption.OriginalText.Trim();
        }

        for (var i = 0; i < result.Count - 1; i++)
        {
            if (result[i].End > result[i + 1].Start)
            {
                var trimmedEnd = result[i + 1].Start;
                result[i].End = trimmedEnd > result[i].Start
                    ? trimmedEnd
                    : result[i].Start + TimeSpan.FromMilliseconds(1);
            }
        }

        return result;
    }

    /// <summary>
    /// Splits one over-long caption into several readable ones, dividing its time span across the pieces
    /// *proportionally to how many words each piece got* - so a 6-word piece holds longer than a 2-word
    /// piece instead of every piece getting an equal, wrong slice. Returns a single-item list when the
    /// caption already fits.
    /// </summary>
    public static IReadOnlyList<CaptionWord> SplitLongCaption(
        CaptionWord caption,
        int maxCharactersPerLine = CaptionReadability.DefaultMaxCharactersPerLine)
    {
        var groups = CaptionReadability.BreakIntoLines(
            caption.OriginalText,
            maxCharactersPerLine,
            maxLines: int.MaxValue);

        if (groups.Count <= 1)
        {
            return new[] { CloneCaption(caption) };
        }

        var start = caption.Start;
        var end = caption.End > caption.Start ? caption.End : caption.Start + CaptionReadability.MinimumDisplayDuration;
        var totalTicks = (end - start).Ticks;

        var wordCounts = groups
            .Select(g => g.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length)
            .ToList();
        var totalWords = Math.Max(1, wordCounts.Sum());

        var pieces = new List<CaptionWord>(groups.Count);
        var cursor = start;

        for (var i = 0; i < groups.Count; i++)
        {
            var pieceEnd = i == groups.Count - 1
                ? end
                : cursor + TimeSpan.FromTicks(Math.Max(1, totalTicks * wordCounts[i] / totalWords));

            if (pieceEnd > end)
            {
                pieceEnd = end;
            }

            var piece = CloneCaption(caption);
            piece.Id = Guid.NewGuid();
            piece.OriginalText = groups[i];
            piece.Start = cursor;
            piece.End = pieceEnd;
            pieces.Add(piece);

            cursor = pieceEnd;
        }

        return pieces;
    }

    /// <summary>Explicit field-by-field copy on purpose: a missed field here silently loses a word's
    /// timing/confidence/verification state, which is exactly the class of bug that already bit this
    /// codebase once in TimelineEditSession.Clone.</summary>
    private static CaptionWord CloneCaption(CaptionWord source) => new()
    {
        Id = source.Id,
        OriginalText = source.OriginalText,
        NormalizedText = source.NormalizedText,
        Start = source.Start,
        End = source.End,
        Confidence = source.Confidence,
        Source = source.Source,
        VerificationStatus = source.VerificationStatus,
        LineBreakAfter = source.LineBreakAfter
    };
}
