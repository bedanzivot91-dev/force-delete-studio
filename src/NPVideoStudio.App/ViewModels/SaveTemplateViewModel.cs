using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Domain;
using NPVideoStudio.Infrastructure.Persistence;

namespace NPVideoStudio.App.ViewModels;

/// <summary>Normal UI workflow for saving the currently open project as a reusable user template.
/// Media files and clip contents are never copied: UserTemplateRepository.FromProject stores only format
/// and starter track kinds.</summary>
public sealed partial class SaveTemplateViewModel : ViewModelBase
{
    private readonly Project _project;
    private readonly UserTemplateRepository _repository;

    public string ProjectName => _project.Name;
    public string ProjectFormatLabel => $"{_project.Format.Width} × {_project.Format.Height} • {_project.Format.Fps:0.##} fps";
    public string TrackSummary => _project.Timeline.Tracks.Count == 0
        ? "Bez početnih traka"
        : $"{_project.Timeline.Tracks.Count} traka: {string.Join(", ", _project.Timeline.Tracks.Select(t => t.Kind))}";

    [ObservableProperty]
    private string _templateName;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverwriteConfirmationVisible))]
    private bool _overwritePending;

    public bool IsOverwriteConfirmationVisible => OverwritePending;

    public event Action? BackRequested;
    public event Action? TemplateSaved;

    public SaveTemplateViewModel(Project project, UserTemplateRepository repository)
    {
        _project = project;
        _repository = repository;
        _templateName = project.Name;
    }

    [RelayCommand]
    private void Save()
    {
        SaveCore(overwrite: false);
    }

    [RelayCommand]
    private void ConfirmOverwrite()
    {
        SaveCore(overwrite: true);
    }

    private void SaveCore(bool overwrite)
    {
        var name = TemplateName.Trim();
        if (name.Length == 0)
        {
            Message = "Unesite ime šablona.";
            OverwritePending = false;
            return;
        }

        if (!overwrite && _repository.Exists(name))
        {
            OverwritePending = true;
            Message = $"Šablon „{name}“ već postoji. Potvrdite prepisivanje ako želite da ga zamenite trenutnim podešavanjem.";
            return;
        }

        try
        {
            var template = UserTemplateRepository.FromProject(_project, name, Description.Trim());
            _repository.Save(template);
            OverwritePending = false;
            Message = $"Šablon „{name}“ je sačuvan. Mediji i klipovi nisu kopirani.";
            TemplateSaved?.Invoke();
        }
        catch (Exception ex)
        {
            OverwritePending = false;
            Message = $"Šablon nije sačuvan: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CancelOverwrite()
    {
        OverwritePending = false;
        Message = null;
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke();
}
