using System.Globalization;
using System.Text;
using NPVideoStudio.Domain;

namespace NPVideoStudio.Media;

/// <summary>The real ffmpeg command shape to execute - pure data, so <see cref="FfmpegFilterGraphBuilder"/> can be unit tested without launching any process.</summary>
public sealed class FfmpegRenderPlan
{
    public required IReadOnlyList<string> InputFilePaths { get; init; }
    public required string FilterComplexArgument { get; init; }
    public required string VideoMapLabel { get; init; }
    public required string AudioMapLabel { get; init; }
    public required double TotalDurationSeconds { get; init; }
}

/// <summary>
/// Builds the ffmpeg <c>-filter_complex</c> graph for rendering a project's timeline (spec Phase 9) -
/// entirely as string/data construction, no process execution, so every edge case (gaps between clips,
/// fades, caption timing) is unit-testable without ffmpeg installed. <see cref="RenderService"/> is what
/// actually runs the plan this produces.
///
/// Real, deliberate scope for this pass (see PHASE_STATUS.md): only the first non-empty Video track is
/// rendered (no multi-video-track layering yet); that track's own embedded audio is kept, but separate
/// standalone Audio-kind tracks are not yet mixed in; Caption/Text tracks are burned in via `drawtext`
/// at a fixed default position (no per-clip position exists in the timeline model yet); ImageOverlay
/// tracks are not composited yet. All of these are honest gaps, not silently-wrong behavior - a track
/// this doesn't render from is simply not included, never partially/incorrectly rendered.
/// </summary>
public static class FfmpegFilterGraphBuilder
{
    private const double GapEpsilonSeconds = 0.01;

    /// <summary>
    /// <paramref name="targetWidth"/>/<paramref name="targetHeight"/> should be the project's export
    /// format (<c>Project.Format.Width/Height</c>) - every video segment (including gap fillers) is
    /// scaled/padded to this exact size before concat, since ffmpeg's concat filter requires every input
    /// to already match (mismatched source clip resolutions, or a gap filler sized differently from the
    /// clips around it, both fail concat outright - verified for real while building this).
    /// </summary>
    public static FfmpegRenderPlan Build(
        Timeline timeline, IReadOnlyList<MediaAsset> mediaLibrary, int targetWidth = 1920, int targetHeight = 1080, double frameRate = 30)
    {
        var videoTrack = timeline.Tracks.FirstOrDefault(t => t.Kind == TimelineTrackKind.Video && t.Clips.Count > 0);
        if (videoTrack is null)
        {
            throw new InvalidOperationException("Timeline nema video traku sa klipovima - nema šta da se renderuje.");
        }

        var clips = videoTrack.Clips.OrderBy(c => c.TimelineStartSeconds).ToList();
        var inputs = new List<string>();
        var filterLines = new List<string>();

        // (originalTimelineThreshold, cumulativeSecondsToSubtract) - a real transition overlaps and so
        // shortens the rendered video relative to the authored timeline; any caption/text clip timed
        // after a transition point needs its burned-in timestamp shifted earlier by the same amount, or
        // it would show up late (or not at all) once the transition has compressed the timeline before it.
        var timeShiftPoints = new List<(double OriginalThreshold, double CumulativeShift)>();

        string? currentVideoLabel = null;
        string? currentAudioLabel = null;
        var cursor = 0.0; // original (authored) timeline position, for gap detection between clips
        var renderedDuration = 0.0; // actual output duration so far, after any transition overlap is removed
        var segmentIndex = 0;
        var fillerIndex = 0;
        var joinIndex = 0;
        TimelineClip? previousClip = null;

        foreach (var clip in clips)
        {
            var gap = clip.TimelineStartSeconds - cursor;
            var canTransitionFromPrevious = previousClip is not null && gap <= GapEpsilonSeconds;

            if (gap > GapEpsilonSeconds)
            {
                var vFillLabel = $"[vfill{fillerIndex}]";
                var aFillLabel = $"[afill{fillerIndex}]";
                filterLines.Add(FormattableString.Invariant(
                    $"color=c=black:s={targetWidth}x{targetHeight}:d={gap}:r={frameRate}[vfillraw{fillerIndex}]"));
                filterLines.Add($"[vfillraw{fillerIndex}]format=yuv420p{vFillLabel}");
                filterLines.Add(FormattableString.Invariant($"anullsrc=r=44100:cl=stereo:d={gap}{aFillLabel}"));
                fillerIndex++;

                (currentVideoLabel, currentAudioLabel, renderedDuration) = AppendSegment(
                    filterLines, currentVideoLabel, currentAudioLabel, vFillLabel, aFillLabel, gap, renderedDuration, ref joinIndex);
                previousClip = null; // a filler never participates in a transition
            }

            var asset = mediaLibrary.FirstOrDefault(a => a.Id == clip.MediaAssetId);
            if (asset is null)
            {
                throw new InvalidOperationException($"Klip referencira medij koji ne postoji u biblioteci projekta (Id: {clip.MediaAssetId}).");
            }

            var inputIndex = inputs.Count;
            inputs.Add(asset.FilePath);

            var duration = clip.TimelineDurationSeconds;
            var vLabel = $"[v{segmentIndex}]";
            var aLabel = $"[a{segmentIndex}]";

            var videoFilter = new StringBuilder();
            videoFilter.Append(FormattableString.Invariant(
                $"[{inputIndex}:v]trim=start={clip.SourceTrimInSeconds}:end={clip.SourceTrimOutSeconds},setpts=PTS-STARTPTS"));
            videoFilter.Append(FormattableString.Invariant(
                $",scale={targetWidth}:{targetHeight}:force_original_aspect_ratio=decrease,pad={targetWidth}:{targetHeight}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1"));
            if (clip.FadeInSeconds > 0)
            {
                videoFilter.Append(FormattableString.Invariant($",fade=t=in:st=0:d={clip.FadeInSeconds}"));
            }
            if (clip.FadeOutSeconds > 0)
            {
                var fadeOutStart = Math.Max(0, duration - clip.FadeOutSeconds);
                videoFilter.Append(FormattableString.Invariant($",fade=t=out:st={fadeOutStart}:d={clip.FadeOutSeconds}"));
            }
            videoFilter.Append(vLabel);
            filterLines.Add(videoFilter.ToString());

            var audioFilter = new StringBuilder();
            var volume = clip.IsMuted ? 0 : clip.Volume;
            audioFilter.Append(FormattableString.Invariant(
                $"[{inputIndex}:a]atrim=start={clip.SourceTrimInSeconds}:end={clip.SourceTrimOutSeconds},asetpts=PTS-STARTPTS,volume={volume}"));
            if (clip.FadeInSeconds > 0)
            {
                audioFilter.Append(FormattableString.Invariant($",afade=t=in:st=0:d={clip.FadeInSeconds}"));
            }
            if (clip.FadeOutSeconds > 0)
            {
                var fadeOutStart = Math.Max(0, duration - clip.FadeOutSeconds);
                audioFilter.Append(FormattableString.Invariant($",afade=t=out:st={fadeOutStart}:d={clip.FadeOutSeconds}"));
            }
            audioFilter.Append(aLabel);
            filterLines.Add(audioFilter.ToString());

            var useTransition = canTransitionFromPrevious && clip.TransitionInType != ClipTransitionType.None;
            var transitionDuration = useTransition
                ? Math.Max(0.05, Math.Min(clip.TransitionInDurationSeconds, Math.Min(duration, previousClip!.TimelineDurationSeconds) - 0.05))
                : 0;

            if (useTransition && transitionDuration > 0)
            {
                var xfadeVLabel = $"[vxfade{joinIndex}]";
                var xfadeName = TransitionName(clip.TransitionInType);
                var offset = renderedDuration - transitionDuration;
                filterLines.Add(FormattableString.Invariant(
                    $"{currentVideoLabel}{vLabel}xfade=transition={xfadeName}:duration={transitionDuration}:offset={offset}{xfadeVLabel}"));

                var xfadeALabel = $"[axfade{joinIndex}]";
                filterLines.Add(FormattableString.Invariant(
                    $"{currentAudioLabel}{aLabel}acrossfade=d={transitionDuration}{xfadeALabel}"));

                joinIndex++;
                currentVideoLabel = xfadeVLabel;
                currentAudioLabel = xfadeALabel;
                renderedDuration = renderedDuration + duration - transitionDuration;
                timeShiftPoints.Add((clip.TimelineStartSeconds, timeShiftPoints.Count == 0
                    ? transitionDuration
                    : timeShiftPoints[^1].CumulativeShift + transitionDuration));
            }
            else
            {
                (currentVideoLabel, currentAudioLabel, renderedDuration) = AppendSegment(
                    filterLines, currentVideoLabel, currentAudioLabel, vLabel, aLabel, duration, renderedDuration, ref joinIndex);
            }

            segmentIndex++;
            cursor = clip.TimelineEndSeconds;
            previousClip = clip;
        }

        double MapToRenderedTime(double originalSeconds)
        {
            var shift = 0.0;
            foreach (var (threshold, cumulativeShift) in timeShiftPoints)
            {
                if (originalSeconds >= threshold)
                {
                    shift = cumulativeShift;
                }
            }

            return Math.Max(0, originalSeconds - shift);
        }

        var currentTextVideoLabel = currentVideoLabel!;
        var textClips = timeline.Tracks
            .Where(t => t.Kind is TimelineTrackKind.Caption or TimelineTrackKind.Text)
            .SelectMany(t => t.Clips)
            .Where(c => !string.IsNullOrEmpty(c.TextContent))
            .OrderBy(c => c.TimelineStartSeconds)
            .ToList();

        for (var i = 0; i < textClips.Count; i++)
        {
            var clip = textClips[i];
            var nextLabel = $"[vtext{i}]";
            var displayText = ApplyTextCase(clip.TextContent!, clip.TextCase);
            var escapedText = EscapeDrawtext(displayText);
            var y = clip.TextPosition switch
            {
                CaptionTextPosition.Top => "h*0.08",
                CaptionTextPosition.Middle => "(h-text_h)/2",
                _ => "h*0.85"
            };
            var x = clip.TextHorizontalAlign switch
            {
                TextHorizontalAlign.Left => "w*0.05",
                TextHorizontalAlign.Right => "w-text_w-w*0.05",
                _ => "(w-text_w)/2"
            };
            var fontFilePath = CaptionFontResolver.ResolveFontFilePath(clip.FontChoice, clip.IsTextBold, clip.IsTextItalic);
            var fontFileArgument = fontFilePath is null ? string.Empty : $":fontfile='{EscapeDrawtext(fontFilePath)}'";
            var renderedStart = MapToRenderedTime(clip.TimelineStartSeconds);
            var renderedEnd = MapToRenderedTime(clip.TimelineEndSeconds);

            var extraArguments = new StringBuilder();
            extraArguments.Append(clip.HasTextBackground
                ? FormattableString.Invariant($":box=1:boxcolor={clip.TextBackgroundColor}@{clip.TextBackgroundOpacity.ToString(CultureInfo.InvariantCulture)}:boxborderw=10")
                : string.Empty);
            if (clip.TextOutlineColor is not null)
            {
                extraArguments.Append(FormattableString.Invariant($":borderw={clip.TextOutlineWidthPx}:bordercolor={clip.TextOutlineColor}"));
            }
            if (clip.TextShadowColor is not null)
            {
                extraArguments.Append(FormattableString.Invariant(
                    $":shadowcolor={clip.TextShadowColor}:shadowx={clip.TextShadowOffsetPx}:shadowy={clip.TextShadowOffsetPx}"));
            }
            if (clip.LineSpacingPx != 0)
            {
                extraArguments.Append(FormattableString.Invariant($":line_spacing={clip.LineSpacingPx}"));
            }
            if (clip.FadeInSeconds > 0 || clip.FadeOutSeconds > 0)
            {
                extraArguments.Append($":alpha='{BuildTextAlphaExpression(clip, renderedStart, renderedEnd)}'");
            }

            filterLines.Add(FormattableString.Invariant(
                $"{currentTextVideoLabel}drawtext=text='{escapedText}':enable='between(t,{renderedStart},{renderedEnd})':x={x}:y={y}:fontsize={clip.FontSizePx}:fontcolor={clip.TextColor}{fontFileArgument}{extraArguments}{nextLabel}"));
            currentTextVideoLabel = nextLabel;
        }

        return new FfmpegRenderPlan
        {
            InputFilePaths = inputs,
            FilterComplexArgument = string.Join(';', filterLines),
            VideoMapLabel = currentTextVideoLabel,
            AudioMapLabel = currentAudioLabel!,
            TotalDurationSeconds = renderedDuration
        };
    }

    /// <summary>Joins a new segment onto the running output with a plain hard-cut `concat` (used for the
    /// very first segment - nothing to join yet, so it just becomes the running output - gap fillers, and
    /// any clip that doesn't have a transition into it). Returns the new running (video, audio) labels and
    /// output duration so far.</summary>
    private static (string VideoLabel, string AudioLabel, double Duration) AppendSegment(
        List<string> filterLines, string? currentVideoLabel, string? currentAudioLabel,
        string nextVideoLabel, string nextAudioLabel, double nextDuration, double runningDuration, ref int joinIndex)
    {
        if (currentVideoLabel is null || currentAudioLabel is null)
        {
            return (nextVideoLabel, nextAudioLabel, nextDuration);
        }

        var joinedVideoLabel = $"[vjoin{joinIndex}]";
        var joinedAudioLabel = $"[ajoin{joinIndex}]";
        filterLines.Add($"{currentVideoLabel}{nextVideoLabel}concat=n=2:v=1:a=0{joinedVideoLabel}");
        filterLines.Add($"{currentAudioLabel}{nextAudioLabel}concat=n=2:v=0:a=1{joinedAudioLabel}");
        joinIndex++;
        return (joinedVideoLabel, joinedAudioLabel, runningDuration + nextDuration);
    }

    private static string TransitionName(ClipTransitionType type) => type switch
    {
        ClipTransitionType.Fade => "fade",
        ClipTransitionType.WipeLeft => "wipeleft",
        ClipTransitionType.WipeRight => "wiperight",
        ClipTransitionType.SlideLeft => "slideleft",
        ClipTransitionType.SlideRight => "slideright",
        ClipTransitionType.Dissolve => "dissolve",
        ClipTransitionType.ZoomIn => "zoomin",
        _ => "fade"
    };

    private static string ApplyTextCase(string text, TextCaseTransform textCase) => textCase switch
    {
        TextCaseTransform.UpperCase => text.ToUpperInvariant(),
        TextCaseTransform.LowerCase => text.ToLowerInvariant(),
        TextCaseTransform.TitleCase => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant()),
        _ => text
    };

    /// <summary>
    /// Real fade-in/fade-out for a Caption/Text clip's own text, via drawtext's `alpha` option - which
    /// (per FFmpeg's own filter docs) accepts a per-frame expression, not just a fixed 0.0-1.0 value, the
    /// same way `x`/`y` already do above. Ramps from 0 to 1 over the clip's FadeInSeconds at the start of
    /// its enable window, and from 1 to 0 over its FadeOutSeconds at the end - clamped so overlapping
    /// fade-in/fade-out windows on a very short clip never produce a value outside 0..1.
    /// </summary>
    private static string BuildTextAlphaExpression(TimelineClip clip, double renderedStart, double renderedEnd)
    {
        var fadeIn = Math.Max(0, clip.FadeInSeconds);
        var fadeOut = Math.Max(0, clip.FadeOutSeconds);
        var fadeInEnd = renderedStart + fadeIn;
        var fadeOutStart = renderedEnd - fadeOut;

        var fadeInExpr = fadeIn > 0
            ? FormattableString.Invariant($"min(1,max(0,(t-{renderedStart})/{fadeIn}))")
            : "1";
        var fadeOutExpr = fadeOut > 0
            ? FormattableString.Invariant($"min(1,max(0,({renderedEnd}-t)/{fadeOut}))")
            : "1";

        return FormattableString.Invariant(
            $"if(lt(t,{fadeInEnd}),{fadeInExpr},if(gt(t,{fadeOutStart}),{fadeOutExpr},1))");
    }

    /// <summary>
    /// Escapes text for ffmpeg's drawtext `text=` option, empirically verified (not assumed) against a
    /// real ffmpeg 6.1.1 run: backslash and colon must both be escaped even when the whole value is
    /// wrapped in single quotes - an unescaped colon silently truncates everything before it, and an
    /// unescaped comma without the surrounding quotes breaks the entire filter graph outright.
    /// </summary>
    public static string EscapeDrawtext(string text) =>
        text.Replace("\\", "\\\\").Replace(":", "\\:").Replace("'", "\\'");
}
