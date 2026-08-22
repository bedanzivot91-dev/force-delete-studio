using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.AI;
using NPVideoStudio.Domain;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// Non-destructive timeline editor (spec Phase 8), persisted inside <see cref="Project.Timeline"/> - not
/// just transient UI-control state. All editing logic lives in <see cref="TimelineEditSession"/> (pure/
/// testable); this ViewModel only wires it to the UI and resolves clip labels against the project's
/// media library. Button-based operations (split-at-playhead, nudge by a fixed step) rather than drag-
/// to-resize/move, same reasoning as the Phase 6 caption editor: real and fully testable without a
/// display, versus an unverifiable drag interaction.
/// </summary>
public sealed partial class TimelineViewModel : ViewModelBase
{
    private const double NudgeStepSeconds = 0.5;

    /// <summary>Zoom of the visual lane: how many pixels one second occupies. Mirrors
    /// <c>Timeline.ZoomPixelsPerSecond</c> so a saved project reopens at the zoom the user left it at.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomLabel))]
    private double _zoomPixelsPerSecond;

    public string ZoomLabel => $"{ZoomPixelsPerSecond:0} px/s";
    private const double DefaultTextClipDurationSeconds = 3.0;

    private readonly Project _project;
    private readonly Func<double> _getPlayhead;
    private readonly TimelineEditSession _session;

    public ObservableCollection<TimelineTrackItemViewModel> Tracks { get; } = new();

    /// <summary>
    /// The clip the user last clicked in the visual lane. Added because the timeline had no notion of a
    /// selection at all: every action had to be a per-clip button, which is why Delete and "split" had no
    /// keyboard shortcut and why a clip could not be dragged onto another track.
    ///
    /// Stored as the clip's ID rather than the ViewModel because RefreshFromSession rebuilds every clip
    /// ViewModel from scratch after each edit - holding the object would leave the selection pointing at a
    /// discarded instance the moment anything changed.
    /// </summary>
    [ObservableProperty]
    private string? _selectedClipId;

    public TimelineClipItemViewModel? SelectedClip =>
        Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Clip.Id == SelectedClipId);

    partial void OnSelectedClipIdChanged(string? value)
    {
        OnPropertyChanged(nameof(SelectedClip));
        foreach (var clip in Tracks.SelectMany(t => t.Clips))
        {
            clip.RefreshSelection(value);
        }
    }
    public ObservableCollection<MediaAssetViewModel> AvailableMedia { get; }

    [ObservableProperty]
    private MediaAssetViewModel? _selectedMediaAsset;

    /// <summary>Live (post-undo/redo) track state, for the workspace's preview-frame resolution - unlike
    /// <see cref="Project.Timeline"/>.Tracks, this reflects edits before <see cref="SaveToProject"/> runs.</summary>
    public IReadOnlyList<TimelineTrack> CurrentTracks => _session.Tracks;

    /// <summary>Applies one gallery preset to the currently selected real text/caption clip.</summary>
    public bool ApplyCaptionStylePresetToSelected(CaptionStylePreset preset)
    {
        var selectedId = SelectedClipId;
        if (selectedId is null || SelectedClip is not { IsTextClip: true })
        {
            return false;
        }

        if (!_session.ApplyCaptionStylePreset(selectedId, preset))
        {
            return false;
        }

        RefreshFromSession();
        SelectedClipId = selectedId;
        return true;
    }

    public double TotalDurationSeconds =>
        _session.Tracks.SelectMany(t => t.Clips).Select(c => (double?)c.TimelineEndSeconds).DefaultIfEmpty(0).Max() ?? 0;

    /// <summary>Raised after any edit that could change <see cref="TotalDurationSeconds"/>, so the owning player can retarget.</summary>
    public event Action? TimelineChanged;

    public TimelineViewModel(Project project, ObservableCollection<MediaAssetViewModel> availableMedia, Func<double> getPlayhead)
    {
        _project = project;
        _zoomPixelsPerSecond = Math.Clamp(project.Timeline.ZoomPixelsPerSecond, 10, 300);
        AvailableMedia = availableMedia;
        _getPlayhead = getPlayhead;
        _session = new TimelineEditSession(project.Timeline.Tracks);
        RefreshFromSession();
    }

    /// <summary>Writes the current (post-undo/redo) track state back into the project, for the caller to persist via IProjectRepository.</summary>
    public void SaveToProject() => _project.Timeline.Tracks = _session.Tracks.ToList();

    [RelayCommand]
    private void AddVideoTrack() => AddTrack(TimelineTrackKind.Video);

    [RelayCommand]
    private void AddAudioTrack() => AddTrack(TimelineTrackKind.Audio);

    [RelayCommand]
    private void AddCaptionTrack() => AddTrack(TimelineTrackKind.Caption);

    [RelayCommand]
    private void AddTextTrack() => AddTrack(TimelineTrackKind.Text);

    /// <summary>One practical action: create a text track when needed, add a text clip at the playhead,
    /// select it, and therefore expose the complete text inspector immediately.</summary>
    [RelayCommand]
    private void AddTextAtPlayhead()
    {
        var track = _session.Tracks.FirstOrDefault(t => t.Kind == TimelineTrackKind.Text && !t.IsLocked);
        if (track is null)
        {
            track = new TimelineTrack { Kind = TimelineTrackKind.Text, Name = "Tekst" };
            _session.AddTrack(track);
        }

        var clip = new TimelineClip
        {
            TextContent = "Novi tekst",
            TimelineStartSeconds = _getPlayhead(),
            SourceTrimInSeconds = 0,
            SourceTrimOutSeconds = DefaultTextClipDurationSeconds
        };
        _session.AddClip(track.Id, clip);
        RefreshFromSession();
        SelectedClipId = clip.Id;
    }

    /// <summary>
    /// The direct multi-video workflow: pick a video in the media dropdown and append it after the last
    /// video clip. The old workflow required creating a track, clicking "+ Klip" and manually guessing
    /// the next start time, which made a basic join operation unnecessarily difficult.
    /// </summary>
    [RelayCommand]
    private void AppendSelectedVideo()
    {
        var asset = SelectedMediaAsset?.Asset;
        if (asset is null || !asset.HasVideoStream || asset.ProbeError is not null)
        {
            return;
        }

        var track = _session.Tracks.FirstOrDefault(t => t.Kind == TimelineTrackKind.Video && !t.IsLocked);
        if (track is null)
        {
            track = new TimelineTrack { Kind = TimelineTrackKind.Video, Name = "Video" };
            _session.AddTrack(track);
        }

        var appendAt = _session.Tracks
            .Where(t => t.Kind == TimelineTrackKind.Video)
            .SelectMany(t => t.Clips)
            .Select(c => (double?)c.TimelineEndSeconds)
            .DefaultIfEmpty(0)
            .Max() ?? 0;
        var clip = BuildMediaClip(asset, appendAt);
        _session.AddClip(track.Id, clip);
        RefreshFromSession();
        SelectedClipId = clip.Id;
    }

    [RelayCommand]
    private void ZoomIn() => ZoomPixelsPerSecond = Math.Min(300, ZoomPixelsPerSecond * 1.35);

    [RelayCommand]
    private void ZoomOut() => ZoomPixelsPerSecond = Math.Max(10, ZoomPixelsPerSecond / 1.35);

    [RelayCommand]
    private void ResetZoom() => ZoomPixelsPerSecond = 40;

    partial void OnZoomPixelsPerSecondChanged(double value)
    {
        var clamped = Math.Clamp(value, 10, 300);
        if (Math.Abs(clamped - value) > 0.001)
        {
            ZoomPixelsPerSecond = clamped;
            return;
        }

        _project.Timeline.ZoomPixelsPerSecond = clamped;
        foreach (var track in Tracks) track.ApplyZoom(clamped, TotalDurationSeconds);
    }

    [RelayCommand]
    private void AddImageOverlayTrack() => AddTrack(TimelineTrackKind.ImageOverlay);

    private void AddTrack(TimelineTrackKind kind)
    {
        _session.AddTrack(new TimelineTrack { Kind = kind });
        RefreshFromSession();
    }

    /// <summary>Moves a clip to a new position on its track - what a mouse drag in the visual lane commits
    /// on release. One session call, so the whole drag is a single undo step.</summary>
    public void MoveClipTo(string clipId, double newStartSeconds)
    {
        _session.MoveClip(clipId, Math.Max(0, newStartSeconds));
        RefreshFromSession();
    }

    /// <summary>
    /// Moves a clip to a new position AND onto a different track - dropping a clip on another lane.
    /// Refuses a move onto a locked track, and onto a track of a kind that cannot hold it: a video clip on
    /// an audio lane would silently never render, which is worse than the drop simply not being accepted.
    /// Returns false (and changes nothing) when the move is refused, so the caller can say why.
    /// </summary>
    public bool MoveClipToTrack(string clipId, string targetTrackId, double newStartSeconds)
    {
        var target = _session.Tracks.FirstOrDefault(t => t.Id == targetTrackId);
        var clip = _session.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        if (target is null || clip is null || target.IsLocked)
        {
            return false;
        }

        var sourceTrack = _session.Tracks.First(t => t.Clips.Any(c => c.Id == clipId));
        if (!CanTrackHold(sourceTrack.Kind, target.Kind))
        {
            return false;
        }

        _session.MoveClip(clipId, Math.Max(0, newStartSeconds), targetTrackId);
        RefreshFromSession();
        return true;
    }

    /// <summary>Text belongs on text/caption lanes, pictures on picture lanes, sound on sound lanes -
    /// mirroring what <c>FfmpegFilterGraphBuilder</c> actually reads from each kind of track.</summary>
    private static bool CanTrackHold(TimelineTrackKind from, TimelineTrackKind to)
    {
        if (from == to)
        {
            return true;
        }

        var textKinds = new[] { TimelineTrackKind.Caption, TimelineTrackKind.Text };
        var pictureKinds = new[] { TimelineTrackKind.Video, TimelineTrackKind.ImageOverlay };

        return (textKinds.Contains(from) && textKinds.Contains(to))
            || (pictureKinds.Contains(from) && pictureKinds.Contains(to));
    }

    [RelayCommand]
    private void Undo()
    {
        _session.Undo();
        RefreshFromSession();
    }

    private bool CanUndo => _session.CanUndo;

    [RelayCommand]
    private void Redo()
    {
        _session.Redo();
        RefreshFromSession();
    }

    private bool CanRedo => _session.CanRedo;

    private void RefreshFromSession()
    {
        Tracks.Clear();
        foreach (var track in _session.Tracks)
        {
            Tracks.Add(CreateTrackItem(track));
        }

        // Refresh rebuilds every clip ViewModel. Keep the inspector open for the same selected clip;
        // otherwise changing one font/color value immediately hid the editor because the replacement
        // ViewModel started unselected while SelectedClipId itself had not changed.
        foreach (var clip in Tracks.SelectMany(t => t.Clips))
        {
            clip.RefreshSelection(SelectedClipId);
        }

        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(TotalDurationSeconds));
        TimelineChanged?.Invoke();
    }

    private TimelineTrackItemViewModel CreateTrackItem(TimelineTrack track)
    {
        var toggleLock = new RelayCommand(() => { _session.SetTrackLocked(track.Id, !track.IsLocked); RefreshFromSession(); });
        var toggleHide = new RelayCommand(() => { _session.SetTrackHidden(track.Id, !track.IsHidden); RefreshFromSession(); });
        var toggleMute = new RelayCommand(() => { _session.SetTrackMuted(track.Id, !track.IsMuted); RefreshFromSession(); });
        var toggleSolo = new RelayCommand(() => { _session.SetTrackSolo(track.Id, !track.IsSolo); RefreshFromSession(); });
        var removeTrack = new RelayCommand(() => { _session.RemoveTrack(track.Id); RefreshFromSession(); });
        var addClip = new RelayCommand(() => AddClipToTrack(track));
        void OnTrackVolumeChanged(string trackId, double volume)
        {
            _session.SetTrackVolume(trackId, volume);
            RefreshFromSession();
        }

        var trackItem = new TimelineTrackItemViewModel(track, toggleLock, toggleHide, toggleMute, toggleSolo, removeTrack, addClip, OnTrackVolumeChanged);
        foreach (var clip in track.Clips.OrderBy(c => c.TimelineStartSeconds))
        {
            trackItem.Clips.Add(CreateClipItem(clip, track));
        }

        trackItem.ApplyZoom(ZoomPixelsPerSecond, TotalDurationSeconds);

        return trackItem;
    }

    private TimelineClipItemViewModel CreateClipItem(TimelineClip clip, TimelineTrack track)
    {
        var split = new RelayCommand(() => { _session.SplitClip(clip.Id, _getPlayhead()); RefreshFromSession(); });
        var delete = new RelayCommand(() => { _session.DeleteClips(new[] { clip.Id }); RefreshFromSession(); });
        var duplicate = new RelayCommand(() => { _session.DuplicateClip(clip.Id); RefreshFromSession(); });
        var nudgeEarlier = new RelayCommand(() => { _session.MoveClip(clip.Id, clip.TimelineStartSeconds - NudgeStepSeconds); RefreshFromSession(); });
        var nudgeLater = new RelayCommand(() => { _session.MoveClip(clip.Id, clip.TimelineStartSeconds + NudgeStepSeconds); RefreshFromSession(); });
        var toggleMute = new RelayCommand(() => { _session.SetClipMute(clip.Id, !clip.IsMuted); RefreshFromSession(); });
        var toggleFadeIn = new RelayCommand(() => { _session.SetFade(clip.Id, clip.FadeInSeconds > 0 ? 0 : 0.5, clip.FadeOutSeconds); RefreshFromSession(); });
        var toggleFadeOut = new RelayCommand(() => { _session.SetFade(clip.Id, clip.FadeInSeconds, clip.FadeOutSeconds > 0 ? 0 : 0.5); RefreshFromSession(); });
        var applyStyleToAllOnTrack = new RelayCommand(() => { _session.ApplyTextStyleToAllClipsOnTrack(track.Id, clip.Id); RefreshFromSession(); });
        void OnTextStyleChanged(string clipId, CaptionFontChoice font, int size, string color, CaptionTextPosition position)
        {
            _session.SetTextStyle(clipId, font, size, color, position);
            RefreshFromSession();
        }
        void OnTextFontChanged(string clipId, CaptionFontChoice legacy, string? family, string? filePath)
        {
            _session.SetTextFont(clipId, legacy, family, filePath);
            RefreshFromSession();
        }
        void OnTrimInChanged(string clipId, double sourceSeconds)
        {
            _session.TrimIn(clipId, sourceSeconds);
            RefreshFromSession();
        }
        void OnTrimOutChanged(string clipId, double sourceSeconds)
        {
            _session.TrimOut(clipId, sourceSeconds);
            RefreshFromSession();
        }
        void OnTransitionChanged(string clipId, ClipTransitionType type, double duration)
        {
            _session.SetTransition(clipId, type, duration);
            RefreshFromSession();
        }
        void OnTextContentChanged(string clipId, string newText)
        {
            _session.SetTextContent(clipId, newText);
            Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Clip.Id == clipId)?.NotifyTextContentChanged();
            TimelineChanged?.Invoke();
        }
        void OnAdvancedStyleChanged(string clipId, TextAdvancedStyle style)
        {
            _session.SetTextAdvancedStyle(clipId, style);
            RefreshFromSession();
        }
        void OnLayerPlacementChanged(string clipId, double scale, double x, double y, double opacity)
        {
            _session.SetLayerPlacement(clipId, scale, x, y, opacity);
            RefreshFromSession();
        }
        void OnEffectsChanged(string clipId, ClipVideoEffect effect, double brightness, double contrast, double saturation, double speed)
        {
            _session.SetClipEffects(clipId, effect, brightness, contrast, saturation, speed);
            RefreshFromSession();
        }

        void OnTransformChanged(string clipId, ClipTransformSettings settings)
        {
            _session.SetClipTransform(clipId, settings);
            RefreshFromSession();
        }

        void OnCompositingChanged(string clipId, ClipCompositingSettings settings)
        {
            _session.SetClipCompositing(clipId, settings);
            RefreshFromSession();
        }
        void OnKeyframeUpsert(string clipId, ClipKeyframeProperty property, double localTime, double value, ClipKeyframeEasing easing)
        {
            _session.UpsertKeyframe(clipId, property, localTime, value, easing);
            TimelineChanged?.Invoke();
        }
        void OnKeyframeRemove(string clipId, ClipKeyframeProperty property, double localTime)
        {
            _session.RemoveKeyframe(clipId, property, localTime);
            TimelineChanged?.Invoke();
        }
        var sourceDurationSeconds = clip.MediaAssetId is null
            ? 0
            : AvailableMedia.FirstOrDefault(m => m.Asset.Id == clip.MediaAssetId)?.Asset.Duration.TotalSeconds
              ?? Math.Max(clip.SourceTrimOutSeconds, 0);

        return new TimelineClipItemViewModel(clip, track.Id, ResolveClipLabel(clip), track.Kind == TimelineTrackKind.Video,
            split, delete, duplicate, nudgeEarlier, nudgeLater, toggleMute, toggleFadeIn, toggleFadeOut, applyStyleToAllOnTrack,
            OnTextStyleChanged, OnTransitionChanged, OnTextContentChanged, OnAdvancedStyleChanged,
            OnLayerPlacementChanged, track.Kind == TimelineTrackKind.ImageOverlay || (track.Kind == TimelineTrackKind.Video && _session.Tracks.Where(t => t.Kind == TimelineTrackKind.Video).FirstOrDefault()?.Id != track.Id), OnEffectsChanged, OnTransformChanged, OnCompositingChanged, track.Kind == TimelineTrackKind.Audio,
            _getPlayhead, OnKeyframeUpsert, OnKeyframeRemove, OnTextFontChanged, sourceDurationSeconds, OnTrimInChanged, OnTrimOutChanged)
        {
            PixelsPerSecond = ZoomPixelsPerSecond
        };
    }

    private void AddClipToTrack(TimelineTrack track)
    {
        TimelineClip clip;
        if (track.Kind == TimelineTrackKind.Text)
        {
            clip = new TimelineClip
            {
                TextContent = "Novi tekst",
                TimelineStartSeconds = _getPlayhead(),
                SourceTrimInSeconds = 0,
                SourceTrimOutSeconds = DefaultTextClipDurationSeconds
            };
        }
        else
        {
            if (SelectedMediaAsset is null)
            {
                return;
            }

            clip = BuildMediaClip(SelectedMediaAsset.Asset, _getPlayhead());
        }

        _session.AddClip(track.Id, clip);
        RefreshFromSession();
        SelectedClipId = clip.Id;
    }

    private static TimelineClip BuildMediaClip(MediaAsset asset, double timelineStartSeconds)
    {
        var duration = asset.Duration.TotalSeconds > 0 ? asset.Duration.TotalSeconds : DefaultTextClipDurationSeconds;
        return new TimelineClip
        {
            MediaAssetId = asset.Id,
            TimelineStartSeconds = timelineStartSeconds,
            SourceTrimInSeconds = 0,
            SourceTrimOutSeconds = duration
        };
    }

    /// <summary>
    /// Places a just-imported clip on the timeline automatically, but only the very first time - once the
    /// timeline already has any clip on it, further imports go back to the deliberate "select in the
    /// dropdown, click + Klip" flow instead of silently rearranging an edit already in progress. Without
    /// this, "Dodaj medije" only adds the file to the library and the player keeps showing "Nema kadra za
    /// prikaz" until the user manually adds a track and a clip and moves the playhead onto it - a real,
    /// reported point of confusion for a first-time import (a non-technical user has no reason to expect
    /// importing a file and previewing it on a video track are two separate steps).
    /// </summary>
    /// <returns>True if the clip was actually auto-placed (a fresh, empty timeline), false if this wasn't
    /// the qualifying first-import case - callers use this to know whether it's also safe to adjust the
    /// project's canvas format to match this video (see <see cref="WorkspaceViewModel.ImportFilesAsync"/>).</returns>
    public bool AutoPlaceFirstImportOnEmptyTimeline(MediaAsset asset)
    {
        if (_session.Tracks.Any(t => t.Clips.Count > 0) || asset.ProbeError is not null || !asset.HasVideoStream)
        {
            return false;
        }

        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        _session.AddTrack(track);
        _session.AddClip(track.Id, BuildMediaClip(asset, timelineStartSeconds: 0));
        RefreshFromSession();
        return true;
    }

    /// <summary>
    /// Turns real Whisper transcription output into real caption clips on a new caption track, so
    /// "automatically add text from the video" is an actual pipeline (transcribe → clips on the
    /// timeline → burned in on export via FfmpegFilterGraphBuilder) instead of only a standalone .srt
    /// file the user has to import by hand. Always adds a fresh track rather than merging into an
    /// existing one, so a re-run never overwrites captions the user already edited by hand.
    /// </summary>
    public void AddGeneratedCaptions(IReadOnlyList<TranscribedCaptionSegment> segments)
    {
        var track = new TimelineTrack { Kind = TimelineTrackKind.Caption, Name = "Automatski titlovi" };
        _session.AddTrack(track);
        string? firstCaptionId = null;

        foreach (var segment in segments)
        {
            var text = segment.Text.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            var clip = new TimelineClip
            {
                TextContent = text,
                TimelineStartSeconds = segment.Start.TotalSeconds,
                SourceTrimInSeconds = 0,
                SourceTrimOutSeconds = (segment.End - segment.Start).TotalSeconds
            };
            _session.AddClip(track.Id, clip);
            firstCaptionId ??= clip.Id;
        }

        RefreshFromSession();
        if (firstCaptionId is not null)
        {
            SelectedClipId = firstCaptionId;
        }
    }

    private string ResolveClipLabel(TimelineClip clip)
    {
        if (clip.TextContent is not null)
        {
            return clip.TextContent;
        }

        var asset = _project.MediaLibrary.FirstOrDefault(a => a.Id == clip.MediaAssetId);
        return asset?.FileName ?? "(nepoznat medij)";
    }
}
