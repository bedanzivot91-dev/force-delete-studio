using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NPVideoStudio.App.Services;
using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using Serilog;
using Xunit;

namespace NPVideoStudio.UnitTests;

public class DataSafetyRegressionTests
{
    [Fact]
    public void SongDeleteOriginal_FirstClickAndCancelNeverExecuteDestructiveCommand()
    {
        var destructiveCalls = 0;
        var item = new SongLibraryItemViewModel(
            new SongLibraryEntry { Title = "Original", OriginalAudioPath = "original.mp3" },
            new RelayCommand(() => { }),
            new RelayCommand(() => { }),
            new RelayCommand(() => destructiveCalls++));

        item.DeleteRecordAndFileCommand.Execute(null);

        Assert.True(item.IsDeleteFileConfirmationVisible);
        Assert.Equal(0, destructiveCalls);

        item.CancelDeleteRecordAndFileCommand.Execute(null);

        Assert.False(item.IsDeleteFileConfirmationVisible);
        Assert.Equal(0, destructiveCalls);
    }

    [Fact]
    public void SongDeleteOriginal_ExplicitConfirmationExecutesExactlyOnce()
    {
        var destructiveCalls = 0;
        var item = new SongLibraryItemViewModel(
            new SongLibraryEntry { Title = "Original", OriginalAudioPath = "original.mp3" },
            new RelayCommand(() => { }),
            new RelayCommand(() => { }),
            new RelayCommand(() => destructiveCalls++));

        item.DeleteRecordAndFileCommand.Execute(null);
        item.ConfirmDeleteRecordAndFileCommand.Execute(null);

        Assert.Equal(1, destructiveCalls);
    }

    [Fact]
    public async Task ShowStartScreenAsync_ForcesAutosaveBeforeCurrentProjectIsCleared()
    {
        var autoSave = new RecordingAutoSaveService();
        var recent = new EmptyRecentProjectsService();
        var start = new StartScreenViewModel(
            recent,
            new NoopProjectRepository(),
            autoSave,
            new EmptyStorageService(),
            new LoggerConfiguration().CreateLogger());

        var services = new ServiceCollection();
        services.AddSingleton(start);
        var vm = new MainWindowViewModel(services.BuildServiceProvider(), autoSave)
        {
            CurrentProject = new Project { Name = "Nesačuvane izmene" }
        };

        await vm.ShowStartScreenAsync();

        Assert.Equal(1, autoSave.TriggerNowCalls);
        Assert.Null(vm.CurrentProject);
        Assert.Same(start, vm.CurrentPage);
    }

    private sealed class RecordingAutoSaveService : IAutoSaveService
    {
        public int TriggerNowCalls { get; private set; }
        public void Start(Func<Project?> getCurrentProject) { }
        public void Stop() { }
        public Task TriggerNowAsync(CancellationToken cancellationToken = default)
        {
            TriggerNowCalls++;
            return Task.CompletedTask;
        }
        public Task<string?> FindRecoverableAutoSaveAsync(string projectFilePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
        public Task MarkCleanShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EmptyRecentProjectsService : IRecentProjectsService
    {
        public Task<IReadOnlyList<RecentProjectEntry>> GetRecentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RecentProjectEntry>>(Array.Empty<RecentProjectEntry>());
        public Task RegisterOpenedAsync(Project project, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveAsync(string projectFilePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopProjectRepository : IProjectRepository
    {
        public Task<Project> LoadAsync(string projectFilePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task SaveAsync(Project project, string projectFilePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task BackupAsync(string projectFilePath, int maxBackups = 10, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
