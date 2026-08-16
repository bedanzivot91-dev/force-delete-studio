using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NPVideoStudio.App.ViewModels;

namespace NPVideoStudio.App.Views;

public partial class WorkspaceView : UserControl
{
    public WorkspaceView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Tunnel, not bubble: a bubbling handler never sees the key when focus is inside a TextBox/Slider,
        // which is most of this screen. Typing in a text box must still work normally, so text-entry
        // controls are excluded below rather than the shortcut being dropped everywhere.
        AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// The editing shortcuts every video editor has and this one had none of: space to play/pause,
    /// arrows to step a frame, Ctrl+Z/Ctrl+Y to undo/redo, Ctrl+S to save.
    ///
    /// Deliberately no Delete/split shortcut yet: this timeline has no concept of a "selected clip" (clips
    /// are acted on through their own per-clip buttons), so those keys would have nothing to act on.
    /// Shipping a key that silently does nothing is worse than not binding it.
    /// </summary>
    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel viewModel)
        {
            return;
        }

        // While the user is typing a caption or dragging a slider, these keys belong to that control.
        if (e.Source is TextBox or Slider or NumericUpDown or ComboBox)
        {
            return;
        }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        switch (e.Key)
        {
            case Key.Space:
                if (viewModel.Player.IsPlaying)
                {
                    viewModel.Player.PauseCommand.Execute(null);
                }
                else
                {
                    viewModel.Player.PlayCommand.Execute(null);
                }
                break;

            case Key.Left:
                viewModel.Player.StepBackwardCommand.Execute(null);
                break;

            case Key.Right:
                viewModel.Player.StepForwardCommand.Execute(null);
                break;

            case Key.Z when ctrl:
                viewModel.Timeline.UndoCommand.Execute(null);
                break;

            case Key.Y when ctrl:
                viewModel.Timeline.RedoCommand.Execute(null);
                break;

            case Key.S when ctrl:
                viewModel.SaveProjectCommand.Execute(null);
                break;

            default:
                return; // not ours - let it through untouched
        }

        e.Handled = true;
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
