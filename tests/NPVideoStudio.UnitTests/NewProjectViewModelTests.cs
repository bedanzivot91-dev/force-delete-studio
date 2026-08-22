using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using NPVideoStudio.Infrastructure.Persistence;
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

public sealed class NewProjectViewModelTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"npvs_newproject_test_{Guid.NewGuid():N}");
    private readonly FakeProjectRepository _repository = new();

    public NewProjectViewModelTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private NewProjectViewModel Create(ProjectTemplate? template = null, UserTemplate? userTemplate = null) => new(
        _repository, new FakeRecentProjectsService(), new FakeSettingsService(_tempDir),
        new LoggerConfiguration().CreateLogger(), prefillPlatform: null, template: template, userTemplate: userTemplate);

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
    public async Task CreateAsync_WithBuiltInTemplate_AddsExactlyTheTemplatesStarterTracks()
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
    public async Task CreateAsync_WithUserTemplate_RestoresExactCustomFormatAndStarterTracks()
    {
        var userTemplate = new UserTemplate
        {
            Name = "Moj 29.97 Shorts",
            Description = "test",
            Width = 1080,
            Height = 1920,
            FrameRate = FrameRatePreset.Custom,
            Fps = 29.97,
            StarterTrackKinds =
            {
                TimelineTrackKind.Video,
                TimelineTrackKind.Audio,
                TimelineTrackKind.Text,
                TimelineTrackKind.Caption
            }
        };
        var vm = Create(userTemplate: userTemplate);
        Project? created = null;
        vm.ProjectCreated += p => created = p;

        Assert.Equal(ResolutionPreset.Custom, vm.SelectedResolution);
        Assert.Equal(1080, vm.CustomWidth);
        Assert.Equal(1920, vm.CustomHeight);
        Assert.Equal(FrameRatePreset.Custom, vm.SelectedFrameRate);
        Assert.Equal(29.97, vm.CustomFps, 6);

        await vm.CreateCommand.ExecuteAsync(null);

        Assert.NotNull(created);
        Assert.Equal(1080, created!.Format.Width);
        Assert.Equal(1920, created.Format.Height);
        Assert.Equal(FrameRatePreset.Custom, created.Format.FrameRate);
        Assert.Equal(29.97, created.Format.Fps, 6);
        Assert.Equal(userTemplate.StarterTrackKinds, created.Timeline.Tracks.Select(t => t.Kind));
    }

    [Fact]
    public void TemplateInfoLabel_NoTemplate_IsNull()
    {
        Assert.Null(Create().TemplateInfoLabel);
    }

    [Fact]
    public void TemplateInfoLabel_EmptyBuiltInTemplate_IsNull()
    {
        var emptyTemplate = ProjectTemplate.BuiltIn.Single(t => t.StarterTrackKinds.Count == 0);
        Assert.Null(Create(emptyTemplate).TemplateInfoLabel);
    }

    [Fact]
    public void TemplateInfoLabel_NonEmptyBuiltInTemplate_MentionsTemplateName()
    {
        var template = ProjectTemplate.BuiltIn.Single(t => t.Name == "Govor sa titlovima");
        Assert.Contains(template.Name, Create(template).TemplateInfoLabel);
    }

    [Fact]
    public void TemplateInfoLabel_UserTemplate_MentionsExactSavedFormat()
    {
        var template = new UserTemplate
        {
            Name = "Custom",
            Width = 1080,
            Height = 1920,
            FrameRate = FrameRatePreset.Custom,
            Fps = 23.976,
            StarterTrackKinds = { TimelineTrackKind.Video }
        };

        var label = Create(userTemplate: template).TemplateInfoLabel;

        Assert.Contains("Custom", label);
        Assert.Contains("1080", label);
        Assert.Contains("1920", label);
        Assert.Contains("23.98", label);
    }
}
