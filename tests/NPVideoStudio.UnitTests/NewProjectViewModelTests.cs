using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using Serilog;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Fake so template/new-project tests don't depend on real user settings persistence.</summary>
public sealed class FakeSettingsService : ISettingsService
{
    public AppSettings Current { get; }

    public FakeSettingsService(string projectsFolder) => Current = new AppSettings { ProjectsFolder = projectsFolder };

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ResetToDefaultsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>Fake so template/new-project tests don't need real recent-project persistence.</summary>
public sealed class FakeRecentProjectsService : IRecentProjectsService
{
    public int RegisterOpenedCallCount { get; private set; }

    public Task<IReadOnlyList<RecentProjectEntry>> GetRecentAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RecentProjectEntry>>(Array.Empty<RecentProjectEntry>());

    public Task RegisterOpenedAsync(Project project, CancellationToken cancellationToken = default)
    {
        RegisterOpenedCallCount++;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string projectFilePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// Covers the Phase 10 addition to an existing, already-shipped screen: picking a
/// <see cref="ProjectTemplate"/> pre-populates the new project's timeline with that template's starter
/// tracks, on top of the plain "Novi projekat" flow which stays unchanged (no template = no tracks).
/// </summary>
public sealed class NewProjectViewModelTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"npvs_newproject_test_{Guid.NewGuid():N}");
    private readonly FakeProjectRepository _repository = new();

    public NewProjectViewModelTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private NewProjectViewModel Create(ProjectTemplate? template = null) => new(
        _repository, new FakeRecentProjectsService(), new FakeSettingsService(_tempDir),
        new LoggerConfiguration().CreateLogger(), prefillPlatform: null, template: template);

    [Fact]
    public async Task CreateAsync_NoTemplate_ProducesProjectWithEmptyTimeline()
    {
        var vm = Create();
        Project? created = null;
        vm.ProjectCreated += p => created = p;

        await vm.CreateCommand.ExecuteAsync(null);

        Assert.NotNull(created);
        Assert.Empty(created!.Timeline.Tracks);
    }

    [Fact]
    public async Task CreateAsync_WithTemplate_AddsExactlyTheTemplatesStarterTracks()
    {
        var template = ProjectTemplate.BuiltIn.Single(t => t.Name == "Muzički spot");
        var vm = Create(template);
        Project? created = null;
        vm.ProjectCreated += p => created = p;

        await vm.CreateCommand.ExecuteAsync(null);

        Assert.NotNull(created);
        Assert.Equal(template.StarterTrackKinds, created!.Timeline.Tracks.Select(t => t.Kind));
    }

    [Fact]
    public void TemplateInfoLabel_NoTemplate_IsNull()
    {
        Assert.Null(Create().TemplateInfoLabel);
    }

    [Fact]
    public void TemplateInfoLabel_EmptyTemplate_IsNull()
    {
        var emptyTemplate = ProjectTemplate.BuiltIn.Single(t => t.StarterTrackKinds.Count == 0);
        Assert.Null(Create(emptyTemplate).TemplateInfoLabel);
    }

    [Fact]
    public void TemplateInfoLabel_NonEmptyTemplate_MentionsTemplateName()
    {
        var template = ProjectTemplate.BuiltIn.Single(t => t.Name == "Govor sa titlovima");
        Assert.Contains(template.Name, Create(template).TemplateInfoLabel);
    }
}
