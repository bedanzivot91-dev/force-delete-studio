using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Domain;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// UI-observable wrapper around a plain <see cref="RenderJob"/> (spec Phase 9: "multiple queued export
/// jobs"). <see cref="RenderService"/> mutates the job's fields directly from its own thread with no
/// event channel, so <see cref="RefreshFromJob"/> is called on a timer tick from
/// <see cref="RenderQueueViewModel"/> - the same real-timer-polling pattern <see cref="PlayerViewModel"/>
/// already uses for transport state, rather than inventing a second progress-reporting mechanism.
/// </summary>
public sealed partial class RenderJobItemViewModel : ViewModelBase
{
    private readonly CancellationTokenSource _cts = new();

    public RenderJob Job { get; }

    public string ProjectName => Job.ProjectName;
    public string OutputFilePath => Job.Settings.OutputFilePath;

    [ObservableProperty]
    private RenderJobStatus _status;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public string StatusLabel => Status switch
    {
        RenderJobStatus.Queued => "U redu čekanja",
        RenderJobStatus.Running => "U toku",
        RenderJobStatus.Completed => "Završeno",
        RenderJobStatus.Failed => "Neuspešno",
        RenderJobStatus.Cancelled => "Otkazano",
        _ => Status.ToString()
    };

    public CancellationToken Token => _cts.Token;

    public RenderJobItemViewModel(RenderJob job)
    {
        Job = job;
        RefreshFromJob();
    }

    public void RefreshFromJob()
    {
        Status = Job.Status;
        ProgressPercent = Job.ProgressPercent;
        ErrorMessage = Job.ErrorMessage;
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(HasError));
        CancelCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts.Cancel();

    private bool CanCancel() => Status is RenderJobStatus.Queued or RenderJobStatus.Running;
}
