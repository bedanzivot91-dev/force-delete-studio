using NPVideoStudio.App.Services;
using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using Serilog;
using Xunit;

namespace NPVideoStudio.UnitTests;

public class ProjectFileAssociationTests
{
    [Fact]
    public void ResolveStartupProjectPath_FindsNpvsProjectCaseInsensitively()
    {
        var result = global::NPVideoStudio.App.App.ResolveStartupProjectPath(new[] { "--some-option", @"C:\Video\Moj Projekat.NPVSPROJECT" });

        Assert.Equal(@"C:\Video\Moj Projekat.NPVSPROJECT", result);
    }

    [Fact]
    public void ResolveStartupProjectPath_IgnoresUnrelatedArguments()
    {
        Assert.Null(global::NPVideoStudio.App.App.ResolveStartupProjectPath(new[] { "--debug", @"C:\Video\clip.mp4" }));
    }

    [Fact]
    public async Task OpenProjectPathAsync_UsesRepositoryRegistersRecentAndRaisesProjectOpened()
    {
        var loaded = new Project { Name = "Dvoklik projekat", ProjectFilePath = @"C:\Projects\dvoklik.npvsproject" };
        var repository = new RecordingProjectRepository(loaded);
        var recent = new RecordingRecentProjectsService();
        var start = new StartScreenViewModel(
            recent,
            repository,
            new EmptyAutoSaveService(),
            new EmptyStorageService(),
            new LoggerConfiguration().CreateLogger());

        Project? opened = null;
        start.ProjectOpened += project => opened = project;

        await start.OpenProjectPathAsync(loaded.ProjectFilePath!);

        Assert.Equal(loaded.ProjectFilePath, repository.LastLoadedPath);
        Assert.Same(loaded, recent.LastRegisteredProject);
        Assert.Same(loaded, opened);
    }

    private sealed class RecordingProjectRepository(Project project) : IProjectRepository
    {
        public string? LastLoadedPath { get; private set; }
        public Task<Project> LoadAsync(string projectFilePath, CancellationToken cancellationToken = default)
        {
            LastLoadedPath = projectFilePath;
            return Task.FromResult(project);
        }
        public Task SaveAsync(Project projectToSave, string projectFilePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task BackupAsync(string projectFilePath, int maxBackups = 10, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingRecentProjectsService : IRecentProjectsService
    {
        public Project? LastRegisteredProject { get; private set; }
        public Task<IReadOnlyList<RecentProjectEntry>> GetRecentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RecentProjectEntry>>(Array.Empty<RecentProjectEntry>());
        public Task RegisterOpenedAsync(Project project, CancellationToken cancellationToken = default)
        {
            LastRegisteredProject = project;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(string projectFilePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EmptyAutoSaveService : IAutoSaveService
    {
        public void Start(Func<Project?> getCurrentProject) { }
        public void Stop() { }
        public Task TriggerNowAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> FindRecoverableAutoSaveAsync(string projectFilePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
        public Task MarkCleanShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EmptyStorageService : IStorageService
    {
        public Task<IReadOnlyList<string>> PickFilesAsync(string title, IReadOnlyList<(string Name, string[] Extensions)> filters, bool allowMultiple) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, IReadOnlyList<(string Name, string[] Extensions)> filters) =>
            Task.FromResult<string?>(null);
    }
}
