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

    public TimelineClip Clip { get; }
    public string TrackId { get; }

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
        ICommand toggleFadeOutCommand,
        Action<string, CaptionFontChoice, int, string, CaptionTextPosition>? onTextStyleChanged = null)
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
        _onTextStyleChanged = onTextStyleChanged;
    }

    private static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}.{t.Milliseconds / 100:D1}";
    }
}
