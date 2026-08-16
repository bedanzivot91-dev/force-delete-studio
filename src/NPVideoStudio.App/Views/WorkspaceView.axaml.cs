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

        AddHandler(PointerPressedEvent, OnLanePointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnLanePointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnLanePointerReleased, RoutingStrategies.Tunnel);
    }

    // --- Dragging a clip along the lane ------------------------------------------------------------
    // The timeline had no visual lane at all before this: clips could only be nudged 0.5s at a time with
    // buttons. Drag works on the clip's own Border (tagged with its ViewModel in the lane template), and
    // commits the new position through the timeline session on release, so the whole drag is ONE undo
    // step instead of one per pixel moved.

    private TimelineClipItemViewModel? _draggingClip;
    private double _dragStartX;
    private double _dragOriginalStartSeconds;
    private bool _dragMoved;

    private void OnLanePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Control { Tag: TimelineClipItemViewModel clip } control ||
            !control.Classes.Contains("cliplane"))
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _draggingClip = clip;
        _dragStartX = e.GetPosition(this).X;
        _dragOriginalStartSeconds = clip.StartSeconds;
        _dragMoved = false;
    }

    private void OnLanePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingClip is null)
        {
            return;
        }

        // A few pixels of slack so a plain click doesn't register as a drag and nudge the clip.
        if (Math.Abs(e.GetPosition(this).X - _dragStartX) > 3)
        {
            _dragMoved = true;
        }
    }

    private void OnLanePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var clip = _draggingClip;
        _draggingClip = null;

        if (clip is null || !_dragMoved || DataContext is not WorkspaceViewModel viewModel)
        {
            return;
        }

        var pixelsMoved = e.GetPosition(this).X - _dragStartX;
        var secondsMoved = pixelsMoved / Math.Max(1, clip.PixelsPerSecond);
        var newStart = Math.Max(0, _dragOriginalStartSeconds + secondsMoved);

        viewModel.Timeline.MoveClipTo(clip.Clip.Id, newStart);
        e.Handled = true;
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
