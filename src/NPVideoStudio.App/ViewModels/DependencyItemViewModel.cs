using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Core.Diagnostics;

namespace NPVideoStudio.App.ViewModels;

public sealed partial class DependencyItemViewModel : ViewModelBase
{
    private readonly DependencyInfo _info;
    private readonly IDependencyManagerService _service;

    public DependencyItemViewModel(DependencyInfo info, IDependencyManagerService service)
    {
        _info = info;
        _service = service;
    }

    public string Name => _info.Name;
    public bool IsInstalled => _info.Status == DependencyStatus.Installed;
    public string StatusLabel => IsInstalled ? "Instalirano" : "Nije instalirano";
    public string? VersionLabel => _info.Version;
    public string WhyItMatters => _info.WhyItMatters;
    public string? TechnicalDetails => _info.TechnicalDetails;
    public bool CanDownload => _info.CanDownload;
    public bool CanOpenFolder => _info.CanOpenFolder && _info.Path is not null;

    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private void OpenFolder()
    {
        if (_info.Path is not null)
        {
            _service.OpenContainingFolder(_info.Path);
        }
    }
}
