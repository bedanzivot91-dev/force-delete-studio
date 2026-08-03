using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Domain;
using Xunit;

namespace NPVideoStudio.UnitTests;

public class TemplateGalleryViewModelTests
{
    [Fact]
    public void Templates_ReturnsAllBuiltInTemplates()
    {
        var vm = new TemplateGalleryViewModel();

        Assert.Equal(ProjectTemplate.BuiltIn, vm.Templates);
        Assert.True(vm.Templates.Count >= 3);
    }

    [Fact]
    public void SelectTemplateCommand_RaisesTemplateSelectedWithThatTemplate()
    {
        var vm = new TemplateGalleryViewModel();
        var template = vm.Templates[1];
        ProjectTemplate? selected = null;
        vm.TemplateSelected += t => selected = t;

        vm.SelectTemplateCommand.Execute(template);

        Assert.Same(template, selected);
    }
}
