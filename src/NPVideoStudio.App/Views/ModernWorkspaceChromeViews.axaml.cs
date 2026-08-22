using Avalonia.Controls;
using Avalonia.VisualTree;

namespace NPVideoStudio.App.Views;

public partial class ModernWorkspaceHeaderView : UserControl
{
    public ModernWorkspaceHeaderView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
            this.GetVisualAncestors().OfType<WorkspaceView>().FirstOrDefault()?.InstallModernSecondaryChrome();
    }
}

public partial class ModernWorkspaceCommandBarView : UserControl
{
    public ModernWorkspaceCommandBarView() => InitializeComponent();
}

public partial class ModernInspectorView : UserControl
{
    public ModernInspectorView() => InitializeComponent();
}

public partial class ModernMediaLibraryView : UserControl
{
    public ModernMediaLibraryView() => InitializeComponent();
}

public partial class ModernTimelineView : UserControl
{
    public ModernTimelineView() => InitializeComponent();
}
