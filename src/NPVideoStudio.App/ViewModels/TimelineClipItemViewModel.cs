using System.Windows.Input;
using NPVideoStudio.Domain;

namespace NPVideoStudio.App.ViewModels;

/// <summary>One clip row in the timeline UI - wraps a live <see cref="TimelineClip"/> plus the per-clip commands the parent <see cref="TimelineViewModel"/> wires up.</summary>
public sealed class TimelineClipItemViewModel : ViewModelBase
{
    /// <summary>(clipId, fontChoice, fontSizePx, textColor, textPosition) - deliberately passed as plain
    /// values rather than mutating <see cref="Clip"/> directly here first: <see cref="Clip"/> is the same
    /// live object the owning <c>TimelineEditSession</c> holds internally, so mutating it before the
    /// session's own SetTextStyle call would make its undo snapshot capture the *new* value as if it were
    /// the "before" state, silently breaking undo for style edits.</summary>
    private readonly Action<string, CaptionFontChoice, int, string, CaptionTextPosition>? _onTextStyleChanged;

    /// <summary>(clipId, transitionType, durationSeconds) - same reasoning as <see cref="_onTextStyleChanged"/>
    /// above: goes through the owning session's SetTransition so undo captures the correct "before" state.</summary>
    private readonly Action<string, ClipTransitionType, double>? _onTransitionChanged;

    public TimelineClip Clip { get; }
    public string TrackId { get; }

    /// <summary>True only for a clip on a Video-kind track - transitions only make sense between two
    /// video clips, never on caption/text/audio/image-overlay tracks.</summary>
    public bool IsVideoClip { get; }

    public string Label { get; }
    public double StartSeconds => Clip.TimelineStartSeconds;
    public double DurationSeconds => Clip.TimelineDurationSeconds;
    public string TimingLabel => $"{FormatTime(Clip.TimelineStartSeconds)} → {FormatTime(Clip.TimelineEndSeconds)}";
    public bool IsMuted => Clip.IsMuted;
    public bool HasFadeIn => Clip.FadeInSeconds > 0;
    public bool HasFadeOut => Clip.FadeOutSeconds > 0;

    /// <summary>True for a Caption/Text clip - the font/size/color/position controls below only make
    /// sense (and are only shown in the UI) for these.</summary>
    public bool IsTextClip => Clip.TextContent is not null;

    /// <summary>These four are real, working per-clip text style controls - unlike the 24 "Stilovi
    /// titlova" gallery presets (color-swatch preview only), changing these actually changes what
    /// <c>FfmpegFilterGraphBuilder</c> burns into the exported video for this exact clip.</summary>
    public CaptionFontChoice FontChoice
    {
        get => Clip.FontChoice;
        set
        {
            if (Clip.FontChoice == value) return;
            _onTextStyleChanged?.Invoke(Clip.Id, value, FontSizePx, TextColor, TextPosition);
        }
    }

    public int FontSizePx
    {
        get => Clip.FontSizePx;
        set
        {
            if (Clip.FontSizePx == value) return;
            _onTextStyleChanged?.Invoke(Clip.Id, FontChoice, value, TextColor, TextPosition);
        }
    }

    public string TextColor
    {
        get => Clip.TextColor;
        set
        {
            if (Clip.TextColor == value || string.IsNullOrWhiteSpace(value)) return;
            _onTextStyleChanged?.Invoke(Clip.Id, FontChoice, FontSizePx, value, TextPosition);
        }
    }

    public CaptionTextPosition TextPosition
    {
        get => Clip.TextPosition;
        set
        {
            if (Clip.TextPosition == value) return;
            _onTextStyleChanged?.Invoke(Clip.Id, FontChoice, FontSizePx, TextColor, value);
        }
    }

    public IReadOnlyList<CaptionFontChoice> AvailableFontChoices { get; } = Enum.GetValues<CaptionFontChoice>();
    public IReadOnlyList<CaptionTextPosition> AvailablePositions { get; } = Enum.GetValues<CaptionTextPosition>();

    /// <summary>Real transition into this clip from whichever Video-track clip is right before it - burnt
    /// into the exported video via ffmpeg's own <c>xfade</c>/<c>acrossfade</c> filters, not a placeholder.
    /// Has no visible effect on the very first clip on a track (nothing to transition from) or when there's
    /// a real gap before this clip - both cases are handled gracefully by the render pipeline rather than
    /// erroring, so leaving this set doesn't break anything if the clip before it is later moved/deleted.</summary>
    public ClipTransitionType TransitionInType
    {
        get => Clip.TransitionInType;
        set
        {
            if (Clip.TransitionInType == value) return;
            _onTransitionChanged?.Invoke(Clip.Id, value, TransitionInDurationSeconds);
        }
    }

    public double TransitionInDurationSeconds
    {
        get => Clip.TransitionInDurationSeconds;
        set
        {
            if (Math.Abs(Clip.TransitionInDurationSeconds - value) < 1e-9) return;
            _onTransitionChanged?.Invoke(Clip.Id, TransitionInType, value);
        }
    }

    public IReadOnlyList<ClipTransitionType> AvailableTransitions { get; } = Enum.GetValues<ClipTransitionType>();

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
        bool isVideoClip,
        ICommand splitAtPlayheadCommand,
        ICommand deleteCommand,
        ICommand duplicateCommand,
        ICommand nudgeEarlierCommand,
        ICommand nudgeLaterCommand,
        ICommand toggleMuteCommand,
        ICommand toggleFadeInCommand,
        ICommand toggleFadeOutCommand,
        Action<string, CaptionFontChoice, int, string, CaptionTextPosition>? onTextStyleChanged = null,
        Action<string, ClipTransitionType, double>? onTransitionChanged = null)
    {
        Clip = clip;
        TrackId = trackId;
        Label = label;
        IsVideoClip = isVideoClip;
        SplitAtPlayheadCommand = splitAtPlayheadCommand;
        DeleteCommand = deleteCommand;
        DuplicateCommand = duplicateCommand;
        NudgeEarlierCommand = nudgeEarlierCommand;
        NudgeLaterCommand = nudgeLaterCommand;
        ToggleMuteCommand = toggleMuteCommand;
        ToggleFadeInCommand = toggleFadeInCommand;
        ToggleFadeOutCommand = toggleFadeOutCommand;
        _onTextStyleChanged = onTextStyleChanged;
        _onTransitionChanged = onTransitionChanged;
    }

    private static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}.{t.Milliseconds / 100:D1}";
    }
}
