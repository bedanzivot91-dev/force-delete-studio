using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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

        // Point the surface at the decoded frames as soon as the view model has some. Done here rather
        // than through a binding because the surface owns a bitmap and a paint timer - it is a control to
        // hand a buffer to, not a value to display. DataContextChanged rather than the constructor
        // because this view is reused as the workspace view model is swapped.
        DataContextChanged += (_, _) => SubscribeToRealPreviewFrames();
        SubscribeToRealPreviewFrames();
        WireZoomControls();

        // Esc leaves full screen, the way every player does it.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && this.GetVisualRoot() is Window { WindowState: WindowState.FullScreen })
            {
                ToggleFullScreen();
            }
        }, RoutingStrategies.Tunnel);
    }

    private RealPreviewViewModel? _subscribedPreview;

    private void SubscribeToRealPreviewFrames()
    {
        if (DataContext is not WorkspaceViewModel workspace || ReferenceEquals(_subscribedPreview, workspace.RealPreview))
        {
            return;
        }

        if (_subscribedPreview is not null)
        {
            _subscribedPreview.FramesReady -= OnRealPreviewFramesReady;
        }

        _subscribedPreview = workspace.RealPreview;
        _subscribedPreview.FramesReady += OnRealPreviewFramesReady;
    }

    private void OnRealPreviewFramesReady(NPVideoStudio.App.Services.VlcVideoFrameBuffer frames) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => PlayerSurface.Attach(frames));

    // --- Making the picture bigger, smaller and movable ---------------------------------------------
    // "Ne moze da se poveca video" was literally true: the old panel drew into a fixed 220px box with a
    // native window on top of it, so there was nothing to zoom and nothing to click. Now the picture is
    // an ordinary painted control, so these are just numbers on it.

    private void WireZoomControls()
    {
        ZoomInButton.Click += (_, _) => StepZoom(1.25);
        ZoomOutButton.Click += (_, _) => StepZoom(1 / 1.25);
        ZoomFitButton.Click += (_, _) => { PlayerSurface.ResetView(); UpdateZoomLabel(); };
        FullScreenButton.Click += (_, _) => ToggleFullScreen();

        PlayerSurface.PointerWheelChanged += (_, _) => UpdateZoomLabel();
        PlayerSurface.DoubleTapped += (_, _) => ToggleFullScreen();

        UpdateZoomLabel();
    }

    private void StepZoom(double factor)
    {
        // Zoom about the middle of the panel, which is what a button press means - the wheel zooms about
        // the pointer instead, handled inside the surface.
        PlayerSurface.ZoomAt(
            new Avalonia.Point(PlayerSurface.Bounds.Width / 2, PlayerSurface.Bounds.Height / 2),
            factor);

        UpdateZoomLabel();
    }

    private void UpdateZoomLabel() =>
        ZoomLabel.Text = $"{PlayerSurface.Zoom * 100:0}%";

    /// <summary>Full screen for the picture. Uses the hosting window rather than a second player: a
    /// separate window is how this app ended up looking like it had several players.</summary>
    private void ToggleFullScreen()
    {
        if (this.GetVisualRoot() is not Window window)
        {
            return;
        }

        window.WindowState = window.WindowState == WindowState.FullScreen
            ? WindowState.Normal
            : WindowState.FullScreen;
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

        // Clicking a clip selects it, so the keyboard has something to act on.
        if (DataContext is WorkspaceViewModel vm)
        {
            vm.Timeline.SelectedClipId = clip.Clip.Id;
        }
    }

    /// <summary>Finds the track lane under the pointer, so a clip dropped on another lane lands there.
    /// Walks up from whatever was hit to the nearest element carrying a track ViewModel - the lane's own
    /// Canvas, the clips on it and the border around them all resolve to the same track that way.</summary>
    private static TimelineTrackItemViewModel? FindTrackUnderPointer(object? source)
    {
        var current = source as Visual;
        while (current is not null)
        {
            if (current is Control { DataContext: TimelineTrackItemViewModel track })
            {
                return track;
            }

            current = current.GetVisualParent();
        }

        return null;
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

        var targetTrack = FindTrackUnderPointer(e.Source);

        if (targetTrack is not null && targetTrack.Track.Id != clip.TrackId)
        {
            if (!viewModel.Timeline.MoveClipToTrack(clip.Clip.Id, targetTrack.Track.Id, newStart))
            {
                viewModel.StatusMessage =
                    "Klip ne može na tu traku (zaključana je, ili je druge vrste - video na audio traku i sl.).";
                e.Handled = true;
                return;
            }

            viewModel.StatusMessage = $"Klip premešten na traku „{targetTrack.DisplayName}“.";
        }
        else
        {
            viewModel.Timeline.MoveClipTo(clip.Clip.Id, newStart);
        }

        viewModel.Timeline.SelectedClipId = clip.Clip.Id;
        e.Handled = true;
    }

    /// <summary>
    /// The editing shortcuts every video editor has and this one had none of: space to play/pause,
    /// arrows to step a frame, Ctrl+Z/Ctrl+Y to undo/redo, Ctrl+S to save.
    ///
    /// Delete removes the selected clip and S splits it at the playhead - both now real, since clicking a
    /// clip in the visual lane selects it.
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

            case Key.S:
                viewModel.Timeline.SelectedClip?.SplitAtPlayheadCommand.Execute(null);
                break;

            case Key.Delete:
                viewModel.Timeline.SelectedClip?.DeleteCommand.Execute(null);
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
