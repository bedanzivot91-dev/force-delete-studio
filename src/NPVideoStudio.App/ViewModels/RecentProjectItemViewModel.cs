using System.Windows.Input;
using NPVideoStudio.Domain;

namespace NPVideoStudio.App.ViewModels;

public sealed class RecentProjectItemViewModel : ViewModelBase
{
    public RecentProjectEntry Entry { get; }

    public string ProjectName => Entry.ProjectName;
    public string AspectRatioLabel => Entry.AspectRatioLabel;
    public bool IsMissing => Entry.IsMissing;
    public string ProjectFilePath => Entry.ProjectFilePath;

    public ICommand OpenCommand { get; }
    public ICommand RemoveCommand { get; }

    public RecentProjectItemViewModel(RecentProjectEntry entry, ICommand openCommand, ICommand removeCommand)
    {
        Entry = entry;
        OpenCommand = openCommand;
        RemoveCommand = removeCommand;
    }
}
