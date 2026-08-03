using System.Windows.Input;
using NPVideoStudio.Domain;

namespace NPVideoStudio.App.ViewModels;

/// <summary>One clip row in the timeline UI - wraps a live <see cref="TimelineClip"/> plus the per-clip commands the parent <see cref="TimelineViewModel"/> wires up.</summary>
public sealed class TimelineClipItemViewModel : ViewModelBase
{
    public TimelineClip Clip { get; }
    public string TrackId { get; }

    public string Label { get; }
    public double StartSeconds => Clip.TimelineStartSeconds;
    public double DurationSeconds => Clip.TimelineDurationSeconds;
    public string TimingLabel => $"{FormatTime(Clip.TimelineStartSeconds)} → {FormatTime(Clip.TimelineEndSeconds)}";
    public bool IsMuted => Clip.IsMuted;
    public bool HasFadeIn => Clip.FadeInSeconds > 0;
    public bool HasFadeOut => Clip.FadeOutSeconds > 0;

    public ICommand SplitAtPlayheadCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand NudgeEarlierCommand { get; }
    public ICommand NudgeLaterCommand { get; }
    public ICommand ToggleMuteCommand { get; }
    public ICommand ToggleFadeInCommand { get; }
    public ICommand ToggleFadeOutCommand { get; }

    public TimelineClipItemViewModel(
        TimelineClip clip,
        string trackId,
        string label,
        ICommand splitAtPlayheadCommand,
        ICommand deleteCommand,
        ICommand duplicateCommand,
        ICommand nudgeEarlierCommand,
        ICommand nudgeLaterCommand,
        ICommand toggleMuteCommand,
        ICommand toggleFadeInCommand,
        ICommand toggleFadeOutCommand)
    {
        Clip = clip;
        TrackId = trackId;
        Label = label;
        SplitAtPlayheadCommand = splitAtPlayheadCommand;
        DeleteCommand = deleteCommand;
        DuplicateCommand = duplicateCommand;
        NudgeEarlierCommand = nudgeEarlierCommand;
        NudgeLaterCommand = nudgeLaterCommand;
        ToggleMuteCommand = toggleMuteCommand;
        ToggleFadeInCommand = toggleFadeInCommand;
        ToggleFadeOutCommand = toggleFadeOutCommand;
    }

    private static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}.{t.Milliseconds / 100:D1}";
    }
}
