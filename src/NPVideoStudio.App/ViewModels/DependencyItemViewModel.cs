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
    public bool IsInstalled => _info.IsUsable;
    public bool NeedsAttention => _info.NeedsAttention;
    public string StatusLabel => _info.Status switch
    {
        DependencyStatus.Installed => "Instalirano",
        DependencyStatus.NotInstalled => "Nije instalirano",
        DependencyStatus.UpdateAvailable => "Ažuriranje dostupno",
        DependencyStatus.Corrupt => "Oštećeno / neispravno",
        DependencyStatus.Incompatible => "Nekompatibilno",
        DependencyStatus.Checking => "Provera...",
        DependencyStatus.Downloading => "Preuzimanje...",
        _ => _info.Status.ToString()
    };
    public string? VersionLabel => _info.Version;
    public string? ExpectedVersionLabel => string.IsNullOrWhiteSpace(_info.ExpectedVersion)
        ? null
        : $"Očekivano: {_info.ExpectedVersion}";
    public string? LicenseLabel => string.IsNullOrWhiteSpace(_info.License) ? null : $"Licenca: {_info.License}";
    public string LastCheckedLabel => $"Provereno: {_info.LastCheckedUtc.ToLocalTime():dd.MM.yyyy HH:mm}";
    public string WhyItMatters => _info.WhyItMatters;
    public string? TechnicalDetails => _info.TechnicalDetails;
    public bool CanDownload => _info.CanDownload;
    public bool CanRepair => _info.CanRepair;
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
