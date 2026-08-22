using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Domain;
using NPVideoStudio.App.Services;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// Caption preset browser. It can still be opened from Home as a read-only catalog, but when opened from
/// an active project it receives the real workspace apply callback and every card becomes an actual edit.
/// </summary>
public sealed partial class CaptionStyleGalleryViewModel : ViewModelBase
{
    private readonly Func<CaptionStylePreset, Task<string>>? _applyPreset;

    public IReadOnlyList<AppTheme?> Themes { get; } = new AppTheme?[] { null }.Concat(Enum.GetValues<AppTheme>().Cast<AppTheme?>()).ToList();
    public ObservableCollection<CaptionStylePresetItemViewModel> Presets { get; } = new();
    public bool CanApplyToProject => _applyPreset is not null;

    [ObservableProperty]
    private AppTheme? _selectedThemeFilter;

    [ObservableProperty]
    private string? _statusMessage;

    public event Action? BackRequested;

    public CaptionStyleGalleryViewModel() : this(null)
    {
    }

    public CaptionStyleGalleryViewModel(Func<CaptionStylePreset, Task<string>>? applyPreset)
    {
        _applyPreset = applyPreset;
        Refresh();
    }

    partial void OnSelectedThemeFilterChanged(AppTheme? value) => Refresh();

    [RelayCommand]
    private void Back() => BackRequested?.Invoke();

    public async Task ApplyPresetAsync(CaptionStylePreset preset)
    {
        if (_applyPreset is null)
        {
            StatusMessage = "Otvorite galeriju iz projekta i izaberite titl/tekst klip da biste primenili preset.";
            return;
        }

        StatusMessage = await _applyPreset(preset);
    }

    private void Refresh()
    {
        var source = SelectedThemeFilter is null
            ? CaptionStylePresetCatalog.All
            : CaptionStylePresetCatalog.ForTheme(SelectedThemeFilter.Value);

        Presets.Clear();
        foreach (var preset in source)
        {
            Presets.Add(new CaptionStylePresetItemViewModel(
                preset,
                _applyPreset is null ? null : () => ApplyPresetAsync(preset)));
        }
    }
}
