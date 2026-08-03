using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Domain;

namespace NPVideoStudio.App.ViewModels;

/// <summary>Template picker (spec Phase 10: "Kreiraj video iz šablona") - a thin selection screen over
/// the fixed <see cref="ProjectTemplate.BuiltIn"/> list; picking one forwards straight into the existing,
/// already-tested "Novi projekat" flow with that template attached (see NewProjectViewModel).</summary>
public sealed partial class TemplateGalleryViewModel : ViewModelBase
{
    public IReadOnlyList<ProjectTemplate> Templates => ProjectTemplate.BuiltIn;

    public event Action<ProjectTemplate>? TemplateSelected;

    [RelayCommand]
    private void SelectTemplate(ProjectTemplate template) => TemplateSelected?.Invoke(template);
}
