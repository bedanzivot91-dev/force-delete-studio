using NPVideoStudio.Domain;

namespace NPVideoStudio.AI;

/// <summary>
/// Pure, testable editing operations over a timeline's tracks/clips, with full undo/redo (spec Phase 8:
/// split/trim-in/trim-out/move/delete/duplicate/mute/volume/fade/lock/hide/solo/undo/redo). Same whole-
/// state-snapshot undo/redo approach as <see cref="CaptionEditSession"/>, for the same reason: simple to
/// reason about correctly, no hand-written inverse needed per operation. Holds no persistence/UI state -
/// callers own building the initial track list from (and writing the result back to) <see cref="Domain.Timeline"/>.
///
/// Deliberately does not enforce "clips on a track never overlap" - real editors commonly allow
/// overlapping layers (especially on caption/text/image-overlay tracks), so this doesn't guess at a
/// constraint the spec never actually states.
/// </summary>
public sealed class TimelineEditSession
{
    private const double MinClipDurationSeconds = 0.05;

    private readonly List<List<TimelineTrack>> _undoStack = new();
    private readonly List<List<TimelineTrack>> _redoStack = new();
    private List<TimelineTrack> _tracks;

    public TimelineEditSession(IEnumerable<TimelineTrack> tracks)
    {
        _tracks = CloneTracks(tracks);
    }

    public IReadOnlyList<TimelineTrack> Tracks => _tracks;
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        _redoStack.Add(CloneTracks(_tracks));
        _tracks = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
    }

    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        _undoStack.Add(CloneTracks(_tracks));
        _tracks = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
    }

    public void AddTrack(TimelineTrack track)
    {
        SaveSnapshot();
        _tracks.Add(track);
    }

    public void RemoveTrack(string trackId)
    {
        if (FindTrack(trackId) is null)
        {
            return;
        }

        SaveSnapshot();
        _tracks.RemoveAll(t => t.Id == trackId);
    }

    public void AddClip(string trackId, TimelineClip clip)
    {
        var track = FindTrack(trackId);
        if (track is null)
        {
            return;
        }

        SaveSnapshot();
        FindTrack(trackId)!.Clips.Add(clip);
    }

    public void DeleteClips(IEnumerable<string> clipIds)
    {
        var idSet = clipIds.ToHashSet();
        if (!_tracks.Any(t => t.Clips.Any(c => idSet.Contains(c.Id))))
        {
            return;
        }

        SaveSnapshot();
        foreach (var track in _tracks)
        {
            track.Clips.RemoveAll(c => idSet.Contains(c.Id));
        }
    }

    public void DuplicateClip(string clipId)
    {
        var (track, clip) = FindClipWithTrack(clipId);
        if (track is null || clip is null)
        {
            return;
        }

        SaveSnapshot();
        var (liveTrack, liveClip) = FindClipWithTrack(clipId);
        var duplicate = Clone(liveClip!);
        duplicate.Id = Guid.NewGuid().ToString("N");
        duplicate.TimelineStartSeconds = liveClip!.TimelineEndSeconds;
        liveTrack!.Clips.Add(duplicate);
    }

    /// <summary>Moves a clip to a new timeline position, optionally onto a different track.</summary>
    public void MoveClip(string clipId, double newTimelineStartSeconds, string? newTrackId = null)
    {
        var (track, clip) = FindClipWithTrack(clipId);
        if (track is null || clip is null)
        {
            return;
        }

        var targetTrack = newTrackId is null ? track : FindTrack(newTrackId);
        if (targetTrack is null)
        {
            return;
        }

        SaveSnapshot();
        var (liveTrack, liveClip) = FindClipWithTrack(clipId);
        liveClip!.TimelineStartSeconds = Math.Max(0, newTimelineStartSeconds);

        if (newTrackId is not null && liveTrack!.Id != newTrackId)
        {
            liveTrack.Clips.Remove(liveClip);
            FindTrack(newTrackId)!.Clips.Add(liveClip);
        }
    }

    /// <summary>Splits one clip into two at an absolute timeline position - the spec's "split" operation.</summary>
    public void SplitClip(string clipId, double atTimelineSeconds)
    {
        var (track, clip) = FindClipWithTrack(clipId);
        if (track is null || clip is null)
        {
            return;
        }

        if (atTimelineSeconds <= clip.TimelineStartSeconds + MinClipDurationSeconds ||
            atTimelineSeconds >= clip.TimelineEndSeconds - MinClipDurationSeconds)
        {
            return; // Split point too close to either edge to leave two valid clips.
        }

        var offsetIntoClip = atTimelineSeconds - clip.TimelineStartSeconds;
        var splitSourcePoint = clip.SourceTrimInSeconds + offsetIntoClip;

        SaveSnapshot();
        var (liveTrack, liveClip) = FindClipWithTrack(clipId);
        var second = Clone(liveClip!);
        second.Id = Guid.NewGuid().ToString("N");
        second.SourceTrimInSeconds = splitSourcePoint;
        second.TimelineStartSeconds = atTimelineSeconds;
        second.FadeInSeconds = 0; // The new leading edge is a fresh cut, not the original fade-in point.

        liveClip!.SourceTrimOutSeconds = splitSourcePoint;
        liveClip.FadeOutSeconds = 0; // Same reasoning for the original clip's new trailing edge.

        liveTrack!.Clips.Add(second);
    }

    public void TrimIn(string clipId, double newSourceTrimInSeconds)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        var clamped = Math.Clamp(newSourceTrimInSeconds, 0, clip.SourceTrimOutSeconds - MinClipDurationSeconds);
        var delta = clamped - clip.SourceTrimInSeconds;
        if (Math.Abs(delta) < 1e-9)
        {
            return;
        }

        SaveSnapshot();
        var (_, liveClip) = FindClipWithTrack(clipId);
        liveClip!.SourceTrimInSeconds = clamped;
        liveClip.TimelineStartSeconds = Math.Max(0, liveClip.TimelineStartSeconds + delta);
    }

    public void TrimOut(string clipId, double newSourceTrimOutSeconds)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        var clamped = Math.Max(newSourceTrimOutSeconds, clip.SourceTrimInSeconds + MinClipDurationSeconds);
        if (Math.Abs(clamped - clip.SourceTrimOutSeconds) < 1e-9)
        {
            return;
        }

        SaveSnapshot();
        FindClipWithTrack(clipId).Clip!.SourceTrimOutSeconds = clamped;
    }

    public void SetFade(string clipId, double fadeInSeconds, double fadeOutSeconds)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        SaveSnapshot();
        var (_, liveClip) = FindClipWithTrack(clipId);
        liveClip!.FadeInSeconds = Math.Max(0, fadeInSeconds);
        liveClip.FadeOutSeconds = Math.Max(0, fadeOutSeconds);
    }

    /// <summary>Lets the user correct a Caption/Text clip's own words - most importantly, what
    /// auto-generated speech-to-text captions actually got right or wrong, since Whisper is never
    /// guaranteed accurate (especially on singing/music) and there was previously no way to fix a
    /// misheard word short of deleting the whole clip and retyping it from scratch on a Text track.</summary>
    public void SetTextContent(string clipId, string textContent)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null || clip.TextContent is null)
        {
            return;
        }

        SaveSnapshot();
        FindClipWithTrack(clipId).Clip!.TextContent = textContent;
    }

    public void SetTransition(string clipId, ClipTransitionType type, double durationSeconds)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        liveClip.TransitionInType = type;
        liveClip.TransitionInDurationSeconds = Math.Max(0.05, durationSeconds);
    }

    /// <summary>
    /// Sets where an overlay clip sits over the video underneath it - its size, its centre position and
    /// how see-through it is (the CapCut-style picture-in-picture / sticker / logo placement rendered by
    /// <c>FfmpegFilterGraphBuilder.AppendOverlayLayers</c>). Goes through the session, like every other
    /// edit here, so one undo takes the whole placement change back.
    ///
    /// Values are clamped to what the renderer can actually honour rather than trusted: a scale of 0 or a
    /// negative opacity would produce a filter graph ffmpeg rejects outright, failing the whole export for
    /// what is really just a slider dragged to its end.
    /// </summary>
    public void SetLayerPlacement(string clipId, double scalePercent, double positionXPercent, double positionYPercent, double opacity)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        liveClip.ScalePercent = Math.Clamp(scalePercent, 1, 1000);
        liveClip.PositionXPercent = Math.Clamp(positionXPercent, 0, 100);
        liveClip.PositionYPercent = Math.Clamp(positionYPercent, 0, 100);
        liveClip.Opacity = Math.Clamp(opacity, 0, 1);
    }

    public void SetClipMute(string clipId, bool muted)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null || clip.IsMuted == muted)
        {
            return;
        }

        SaveSnapshot();
        FindClipWithTrack(clipId).Clip!.IsMuted = muted;
    }

    public void SetClipVolume(string clipId, double volume)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        var clamped = Math.Clamp(volume, 0, 2.0);
        if (clip is null || Math.Abs(clip.Volume - clamped) < 1e-9)
        {
            return;
        }

        SaveSnapshot();
        FindClipWithTrack(clipId).Clip!.Volume = clamped;
    }

    public void SetTextStyle(string clipId, CaptionFontChoice fontChoice, int fontSizePx, string textColor, CaptionTextPosition position)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        liveClip.FontChoice = fontChoice;
        liveClip.FontSizePx = Math.Clamp(fontSizePx, 8, 200);
        liveClip.TextColor = textColor;
        liveClip.TextPosition = position;
    }

    /// <summary>The extra text style knobs beyond font/size/color/position - outline, shadow, background
    /// on/off/color/opacity, horizontal alignment, bold/italic, case transform, line spacing.</summary>
    public void SetTextAdvancedStyle(string clipId, TextAdvancedStyle style)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        liveClip.TextOutlineColor = style.OutlineColor;
        liveClip.TextOutlineWidthPx = Math.Max(0, style.OutlineWidthPx);
        liveClip.TextShadowColor = style.ShadowColor;
        liveClip.TextShadowOffsetPx = Math.Max(0, style.ShadowOffsetPx);
        liveClip.HasTextBackground = style.HasBackground;
        liveClip.TextBackgroundColor = style.BackgroundColor;
        liveClip.TextBackgroundOpacity = Math.Clamp(style.BackgroundOpacity, 0, 1);
        liveClip.TextHorizontalAlign = style.HorizontalAlign;
        liveClip.IsTextBold = style.IsBold;
        liveClip.IsTextItalic = style.IsItalic;
        liveClip.TextCase = style.TextCase;
        liveClip.LineSpacingPx = Math.Max(0, style.LineSpacingPx);
    }

    /// <summary>
    /// "Primeni na sve titlove na ovoj traci" - copies every text style field (font/size/color/position
    /// plus everything <see cref="SetTextAdvancedStyle"/> covers) from one clip onto every other Caption/
    /// Text clip on the same track, so styling a batch of auto-generated captions doesn't mean re-clicking
    /// the same font/size/color/outline/etc. on every single clip by hand. Never touches
    /// <see cref="TimelineClip.TextContent"/> itself - only the styling around it.
    /// </summary>
    public void ApplyTextStyleToAllClipsOnTrack(string trackId, string sourceClipId)
    {
        var track = FindTrack(trackId);
        var source = track?.Clips.FirstOrDefault(c => c.Id == sourceClipId);
        if (track is null || source is null || source.TextContent is null)
        {
            return;
        }

        var targets = track.Clips.Where(c => c.Id != sourceClipId && c.TextContent is not null).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        SaveSnapshot();
        foreach (var target in track.Clips.Where(c => c.Id != sourceClipId && c.TextContent is not null))
        {
            target.FontChoice = source.FontChoice;
            target.FontSizePx = source.FontSizePx;
            target.TextColor = source.TextColor;
            target.TextPosition = source.TextPosition;
            target.TextHorizontalAlign = source.TextHorizontalAlign;
            target.TextOutlineColor = source.TextOutlineColor;
            target.TextOutlineWidthPx = source.TextOutlineWidthPx;
            target.TextShadowColor = source.TextShadowColor;
            target.TextShadowOffsetPx = source.TextShadowOffsetPx;
            target.HasTextBackground = source.HasTextBackground;
            target.TextBackgroundColor = source.TextBackgroundColor;
            target.TextBackgroundOpacity = source.TextBackgroundOpacity;
            target.IsTextBold = source.IsTextBold;
            target.IsTextItalic = source.IsTextItalic;
            target.TextCase = source.TextCase;
            target.LineSpacingPx = source.LineSpacingPx;
        }
    }

    public void SetTrackLocked(string trackId, bool locked) => SetTrackFlag(trackId, t => t.IsLocked, (t, v) => t.IsLocked = v, locked);
    public void SetTrackHidden(string trackId, bool hidden) => SetTrackFlag(trackId, t => t.IsHidden, (t, v) => t.IsHidden = v, hidden);
    public void SetTrackMuted(string trackId, bool muted) => SetTrackFlag(trackId, t => t.IsMuted, (t, v) => t.IsMuted = v, muted);
    public void SetTrackSolo(string trackId, bool solo) => SetTrackFlag(trackId, t => t.IsSolo, (t, v) => t.IsSolo = v, solo);

    public void SetTrackVolume(string trackId, double volume)
    {
        var track = FindTrack(trackId);
        var clamped = Math.Clamp(volume, 0, 2.0);
        if (track is null || Math.Abs(track.Volume - clamped) < 1e-9)
        {
            return;
        }

        SaveSnapshot();
        FindTrack(trackId)!.Volume = clamped;
    }

    /// <summary>Returns the nearest value in <paramref name="candidates"/> within <paramref name="thresholdSeconds"/>, or the original position if nothing is close enough (spec's "snap").</summary>
    public static double SnapToNearest(double seconds, IEnumerable<double> candidates, double thresholdSeconds)
    {
        var best = seconds;
        var bestDelta = thresholdSeconds;
        foreach (var candidate in candidates)
        {
            var delta = Math.Abs(candidate - seconds);
            if (delta <= bestDelta)
            {
                bestDelta = delta;
                best = candidate;
            }
        }

        return best;
    }

    private void SetTrackFlag(string trackId, Func<TimelineTrack, bool> getter, Action<TimelineTrack, bool> setter, bool value)
    {
        var track = FindTrack(trackId);
        if (track is null || getter(track) == value)
        {
            return;
        }

        SaveSnapshot();
        setter(FindTrack(trackId)!, value);
    }

    private TimelineTrack? FindTrack(string trackId) => _tracks.FirstOrDefault(t => t.Id == trackId);

    private (TimelineTrack? Track, TimelineClip? Clip) FindClipWithTrack(string clipId)
    {
        foreach (var track in _tracks)
        {
            var clip = track.Clips.FirstOrDefault(c => c.Id == clipId);
            if (clip is not null)
            {
                return (track, clip);
            }
        }

        return (null, null);
    }

    private void SaveSnapshot()
    {
        _undoStack.Add(CloneTracks(_tracks));
        _redoStack.Clear();
    }

    private static List<TimelineTrack> CloneTracks(IEnumerable<TimelineTrack> tracks) => tracks.Select(Clone).ToList();

    private static TimelineTrack Clone(TimelineTrack track) => new()
    {
        Id = track.Id,
        Kind = track.Kind,
        Name = track.Name,
        Clips = track.Clips.Select(Clone).ToList(),
        IsLocked = track.IsLocked,
        IsHidden = track.IsHidden,
        IsMuted = track.IsMuted,
        IsSolo = track.IsSolo,
        Volume = track.Volume
    };

    /// <summary>
    /// Real bug found and fixed while adding <see cref="ApplyTextStyleToAllClipsOnTrack"/>'s test: this
    /// explicit field list had silently fallen behind <see cref="TimelineClip"/> itself over several past
    /// sessions - FontChoice/FontSizePx/TextColor/TextPosition/TransitionInType/TransitionInDurationSeconds
    /// were all missing, meaning every undo snapshot (every <see cref="SaveSnapshot"/> call, i.e. every
    /// single edit) silently reset a clip's text style and transition back to their type defaults. Existing
    /// tests never caught this because they all happened to undo from a non-default style back to the
    /// still-default starting state, where the bug is invisible - a real style-A -> style-B -> undo
    /// sequence would have incorrectly landed on hardcoded defaults instead of style A. Every field on
    /// <see cref="TimelineClip"/> is now listed explicitly here on purpose (no reflection/serialization
    /// shortcut) so a future field addition has to be a deliberate, visible one-line change instead of a
    /// silent gap like this one.
    /// </summary>
    private static TimelineClip Clone(TimelineClip clip) => new()
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
        Volume = clip.Volume
    };
}
