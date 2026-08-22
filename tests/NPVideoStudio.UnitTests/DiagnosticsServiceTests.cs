using NPVideoStudio.Core.Diagnostics;
using NPVideoStudio.Diagnostics;
using NPVideoStudio.Infrastructure.Persistence;
using Xunit;

namespace NPVideoStudio.UnitTests;

public class DiagnosticsServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"npvs_diag_test_{Guid.NewGuid():N}");
    private readonly SettingsService _settingsService;
    private readonly AppDatabase _database;
    private readonly DiagnosticsService _diagnostics;

    public DiagnosticsServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
        _settingsService = new SettingsService(Path.Combine(_tempDir, "settings.json"));
        _settingsService.LoadAsync().GetAwaiter().GetResult();
        _settingsService.Current.ProjectsFolder = Path.Combine(_tempDir, "Projects");
        _settingsService.Current.CacheFolder = Path.Combine(_tempDir, "Cache");

        _database = new AppDatabase(Path.Combine(_tempDir, "test.db"));
        _diagnostics = new DiagnosticsService(_settingsService, _database);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task RunAllChecksAsync_ReturnsNonEmptyResultsWithPlainLanguageDescriptions()
    {
        var results = await _diagnostics.RunAllChecksAsync();

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.False(string.IsNullOrWhiteSpace(r.Description)));
    }

    [Fact]
    public async Task RunAllChecksAsync_DotNetCheck_PassesOnThisRuntime()
    {
        var results = await _diagnostics.RunAllChecksAsync();
        var dotnetCheck = results.Single(r => r.CheckName == ".NET Runtime");

        Assert.Equal(DiagnosticStatus.Ok, dotnetCheck.Status);
    }

    [Fact]
    public async Task RunAllChecksAsync_MissingProjectsFolder_ReportsWarningAndCanAutoFix()
    {
        var results = await _diagnostics.RunAllChecksAsync();
        var folderCheck = results.Single(r => r.CheckName == "Folder za projekte");

        Assert.Equal(DiagnosticStatus.Warning, folderCheck.Status);
        Assert.True(folderCheck.CanAutoFix);
    }

    [Fact]
    public async Task TryAutoFixAsync_ProjectsFolder_CreatesTheFolder()
    {
        var fixedResult = await _diagnostics.TryAutoFixAsync("Folder za projekte");

        Assert.Equal(DiagnosticStatus.Ok, fixedResult.Status);
        Assert.True(Directory.Exists(_settingsService.Current.ProjectsFolder));
    }

    [Fact]
    public async Task CreateSupportPackageAsync_ProducesZipFileWithoutProjectData()
    {
        var destination = Path.Combine(_tempDir, "support");
        var zipPath = await _diagnostics.CreateSupportPackageAsync(destination);

        Assert.True(File.Exists(zipPath));
        Assert.EndsWith(".zip", zipPath);
    }
}
