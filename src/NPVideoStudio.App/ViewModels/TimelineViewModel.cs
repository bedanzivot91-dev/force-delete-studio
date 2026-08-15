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
    private const double DefaultTextClipDurationSeconds = 3.0;

    private readonly Project _project;
    private readonly Func<double> _getPlayhead;
    private readonly TimelineEditSession _session;

    public ObservableCollection<TimelineTrackItemViewModel> Tracks { get; } = new();
    public ObservableCollection<MediaAssetViewModel> AvailableMedia { get; }

    [ObservableProperty]
    private MediaAssetViewModel? _selectedMediaAsset;

    /// <summary>Live (post-undo/redo) track state, for the workspace's preview-frame resolution - unlike
    /// <see cref="Project.Timeline"/>.Tracks, this reflects edits before <see cref="SaveToProject"/> runs.</summary>
    public IReadOnlyList<TimelineTrack> CurrentTracks => _session.Tracks;

    public double TotalDurationSeconds =>
        _session.Tracks.SelectMany(t => t.Clips).Select(c => (double?)c.TimelineEndSeconds).DefaultIfEmpty(0).Max() ?? 0;

    /// <summary>Raised after any edit that could change <see cref="TotalDurationSeconds"/>, so the owning player can retarget.</summary>
    public event Action? TimelineChanged;

    public TimelineViewModel(Project project, ObservableCollection<MediaAssetViewModel> availableMedia, Func<double> getPlayhead)
    {
        _project = project;
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

    [RelayCommand]
    private void AddImageOverlayTrack() => AddTrack(TimelineTrackKind.ImageOverlay);

    private void AddTrack(TimelineTrackKind kind)
    {
        _session.AddTrack(new TimelineTrack { Kind = kind });
        RefreshFromSession();
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

        var trackItem = new TimelineTrackItemViewModel(track, toggleLock, toggleHide, toggleMute, toggleSolo, removeTrack, addClip);
        foreach (var clip in track.Clips.OrderBy(c => c.TimelineStartSeconds))
        {
            trackItem.Clips.Add(CreateClipItem(clip, track.Id));
        }

        return trackItem;
    }

    private TimelineClipItemViewModel CreateClipItem(TimelineClip clip, string trackId)
    {
        var split = new RelayCommand(() => { _session.SplitClip(clip.Id, _getPlayhead()); RefreshFromSession(); });
        var delete = new RelayCommand(() => { _session.DeleteClips(new[] { clip.Id }); RefreshFromSession(); });
        var duplicate = new RelayCommand(() => { _session.DuplicateClip(clip.Id); RefreshFromSession(); });
        var nudgeEarlier = new RelayCommand(() => { _session.MoveClip(clip.Id, clip.TimelineStartSeconds - NudgeStepSeconds); RefreshFromSession(); });
        var nudgeLater = new RelayCommand(() => { _session.MoveClip(clip.Id, clip.TimelineStartSeconds + NudgeStepSeconds); RefreshFromSession(); });
        var toggleMute = new RelayCommand(() => { _session.SetClipMute(clip.Id, !clip.IsMuted); RefreshFromSession(); });
        var toggleFadeIn = new RelayCommand(() => { _session.SetFade(clip.Id, clip.FadeInSeconds > 0 ? 0 : 0.5, clip.FadeOutSeconds); RefreshFromSession(); });
        var toggleFadeOut = new RelayCommand(() => { _session.SetFade(clip.Id, clip.FadeInSeconds, clip.FadeOutSeconds > 0 ? 0 : 0.5); RefreshFromSession(); });
        void OnTextStyleChanged(string clipId, CaptionFontChoice font, int size, string color, CaptionTextPosition position)
        {
            _session.SetTextStyle(clipId, font, size, color, position);
            RefreshFromSession();
        }

        return new TimelineClipItemViewModel(clip, trackId, ResolveClipLabel(clip),
            split, delete, duplicate, nudgeEarlier, nudgeLater, toggleMute, toggleFadeIn, toggleFadeOut, OnTextStyleChanged);
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
    public void AutoPlaceFirstImportOnEmptyTimeline(MediaAsset asset)
    {
        if (_session.Tracks.Any(t => t.Clips.Count > 0) || asset.ProbeError is not null || !asset.HasVideoStream)
        {
            return;
        }

        var track = new TimelineTrack { Kind = TimelineTrackKind.Video };
        _session.AddTrack(track);
        _session.AddClip(track.Id, BuildMediaClip(asset, timelineStartSeconds: 0));
        RefreshFromSession();
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
        }

        RefreshFromSession();
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
