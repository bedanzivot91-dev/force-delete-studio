using Avalonia.Headless.XUnit;
using NPVideoStudio.App.ViewModels;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// [AvaloniaFact] because the constructor starts a real Avalonia DispatcherTimer, same reason
/// WorkspaceViewModelTests needs it for PlayerViewModel.
///
/// These tests deliberately assert the *contract* in both directions rather than hardcoding either
/// outcome, because whether libvlc's native library is actually present is a real property of the
/// machine running the tests, not of the code: this project's Linux dev sandbox never has it (only the
/// win-x64 build bundles it, via VideoLAN.LibVLC.Windows), while the real windows-latest CI runner does.
/// A real CI failure came from exactly that gap - four tests written in the sandbox asserted
/// `IsAvailable == false` as if it were a fact about the code, and they failed on Windows precisely
/// because the player genuinely works there. Asserting both branches keeps the graceful-degradation
/// guarantee under test where it applies, without turning "the feature works here" into a failure.
/// </summary>
public class RealPreviewViewModelTests
{
    [AvaloniaFact]
    public void Construction_EitherReportsUnavailableWithARealReason_OrIsGenuinelyAvailable()
    {
        using var preview = new RealPreviewViewModel();

        if (preview.IsAvailable)
        {
            // Native libvlc really is present (a real Windows machine) - then there is nothing to
            // explain, and the view model must not be carrying a stale "unavailable" excuse.
            Assert.True(string.IsNullOrWhiteSpace(preview.UnavailableReason));
        }
        else
        {
            // No native libvlc - construction must degrade gracefully with a real, non-null reason
            // instead of throwing and taking the whole workspace down with it.
            Assert.False(string.IsNullOrWhiteSpace(preview.UnavailableReason));
        }
    }

    [AvaloniaFact]
    public void LoadAndPlay_MissingFile_NeverThrowsAndNeverClaimsToHaveLoadedIt()
    {
        using var preview = new RealPreviewViewModel();

        // The real point of this test: a nonexistent path must not throw, whether or not the player
        // itself is available. Asserting HasLoadedFile stays false is valid in both environments -
        // even a fully working libvlc has nothing to load here.
        preview.LoadAndPlay("/tmp/does-not-exist.mp4");

        Assert.False(preview.HasLoadedFile);
    }
}
