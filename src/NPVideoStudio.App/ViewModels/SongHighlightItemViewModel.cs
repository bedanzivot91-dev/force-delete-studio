using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using Serilog;

namespace NPVideoStudio.App.ViewModels;

public sealed partial class SongHighlightItemViewModel : ViewModelBase
{
    private readonly SongHighlight _highlight;
    private readonly string _sourceAudioFilePath;
    private readonly ISongHighlightService _service;
    private readonly ILogger _logger;

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private string? _statusMessage;

    public int Index { get; }
    public string StartLabel => _highlight.Start.ToString(@"mm\:ss");
    public string EndLabel => _highlight.End.ToString(@"mm\:ss");
    public string DurationLabel => $"{_highlight.Duration.TotalSeconds:0} sek";
    public string LoudnessLabel => $"{_highlight.AverageLoudnessDb:0.0} dB";
    public bool IsExported => _highlight.ExportedFilePath is not null;
    public string? ExportedFilePath => _highlight.ExportedFilePath;

    public SongHighlightItemViewModel(SongHighlight highlight, int index, string sourceAudioFilePath,
        ISongHighlightService service, ILogger logger)
    {
        _highlight = highlight;
        Index = index;
        _sourceAudioFilePath = sourceAudioFilePath;
        _service = service;
        _logger = logger.ForContext("SourceContext", nameof(SongHighlightItemViewModel));
    }

    public async Task ExportAsync(string outputFilePath)
    {
        IsExporting = true;
        StatusMessage = null;
        try
        {
            await _service.ExportHighlightAsync(_sourceAudioFilePath, _highlight, outputFilePath);
            OnPropertyChanged(nameof(IsExported));
            OnPropertyChanged(nameof(ExportedFilePath));
            StatusMessage = "Isečak je sačuvan.";
            _logger.Information("Izvezen isečak pesme {Start}-{End} u {Path}", StartLabel, EndLabel, outputFilePath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Izvoz nije uspeo: {ex.Message}";
            _logger.Error(ex, "Izvoz isečka pesme nije uspeo");
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
