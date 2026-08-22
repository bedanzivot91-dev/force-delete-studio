using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Domain;
using NPVideoStudio.Infrastructure.Persistence;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class SaveTemplateViewModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"npvs-save-template-{Guid.NewGuid():N}");
    private readonly UserTemplateRepository _repository;

    public SaveTemplateViewModelTests()
    {
        _repository = new UserTemplateRepository(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static Project ProjectWithCustomFormat()
    {
        var project = new Project { Name = "Aktivni projekat" };
        project.Format.Width = 1080;
        project.Format.Height = 1920;
        project.Format.FrameRate = FrameRatePreset.Custom;
        project.Format.Fps = 29.97;
        project.Timeline.Tracks.Add(new TimelineTrack { Kind = TimelineTrackKind.Video });
        project.Timeline.Tracks.Add(new TimelineTrack { Kind = TimelineTrackKind.Text });
        return project;
    }

    [Fact]
    public void SaveCommand_PersistsCurrentProjectAsReusableTemplate()
    {
        var vm = new SaveTemplateViewModel(ProjectWithCustomFormat(), _repository)
        {
            TemplateName = "Moj Shorts",
            Description = "Vertikalni format"
        };

        vm.SaveCommand.Execute(null);

        var saved = Assert.Single(_repository.LoadAll());
        Assert.Equal("Moj Shorts", saved.Name);
        Assert.Equal("Vertikalni format", saved.Description);
        Assert.Equal(1080, saved.Width);
        Assert.Equal(1920, saved.Height);
        Assert.Equal(29.97, saved.Fps, 6);
        Assert.Equal(new[] { TimelineTrackKind.Video, TimelineTrackKind.Text }, saved.StarterTrackKinds);
        Assert.False(vm.IsOverwriteConfirmationVisible);
    }

    [Fact]
    public void SaveCommand_DuplicateNameDoesNotOverwriteUntilExplicitConfirmation()
    {
        _repository.Save(new UserTemplate
        {
            Name = "Postojeći",
            Description = "staro",
            Width = 1920,
            Height = 1080,
            Fps = 30
        });
        var vm = new SaveTemplateViewModel(ProjectWithCustomFormat(), _repository)
        {
            TemplateName = "Postojeći",
            Description = "novo"
        };

        vm.SaveCommand.Execute(null);

        Assert.True(vm.IsOverwriteConfirmationVisible);
        Assert.Equal("staro", Assert.Single(_repository.LoadAll()).Description);

        vm.ConfirmOverwriteCommand.Execute(null);

        var replaced = Assert.Single(_repository.LoadAll());
        Assert.Equal("novo", replaced.Description);
        Assert.Equal(1080, replaced.Width);
        Assert.Equal(1920, replaced.Height);
        Assert.False(vm.IsOverwriteConfirmationVisible);
    }

    [Fact]
    public void SaveCommand_BlankNameIsRejectedWithoutCreatingAFile()
    {
        var vm = new SaveTemplateViewModel(ProjectWithCustomFormat(), _repository)
        {
            TemplateName = "   "
        };

        vm.SaveCommand.Execute(null);

        Assert.Empty(_repository.LoadAll());
        Assert.Contains("ime", vm.Message, StringComparison.OrdinalIgnoreCase);
    }
}
