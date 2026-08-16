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

    /// <summary>
    /// The real, environment-independent guarantee here is that <see cref="RealPreviewViewModel.LoadAndPlay"/>
    /// never throws - not that it refuses the file. A first attempt at this test asserted
    /// <c>HasLoadedFile == false</c> for a nonexistent path on the assumption that "even a working libvlc
    /// has nothing to load here", and that assumption was wrong on the real Windows runner: libvlc opens
    /// media asynchronously, so handing it a missing path fails later on VLC's own thread rather than
    /// synchronously, and <c>LoadAndPlay</c> sets <c>HasLoadedFile</c> right after handing the media over.
    /// So the flag honestly means "a file was handed to the player", not "the file exists and decoded" -
    /// which is exactly what this now asserts, per environment.
    /// </summary>
    [AvaloniaFact]
    public void LoadAndPlay_MissingFile_NeverThrows()
    {
        using var preview = new RealPreviewViewModel();

        preview.LoadAndPlay("/tmp/does-not-exist.mp4");

        if (preview.IsAvailable)
        {
            // Player present: the path was accepted and handed to libvlc without throwing.
            Assert.True(preview.HasLoadedFile);
        }
        else
        {
            // No player: LoadAndPlay returns early and must not pretend anything was loaded.
            Assert.False(preview.HasLoadedFile);
        }
    }
}
