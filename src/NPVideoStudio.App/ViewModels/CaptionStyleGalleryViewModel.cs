using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NPVideoStudio.Domain;
using NPVideoStudio.App.Services;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// "Stilovi titlova" gallery (spec Phase 7): browse the >=3-per-theme preset catalog with a static color
/// preview per card. Deliberately not a live animation preview - see PHASE_STATUS.md for why that's
/// scoped out this pass (10 real, distinct Avalonia animations is a substantial separate piece of work).
/// </summary>
public sealed partial class CaptionStyleGalleryViewModel : ViewModelBase
{
    /// <summary>Leading null represents "Sve teme" (no filter) - lets the ComboBox itself offer a clear-filter option.</summary>
    public IReadOnlyList<AppTheme?> Themes { get; } = new AppTheme?[] { null }.Concat(Enum.GetValues<AppTheme>().Cast<AppTheme?>()).ToList();

    public ObservableCollection<CaptionStylePresetItemViewModel> Presets { get; } = new();

    [ObservableProperty]
    private AppTheme? _selectedThemeFilter;

    public CaptionStyleGalleryViewModel()
    {
        Refresh();
    }

    partial void OnSelectedThemeFilterChanged(AppTheme? value) => Refresh();

    private void Refresh()
    {
        var source = SelectedThemeFilter is null
            ? CaptionStylePresetCatalog.All
            : CaptionStylePresetCatalog.ForTheme(SelectedThemeFilter.Value);

        Presets.Clear();
        foreach (var preset in source)
        {
            Presets.Add(new CaptionStylePresetItemViewModel(preset));
        }
    }
}
