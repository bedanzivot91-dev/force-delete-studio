using NPVideoStudio.Domain;
using NPVideoStudio.Infrastructure.Persistence;
using Xunit;

namespace NPVideoStudio.UnitTests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"npvs_settings_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _settingsPath = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task LoadAsync_WhenFileMissing_CreatesDefaultsAndPersistsThem()
    {
        var service = new SettingsService(_settingsPath);
        await service.LoadAsync();

        Assert.True(File.Exists(_settingsPath));
        Assert.Equal(AppTheme.Studio2026, service.Current.Theme);
    }

    [Fact]
    public async Task SaveAndReload_RoundTripsChangedValues()
    {
        var service = new SettingsService(_settingsPath);
        await service.LoadAsync();
        service.Current.Theme = AppTheme.MinimalLight;
        service.Current.AutoSaveIntervalSeconds = 120;
        await service.SaveAsync();

        var reloaded = new SettingsService(_settingsPath);
        await reloaded.LoadAsync();

        Assert.Equal(AppTheme.MinimalLight, reloaded.Current.Theme);
        Assert.Equal(120, reloaded.Current.AutoSaveIntervalSeconds);
    }

    [Fact]
    public async Task LoadAsync_WhenFileIsCorrupted_FallsBackToDefaultsWithoutThrowing()
    {
        await File.WriteAllTextAsync(_settingsPath, "{ ovo nije ispravan json");
        var service = new SettingsService(_settingsPath);

        await service.LoadAsync();

        Assert.Equal(AppTheme.Studio2026, service.Current.Theme);
    }

    [Fact]
    public async Task ResetToDefaultsAsync_OverwritesModifiedValues()
    {
        var service = new SettingsService(_settingsPath);
        await service.LoadAsync();
        service.Current.AutoSaveEnabled = false;
        await service.SaveAsync();

        await service.ResetToDefaultsAsync();

        Assert.True(service.Current.AutoSaveEnabled);
        Assert.Equal(AppTheme.Studio2026, service.Current.Theme);
    }
}