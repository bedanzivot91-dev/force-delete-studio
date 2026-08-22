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
        Timeline timeline, IReadOnlyList<MediaAsset> mediaLibrary, int targetWidth = 1920, int targetHeight = 1080, double frameRate = 30,
        IReadOnlyDictionary<string, string>? stabilizationTransforms = null)
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
            videoFilter.Append(BuildAutoReframeFilter(clip, targetWidth, targetHeight));
            videoFilter.Append(BuildStabilizationFilter(clip, stabilizationTransforms));
            videoFilter.Append(BuildTemporalVideoFilters(clip, duration));
            videoFilter.Append(BuildSpeedFilter(clip));
            videoFilter.Append(BuildTransformFilters(clip));

            if (!HasVisualKeyframes(clip))
            {
                videoFilter.Append(FormattableString.Invariant(
                    $",scale={targetWidth}:{targetHeight}:force_original_aspect_ratio=decrease,pad={targetWidth}:{targetHeight}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1"));
                videoFilter.Append(BuildEffectFilters(clip));
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
            }
            else
            {
                var animatedSource = $"[vanimsrc{segmentIndex}]";
                var scaleExpr = BuildKeyframeValueExpression(clip, ClipKeyframeProperty.Scale, "t", clip.ScalePercent);
                var fitWidth = FormattableString.Invariant($"min({targetWidth},{targetHeight}*iw/ih)");
                videoFilter.Append(FormattableString.Invariant(
                    $",scale=w='max(2,({fitWidth})*({scaleExpr})/100)':h=-1:eval=frame"));
                videoFilter.Append(BuildAnimatedRotationFilter(clip, "t"));
                videoFilter.Append(BuildEffectFilters(clip));
                videoFilter.Append(",format=rgba");
                videoFilter.Append(BuildAnimatedOpacityFilter(clip, "T"));
                if (clip.FadeInSeconds > 0)
                {
                    videoFilter.Append(FormattableString.Invariant($",fade=t=in:st=0:d={clip.FadeInSeconds}:alpha=1"));
                }
                if (clip.FadeOutSeconds > 0)
                {
                    var fadeOutStart = Math.Max(0, duration - clip.FadeOutSeconds);
                    videoFilter.Append(FormattableString.Invariant($",fade=t=out:st={fadeOutStart}:d={clip.FadeOutSeconds}:alpha=1"));
                }
                videoFilter.Append(animatedSource);
                filterLines.Add(videoFilter.ToString());

                var animatedCanvas = $"[vanimbg{segmentIndex}]";
                filterLines.Add(FormattableString.Invariant(
                    $"color=c=black:s={targetWidth}x{targetHeight}:d={duration}:r={frameRate},format=rgba{animatedCanvas}"));
                var posX = BuildKeyframeValueExpression(clip, ClipKeyframeProperty.PositionX, "t", clip.PositionXPercent);
                var posY = BuildKeyframeValueExpression(clip, ClipKeyframeProperty.PositionY, "t", clip.PositionYPercent);
                var x = $"(main_w*(({posX})/100))-(overlay_w/2)";
                var y = $"(main_h*(({posY})/100))-(overlay_h/2)";
                filterLines.Add(FormattableString.Invariant(
                    $"{animatedCanvas}{animatedSource}overlay=x='{x}':y='{y}':shortest=1:format=auto,format=yuv420p,setsar=1{vLabel}"));
            }

            var audioFilter = new StringBuilder();
            var volume = clip.IsMuted ? 0 : clip.Volume;
            audioFilter.Append(FormattableString.Invariant(
                $"[{inputIndex}:a]atrim=start={clip.SourceTrimInSeconds}:end={clip.SourceTrimOutSeconds},asetpts=PTS-STARTPTS"));
            if (clip.IsReversed && !clip.IsFreezeFrame)
            {
                audioFilter.Append(",areverse");
            }
            audioFilter.Append(BuildAudioSpeedFilter(clip));
            audioFilter.Append(BuildAudioEnhancementFilters(clip));
            audioFilter.Append(FormattableString.Invariant($",volume={(clip.IsFreezeFrame ? 0 : volume)}"));
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

        // --- Layer compositing (the CapCut-style part) -------------------------------------------
        // Everything above the base video track gets laid over it here, before any text is burned in, so
        // captions stay on top of overlays rather than being hidden behind a sticker or picture-in-picture.
        //
        // Deliberately keyed off the *first* Video track being the background: that matches how every
        // layer-based editor works (bottom layer fills the frame) and matches what the base chain above
        // already built. Track order in Timeline.Tracks is the z-order, first = furthest back.
        currentVideoLabel = AppendOverlayLayers(
            timeline, videoTrack, mediaLibrary, inputs, filterLines,
            currentVideoLabel!, targetWidth, targetHeight, MapToRenderedTime, stabilizationTransforms);

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
            var defaultYPercent = clip.TextPosition switch
            {
                CaptionTextPosition.Top => 8.0,
                CaptionTextPosition.Middle => 50.0,
                _ => 85.0
            };
            var defaultXPercent = clip.TextHorizontalAlign switch
            {
                TextHorizontalAlign.Left => 5.0,
                TextHorizontalAlign.Right => 95.0,
                _ => 50.0
            };
            var textLocalTime = FormattableString.Invariant($"(t-{MapToRenderedTime(clip.TimelineStartSeconds)})");
            var y = HasKeyframes(clip, ClipKeyframeProperty.PositionY)
                ? $"h*(({BuildKeyframeValueExpression(clip, ClipKeyframeProperty.PositionY, textLocalTime, defaultYPercent)})/100)-text_h/2"
                : clip.TextPosition switch
                {
                    CaptionTextPosition.Top => "h*0.08",
                    CaptionTextPosition.Middle => "(h-text_h)/2",
                    _ => "h*0.85"
                };
            var x = HasKeyframes(clip, ClipKeyframeProperty.PositionX)
                ? $"w*(({BuildKeyframeValueExpression(clip, ClipKeyframeProperty.PositionX, textLocalTime, defaultXPercent)})/100)-text_w/2"
                : clip.TextHorizontalAlign switch
                {
                    TextHorizontalAlign.Left => "w*0.05",
                    TextHorizontalAlign.Right => "w-text_w-w*0.05",
                    _ => "(w-text_w)/2"
                };
            var fontFilePath = CaptionFontResolver.ResolveFontFilePath(clip);
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
            var textAlpha = clip.FadeInSeconds > 0 || clip.FadeOutSeconds > 0
                ? BuildTextAlphaExpression(clip, renderedStart, renderedEnd)
                : "1";
            if (HasKeyframes(clip, ClipKeyframeProperty.Opacity))
            {
                var opacity = BuildKeyframeValueExpression(clip, ClipKeyframeProperty.Opacity, textLocalTime, 1);
                textAlpha = $"({textAlpha})*({opacity})";
            }
            if (textAlpha != "1")
            {
                extraArguments.Append($":alpha='{textAlpha}'");
            }

            var fontSize = HasKeyframes(clip, ClipKeyframeProperty.Scale)
                ? $"'{clip.FontSizePx}*({BuildKeyframeValueExpression(clip, ClipKeyframeProperty.Scale, textLocalTime, 100)})/100'"
                : clip.FontSizePx.ToString(CultureInfo.InvariantCulture);

            var drawTextX = HasKeyframes(clip, ClipKeyframeProperty.PositionX) ? $"'{x}'" : x;
            var drawTextY = HasKeyframes(clip, ClipKeyframeProperty.PositionY) ? $"'{y}'" : y;
            filterLines.Add(FormattableString.Invariant(
                $"{currentTextVideoLabel}drawtext=text='{escapedText}':enable='between(t,{renderedStart},{renderedEnd})':x={drawTextX}:y={drawTextY}:fontsize={fontSize}:fontcolor={clip.TextColor}{fontFileArgument}{extraArguments}{nextLabel}"));
            currentTextVideoLabel = nextLabel;
        }

        // Separate music/voice-over tracks get mixed over the video track's own audio here, last, so the
        // mix includes everything above.
        var finalAudioLabel = AppendAudioTracks(
            timeline, mediaLibrary, inputs, filterLines, currentAudioLabel!, MapToRenderedTime);

        return new FfmpegRenderPlan
        {
            InputFilePaths = inputs,
            FilterComplexArgument = string.Join(';', filterLines),
            VideoMapLabel = currentTextVideoLabel,
            AudioMapLabel = finalAudioLabel,
            TotalDurationSeconds = renderedDuration
        };
    }

    /// <summary>
    /// Mixes every standalone Audio-kind track over the video track's own sound, and returns the label of
    /// the mixed result. Returns <paramref name="videoTrackAudioLabel"/> unchanged when there is nothing to
    /// mix, so a project without a music track produces exactly the graph it did before.
    ///
    /// This closes what was the single most damaging gap in the whole app for its actual purpose: the
    /// built-in "Muzički spot" template creates an Audio track, the UI has a "+ Audio traka" button, the
    /// user could drop their song on it - and the exported video simply had no music, with no error. For an
    /// app whose whole job is making videos from songs, silently dropping the song is as bad as crashing.
    ///
    /// Each clip is delayed to its own timeline position with <c>adelay</c> (so a chorus placed at 0:45
    /// lands at 0:45, not at the start), gets its own volume/fades, is scaled by its track's volume, and
    /// respects mute/hide/solo. <c>amix</c> uses <c>duration=first</c> deliberately: the first input is the
    /// video's own audio, so the finished file's length follows the picture and a song longer than the
    /// video is cut off at the end rather than extending the export past its last frame.
    /// </summary>
    private static string AppendAudioTracks(
        Timeline timeline,
        IReadOnlyList<MediaAsset> mediaLibrary,
        List<string> inputs,
        List<string> filterLines,
        string videoTrackAudioLabel,
        Func<double, double> mapToRenderedTime)
    {
        var audioTracks = timeline.Tracks
            .Where(t => t.Kind == TimelineTrackKind.Audio && !t.IsHidden && !t.IsMuted)
            .ToList();

        // Solo on any audio track means "only the soloed ones", the standard behaviour in every mixer.
        if (audioTracks.Any(t => t.IsSolo))
        {
            audioTracks = audioTracks.Where(t => t.IsSolo).ToList();
        }

        var mixLabels = new List<string> { videoTrackAudioLabel };
        var clipIndex = 0;

        foreach (var track in audioTracks)
        {
            foreach (var clip in track.Clips.Where(c => !string.IsNullOrEmpty(c.MediaAssetId)).OrderBy(c => c.TimelineStartSeconds))
            {
                var asset = mediaLibrary.FirstOrDefault(a => a.Id == clip.MediaAssetId);
                if (asset is null)
                {
                    throw new InvalidOperationException(
                        $"Audio traka referencira medij koji ne postoji u biblioteci projekta (Id: {clip.MediaAssetId}).");
                }

                var inputIndex = inputs.Count;
                inputs.Add(asset.FilePath);

                var label = $"[amus{clipIndex}]";
                var duration = clip.TimelineDurationSeconds;
                var volume = clip.IsMuted ? 0 : clip.Volume * track.Volume;
                var delayMs = (int)Math.Round(Math.Max(0, mapToRenderedTime(clip.TimelineStartSeconds)) * 1000);

                var chain = new StringBuilder();
                chain.Append(FormattableString.Invariant(
                    $"[{inputIndex}:a]atrim=start={clip.SourceTrimInSeconds}:end={clip.SourceTrimOutSeconds},asetpts=PTS-STARTPTS"));
                chain.Append(BuildAudioSpeedFilter(clip));
                chain.Append(BuildAudioEnhancementFilters(clip));
                chain.Append(FormattableString.Invariant($",volume={volume}"));

                if (clip.FadeInSeconds > 0)
                {
                    chain.Append(FormattableString.Invariant($",afade=t=in:st=0:d={clip.FadeInSeconds}"));
                }

                if (clip.FadeOutSeconds > 0)
                {
                    var fadeOutStart = Math.Max(0, duration - clip.FadeOutSeconds);
                    chain.Append(FormattableString.Invariant($",afade=t=out:st={fadeOutStart}:d={clip.FadeOutSeconds}"));
                }

                if (delayMs > 0)
                {
                    // all=1 applies the delay to every channel - without it, adelay silently delays only
                    // the first channel and the music arrives lopsided across the stereo field.
                    chain.Append(FormattableString.Invariant($",adelay={delayMs}:all=1"));
                }

                chain.Append(label);
                filterLines.Add(chain.ToString());

                mixLabels.Add(label);
                clipIndex++;
            }
        }

        if (mixLabels.Count == 1)
        {
            return videoTrackAudioLabel;
        }

        const string mixedLabel = "[amixed]";
        filterLines.Add(FormattableString.Invariant(
            $"{string.Concat(mixLabels)}amix=inputs={mixLabels.Count}:duration=first:dropout_transition=0:normalize=0{mixedLabel}"));

        return mixedLabel;
    }

    /// <summary>
    /// Cuts a new, standalone <see cref="Timeline"/> containing only the clips that overlap
    /// [<paramref name="rangeStartSeconds"/>, <paramref name="rangeEndSeconds"/>), each re-timed relative
    /// to the range's own start - so <see cref="Build"/> can render just that window instead of the whole
    /// project. Real, researched motivation (see PHASE_STATUS.md): even a dedicated open-source non-linear
    /// editor on the same stack this app uses (FramePFX, github.com/AngryCarrot789/FramePFX, C#/Avalonia)
    /// documents live full-timeline compositing as a genuinely hard, still-unsolved performance problem for
    /// them (40ms to decode a single 4K frame, undergoing a full rewrite because of it) - so instead of
    /// chasing true live compositing, this makes the existing real "render then play" pipeline
    /// (<see cref="RenderService"/>, real ffmpeg, real audio) fast enough to feel interactive by rendering
    /// a short window around the playhead instead of the entire timeline every time.
    ///
    /// A clip whose start gets cut off by the range boundary has its <see cref="TimelineClip.TransitionInType"/>
    /// reset to <see cref="ClipTransitionType.None"/> - a transition into a clip that no longer has its
    /// predecessor in this reduced timeline has nothing left to transition from, so keeping it set would
    /// either crash or produce a nonsensical partial transition.
    /// </summary>
    public static Timeline ExtractRangeTimeline(Timeline timeline, double rangeStartSeconds, double rangeEndSeconds)
    {
        var result = new Timeline();
        foreach (var track in timeline.Tracks)
        {
            var newTrack = new TimelineTrack
            {
                Kind = track.Kind,
                Name = track.Name,
                IsLocked = track.IsLocked,
                IsHidden = track.IsHidden,
                IsMuted = track.IsMuted,
                IsSolo = track.IsSolo,
                Volume = track.Volume
            };

            foreach (var clip in track.Clips)
            {
                var clipStart = clip.TimelineStartSeconds;
                var clipEnd = clip.TimelineEndSeconds;
                if (clipEnd <= rangeStartSeconds || clipStart >= rangeEndSeconds)
                {
                    continue; // no overlap with the requested window at all
                }

                var overlapStart = Math.Max(clipStart, rangeStartSeconds);
                var overlapEnd = Math.Min(clipEnd, rangeEndSeconds);
                var trimmedFromStart = overlapStart - clipStart;
                var trimmedFromEnd = clipEnd - overlapEnd;

                var newClip = CloneClipForRange(clip);
                newClip.TimelineStartSeconds = overlapStart - rangeStartSeconds;
                var sourceRate = clip.IsFreezeFrame ? 1.0 : SpeedCurveMath.ClampSpeed(clip.SpeedMultiplier);
                if (clip.IsFreezeFrame)
                {
                    var visibleDuration = Math.Max(0.05, overlapEnd - overlapStart);
                    if (clip.IsReversed)
                    {
                        newClip.SourceTrimOutSeconds = clip.SourceTrimOutSeconds;
                        newClip.SourceTrimInSeconds = Math.Max(clip.SourceTrimInSeconds, clip.SourceTrimOutSeconds - visibleDuration);
                    }
                    else
                    {
                        newClip.SourceTrimInSeconds = clip.SourceTrimInSeconds;
                        newClip.SourceTrimOutSeconds = Math.Min(clip.SourceTrimOutSeconds, clip.SourceTrimInSeconds + visibleDuration);
                    }
                }
                else if (SpeedCurveMath.HasCurve(clip) && !clip.IsReversed)
                {
                    newClip.SourceTrimInSeconds = SpeedCurveMath.SourceTimeAtTimelineOffset(clip, trimmedFromStart);
                    newClip.SourceTrimOutSeconds = SpeedCurveMath.SourceTimeAtTimelineOffset(
                        clip, Math.Max(0, clip.TimelineDurationSeconds - trimmedFromEnd));
                }
                else if (clip.IsReversed)
                {
                    newClip.SourceTrimInSeconds = clip.SourceTrimInSeconds + trimmedFromEnd * sourceRate;
                    newClip.SourceTrimOutSeconds = clip.SourceTrimOutSeconds - trimmedFromStart * sourceRate;
                }
                else
                {
                    newClip.SourceTrimInSeconds = clip.SourceTrimInSeconds + trimmedFromStart * sourceRate;
                    newClip.SourceTrimOutSeconds = clip.SourceTrimOutSeconds - trimmedFromEnd * sourceRate;
                }
                if (trimmedFromStart > 0)
                {
                    newClip.TransitionInType = ClipTransitionType.None;
                }

                SliceKeyframesForRange(clip, newClip, trimmedFromStart, Math.Max(0.05, overlapEnd - overlapStart));
                newTrack.Clips.Add(newClip);
            }

            if (newTrack.Clips.Count > 0)
            {
                result.Tracks.Add(newTrack);
            }
        }

        return result;
    }

    /// <summary>Every <see cref="TimelineClip"/> field, listed explicitly on purpose - the same real bug
    /// (an implicit field list silently falling behind the type over time, see
    /// <c>TimelineEditSession.Clone</c>) is exactly what this guards against here too.</summary>
    private static TimelineClip CloneClipForRange(TimelineClip clip) => new()
    {
        Id = clip.Id,
        MediaAssetId = clip.MediaAssetId,
        TextContent = clip.TextContent,
        FontChoice = clip.FontChoice,
        TextFontFamilyName = clip.TextFontFamilyName,
        TextFontFilePath = clip.TextFontFilePath,
        FontSizePx = clip.FontSizePx,
        TextColor = clip.TextColor,
        TextPosition = clip.TextPosition,
        TextHorizontalAlign = clip.TextHorizontalAlign,
        TextOutlineColor = clip.TextOutlineColor,
        TextOutlineWidthPx = clip.TextOutlineWidthPx,
        TextShadowColor = clip.TextShadowColor,
        TextShadowOffsetPx = clip.TextShadowOffsetPx,
        HasTextBackground = clip.HasTextBackground,
        TextBackgroundColor = clip.TextBackgroundColor,
        TextBackgroundOpacity = clip.TextBackgroundOpacity,
        IsTextBold = clip.IsTextBold,
        IsTextItalic = clip.IsTextItalic,
        TextCase = clip.TextCase,
        LineSpacingPx = clip.LineSpacingPx,
        SourceTrimInSeconds = clip.SourceTrimInSeconds,
        SourceTrimOutSeconds = clip.SourceTrimOutSeconds,
        TimelineStartSeconds = clip.TimelineStartSeconds,
        FadeInSeconds = clip.FadeInSeconds,
        FadeOutSeconds = clip.FadeOutSeconds,
        TransitionInType = clip.TransitionInType,
        TransitionInDurationSeconds = clip.TransitionInDurationSeconds,
        IsMuted = clip.IsMuted,
        Volume = clip.Volume,
        AudioNoiseReductionEnabled = clip.AudioNoiseReductionEnabled,
        AudioNoiseReductionStrength = clip.AudioNoiseReductionStrength,
        AudioEnhanceVoiceEnabled = clip.AudioEnhanceVoiceEnabled,
        AudioLoudnessNormalizationEnabled = clip.AudioLoudnessNormalizationEnabled,
        ScalePercent = clip.ScalePercent,
        PositionXPercent = clip.PositionXPercent,
        PositionYPercent = clip.PositionYPercent,
        Opacity = clip.Opacity,
        Effect = clip.Effect,
        Brightness = clip.Brightness,
        Contrast = clip.Contrast,
        Saturation = clip.Saturation,
        ExposureStops = clip.ExposureStops,
        Highlights = clip.Highlights,
        Shadows = clip.Shadows,
        Temperature = clip.Temperature,
        Tint = clip.Tint,
        SpeedMultiplier = clip.SpeedMultiplier,
        SpeedCurvePreset = clip.SpeedCurvePreset,
        SpeedCurvePoints = clip.SpeedCurvePoints.Select(point => new SpeedCurvePoint
        {
            Id = point.Id,
            SourceTimeSeconds = point.SourceTimeSeconds,
            SpeedMultiplier = point.SpeedMultiplier
        }).ToList(),
        StabilizationEnabled = clip.StabilizationEnabled,
        StabilizationShakiness = clip.StabilizationShakiness,
        StabilizationAccuracy = clip.StabilizationAccuracy,
        StabilizationSmoothing = clip.StabilizationSmoothing,
        StabilizationZoomPercent = clip.StabilizationZoomPercent,
        StabilizationOptimalZoom = clip.StabilizationOptimalZoom,
        TrackingRegionCenterX = clip.TrackingRegionCenterX,
        TrackingRegionCenterY = clip.TrackingRegionCenterY,
        TrackingRegionWidth = clip.TrackingRegionWidth,
        TrackingRegionHeight = clip.TrackingRegionHeight,
        MotionTrackingPoints = clip.MotionTrackingPoints.Select(point => new MotionTrackingPoint
        {
            SourceTimeSeconds = point.SourceTimeSeconds,
            CenterX = point.CenterX,
            CenterY = point.CenterY,
            Width = point.Width,
            Height = point.Height,
            Confidence = point.Confidence
        }).ToList(),
        AutoReframeEnabled = clip.AutoReframeEnabled,
        RotationDegrees = clip.RotationDegrees,
        FlipHorizontal = clip.FlipHorizontal,
        FlipVertical = clip.FlipVertical,
        CropLeftPercent = clip.CropLeftPercent,
        CropTopPercent = clip.CropTopPercent,
        CropRightPercent = clip.CropRightPercent,
        CropBottomPercent = clip.CropBottomPercent,
        IsReversed = clip.IsReversed,
        IsFreezeFrame = clip.IsFreezeFrame,
        ChromaKeyEnabled = clip.ChromaKeyEnabled,
        ChromaKeyColor = clip.ChromaKeyColor,
        ChromaKeySimilarity = clip.ChromaKeySimilarity,
        ChromaKeyBlend = clip.ChromaKeyBlend,
        MaskType = clip.MaskType,
        MaskCenterXPercent = clip.MaskCenterXPercent,
        MaskCenterYPercent = clip.MaskCenterYPercent,
        MaskWidthPercent = clip.MaskWidthPercent,
        MaskHeightPercent = clip.MaskHeightPercent,
        MaskFeatherPercent = clip.MaskFeatherPercent,
        MaskRotationDegrees = clip.MaskRotationDegrees,
        MaskInvert = clip.MaskInvert,
        BlendMode = clip.BlendMode,
        Keyframes = clip.Keyframes.Select(CloneKeyframe).ToList()
    };

    private static ClipKeyframe CloneKeyframe(ClipKeyframe keyframe) => new()
    {
        Id = keyframe.Id,
        Property = keyframe.Property,
        TimeSeconds = keyframe.TimeSeconds,
        Value = keyframe.Value,
        Easing = keyframe.Easing
    };

    private static void SliceKeyframesForRange(TimelineClip original, TimelineClip sliced, double trimmedFromStart, double visibleDuration)
    {
        if (original.Keyframes.Count == 0)
        {
            return;
        }

        var result = new List<ClipKeyframe>();
        foreach (var property in original.Keyframes.Select(k => k.Property).Distinct())
        {
            var fallback = ClipKeyframeEvaluator.StaticValue(original, property);
            result.Add(new ClipKeyframe
            {
                Property = property,
                TimeSeconds = 0,
                Value = ClipKeyframeEvaluator.Evaluate(original, property, trimmedFromStart),
                Easing = ClipKeyframeEasing.Linear
            });

            foreach (var point in original.Keyframes.Where(k => k.Property == property &&
                         k.TimeSeconds > trimmedFromStart + 0.001 &&
                         k.TimeSeconds < trimmedFromStart + visibleDuration - 0.001))
            {
                var clone = CloneKeyframe(point);
                clone.TimeSeconds -= trimmedFromStart;
                result.Add(clone);
            }

            result.Add(new ClipKeyframe
            {
                Property = property,
                TimeSeconds = visibleDuration,
                Value = ClipKeyframeEvaluator.Evaluate(original, property, trimmedFromStart + visibleDuration),
                Easing = original.Keyframes.Where(k => k.Property == property && k.TimeSeconds >= trimmedFromStart + visibleDuration)
                    .OrderBy(k => k.TimeSeconds)
                    .Select(k => k.Easing)
                    .FirstOrDefault()
            });
        }
        sliced.Keyframes = result.OrderBy(k => k.Property).ThenBy(k => k.TimeSeconds).ToList();
    }

    /// <summary>Joins a new segment onto the running output with a plain hard-cut `concat` (used for the
    /// very first segment - nothing to join yet, so it just becomes the running output - gap fillers, and
    /// any clip that doesn't have a transition into it). Returns the new running (video, audio) labels and
    /// output duration so far.</summary>
    /// <summary>
    /// Lays every overlay clip over the already-built base video, in track order (z-order), and returns
    /// the label of the composited result. Returns <paramref name="baseVideoLabel"/> untouched when there
    /// is nothing to overlay, so a single-track project produces the exact same filter graph as before -
    /// this feature costs nothing when unused.
    ///
    /// Each overlay is: scaled to its own <see cref="TimelineClip.ScalePercent"/> of the frame width
    /// (height follows the source aspect ratio, never stretched), given its
    /// <see cref="TimelineClip.Opacity"/>, and switched on only for its own time range via ffmpeg's
    /// <c>enable='between(t,...)'</c> - the overlay input keeps running underneath, but is simply not
    /// drawn outside its window, which is what makes a sticker appear and disappear on cue.
    ///
    /// Positions are computed from the clip's CENTER (see <see cref="TimelineClip.PositionXPercent"/>),
    /// because that is what a user dragging a sticker actually means by "put it here" - a top-left anchor
    /// would make a large overlay jump away from the cursor.
    /// </summary>
    private static string AppendOverlayLayers(
        Timeline timeline,
        TimelineTrack baseVideoTrack,
        IReadOnlyList<MediaAsset> mediaLibrary,
        List<string> inputs,
        List<string> filterLines,
        string baseVideoLabel,
        int targetWidth,
        int targetHeight,
        Func<double, double> mapToRenderedTime,
        IReadOnlyDictionary<string, string>? stabilizationTransforms)
    {
        var overlayClips = timeline.Tracks
            .Where(t => !t.IsHidden)
            .Where(t => (t.Kind == TimelineTrackKind.Video && !ReferenceEquals(t, baseVideoTrack))
                        || t.Kind == TimelineTrackKind.ImageOverlay)
            .SelectMany(t => t.Clips)
            .Where(c => !string.IsNullOrEmpty(c.MediaAssetId))
            .OrderBy(c => c.TimelineStartSeconds)
            .ToList();

        if (overlayClips.Count == 0)
        {
            return baseVideoLabel;
        }

        var currentLabel = baseVideoLabel;

        for (var i = 0; i < overlayClips.Count; i++)
        {
            var clip = overlayClips[i];

            var asset = mediaLibrary.FirstOrDefault(a => a.Id == clip.MediaAssetId);
            if (asset is null)
            {
                throw new InvalidOperationException(
                    $"Sloj referencira medij koji ne postoji u biblioteci projekta (Id: {clip.MediaAssetId}).");
            }

            var inputIndex = inputs.Count;
            inputs.Add(asset.FilePath);

            var scale = Math.Clamp(clip.ScalePercent, 1, 1000) / 100.0;
            var overlayWidth = Math.Max(1, (int)Math.Round(targetWidth * scale));
            var opacity = Math.Clamp(clip.Opacity, 0, 1);
            var start = mapToRenderedTime(clip.TimelineStartSeconds);
            var end = mapToRenderedTime(clip.TimelineEndSeconds);

            var preparedLabel = $"[ovl{i}]";
            var prepared = new StringBuilder();
            prepared.Append(FormattableString.Invariant(
                $"[{inputIndex}:v]trim=start={clip.SourceTrimInSeconds}:end={clip.SourceTrimOutSeconds},setpts=PTS-STARTPTS"));
            // -1 keeps the source aspect ratio; the overlay is sized by width only.
            prepared.Append(BuildAutoReframeFilter(clip, targetWidth, targetHeight));
            prepared.Append(BuildStabilizationFilter(clip, stabilizationTransforms));
            prepared.Append(BuildTemporalVideoFilters(clip, clip.TimelineDurationSeconds));
            prepared.Append(BuildSpeedFilter(clip));
            prepared.Append(BuildTransformFilters(clip));
            if (HasKeyframes(clip, ClipKeyframeProperty.Scale))
            {
                var scaleExpr = BuildKeyframeValueExpression(clip, ClipKeyframeProperty.Scale, "t", clip.ScalePercent);
                prepared.Append(FormattableString.Invariant($",scale=w='max(2,{targetWidth}*({scaleExpr})/100)':h=-1:eval=frame"));
            }
            else
            {
                prepared.Append(FormattableString.Invariant($",scale={overlayWidth}:-1"));
            }
            prepared.Append(BuildAnimatedRotationFilter(clip, "t"));
            prepared.Append(BuildEffectFilters(clip));
            // geq/chromakey/masks need an alpha-capable pixel format.
            prepared.Append(",format=rgba");
            prepared.Append(BuildChromaKeyFilter(clip));
            prepared.Append(BuildMaskFilter(clip));
            if (HasKeyframes(clip, ClipKeyframeProperty.Opacity))
            {
                prepared.Append(BuildAnimatedOpacityFilter(clip, "T"));
            }
            else
            {
                prepared.Append(FormattableString.Invariant($",colorchannelmixer=aa={opacity}"));
            }
            // Overlay streams used to stay at t=0 regardless of where the clip lived on the timeline.
            // That makes a delayed overlay repeat its last decoded frame when enable() finally turns on.
            // Shift the prepared overlay into the same rendered clock as the base before compositing it.
            prepared.Append(FormattableString.Invariant($",setpts=PTS+{start}/TB"));
            prepared.Append(preparedLabel);
            filterLines.Add(prepared.ToString());

            // Centre-anchored. Static clips keep the exact old expressions; animated clips use the global
            // rendered clock minus this clip's rendered start as their local keyframe time.
            var localOverlayTime = FormattableString.Invariant($"(t-{start})");
            var centreX = HasKeyframes(clip, ClipKeyframeProperty.PositionX)
                ? $"(main_w*(({BuildKeyframeValueExpression(clip, ClipKeyframeProperty.PositionX, localOverlayTime, clip.PositionXPercent)})/100))-(overlay_w/2)"
                : FormattableString.Invariant($"(main_w*{clip.PositionXPercent / 100.0})-(overlay_w/2)");
            var centreY = HasKeyframes(clip, ClipKeyframeProperty.PositionY)
                ? $"(main_h*(({BuildKeyframeValueExpression(clip, ClipKeyframeProperty.PositionY, localOverlayTime, clip.PositionYPercent)})/100))-(overlay_h/2)"
                : FormattableString.Invariant($"(main_h*{clip.PositionYPercent / 100.0})-(overlay_h/2)");

            var outLabel = i == overlayClips.Count - 1 ? "[vlayered]" : $"[vlay{i}]";
            if (clip.BlendMode == ClipBlendMode.Normal)
            {
                filterLines.Add(FormattableString.Invariant(
                    $"{currentLabel}{preparedLabel}overlay=x='{centreX}':y='{centreY}':enable='between(t,{start},{end})':format=auto{outLabel}"));
            }
            else
            {
                // Build a transparent full-frame layer, place the masked overlay on it, then blend only
                // inside that layer's alpha. maskedmerge prevents multiply/screen/etc. from changing the
                // base outside the overlay's actual visible pixels.
                var canvasSource = $"[blendcanvassrc{i}]";
                var canvas = $"[blendcanvas{i}]";
                var overlayCanvas = $"[blendovlcanvas{i}]";
                var baseBlend = $"[blendbase{i}]";

                // A blend mode needs a mathematically neutral value outside the visible overlay:
                //   screen/add/difference -> black (0), multiply -> white (255), overlay -> middle grey (128).
                // We derive that canvas from the finite base stream, so it has the exact same duration and
                // cadence and cannot keep framesync alive forever like an unbounded `color` source can.
                var neutral = clip.BlendMode switch
                {
                    ClipBlendMode.Multiply => 255,
                    ClipBlendMode.Overlay => 128,
                    _ => 0
                };

                filterLines.Add($"{currentLabel}format=rgba,split=2{baseBlend}{canvasSource}");
                filterLines.Add($"{canvasSource}lutrgb=r={neutral}:g={neutral}:b={neutral}{canvas}");

                // preparedLabel already contains chroma/mask/opacity in its alpha channel. Normal overlaying
                // it over a neutral canvas naturally applies feather/invert/opacity. Outside the mask the
                // neutral colour remains, which is a no-op for the selected mathematical blend mode.
                filterLines.Add(FormattableString.Invariant(
                    $"{canvas}{preparedLabel}overlay=x='{centreX}':y='{centreY}':enable='between(t,{start},{end})':eof_action=pass:format=auto{overlayCanvas}"));

                // blend outputs planar GBR(A). Force it back to packed RGBA before later filters/encoding.
                // Also restore an opaque alpha plane: `difference` applied through all_mode would otherwise
                // calculate abs(255-255)=0 for alpha and make the entire composited frame transparent.
                filterLines.Add($"{baseBlend}{overlayCanvas}blend=all_mode={BlendModeName(clip.BlendMode)}:shortest=1,format=rgba,lutrgb=a=255{outLabel}");
            }

            currentLabel = outLabel;
        }

        return currentLabel;
    }

    public static string BuildStabilizationFilter(
        TimelineClip clip,
        IReadOnlyDictionary<string, string>? stabilizationTransforms)
    {
        if (!clip.StabilizationEnabled)
        {
            return string.Empty;
        }
        if (clip.IsReversed || clip.IsFreezeFrame)
        {
            throw new InvalidOperationException("Stabilizacija nije podržana zajedno sa Reverse/Freeze Frame.");
        }
        if (stabilizationTransforms is null ||
            !stabilizationTransforms.TryGetValue(clip.Id, out var transformPath) ||
            string.IsNullOrWhiteSpace(transformPath))
        {
            throw new InvalidOperationException(
                $"Nedostaje vidstab motion analiza za stabilizovani klip {clip.Id}; render ne sme tiho da preskoči stabilizaciju.");
        }

        var escapedPath = EscapeFilterPath(transformPath);
        var smoothing = Math.Clamp(clip.StabilizationSmoothing, 0, 120);
        var zoom = Math.Clamp(clip.StabilizationZoomPercent, 0, 30);
        var optimalZoom = Math.Clamp(clip.StabilizationOptimalZoom, 0, 2);
        return FormattableString.Invariant(
            $",vidstabtransform=input='{escapedPath}':smoothing={smoothing}:zoom={zoom}:optzoom={optimalZoom}:interpol=bicubic");
    }

    public static string BuildAutoReframeFilter(TimelineClip clip, int targetWidth, int targetHeight)
    {
        if (!clip.AutoReframeEnabled) return string.Empty;
        if (targetWidth <= 0 || targetHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetWidth), "Auto Reframe zahteva validnu ciljnu rezoluciju.");
        if (clip.IsReversed || clip.IsFreezeFrame)
            throw new InvalidOperationException("Auto Reframe nije podržan zajedno sa Reverse/Freeze Frame.");
        if (clip.MotionTrackingPoints.Count < 2)
            throw new InvalidOperationException("Auto Reframe je uključen, ali klip nema kompletnu Motion Tracking putanju.");
        var firstTrackingTime = clip.MotionTrackingPoints.Min(point => point.SourceTimeSeconds);
        var lastTrackingTime = clip.MotionTrackingPoints.Max(point => point.SourceTimeSeconds);
        if (firstTrackingTime > clip.SourceTrimInSeconds + 0.05 ||
            lastTrackingTime < clip.SourceTrimOutSeconds - 0.05)
        {
            throw new InvalidOperationException("Auto Reframe putanja ne pokriva ceo trenutno isečeni klip. Pokrenite Motion Tracking ponovo.");
        }

        var x = BuildTrackingValueExpression(clip, point => point.CenterX);
        var y = BuildTrackingValueExpression(clip, point => point.CenterY);
        var aspect = F((double)targetWidth / targetHeight);
        var cropWidth = $"if(gt(iw/ih,{aspect}),ih*{aspect},iw)";
        var cropHeight = $"if(gt(iw/ih,{aspect}),ih,iw/{aspect})";
        return $",crop=w='{cropWidth}':h='{cropHeight}':" +
               $"x='max(0,min(iw-ow,iw*({x})-ow/2))':" +
               $"y='max(0,min(ih-oh,ih*({y})-oh/2))'";
    }

    /// <summary>Piecewise-linear normalized tracking coordinate evaluated in the clip's source-local
    /// pre-speed clock. Tracking points are stored in absolute source time, so trim/split do not deform
    /// the authored path.</summary>
    public static string BuildTrackingValueExpression(TimelineClip clip, Func<MotionTrackingPoint, double> selector)
    {
        if (clip.MotionTrackingPoints.Count == 0) return "0.5";
        var start = clip.SourceTrimInSeconds;
        var end = clip.SourceTrimOutSeconds;
        var sourcePoints = clip.MotionTrackingPoints.OrderBy(point => point.SourceTimeSeconds).ToArray();

        double ValueAt(double sourceTime)
        {
            if (sourceTime <= sourcePoints[0].SourceTimeSeconds) return Math.Clamp(selector(sourcePoints[0]), 0, 1);
            if (sourceTime >= sourcePoints[^1].SourceTimeSeconds) return Math.Clamp(selector(sourcePoints[^1]), 0, 1);
            for (var i = 1; i < sourcePoints.Length; i++)
            {
                var right = sourcePoints[i];
                if (sourceTime > right.SourceTimeSeconds) continue;
                var left = sourcePoints[i - 1];
                var span = Math.Max(1e-9, right.SourceTimeSeconds - left.SourceTimeSeconds);
                var u = Math.Clamp((sourceTime - left.SourceTimeSeconds) / span, 0, 1);
                return Math.Clamp(selector(left) + (selector(right) - selector(left)) * u, 0, 1);
            }
            return Math.Clamp(selector(sourcePoints[^1]), 0, 1);
        }

        var anchors = new List<(double LocalTime, double Value)> { (0, ValueAt(start)) };
        anchors.AddRange(sourcePoints
            .Where(point => point.SourceTimeSeconds > start + 1e-6 && point.SourceTimeSeconds < end - 1e-6)
            .Select(point => (point.SourceTimeSeconds - start, Math.Clamp(selector(point), 0, 1))));
        anchors.Add((Math.Max(0, end - start), ValueAt(end)));
        anchors = anchors.OrderBy(anchorPoint => anchorPoint.LocalTime).ToList();

        if (anchors.Count == 1) return F(anchors[0].Value);
        var result = F(anchors[^1].Value);
        for (var i = anchors.Count - 1; i >= 1; i--)
        {
            var left = anchors[i - 1];
            var right = anchors[i];
            var span = Math.Max(1e-9, right.LocalTime - left.LocalTime);
            var segment = $"({F(left.Value)}+({F(right.Value)}-{F(left.Value)})*max(0,min(1,(t-{F(left.LocalTime)})/{F(span)})))";
            result = $"if(lt(t,{F(right.LocalTime)}),{segment},{result})";
        }
        return result;
    }

    public static string EscapeFilterPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return path.Replace("\\", "/", StringComparison.Ordinal)
            .Replace(":", "\\:", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
    }

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

    /// <summary>
    /// The real ffmpeg filters behind a clip's picture effects, as a string ready to append to that clip's
    /// own filter chain (empty when the clip is untouched, so an unedited project's graph is unchanged).
    ///
    /// Order matters and is deliberate: the named look goes on first, then any manual brightness/contrast/
    /// saturation, so a user who picks "Crno-belo" and then nudges brightness gets a brighter black-and-
    /// white picture - not a grayscale filter silently undoing their colour adjustment.
    ///
    /// Filters chosen from ffmpeg's own documented set (eq/hue/gblur/vignette/unsharp/negate/hflip); the
    /// sepia matrix is the standard colorchannelmixer one.
    /// </summary>
    private static bool HasVisualKeyframes(TimelineClip clip) =>
        HasKeyframes(clip, ClipKeyframeProperty.PositionX) ||
        HasKeyframes(clip, ClipKeyframeProperty.PositionY) ||
        HasKeyframes(clip, ClipKeyframeProperty.Scale) ||
        HasKeyframes(clip, ClipKeyframeProperty.Rotation) ||
        HasKeyframes(clip, ClipKeyframeProperty.Opacity);

    private static bool HasKeyframes(TimelineClip clip, ClipKeyframeProperty property) =>
        clip.Keyframes.Any(k => k.Property == property);

    /// <summary>Piecewise FFmpeg expression for one animated property. The easing attached to the RIGHT
    /// keyframe controls travel into that point, matching ClipKeyframeEvaluator exactly.</summary>
    public static string BuildKeyframeValueExpression(
        TimelineClip clip,
        ClipKeyframeProperty property,
        string timeExpression,
        double fallback)
    {
        var points = clip.Keyframes.Where(k => k.Property == property).OrderBy(k => k.TimeSeconds).ToArray();
        if (points.Length == 0)
        {
            return F(ClipKeyframeEvaluator.ClampValue(property, fallback));
        }
        if (points.Length == 1)
        {
            return F(ClipKeyframeEvaluator.ClampValue(property, points[0].Value));
        }

        string result = F(ClipKeyframeEvaluator.ClampValue(property, points[^1].Value));
        for (var i = points.Length - 1; i >= 1; i--)
        {
            var left = points[i - 1];
            var right = points[i];
            var leftValue = ClipKeyframeEvaluator.ClampValue(property, left.Value);
            var rightValue = ClipKeyframeEvaluator.ClampValue(property, right.Value);
            var span = Math.Max(1e-9, right.TimeSeconds - left.TimeSeconds);
            var u = $"(({timeExpression}-{F(left.TimeSeconds)})/{F(span)})";
            var eased = BuildEasingExpression(u, right.Easing);
            var segment = $"({F(leftValue)}+({F(rightValue)}-{F(leftValue)})*({eased}))";
            result = $"if(lt({timeExpression},{F(right.TimeSeconds)}),{segment},{result})";
        }
        return $"if(lte({timeExpression},{F(points[0].TimeSeconds)}),{F(ClipKeyframeEvaluator.ClampValue(property, points[0].Value))},{result})";
    }

    private static string BuildEasingExpression(string u, ClipKeyframeEasing easing) => easing switch
    {
        ClipKeyframeEasing.EaseIn => $"(({u})*({u}))",
        ClipKeyframeEasing.EaseOut => $"(1-(1-({u}))*(1-({u})))",
        ClipKeyframeEasing.EaseInOut => $"if(lt(({u}),0.5),2*({u})*({u}),1-2*(1-({u}))*(1-({u})))",
        ClipKeyframeEasing.Hold => "0",
        _ => u
    };

    private static string BuildAnimatedRotationFilter(TimelineClip clip, string localTimeExpression)
    {
        if (!HasKeyframes(clip, ClipKeyframeProperty.Rotation))
        {
            return string.Empty;
        }
        var angle = BuildKeyframeValueExpression(clip, ClipKeyframeProperty.Rotation, localTimeExpression, clip.RotationDegrees);
        return $",format=rgba,rotate=angle='({angle})*PI/180':ow='hypot(iw,ih)':oh='hypot(iw,ih)':c=black@0";
    }

    private static string BuildAnimatedOpacityFilter(TimelineClip clip, string localTimeExpression)
    {
        var opacity = BuildKeyframeValueExpression(clip, ClipKeyframeProperty.Opacity, localTimeExpression, clip.Opacity);
        return $",geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='alpha(X,Y)*({opacity})'";
    }

    private static string F(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);

    public static string BuildTemporalVideoFilters(TimelineClip clip, double durationSeconds)
    {
        var parts = new List<string>();
        if (clip.IsReversed)
        {
            parts.Add("reverse");
        }
        if (clip.IsFreezeFrame)
        {
            var hold = Math.Max(0.01, durationSeconds - 0.04);
            parts.Add("trim=start=0:end=0.04");
            parts.Add("setpts=PTS-STARTPTS");
            parts.Add(FormattableString.Invariant($"tpad=stop_mode=clone:stop_duration={hold}"));
        }
        return parts.Count == 0 ? string.Empty : "," + string.Join(",", parts);
    }

    public static string BuildTransformFilters(TimelineClip clip)
    {
        var parts = new List<string>();
        var left = Math.Clamp(clip.CropLeftPercent, 0, 45) / 100.0;
        var top = Math.Clamp(clip.CropTopPercent, 0, 45) / 100.0;
        var right = Math.Clamp(clip.CropRightPercent, 0, 45) / 100.0;
        var bottom = Math.Clamp(clip.CropBottomPercent, 0, 45) / 100.0;
        if (left + top + right + bottom > 1e-8)
        {
            var width = Math.Max(0.1, 1 - left - right);
            var height = Math.Max(0.1, 1 - top - bottom);
            parts.Add(FormattableString.Invariant($"crop=iw*{width}:ih*{height}:iw*{left}:ih*{top}"));
        }
        if (clip.FlipHorizontal) parts.Add("hflip");
        if (clip.FlipVertical) parts.Add("vflip");

        var rotation = clip.RotationDegrees % 360.0;
        if (!HasKeyframes(clip, ClipKeyframeProperty.Rotation) && Math.Abs(rotation) > 1e-6)
        {
            parts.Add(FormattableString.Invariant(
                $"rotate={rotation}*PI/180:ow=rotw({rotation}*PI/180):oh=roth({rotation}*PI/180):c=black"));
        }
        return parts.Count == 0 ? string.Empty : "," + string.Join(",", parts);
    }

    public static string BuildMaskFilter(TimelineClip clip)
    {
        if (clip.MaskType == ClipMaskType.None)
        {
            return string.Empty;
        }

        var cx = Math.Clamp(clip.MaskCenterXPercent, 0, 100) / 100.0;
        var cy = Math.Clamp(clip.MaskCenterYPercent, 0, 100) / 100.0;
        var width = Math.Clamp(clip.MaskWidthPercent, 1, 100) / 100.0;
        var height = Math.Clamp(clip.MaskHeightPercent, 1, 100) / 100.0;
        var feather = Math.Clamp(clip.MaskFeatherPercent, 0, 50) / 100.0;
        var radians = Math.Clamp(clip.MaskRotationDegrees, -180, 180) * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        var dx = FormattableString.Invariant($"(X-W*{cx})");
        var dy = FormattableString.Invariant($"(Y-H*{cy})");
        var rx = FormattableString.Invariant($"(({dx})*{cos}+({dy})*{sin})");
        var ry = FormattableString.Invariant($"(-({dx})*{sin}+({dy})*{cos})");

        string mask;
        switch (clip.MaskType)
        {
            case ClipMaskType.Rectangle:
            {
                var halfW = FormattableString.Invariant($"W*{width / 2.0}");
                var halfH = FormattableString.Invariant($"H*{height / 2.0}");
                mask = feather <= 1e-8
                    ? $"between({rx},-{halfW},{halfW})*between({ry},-{halfH},{halfH})"
                    : FormattableString.Invariant($"clip(min({halfW}-abs({rx}),{halfH}-abs({ry}))/(min(W,H)*{feather}),0,1)");
                break;
            }
            case ClipMaskType.Circle:
            {
                var radius = Math.Min(width, height) / 2.0;
                var distance = $"sqrt(({dx})^2+({dy})^2)";
                var radiusExpr = FormattableString.Invariant($"min(W,H)*{radius}");
                mask = feather <= 1e-8
                    ? $"lte({distance},{radiusExpr})"
                    : FormattableString.Invariant($"clip(({radiusExpr}-{distance})/(min(W,H)*{feather}),0,1)");
                break;
            }
            case ClipMaskType.Linear:
            {
                var projection = FormattableString.Invariant($"(({dx})*{cos}+({dy})*{sin})");
                mask = feather <= 1e-8
                    ? $"gte({projection},0)"
                    : FormattableString.Invariant($"clip(0.5+({projection})/(min(W,H)*{feather}*2),0,1)");
                break;
            }
            default:
                return string.Empty;
        }

        if (clip.MaskInvert)
        {
            mask = $"1-({mask})";
        }

        return $",geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='alpha(X,Y)*({mask})'";
    }

    private static string BlendModeName(ClipBlendMode mode) => mode switch
    {
        ClipBlendMode.Multiply => "multiply",
        ClipBlendMode.Screen => "screen",
        ClipBlendMode.Overlay => "overlay",
        ClipBlendMode.Add => "addition",
        ClipBlendMode.Difference => "difference",
        _ => "normal"
    };
    public static string BuildChromaKeyFilter(TimelineClip clip)
    {
        if (!clip.ChromaKeyEnabled)
        {
            return string.Empty;
        }
        var color = string.IsNullOrWhiteSpace(clip.ChromaKeyColor) ? "00FF00" : clip.ChromaKeyColor.Trim().TrimStart('#');
        if (color.Length != 6 || color.Any(c => !Uri.IsHexDigit(c)))
        {
            color = "00FF00";
        }
        var similarity = Math.Clamp(clip.ChromaKeySimilarity, 0.01, 1.0);
        var blend = Math.Clamp(clip.ChromaKeyBlend, 0, 1.0);
        return FormattableString.Invariant($",chromakey=0x{color}:{similarity}:{blend}");
    }
    public static string BuildEffectFilters(TimelineClip clip)
    {
        var parts = new List<string>();

        var named = clip.Effect switch
        {
            ClipVideoEffect.Grayscale => "hue=s=0",
            ClipVideoEffect.Sepia => "colorchannelmixer=.393:.769:.189:0:.349:.686:.168:0:.272:.534:.131",
            ClipVideoEffect.Blur => "gblur=sigma=8",
            ClipVideoEffect.Vignette => "vignette",
            ClipVideoEffect.Sharpen => "unsharp=5:5:1.0:5:5:0.0",
            ClipVideoEffect.Invert => "negate",
            ClipVideoEffect.SmoothSlowMotion => "minterpolate=fps=60:mi_mode=mci:mc_mode=aobmc:me_mode=bidir:me=epzs:vsbmc=1",
            ClipVideoEffect.Mirror => "hflip",
            _ => null
        };

        if (named is not null)
        {
            parts.Add(named);
        }

        // Only emit `eq` when something actually differs from neutral - an always-on eq would add a real
        // decode/encode cost to every clip in every project for no visible change.
        var brightness = Math.Clamp(clip.Brightness, -1, 1);
        var contrast = Math.Clamp(clip.Contrast, 0, 3);
        var saturation = Math.Clamp(clip.Saturation, 0, 3);

        if (Math.Abs(brightness) > 1e-6 || Math.Abs(contrast - 1) > 1e-6 || Math.Abs(saturation - 1) > 1e-6)
        {
            parts.Add(FormattableString.Invariant(
                $"eq=brightness={brightness}:contrast={contrast}:saturation={saturation}"));
        }

        // Advanced grading is intentionally composed from standard FFmpeg filters available in the
        // bundled Windows build. Exposure is a real per-channel luminance multiplier; highlights/shadows
        // use a monotonic five-point tone curve; temperature/tint use colorbalance while preserving lightness.
        var exposure = Math.Clamp(clip.ExposureStops, -3, 3);
        if (Math.Abs(exposure) > 1e-6)
        {
            var factor = Math.Pow(2, exposure);
            parts.Add(FormattableString.Invariant(
                $"lutrgb=r='val*{factor}':g='val*{factor}':b='val*{factor}'"));
        }

        var shadows = Math.Clamp(clip.Shadows, -1, 1);
        var highlights = Math.Clamp(clip.Highlights, -1, 1);
        if (Math.Abs(shadows) > 1e-6 || Math.Abs(highlights) > 1e-6)
        {
            var shadowY = Math.Clamp(0.25 + shadows * 0.18, 0.02, 0.48);
            var highlightY = Math.Clamp(0.75 + highlights * 0.18, 0.52, 0.98);
            parts.Add(FormattableString.Invariant(
                $"curves=all='0/0 0.25/{shadowY} 0.5/0.5 0.75/{highlightY} 1/1'"));
        }

        var temperature = Math.Clamp(clip.Temperature, -1, 1);
        var tint = Math.Clamp(clip.Tint, -1, 1);
        if (Math.Abs(temperature) > 1e-6 || Math.Abs(tint) > 1e-6)
        {
            var redShadows = temperature * 0.25;
            var blueShadows = -temperature * 0.25;
            var redMid = tint * 0.10;
            var greenMid = -tint * 0.20;
            var blueMid = tint * 0.10;
            parts.Add(FormattableString.Invariant(
                $"colorbalance=rs={redShadows}:bs={blueShadows}:rm={redMid}:gm={greenMid}:bm={blueMid}:pl=1"));
        }

        return parts.Count == 0 ? string.Empty : "," + string.Join(",", parts);
    }

    /// <summary>
    /// The <c>setpts</c> filter that changes a clip's playback speed, or empty at normal speed.
    /// Deliberately separate from <see cref="BuildEffectFilters"/> because speed must be applied
    /// immediately after <c>setpts=PTS-STARTPTS</c> (which resets the timestamps it operates on) and
    /// before anything that depends on the clip's duration.
    /// </summary>
    public static string BuildSpeedFilter(TimelineClip clip)
    {
        if (clip.IsFreezeFrame)
        {
            return string.Empty;
        }

        if (SpeedCurveMath.HasCurve(clip))
        {
            if (clip.IsReversed)
            {
                throw new InvalidOperationException("Velocity / Speed Curve ne može istovremeno sa Reverse. Isključite Reverse ili uklonite krivu brzine.");
            }

            var segments = SpeedCurveMath.BuildRenderSegments(clip);
            if (segments.Count == 0)
            {
                return string.Empty;
            }

            // trim+setpts before this filter makes PTS*TB clip-local source time. Each exact-duration
            // renderer cell maps that source interval to its cumulative output clock.
            string SegmentValue(SpeedCurveRenderSegment segment) =>
                $"({F(segment.OutputStartSeconds)}+((PTS*TB)-{F(segment.SourceStartSeconds - clip.SourceTrimInSeconds)})/{F(segment.SpeedMultiplier)})/TB";

            var expression = SegmentValue(segments[^1]);
            for (var i = segments.Count - 2; i >= 0; i--)
            {
                var segment = segments[i];
                var endLocal = segment.SourceEndSeconds - clip.SourceTrimInSeconds;
                expression = $"if(lt(PTS*TB,{F(endLocal)}),{SegmentValue(segment)},{expression})";
            }
            return $",setpts='{expression}'";
        }

        var speed = SpeedCurveMath.ClampSpeed(clip.SpeedMultiplier);
        if (Math.Abs(speed - 1) < 1e-6)
        {
            return string.Empty;
        }

        return FormattableString.Invariant($",setpts=PTS/{speed}");
    }

    /// <summary>Audio timing matching <see cref="BuildSpeedFilter"/>. Constant speed keeps the proven
    /// atempo chain. Velocity curves drive one named librubberband instance through asendcmd at the same
    /// source-time cell boundaries used by video, preserving pitch while tempo changes.</summary>
    public static string BuildAudioSpeedFilter(TimelineClip clip)
    {
        if (clip.IsFreezeFrame)
        {
            return string.Empty;
        }

        if (SpeedCurveMath.HasCurve(clip))
        {
            if (clip.IsReversed)
            {
                throw new InvalidOperationException("Velocity / Speed Curve ne može istovremeno sa Reverse. Isključite Reverse ili uklonite krivu brzine.");
            }

            var segments = SpeedCurveMath.BuildRenderSegments(clip);
            if (segments.Count == 0)
            {
                return string.Empty;
            }

            var initial = F(segments[0].SpeedMultiplier);
            if (segments.Count == 1)
            {
                return $",rubberband@npvsspeed=tempo={initial}";
            }

            var commands = segments.Skip(1).Select(segment =>
                $"{F(segment.SourceStartSeconds - clip.SourceTrimInSeconds)} rubberband@npvsspeed tempo {F(segment.SpeedMultiplier)}");
            return $",asendcmd=c='{string.Join(";", commands)}',rubberband@npvsspeed=tempo={initial}";
        }

        var remaining = SpeedCurveMath.ClampSpeed(clip.SpeedMultiplier);
        if (Math.Abs(remaining - 1) < 1e-6)
        {
            return string.Empty;
        }

        var stages = new List<double>();
        while (remaining < 0.5 - 1e-9)
        {
            stages.Add(0.5);
            remaining /= 0.5;
        }
        while (remaining > 2.0 + 1e-9)
        {
            stages.Add(2.0);
            remaining /= 2.0;
        }
        if (Math.Abs(remaining - 1) > 1e-6)
        {
            stages.Add(remaining);
        }

        return stages.Count == 0
            ? string.Empty
            : "," + string.Join(",", stages.Select(s => FormattableString.Invariant($"atempo={s}")));
    }

    /// <summary>Real per-clip audio cleanup. The chain intentionally uses only filters present in the
    /// bundled Windows FFmpeg build and returns an empty string for neutral settings.</summary>
    public static string BuildAudioEnhancementFilters(TimelineClip clip)
    {
        var parts = new List<string>();
        if (clip.AudioNoiseReductionEnabled)
        {
            var strength = Math.Clamp(clip.AudioNoiseReductionStrength, 0, 1);
            var reductionDb = 6 + strength * 24; // afftdn nr: 6..30 dB, conservative and stable.
            parts.Add($"afftdn=nr={F(reductionDb)}:nf=-50:tn=1");
        }

        if (clip.AudioEnhanceVoiceEnabled)
        {
            parts.Add("highpass=f=80");
            parts.Add("lowpass=f=12000");
            parts.Add("equalizer=f=2500:t=q:w=1:g=3");
            parts.Add("acompressor=threshold=0.125:ratio=3:attack=20:release=250:makeup=1.5");
        }

        if (clip.AudioLoudnessNormalizationEnabled)
        {
            parts.Add("loudnorm=I=-16:TP=-1.5:LRA=11");
        }

        return parts.Count == 0 ? string.Empty : "," + string.Join(",", parts);
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
