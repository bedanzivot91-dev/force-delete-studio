using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using Serilog;

namespace NPVideoStudio.App.ViewModels;

public sealed partial class NewProjectViewModel : ViewModelBase
{
    private readonly IProjectRepository _projectRepository;
    private readonly IRecentProjectsService _recentProjectsService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger _logger;

    public IReadOnlyList<AspectRatioPreset> AspectRatioOptions { get; } = Enum.GetValues<AspectRatioPreset>();
    public IReadOnlyList<ResolutionPreset> ResolutionOptions { get; } = Enum.GetValues<ResolutionPreset>();
    public IReadOnlyList<FrameRatePreset> FrameRateOptions { get; } = Enum.GetValues<FrameRatePreset>();

    [ObservableProperty]
    private string _projectName = "Novi projekat";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewLabel))]
    private AspectRatioPreset _selectedAspectRatio = AspectRatioPreset.Widescreen16x9;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewLabel))]
    private ResolutionPreset _selectedResolution = ResolutionPreset.FullHd1080;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewLabel))]
    private FrameRatePreset _selectedFrameRate = FrameRatePreset.Fps30;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewLabel))]
    private int _customWidth = 1920;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewLabel))]
    private int _customHeight = 1080;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewLabel))]
    private double _customFps = 30.0;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);
    public bool IsCustomResolution => SelectedResolution == ResolutionPreset.Custom;
    public bool IsCustomFrameRate => SelectedFrameRate == FrameRatePreset.Custom;

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasErrorMessage));
    partial void OnSelectedResolutionChanged(ResolutionPreset value) => OnPropertyChanged(nameof(IsCustomResolution));
    partial void OnSelectedFrameRateChanged(FrameRatePreset value) => OnPropertyChanged(nameof(IsCustomFrameRate));

    public string PreviewLabel
    {
        get
        {
            var (w, h) = SelectedResolution == ResolutionPreset.Custom
                ? (CustomWidth, CustomHeight)
                : ProjectFormat.ResolveResolution(SelectedResolution, SelectedAspectRatio);
            var fps = SelectedFrameRate == FrameRatePreset.Custom ? CustomFps : ProjectFormat.ResolveFps(SelectedFrameRate);
            var orientation = w == h ? "Kvadratni" : w > h ? "Horizontalni" : "Vertikalni";
            return $"{w} x {h}  •  {fps:0.##} fps  •  {orientation}";
        }
    }

    private readonly ProjectTemplate? _template;

    /// <summary>Set only when opened from the template gallery (spec Phase 10) - shown as a small info
    /// banner so the user knows which starter tracks will be added, and null otherwise (plain "Novi
    /// projekat" flow, unchanged).</summary>
    public string? TemplateInfoLabel => _template is null || _template.StarterTrackKinds.Count == 0
        ? null
        : $"Šablon „{_template.Name}“: dodaće se {_template.StarterTrackKinds.Count} početnih traka ({string.Join(", ", _template.StarterTrackKinds.Select(TrackKindLabel))}).";

    public event Action<Project>? ProjectCreated;

    public NewProjectViewModel(IProjectRepository projectRepository, IRecentProjectsService recentProjectsService, ISettingsService settingsService, ILogger logger, TargetPlatform? prefillPlatform = null, ProjectTemplate? template = null)
    {
        _projectRepository = projectRepository;
        _recentProjectsService = recentProjectsService;
        _settingsService = settingsService;
        _logger = logger.ForContext("SourceContext", nameof(NewProjectViewModel));
        _template = template;

        if (template is not null)
        {
            ProjectName = template.Name;
        }

        if (prefillPlatform is { } platform)
        {
            var format = ProjectFormat.ForPlatform(platform);
            SelectedAspectRatio = format.AspectRatio;
            SelectedResolution = format.Resolution;
            SelectedFrameRate = format.FrameRate;
            ProjectName = platform switch
            {
                Domain.TargetPlatform.YouTube => "YouTube video",
                Domain.TargetPlatform.YouTubeShorts => "YouTube Shorts",
                Domain.TargetPlatform.TikTok => "TikTok video",
                Domain.TargetPlatform.InstagramReel => "Instagram Reel",
                Domain.TargetPlatform.FacebookReel => "Facebook Reel",
                _ => ProjectName
            };
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(ProjectName))
        {
            ErrorMessage = "Unesite naziv projekta.";
            return;
        }

        try
        {
            var format = SelectedResolution == ResolutionPreset.Custom || SelectedFrameRate == FrameRatePreset.Custom
                ? new ProjectFormat
                {
                    AspectRatio = SelectedAspectRatio,
                    Resolution = SelectedResolution,
                    FrameRate = SelectedFrameRate,
                    Width = SelectedResolution == ResolutionPreset.Custom ? CustomWidth : ProjectFormat.ResolveResolution(SelectedResolution, SelectedAspectRatio).Width,
                    Height = SelectedResolution == ResolutionPreset.Custom ? CustomHeight : ProjectFormat.ResolveResolution(SelectedResolution, SelectedAspectRatio).Height,
                    Fps = SelectedFrameRate == FrameRatePreset.Custom ? CustomFps : ProjectFormat.ResolveFps(SelectedFrameRate)
                }
                : ProjectFormat.FromPresets(SelectedAspectRatio, SelectedResolution, SelectedFrameRate);

            if (format.Width <= 0 || format.Height <= 0 || format.Fps <= 0)
            {
                ErrorMessage = "Rezolucija i FPS moraju biti veći od nule.";
                return;
            }

            var project = new Project { Name = ProjectName.Trim(), Format = format };

            if (_template is not null)
            {
                foreach (var kind in _template.StarterTrackKinds)
                {
                    project.Timeline.Tracks.Add(new TimelineTrack { Kind = kind });
                }
            }

            var safeName = SanitizeFileName(project.Name);
            var projectDir = Path.Combine(_settingsService.Current.ProjectsFolder, safeName);
            var projectFilePath = Path.Combine(projectDir, $"{safeName}.npvsproject");

            Directory.CreateDirectory(projectDir);
            await _projectRepository.SaveAsync(project, projectFilePath);
            await _recentProjectsService.RegisterOpenedAsync(project);

            _logger.Information("Napravljen novi projekat {ProjectName} ({Width}x{Height}@{Fps}) na {Path}",
                project.Name, format.Width, format.Height, format.Fps, projectFilePath);

            ProjectCreated?.Invoke(project);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Projekat nije mogao da se napravi: {ex.Message}";
            _logger.Error(ex, "Pravljenje novog projekta nije uspelo");
        }
    }

    private static string TrackKindLabel(TimelineTrackKind kind) => kind switch
    {
        TimelineTrackKind.Video => "video",
        TimelineTrackKind.Audio => "audio",
        TimelineTrackKind.Caption => "titl",
        TimelineTrackKind.Text => "tekst",
        TimelineTrackKind.ImageOverlay => "slika (overlay)",
        _ => kind.ToString()
    };

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "Projekat" : cleaned;
    }
}
