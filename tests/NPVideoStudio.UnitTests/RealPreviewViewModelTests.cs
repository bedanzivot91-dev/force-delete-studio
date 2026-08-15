using Avalonia.Headless.XUnit;
using NPVideoStudio.App.ViewModels;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// [AvaloniaFact] because the constructor starts a real Avalonia DispatcherTimer, same reason
/// WorkspaceViewModelTests needs it for PlayerViewModel.
/// </summary>
public class RealPreviewViewModelTests
{
    /// <summary>
    /// This project's own Linux dev sandbox never has libvlc's native library available (only the win-x64
    /// build bundles it, via VideoLAN.LibVLC.Windows) - construction must degrade gracefully with a real,
    /// non-null reason instead of throwing and taking the whole workspace down with it.
    /// </summary>
    [AvaloniaFact]
    public void Construction_NoNativeLibVlcOnThisMachine_ReportsUnavailableInsteadOfThrowing()
    {
        using var preview = new RealPreviewViewModel();

        Assert.False(preview.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(preview.UnavailableReason));
    }

    [AvaloniaFact]
    public void LoadAndPlay_WhenUnavailable_DoesNothingInsteadOfThrowing()
    {
        using var preview = new RealPreviewViewModel();

        preview.LoadAndPlay("/tmp/does-not-exist.mp4");

        Assert.False(preview.HasLoadedFile);
    }
}
