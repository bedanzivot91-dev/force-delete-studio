using Avalonia.Controls;
using Avalonia.Input;
using NPVideoStudio.App.ViewModels;

namespace NPVideoStudio.App.Views;

public partial class WorkspaceView : UserControl
{
    public WorkspaceView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel viewModel)
        {
            return;
        }

        var files = e.Data.GetFiles();
        if (files is null)
        {
            return;
        }

        var paths = files.Select(f => f.Path.LocalPath).ToList();
        await viewModel.ImportFilesAsync(paths);
    }
}
