using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Domain;
using NPVideoStudio.Infrastructure.Persistence;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class TemplateGalleryViewModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"npvs-template-gallery-{Guid.NewGuid():N}");
    private readonly UserTemplateRepository _repository;

    public TemplateGalleryViewModelTests()
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

    private UserTemplate SaveUser(string name, double fps = 29.97)
    {
        var template = new UserTemplate
        {
            Name = name,
            Description = "Moj test šablon",
            Width = 1080,
            Height = 1920,
            FrameRate = FrameRatePreset.Custom,
            Fps = fps,
            StarterTrackKinds = { TimelineTrackKind.Video, TimelineTrackKind.Text }
        };
        _repository.Save(template);
        return template;
    }

    [Fact]
    public void Templates_ContainsAllBuiltInsAndRealUserTemplates()
    {
        SaveUser("Moj Shorts");

        var vm = new TemplateGalleryViewModel(_repository);

        Assert.Equal(ProjectTemplate.BuiltIn.Count + 1, vm.Templates.Count);
        Assert.Equal(ProjectTemplate.BuiltIn.Count, vm.Templates.Count(t => !t.IsUserTemplate));
        var user = Assert.Single(vm.Templates.Where(t => t.IsUserTemplate));
        Assert.Equal("Moj Shorts", user.Name);
        Assert.Contains("29.97 fps", user.FormatLabel);
        Assert.Contains("1080", user.FormatLabel);
        Assert.Contains("1920", user.FormatLabel);
    }

    [Fact]
    public void SelectTemplateCommand_RaisesCorrectBuiltInAndUserEvents()
    {
        SaveUser("Moj Shorts");
        var vm = new TemplateGalleryViewModel(_repository);
        ProjectTemplate? builtInSelected = null;
        UserTemplate? userSelected = null;
        vm.BuiltInTemplateSelected += t => builtInSelected = t;
        vm.UserTemplateSelected += t => userSelected = t;

        var builtInItem = vm.Templates.First(t => !t.IsUserTemplate);
        vm.SelectTemplateCommand.Execute(builtInItem);
        Assert.Same(builtInItem.BuiltInTemplate, builtInSelected);

        var userItem = vm.Templates.Single(t => t.IsUserTemplate);
        vm.SelectTemplateCommand.Execute(userItem);
        Assert.Equal("Moj Shorts", userSelected?.Name);
        Assert.Equal(29.97, userSelected!.Fps, 6);
    }

    [Fact]
    public void RenameWorkflow_RequiresExplicitOverwrite_ThenPersistsChange()
    {
        SaveUser("Prvi");
        SaveUser("Drugi", 60);
        var vm = new TemplateGalleryViewModel(_repository);
        var first = vm.Templates.Single(t => t.IsUserTemplate && t.Name == "Prvi");

        vm.BeginRenameCommand.Execute(first);
        vm.RenameName = "Drugi";
        vm.ConfirmRenameCommand.Execute(null);

        Assert.True(vm.IsRenameOverwriteRequired);
        Assert.True(_repository.Exists("Prvi"));
        Assert.Equal(2, _repository.LoadAll().Count);

        vm.ConfirmRenameOverwriteCommand.Execute(null);

        Assert.False(_repository.Exists("Prvi"));
        var remaining = Assert.Single(_repository.LoadAll());
        Assert.Equal("Drugi", remaining.Name);
        Assert.Equal(29.97, remaining.Fps, 6);
    }

    [Fact]
    public void DeleteWorkflow_DoesNothingUntilConfirmation_ThenRemovesTemplate()
    {
        SaveUser("Za brisanje");
        var vm = new TemplateGalleryViewModel(_repository);
        var item = vm.Templates.Single(t => t.IsUserTemplate);

        vm.BeginDeleteCommand.Execute(item);
        Assert.True(vm.IsDeleteConfirmationVisible);
        Assert.True(_repository.Exists("Za brisanje"));

        vm.ConfirmDeleteCommand.Execute(null);

        Assert.False(_repository.Exists("Za brisanje"));
        Assert.DoesNotContain(vm.Templates, t => t.IsUserTemplate);
    }
}
