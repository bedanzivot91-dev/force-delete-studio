using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Domain;
using NPVideoStudio.Infrastructure.Persistence;
using Serilog;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Drives SettingsViewModel's real SaveCommand/ResetToDefaultsCommand against a real (isolated,
/// temp-path) SettingsService - upgrades settings.save/settings.reset from service-only to real
/// command-chain coverage (FUNCTION_MATRIX.md), including the new FFmpeg/FFprobe/yt-dlp path fields
/// added in Phase 1.
/// </summary>
public class SettingsViewModelTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"npvs_settings_vm_test_{Guid.NewGuid():N}");
    private readonly SettingsService _settingsService;
    private readonly SettingsViewModel _viewModel;

    public SettingsViewModelTests()
    {
        Directory.CreateDirectory(_tempDir);
        _settingsService = new SettingsService(Path.Combine(_tempDir, "settings.json"));
        _settingsService.LoadAsync().GetAwaiter().GetResult();
        _viewModel = new SettingsViewModel(_settingsService, new FakeStorageService(), new LoggerConfiguration().CreateLogger());
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task SaveCommand_PersistsToolPathsAndTheme()
    {
        _viewModel.Theme = AppTheme.MinimalLight;
        _viewModel.FfmpegPath = "/custom/ffmpeg";
        _viewModel.FfprobePath = "/custom/ffprobe";
        _viewModel.YtDlpPath = "/custom/yt-dlp";
        _viewModel.AutoSaveIntervalSeconds = 120;
        _viewModel.ToolUpdatePolicy = ToolUpdatePolicy.Automatic;
        _viewModel.ToolUpdateIntervalDays = 14;

        await _viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(AppTheme.MinimalLight, _settingsService.Current.Theme);
        Assert.Equal("/custom/ffmpeg", _settingsService.Current.FfmpegPath);
        Assert.Equal("/custom/ffprobe", _settingsService.Current.FfprobePath);
        Assert.Equal("/custom/yt-dlp", _settingsService.Current.YtDlpPath);
        Assert.Equal(120, _settingsService.Current.AutoSaveIntervalSeconds);
        Assert.Equal(ToolUpdatePolicy.Automatic, _settingsService.Current.ToolUpdatePolicy);
        Assert.Equal(14, _settingsService.Current.ToolUpdateIntervalDays);
        Assert.Equal("Podešavanja su sačuvana.", _viewModel.StatusMessage);

        // Reload from disk into a fresh service instance to prove it was actually written, not just
        // held in the shared in-memory Current object.
        var reloaded = new SettingsService(Path.Combine(_tempDir, "settings.json"));
        await reloaded.LoadAsync();
        Assert.Equal("/custom/ffmpeg", reloaded.Current.FfmpegPath);
        Assert.Equal("/custom/yt-dlp", reloaded.Current.YtDlpPath);
        Assert.Equal(ToolUpdatePolicy.Automatic, reloaded.Current.ToolUpdatePolicy);
    }

    [Fact]
    public async Task SaveCommand_BlankToolPaths_PersistsAsNull()
    {
        _viewModel.FfmpegPath = "   ";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Null(_settingsService.Current.FfmpegPath);
    }

    [Fact]
    public async Task ResetToDefaultsCommand_RestoresDefaultsAndRefreshesViewModel()
    {
        _viewModel.FfmpegPath = "/custom/ffmpeg";
        await _viewModel.SaveCommand.ExecuteAsync(null);

        await _viewModel.ResetToDefaultsCommand.ExecuteAsync(null);

        Assert.Null(_viewModel.FfmpegPath);
        Assert.Null(_settingsService.Current.FfmpegPath);
        Assert.Equal("Podešavanja su vraćena na podrazumevane vrednosti.", _viewModel.StatusMessage);
    }
}
