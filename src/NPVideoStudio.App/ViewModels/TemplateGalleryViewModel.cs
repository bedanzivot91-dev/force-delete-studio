using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Domain;
using NPVideoStudio.Infrastructure.Persistence;

namespace NPVideoStudio.App.ViewModels;

public sealed class TemplateGalleryItemViewModel
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string FormatLabel { get; init; }
    public bool IsUserTemplate { get; init; }
    public ProjectTemplate? BuiltInTemplate { get; init; }
    public UserTemplate? UserTemplate { get; init; }

    public static TemplateGalleryItemViewModel FromBuiltIn(ProjectTemplate template) => new()
    {
        Name = template.Name,
        Description = template.Description,
        FormatLabel = "Ugrađeni šablon",
        BuiltInTemplate = template
    };

    public static TemplateGalleryItemViewModel FromUser(UserTemplate template) => new()
    {
        Name = template.Name,
        Description = string.IsNullOrWhiteSpace(template.Description) ? "Moj sačuvani raspored projekta" : template.Description,
        FormatLabel = $"{template.Width} × {template.Height} • {template.Fps:0.##} fps • {template.StarterTrackKinds.Count} traka",
        IsUserTemplate = true,
        UserTemplate = template
    };
}

/// <summary>One gallery for built-in and real user-created templates. User template CRUD goes through
/// <see cref="UserTemplateRepository"/>; built-ins remain immutable.</summary>
public sealed partial class TemplateGalleryViewModel : ViewModelBase
{
    private readonly UserTemplateRepository _repository;

    public ObservableCollection<TemplateGalleryItemViewModel> Templates { get; } = new();

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRenameActive))]
    private TemplateGalleryItemViewModel? _renameTarget;

    [ObservableProperty]
    private string _renameName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRenameOverwriteRequired))]
    private bool _renameOverwriteRequired;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteConfirmationVisible))]
    private TemplateGalleryItemViewModel? _deleteTarget;

    public bool IsRenameActive => RenameTarget is not null;
    public bool IsRenameOverwriteRequired => RenameOverwriteRequired;
    public bool IsDeleteConfirmationVisible => DeleteTarget is not null;

    public event Action<ProjectTemplate>? BuiltInTemplateSelected;
    public event Action<UserTemplate>? UserTemplateSelected;

    // Keep DI registration safe even if another navigation path resolves this ViewModel directly.
    public TemplateGalleryViewModel() : this(new UserTemplateRepository())
    {
    }

    public TemplateGalleryViewModel(UserTemplateRepository repository)
    {
        _repository = repository;
        Reload();
    }

    [RelayCommand]
    private void SelectTemplate(TemplateGalleryItemViewModel item)
    {
        if (item.UserTemplate is not null)
        {
            UserTemplateSelected?.Invoke(item.UserTemplate);
            return;
        }

        if (item.BuiltInTemplate is not null)
        {
            BuiltInTemplateSelected?.Invoke(item.BuiltInTemplate);
        }
    }

    [RelayCommand]
    private void BeginRename(TemplateGalleryItemViewModel item)
    {
        if (!item.IsUserTemplate || item.UserTemplate is null)
        {
            return;
        }

        RenameTarget = item;
        RenameName = item.Name;
        RenameOverwriteRequired = false;
        DeleteTarget = null;
        Message = null;
    }

    [RelayCommand]
    private void ConfirmRename()
    {
        RenameCore(overwrite: false);
    }

    [RelayCommand]
    private void ConfirmRenameOverwrite()
    {
        RenameCore(overwrite: true);
    }

    private void RenameCore(bool overwrite)
    {
        if (RenameTarget?.UserTemplate is null)
        {
            return;
        }

        var newName = RenameName.Trim();
        if (newName.Length == 0)
        {
            Message = "Unesite novo ime šablona.";
            return;
        }

        if (!overwrite &&
            !string.Equals(UserTemplateRepository.SanitizeFileName(newName), UserTemplateRepository.SanitizeFileName(RenameTarget.Name), StringComparison.OrdinalIgnoreCase) &&
            _repository.Exists(newName))
        {
            RenameOverwriteRequired = true;
            Message = "Šablon sa tim imenom već postoji. Potvrdite prepisivanje ili promenite ime.";
            return;
        }

        try
        {
            var oldName = RenameTarget.Name;
            _repository.Rename(oldName, newName, overwrite);
            Message = $"Šablon „{oldName}“ je preimenovan u „{newName}“.";
            RenameTarget = null;
            RenameOverwriteRequired = false;
            Reload(keepMessage: true);
        }
        catch (Exception ex)
        {
            Message = $"Preimenovanje nije uspelo: {ex.Message}";
        }
    }

    [RelayCommand]
    private void BeginDelete(TemplateGalleryItemViewModel item)
    {
        if (!item.IsUserTemplate || item.UserTemplate is null)
        {
            return;
        }

        DeleteTarget = item;
        RenameTarget = null;
        RenameOverwriteRequired = false;
        Message = $"Obrisati šablon „{item.Name}“? Ovo ne briše nijedan projekat.";
    }

    [RelayCommand]
    private void ConfirmDelete()
    {
        if (DeleteTarget is null)
        {
            return;
        }

        var name = DeleteTarget.Name;
        var deleted = _repository.Delete(name);
        Message = deleted ? $"Šablon „{name}“ je obrisan." : "Šablon više ne postoji.";
        DeleteTarget = null;
        Reload(keepMessage: true);
    }

    [RelayCommand]
    private void CancelManage()
    {
        RenameTarget = null;
        DeleteTarget = null;
        RenameOverwriteRequired = false;
        Message = null;
    }

    [RelayCommand]
    private void RefreshTemplates() => Reload();

    private void Reload(bool keepMessage = false)
    {
        var previousMessage = Message;
        Templates.Clear();
        foreach (var builtIn in ProjectTemplate.BuiltIn)
        {
            Templates.Add(TemplateGalleryItemViewModel.FromBuiltIn(builtIn));
        }
        foreach (var user in _repository.LoadAll())
        {
            Templates.Add(TemplateGalleryItemViewModel.FromUser(user));
        }

        if (!keepMessage)
        {
            Message = null;
        }
        else
        {
            Message = previousMessage;
        }
    }
}
