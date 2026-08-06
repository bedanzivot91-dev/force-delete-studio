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

        return new TimelineClipItemViewModel(clip, trackId, ResolveClipLabel(clip),
            split, delete, duplicate, nudgeEarlier, nudgeLater, toggleMute, toggleFadeIn, toggleFadeOut);
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

            var asset = SelectedMediaAsset.Asset;
            var duration = asset.Duration.TotalSeconds > 0 ? asset.Duration.TotalSeconds : DefaultTextClipDurationSeconds;
            clip = new TimelineClip
            {
                MediaAssetId = asset.Id,
                TimelineStartSeconds = _getPlayhead(),
                SourceTrimInSeconds = 0,
                SourceTrimOutSeconds = duration
            };
        }

        _session.AddClip(track.Id, clip);
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
