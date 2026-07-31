using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using Serilog;

namespace NPVideoStudio.App.ViewModels;

public sealed partial class LyricMatchItemViewModel : ViewModelBase
{
    private readonly LyricMatch _match;
    private readonly string _sourceAudioFilePath;
    private readonly ILyricSearchService _service;
    private readonly ILogger _logger;

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private string? _statusMessage;

    public int Index { get; }
    public string StartLabel => _match.Start.ToString(@"mm\:ss");
    public string EndLabel => _match.End.ToString(@"mm\:ss");
    public string RecognizedText => _match.RecognizedText;
    public string ConfidenceLabel => _match.Confidence >= 0.99
        ? "Tačno poklapanje"
        : $"Približno poklapanje ({_match.Confidence:P0})";
    public bool IsExactMatch => _match.Confidence >= 0.99;
    public bool IsExported => _match.ExportedFilePath is not null;
    public string? ExportedFilePath => _match.ExportedFilePath;

    public LyricMatchItemViewModel(LyricMatch match, int index, string sourceAudioFilePath,
        ILyricSearchService service, ILogger logger)
    {
        _match = match;
        Index = index;
        _sourceAudioFilePath = sourceAudioFilePath;
        _service = service;
        _logger = logger.ForContext("SourceContext", nameof(LyricMatchItemViewModel));
    }

    public async Task ExportAsync(string outputFilePath)
    {
        IsExporting = true;
        StatusMessage = null;
        try
        {
            await _service.ExportMatchAsync(_sourceAudioFilePath, _match, outputFilePath);
            OnPropertyChanged(nameof(IsExported));
            OnPropertyChanged(nameof(ExportedFilePath));
            StatusMessage = "Isečak je sačuvan.";
            _logger.Information("Izvezen isečak teksta {Start}-{End}", StartLabel, EndLabel);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Izvoz nije uspeo: {ex.Message}";
            _logger.Error(ex, "Izvoz isečka teksta nije uspeo");
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    private void OpenExported()
    {
        if (ExportedFilePath is null || !File.Exists(ExportedFilePath))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = ExportedFilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Nije moguće otvoriti fajl: {ex.Message}";
            _logger.Error(ex, "Otvaranje izvezenog isečka nije uspelo");
        }
    }
}
