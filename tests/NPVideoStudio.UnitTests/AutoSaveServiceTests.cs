using NPVideoStudio.Domain;
using NPVideoStudio.Infrastructure.Persistence;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// AutoSaveService intentionally stores autosave slots under the real per-user AppData folder (not an
/// injectable path) since that's where a real crash recovery needs to look. Tests use unique fake
/// project paths so their autosave files never collide with each other or a real user's data, and
/// clean up after themselves.
/// </summary>
public class AutoSaveServiceTests : IDisposable
{
    private readonly List<string> _fakeProjectPaths = new();
    private readonly ProjectRepository _projectRepository = new();
    private readonly SettingsService _settingsService;
    private readonly AutoSaveService _autoSaveService;

    public AutoSaveServiceTests()
    {
        var tempSettingsDir = Path.Combine(Path.GetTempPath(), $"npvs_autosave_settings_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempSettingsDir);
        _settingsService = new SettingsService(Path.Combine(tempSettingsDir, "settings.json"));
        _settingsService.LoadAsync().GetAwaiter().GetResult();
        _autoSaveService = new AutoSaveService(_projectRepository, _settingsService);
    }

    public void Dispose()
    {
        _autoSaveService.Stop();
        foreach (var fakePath in _fakeProjectPaths)
        {
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(fakePath))));
            var autoSavePath = Path.Combine(AppSettings.AutoSaveFolder(), $"{hash}.npvsproject");
            if (File.Exists(autoSavePath))
            {
                File.Delete(autoSavePath);
            }
        }
    }

    private string NewFakeProjectPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"npvs_fake_project_{Guid.NewGuid():N}", "project.npvsproject");
        _fakeProjectPaths.Add(path);
        return path;
    }

    [Fact]
    public async Task FindRecoverableAutoSaveAsync_ReturnsNull_WhenNoAutoSaveExists()
    {
        var fakePath = NewFakeProjectPath();

        var result = await _autoSaveService.FindRecoverableAutoSaveAsync(fakePath);

        Assert.Null(result);
    }

    [Fact]
    public async Task TriggerNowAsync_ThenFindRecoverable_FindsNewerAutoSaveWhenOriginalMissing()
    {
        var fakePath = NewFakeProjectPath();
        var project = new Project { Name = "Autosave test", ProjectFilePath = fakePath };

        _autoSaveService.Start(() => project);
        await _autoSaveService.TriggerNowAsync();

        var recoverable = await _autoSaveService.FindRecoverableAutoSaveAsync(fakePath);

        Assert.NotNull(recoverable);
        Assert.True(File.Exists(recoverable));
    }

    [Fact]
    public async Task MarkCleanShutdownAsync_WritesMarkerFileWithoutThrowing()
    {
        await _autoSaveService.MarkCleanShutdownAsync();
        Assert.True(File.Exists(Path.Combine(AppSettings.AppDataRoot(), "clean_shutdown.marker")));
    }

    /// <summary>
    /// Regression test for a real CI failure that is also reachable by a user launching the app twice:
    /// the clean-shutdown marker lives at one fixed per-user path, and Start() used to delete it with an
    /// unguarded File.Delete - so if anything else held the file at that instant, the IOException came
    /// straight out of the MainWindowViewModel constructor and the app simply did not open.
    /// </summary>
    [Fact]
    public void Start_WhileTheShutdownMarkerIsHeldOpenByAnotherProcess_DoesNotThrow()
    {
        var markerPath = Path.Combine(AppSettings.AppDataRoot(), "clean_shutdown.marker");
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        File.WriteAllText(markerPath, "x");

        // Holding it open with no sharing is exactly what the losing instance runs into.
        using var holdOpen = new FileStream(markerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var exception = Record.Exception(() => _autoSaveService.Start(() => null));

        Assert.Null(exception);
        _autoSaveService.Stop();
    }
}
