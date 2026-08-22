using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.AI;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using NPVideoStudio.App.Services;
using Serilog;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// "Analiza rasporeda videa" (spec Phase 7's <see cref="IVideoLayoutAnalysisService"/>): samples a few
/// frames from a real video, runs real local OCR (Tesseract) on each, and recommends a vertical caption
/// position via <see cref="CaptionPlacementAdvisor"/>. Only existing-text detection is real - see
/// <see cref="VideoLayoutAnalysisResult"/>'s doc comment for the honest, documented gap (no face/logo/
/// CTA detection yet).
/// </summary>
public sealed partial class VideoLayoutAnalyzerViewModel : ViewModelBase
{
    private static readonly (string Name, string[] Extensions) VideoFilter = ("Video", new[]
    {
        "mp4", "mov", "mkv", "avi", "webm", "m4v", "mpeg", "mpg"
    });

    private readonly IVideoLayoutAnalysisService _analysisService;
    private readonly IStorageService _storageService;
    private readonly ILogger _logger;

    private VideoLayoutAnalysisResult? _result;

    public ObservableCollection<DetectedTextRegion> DetectedRegions { get; } = new();
    public ObservableCollection<ZoneOccupancyItem> ZoneOccupancy { get; } = new();

    public IReadOnlyList<CaptionPlacementMode> PlacementModes { get; } = Enum.GetValues<CaptionPlacementMode>();

    [ObservableProperty]
    private string? _selectedFilePath;

    [ObservableProperty]
    private string? _selectedFileName;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private CaptionPlacementMode _selectedPlacementMode = CaptionPlacementMode.Automatic;

    [ObservableProperty]
    private string? _recommendedPosition;

    [ObservableProperty]
    private string? _placementWarning;

    public bool HasSelectedFile => !string.IsNullOrEmpty(SelectedFilePath);
    public bool CanAnalyze => HasSelectedFile && !IsAnalyzing;

    public VideoLayoutAnalyzerViewModel(IVideoLayoutAnalysisService analysisService, IStorageService storageService, ILogger logger)
    {
        _analysisService = analysisService;
        _storageService = storageService;
        _logger = logger.ForContext("SourceContext", nameof(VideoLayoutAnalyzerViewModel));
    }

    partial void OnSelectedFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasSelectedFile));
        OnPropertyChanged(nameof(CanAnalyze));
        AnalyzeCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsAnalyzingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanAnalyze));
        AnalyzeCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedPlacementModeChanged(CaptionPlacementMode value) => UpdateRecommendation();

    [RelayCommand]
    private async Task PickFileAsync()
    {
        var files = await _storageService.PickFilesAsync("Izaberite video fajl", new[] { VideoFilter }, allowMultiple: false);
        if (files.Count == 0)
        {
            return;
        }

        SelectedFilePath = files[0];
        SelectedFileName = Path.GetFileName(files[0]);
        HasResult = false;
        StatusMessage = null;
    }

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        if (SelectedFilePath is null)
        {
            return;
        }

        IsAnalyzing = true;
        StatusMessage = null;
        HasResult = false;
        DetectedRegions.Clear();
        ZoneOccupancy.Clear();

        try
        {
            if (!await _analysisService.IsAvailableAsync())
            {
                StatusMessage = "Tesseract OCR nije instaliran - proverite „Alati i modeli“. Ostatak programa radi i bez ove analize.";
                return;
            }

            _result = await _analysisService.AnalyzeAsync(SelectedFilePath, sampleFrameCount: 5);

            foreach (var region in _result.DetectedTextRegions)
            {
                DetectedRegions.Add(region);
            }

            foreach (var zone in Enum.GetValues<CaptionGridZone>())
            {
                var ratio = _result.TextOccupancyByZone.TryGetValue(zone, out var value) ? value : 0;
                ZoneOccupancy.Add(new ZoneOccupancyItem(zone, ratio));
            }

            HasResult = true;
            UpdateRecommendation();
            StatusMessage = $"Analizirano {_result.SampledFrameCount} kadrova, pronađeno {_result.DetectedTextRegions.Count} tekstualnih regiona.";
            _logger.Information("Analiza rasporeda videa završena za {File}: {FrameCount} kadrova, {RegionCount} regiona",
                SelectedFileName, _result.SampledFrameCount, _result.DetectedTextRegions.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Analiza nije uspela: {ex.Message}";
            _logger.Error(ex, "Analiza rasporeda videa nije uspela za {File}", SelectedFilePath);
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private void UpdateRecommendation()
    {
        if (_result is null)
        {
            return;
        }

        var (position, warning) = CaptionPlacementAdvisor.Recommend(_result, SelectedPlacementMode);
        RecommendedPosition = position switch
        {
            CaptionPlacementMode.Top => "Vrh",
            CaptionPlacementMode.Middle => "Sredina",
            CaptionPlacementMode.Bottom => "Dno",
            CaptionPlacementMode.Manual => "Ručno",
            _ => position.ToString()
        };
        PlacementWarning = warning;
    }
}

/// <summary>Display row for one grid zone's occupancy percentage.</summary>
public sealed class ZoneOccupancyItem
{
    public CaptionGridZone Zone { get; }
    public double Ratio { get; }
    public string Label => Zone switch
    {
        CaptionGridZone.TopLeft => "Gore levo",
        CaptionGridZone.TopCenter => "Gore sredina",
        CaptionGridZone.TopRight => "Gore desno",
        CaptionGridZone.MiddleLeft => "Sredina levo",
        CaptionGridZone.MiddleCenter => "Sredina",
        CaptionGridZone.MiddleRight => "Sredina desno",
        CaptionGridZone.BottomLeft => "Dole levo",
        CaptionGridZone.BottomCenter => "Dole sredina",
        CaptionGridZone.BottomRight => "Dole desno",
        _ => Zone.ToString()
    };
    public string RatioLabel => Ratio.ToString("P0");

    public ZoneOccupancyItem(CaptionGridZone zone, double ratio)
    {
        Zone = zone;
        Ratio = ratio;
    }
}
