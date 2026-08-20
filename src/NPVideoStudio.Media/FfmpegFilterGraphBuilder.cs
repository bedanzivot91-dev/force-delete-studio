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
            videoFilter.Append(BuildTemporalVideoFilters(clip, duration));
            videoFilter.Append(BuildSpeedFilter(clip));
            videoFilter.Append(BuildTransformFilters(clip));
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

            var audioFilter = new StringBuilder();
            var volume = clip.IsMuted ? 0 : clip.Volume;
            audioFilter.Append(FormattableString.Invariant(
                $"[{inputIndex}:a]atrim=start={clip.SourceTrimInSeconds}:end={clip.SourceTrimOutSeconds},asetpts=PTS-STARTPTS,volume={(clip.IsFreezeFrame ? 0 : volume)}"));
            if (clip.IsReversed && !clip.IsFreezeFrame)
            {
                audioFilter.Append(",areverse");
            }            audioFilter.Append(BuildAudioSpeedFilter(clip));
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
            currentVideoLabel!, targetWidth, targetHeight, MapToRenderedTime);

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
                var sourceRate = clip.IsFreezeFrame ? 1.0 : Math.Clamp(clip.SpeedMultiplier, 0.25, 4);
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
        ScalePercent = clip.ScalePercent,
        PositionXPercent = clip.PositionXPercent,
        PositionYPercent = clip.PositionYPercent,
        Opacity = clip.Opacity,
        Effect = clip.Effect,
        Brightness = clip.Brightness,
        Contrast = clip.Contrast,
        Saturation = clip.Saturation,
        SpeedMultiplier = clip.SpeedMultiplier,
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
        ChromaKeyBlend = clip.ChromaKeyBlend
    };

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
        Func<double, double> mapToRenderedTime)
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

            var preparedLabel = $"[ovl{i}]";
            var prepared = new StringBuilder();
            prepared.Append(FormattableString.Invariant(
                $"[{inputIndex}:v]trim=start={clip.SourceTrimInSeconds}:end={clip.SourceTrimOutSeconds},setpts=PTS-STARTPTS"));
            // -1 keeps the source aspect ratio; the overlay is sized by width only.
            prepared.Append(BuildTemporalVideoFilters(clip, clip.TimelineDurationSeconds));
            prepared.Append(BuildSpeedFilter(clip));
            prepared.Append(BuildTransformFilters(clip));
            prepared.Append(FormattableString.Invariant($",scale={overlayWidth}:-1"));
            prepared.Append(BuildEffectFilters(clip));
            // colorchannelmixer/chromakey need an alpha-capable pixel format.
            prepared.Append(",format=rgba");
            prepared.Append(BuildChromaKeyFilter(clip));
            prepared.Append(FormattableString.Invariant($",colorchannelmixer=aa={opacity}"));
            prepared.Append(preparedLabel);
            filterLines.Add(prepared.ToString());

            // Centre-anchored: shift left/up by half the overlay's own rendered size. main_w/overlay_w are
            // ffmpeg's own variables for the base and overlay sizes, so this stays correct even though the
            // overlay's height is only known to ffmpeg (scale=-1 above).
            var centreX = FormattableString.Invariant($"(main_w*{clip.PositionXPercent / 100.0})-(overlay_w/2)");
            var centreY = FormattableString.Invariant($"(main_h*{clip.PositionYPercent / 100.0})-(overlay_h/2)");

            var start = mapToRenderedTime(clip.TimelineStartSeconds);
            var end = mapToRenderedTime(clip.TimelineEndSeconds);

            var outLabel = i == overlayClips.Count - 1 ? "[vlayered]" : $"[vlay{i}]";
            filterLines.Add(FormattableString.Invariant(
                $"{currentLabel}{preparedLabel}overlay=x='{centreX}':y='{centreY}':enable='between(t,{start},{end})'{outLabel}"));

            currentLabel = outLabel;
        }

        return currentLabel;
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
        if (Math.Abs(rotation) > 1e-6)
        {
            parts.Add(FormattableString.Invariant(
                $"rotate={rotation}*PI/180:ow=rotw({rotation}*PI/180):oh=roth({rotation}*PI/180):c=black"));
        }
        return parts.Count == 0 ? string.Empty : "," + string.Join(",", parts);
    }

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
        var speed = clip.IsFreezeFrame ? 1.0 : Math.Clamp(clip.SpeedMultiplier, 0.25, 4);
        if (Math.Abs(speed - 1) < 1e-6)
        {
            return string.Empty;
        }

        // Higher speed = shorter presentation timestamps, hence dividing by the multiplier.
        return FormattableString.Invariant($",setpts=PTS/{speed}");
    }

    /// <summary>FFmpeg audio-tempo chain matching <see cref="BuildSpeedFilter"/>. Chained 0.5..2.0
    /// stages work across the full UI range 0.25x..4x without pitch-shifting the audio.</summary>
    public static string BuildAudioSpeedFilter(TimelineClip clip)
    {
        if (clip.IsFreezeFrame)
        {
            return string.Empty;
        }

        var remaining = Math.Clamp(clip.SpeedMultiplier, 0.25, 4);
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
