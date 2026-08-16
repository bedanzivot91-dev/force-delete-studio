using System.Collections.ObjectModel;
using System.Windows.Input;
using NPVideoStudio.Domain;

namespace NPVideoStudio.App.ViewModels;

/// <summary>One track row in the timeline UI - wraps a live <see cref="TimelineTrack"/> plus its clip items and the parent-wired track-level commands.</summary>
public sealed class TimelineTrackItemViewModel : ViewModelBase
{
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
        ICommand addClipAtPlayheadCommand)
    {
        Track = track;
        ToggleLockCommand = toggleLockCommand;
        ToggleHideCommand = toggleHideCommand;
        ToggleMuteCommand = toggleMuteCommand;
        ToggleSoloCommand = toggleSoloCommand;
        RemoveTrackCommand = removeTrackCommand;
        AddClipAtPlayheadCommand = addClipAtPlayheadCommand;
    }
}
