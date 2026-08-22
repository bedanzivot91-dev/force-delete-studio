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
/// machine running the tests, not of the code: this project's Linux dev sandbox only has it when VLC is
/// installed there, while the win-x64 build always bundles it via VideoLAN.LibVLC.Windows. A real CI
/// failure came from exactly that gap - four tests written in the sandbox asserted `IsAvailable == false`
/// as if it were a fact about the code, and they failed on Windows precisely because the player genuinely
/// works there.
/// </summary>
public class RealPreviewViewModelTests
{
    [AvaloniaFact]
    public void Construction_EitherReportsUnavailableWithARealReason_OrIsGenuinelyAvailable()
    {
        using var preview = new RealPreviewViewModel();

        if (preview.IsAvailable)
        {
            // Native libvlc really is present - then there is nothing to explain, and the view model
            // must not be carrying a stale "unavailable" excuse.
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
    /// The regression guard for the freeze/crash audit. This view model used to build a full LibVLC +
    /// MediaPlayer in its constructor, so simply opening a workspace left a second native player alive
    /// beside the player window's - and VideoLAN documents deadlocks on play and stop when one
    /// application holds several media players. Availability must be answerable without that.
    /// </summary>
    [AvaloniaFact]
    public void Construction_DoesNotCreateANativePlayer_EvenWhenPlaybackIsSupported()
    {
        using var preview = new RealPreviewViewModel();

        // Reading availability must not be what creates a player either.
        _ = preview.IsAvailable;

        Assert.False(preview.IsPlayerCreated);
        Assert.False(preview.HasLoadedFile);
    }

    /// <summary>
    /// Many workspaces over a session must not accumulate native players. Constructing a batch and
    /// checking none of them made one is the cheap, non-flaky way to assert that.
    /// </summary>
    [AvaloniaFact]
    public void ManyWorkspaces_DoNotAccumulateNativePlayers()
    {
        var previews = Enumerable.Range(0, 25).Select(_ => new RealPreviewViewModel()).ToList();

        try
        {
            Assert.All(previews, p => Assert.False(p.IsPlayerCreated));
        }
        finally
        {
            foreach (var preview in previews)
            {
                preview.Dispose();
            }
        }
    }

    /// <summary>
    /// A missing file must be refused rather than handed to libvlc. This is a deliberate behaviour
    /// change from the previous version, which passed any path straight through: libvlc opens media
    /// asynchronously, so a missing path failed later on VLC's own thread with nothing surfaced to the
    /// user, and HasLoadedFile was set to true regardless. Now the file is checked first, so the flag
    /// means what it says on every machine instead of meaning different things per environment.
    /// </summary>
    [AvaloniaFact]
    public async Task LoadAndPlay_MissingFile_NeverThrows_AndLoadsNothing()
    {
        using var preview = new RealPreviewViewModel();

        await preview.LoadAndPlayAsync(Path.Combine(Path.GetTempPath(), $"nema-{Guid.NewGuid():N}.mp4"));

        Assert.False(preview.HasLoadedFile);
    }

    [AvaloniaFact]
    public async Task Commands_AreSafeBeforeAnythingIsLoaded()
    {
        using var preview = new RealPreviewViewModel();

        // Every one of these runs against a session that does not exist yet. None may throw - a crash
        // here would take the whole workspace down.
        preview.TogglePlayPauseCommand.Execute(null);
        preview.StopCommand.Execute(null);
        preview.Volume = 40;
        preview.IsMuted = true;
        preview.CurrentTimeSeconds = 12;

        Assert.False(preview.IsPlaying);
        Assert.Equal(40, preview.Volume);

        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public void Dispose_IsIdempotent()
    {
        var preview = new RealPreviewViewModel();

        preview.Dispose();
        preview.Dispose();

        Assert.False(preview.IsPlayerCreated);
    }
}
