using System.Collections.ObjectModel;
using System.Windows.Input;
using NPVideoStudio.Domain;

namespace NPVideoStudio.App.ViewModels;

/// <summary>One track row in the timeline UI - wraps a live <see cref="TimelineTrack"/> plus its clip items and the parent-wired track-level commands.</summary>
public sealed class TimelineTrackItemViewModel : ViewModelBase
{
    private readonly Action<string, double>? _onVolumeChanged;

    public TimelineTrack Track { get; }
    public ObservableCollection<TimelineClipItemViewModel> Clips { get; } = new();

    public string KindLabel => Track.Kind switch
    {
        TimelineTrackKind.Video => "Video",
        TimelineTrackKind.Audio => "Audio",
        TimelineTrackKind.Caption => "Titlovi",
        TimelineTrackKind.Text => "Tekst",
        TimelineTrackKind.ImageOverlay => "Slika (overlay)",
        _ => Track.Kind.ToString()
    };

    public string DisplayName => string.IsNullOrWhiteSpace(Track.Name) ? KindLabel : Track.Name;
    public bool IsLocked => Track.IsLocked;
    public bool IsHidden => Track.IsHidden;
    public bool IsMuted => Track.IsMuted;
    public bool IsSolo => Track.IsSolo;

    /// <summary>
    /// Track-level gain is currently rendered only for standalone Audio tracks. Do not expose this on
    /// Video/Caption/Text/ImageOverlay tracks until their renderer paths consume Track.Volume too; a visible
    /// slider that does nothing would violate the editor's no-silent-no-op rule.
    /// </summary>
    public bool HasVolumeControl => Track.Kind == TimelineTrackKind.Audio;

    /// <summary>UI-friendly 0..200 percent mapped to the persisted/rendered 0..2 TimelineTrack.Volume.</summary>
    public double VolumePercent
    {
        get => Math.Round(Track.Volume * 100, 1);
        set
        {
            var clampedPercent = Math.Clamp(value, 0, 200);
            var volume = clampedPercent / 100.0;
            if (Math.Abs(Track.Volume - volume) < 1e-9)
            {
                return;
            }

            // Never mutate Track.Volume directly here. The owning TimelineEditSession must see the old
            // value first so SaveSnapshot() can make this a real single-step Undo operation.
            _onVolumeChanged?.Invoke(Track.Id, volume);
        }
    }

    public string VolumeLabel => $"{VolumePercent:0}%";

    private double _laneWidth = 1200;
    public double LaneWidth
    {
        get => _laneWidth;
        private set { if (Math.Abs(_laneWidth - value) < 0.01) return; _laneWidth = value; OnPropertyChanged(); }
    }

    public void ApplyZoom(double pixelsPerSecond, double projectDurationSeconds)
    {
        foreach (var clip in Clips) clip.PixelsPerSecond = pixelsPerSecond;
        LaneWidth = Math.Max(1200, Math.Max(projectDurationSeconds, 10) * pixelsPerSecond + 120);
    }

    public ICommand ToggleLockCommand { get; }
    public ICommand ToggleHideCommand { get; }
    public ICommand ToggleMuteCommand { get; }
    public ICommand ToggleSoloCommand { get; }
    public ICommand RemoveTrackCommand { get; }
    public ICommand AddClipAtPlayheadCommand { get; }

    public TimelineTrackItemViewModel(
        TimelineTrack track,
        ICommand toggleLockCommand,
        ICommand toggleHideCommand,
        ICommand toggleMuteCommand,
        ICommand toggleSoloCommand,
        ICommand removeTrackCommand,
        ICommand addClipAtPlayheadCommand,
        Action<string, double>? onVolumeChanged = null)
    {
        Track = track;
        _onVolumeChanged = onVolumeChanged;
        ToggleLockCommand = toggleLockCommand;
        ToggleHideCommand = toggleHideCommand;
        ToggleMuteCommand = toggleMuteCommand;
        ToggleSoloCommand = toggleSoloCommand;
        RemoveTrackCommand = removeTrackCommand;
        AddClipAtPlayheadCommand = addClipAtPlayheadCommand;
    }
}