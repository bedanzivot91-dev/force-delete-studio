namespace NPVideoStudio.App.Views;

public partial class WorkspaceView
{
    /// <summary>
    /// Completes the Studio 2026 workspace replacement after the modern header is attached.
    /// The host Borders keep their existing DataContexts, so these views reuse the same production
    /// view models, commands, drag routing and undo/redo engine rather than creating parallel UI state.
    /// </summary>
    internal void InstallModernSecondaryChrome()
    {
        if (MediaLibraryPanel.Child is not ModernMediaLibraryView)
        {
            MediaLibraryPanel.Child = new ModernMediaLibraryView();
        }

        if (TimelinePanel.Child is not ModernTimelineView)
        {
            TimelinePanel.Child = new ModernTimelineView();
        }
    }
}
