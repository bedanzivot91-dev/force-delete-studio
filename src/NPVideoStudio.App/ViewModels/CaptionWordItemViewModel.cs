using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using NPVideoStudio.Domain;

namespace NPVideoStudio.App.ViewModels;

/// <summary>One row in the caption editor - wraps a live <see cref="CaptionWord"/> plus the per-row commands the parent <see cref="CaptionEditorViewModel"/> wires up.</summary>
public sealed partial class CaptionWordItemViewModel : ViewModelBase
{
    public CaptionWord Word { get; }

    [ObservableProperty]
    private bool _isSelected;

    public string Text
    {
        get => Word.OriginalText;
        set
        {
            if (Word.OriginalText == value)
            {
                return;
            }

            Word.OriginalText = value;
            Word.NormalizedText = AI.LyricMatcher.Normalize(value);
            OnPropertyChanged();
        }
    }

    public string TimingLabel => $"{FormatTime(Word.Start)} → {FormatTime(Word.End)}";

    public string SourceLabel => Word.Source switch
    {
        CaptionWordSource.VerifiedLyrics => "Verifikovani stih",
        CaptionWordSource.Lrc => "LRC",
        CaptionWordSource.Whisper => "Whisper",
        CaptionWordSource.WhisperX => "WhisperX",
        CaptionWordSource.FuzzyAligned => "Poravnato (fuzzy)",
        CaptionWordSource.Interpolated => "Interpolirano",
        _ => "Ručno"
    };

    public string VerificationLabel => Word.VerificationStatus switch
    {
        CaptionVerificationStatus.Verified => "Potvrđeno",
        CaptionVerificationStatus.NeedsReview => "Potrebna provera",
        _ => "Nije potvrđeno"
    };

    public bool EndsLine => Word.LineBreakAfter;

    public ICommand SplitCommand { get; }
    public ICommand MergeWithNextCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand NudgeEarlierCommand { get; }
    public ICommand NudgeLaterCommand { get; }
    public ICommand ToggleLineBreakCommand { get; }

    public CaptionWordItemViewModel(
        CaptionWord word,
        ICommand splitCommand,
        ICommand mergeWithNextCommand,
        ICommand deleteCommand,
        ICommand nudgeEarlierCommand,
        ICommand nudgeLaterCommand,
        ICommand toggleLineBreakCommand)
    {
        Word = word;
        SplitCommand = splitCommand;
        MergeWithNextCommand = mergeWithNextCommand;
        DeleteCommand = deleteCommand;
        NudgeEarlierCommand = nudgeEarlierCommand;
        NudgeLaterCommand = nudgeLaterCommand;
        ToggleLineBreakCommand = toggleLineBreakCommand;
    }

    /// <summary>Called after an operation elsewhere in the session may have changed this word's underlying data (timing shift, merge target, etc.) so bound labels refresh.</summary>
    public void RaiseAllChanged()
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(TimingLabel));
        OnPropertyChanged(nameof(SourceLabel));
        OnPropertyChanged(nameof(VerificationLabel));
        OnPropertyChanged(nameof(EndsLine));
    }

    private static string FormatTime(TimeSpan t) => $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}.{t.Milliseconds / 10:D2}";
}
