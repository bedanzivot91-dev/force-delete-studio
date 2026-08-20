using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Domain;

namespace NPVideoStudio.App.ViewModels;

public sealed class SongLibraryItemViewModel : ViewModelBase
{
    public SongLibraryEntry Entry { get; }

    public string Title => Entry.Title;
    public string Artist => Entry.Artist;
    public string DurationLabel => Entry.Duration.ToString(@"mm\:ss");
    public string AddedAtLabel => Entry.AddedAt.ToString("dd.MM.yyyy.");

    public string VerificationStatusLabel => Entry.VerificationStatus switch
    {
        SongVerificationStatus.Verified => "Potvrđeno",
        SongVerificationStatus.NeedsReview => "Potrebna provera",
        _ => "Nije potvrđeno"
    };

    public ICommand ReanalyzeCommand { get; }
    public ICommand DeleteRecordOnlyCommand { get; }

    /// <summary>First destructive click only arms the confirmation UI. It never touches disk.</summary>
    public ICommand DeleteRecordAndFileCommand { get; }
    public ICommand ConfirmDeleteRecordAndFileCommand { get; }
    public ICommand CancelDeleteRecordAndFileCommand { get; }

    private bool _isDeleteFileConfirmationVisible;
    public bool IsDeleteFileConfirmationVisible
    {
        get => _isDeleteFileConfirmationVisible;
        private set
        {
            if (_isDeleteFileConfirmationVisible == value) return;
            _isDeleteFileConfirmationVisible = value;
            OnPropertyChanged();
        }
    }

    public SongLibraryItemViewModel(
        SongLibraryEntry entry, ICommand reanalyzeCommand, ICommand deleteRecordOnlyCommand, ICommand deleteRecordAndFileCommand)
    {
        Entry = entry;
        ReanalyzeCommand = reanalyzeCommand;
        DeleteRecordOnlyCommand = deleteRecordOnlyCommand;
        ConfirmDeleteRecordAndFileCommand = deleteRecordAndFileCommand;
        DeleteRecordAndFileCommand = new RelayCommand(() => IsDeleteFileConfirmationVisible = true);
        CancelDeleteRecordAndFileCommand = new RelayCommand(() => IsDeleteFileConfirmationVisible = false);
    }

    /// <summary>Called after the underlying <see cref="Entry"/> is mutated in place (e.g. re-analyze) so bound labels refresh.</summary>
    public void RaiseAllChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Artist));
        OnPropertyChanged(nameof(DurationLabel));
        OnPropertyChanged(nameof(VerificationStatusLabel));
    }
}
