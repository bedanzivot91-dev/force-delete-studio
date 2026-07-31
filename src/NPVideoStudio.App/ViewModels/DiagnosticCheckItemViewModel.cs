using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Core.Diagnostics;

namespace NPVideoStudio.App.ViewModels;

public sealed partial class DiagnosticCheckItemViewModel : ViewModelBase
{
    private readonly IDiagnosticsService _diagnosticsService;

    [ObservableProperty]
    private DiagnosticCheckResult _result;

    [ObservableProperty]
    private bool _isFixing;

    public DiagnosticCheckItemViewModel(DiagnosticCheckResult result, IDiagnosticsService diagnosticsService)
    {
        _result = result;
        _diagnosticsService = diagnosticsService;
    }

    public string StatusGlyph => Result.Status switch
    {
        DiagnosticStatus.Ok => "✓",
        DiagnosticStatus.Warning => "!",
        DiagnosticStatus.Error => "✕",
        _ => "?"
    };

    public bool CanAutoFix => Result.CanAutoFix;

    [RelayCommand]
    private async Task AutoFixAsync()
    {
        IsFixing = true;
        try
        {
            Result = await _diagnosticsService.TryAutoFixAsync(Result.CheckName);
            OnPropertyChanged(nameof(StatusGlyph));
            OnPropertyChanged(nameof(CanAutoFix));
        }
        finally
        {
            IsFixing = false;
        }
    }
}
