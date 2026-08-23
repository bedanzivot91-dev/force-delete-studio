using Avalonia.Controls;

namespace NPVideoStudio.App.Views;

public partial class ModernWorkspaceHeaderView : UserControl
{
    public ModernWorkspaceHeaderView() => InitializeComponent();
}

public partial class ModernWorkspaceCommandBarView : UserControl
{
    public ModernWorkspaceCommandBarView() => InitializeComponent();
}

public partial class ModernInspectorView : UserControl
{
    public ModernInspectorView() => InitializeComponent();
    private void OnKeyframeGraphPointRequested(object? sender, KeyframeGraphPointEventArgs e)
    {
        if (DataContext is ViewModels.TimelineClipItemViewModel clip) clip.AddKeyframeFromGraph(e.NormalizedX, e.NormalizedY);
    }
}

public partial class ModernMediaLibraryView : UserControl
{
    public ModernMediaLibraryView() => InitializeComponent();
}

public partial class ModernTimelineView : UserControl
{
    public ModernTimelineView() => InitializeComponent();
}
