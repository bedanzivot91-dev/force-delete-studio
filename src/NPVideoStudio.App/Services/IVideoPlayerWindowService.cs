namespace NPVideoStudio.App.Services;

/// <summary>
/// Opens the standalone player window (<c>Views/PlayerWindow</c>) for one media file.
///
/// Exists as a service, mirroring <see cref="IStorageService"/>, for the same two reasons: ViewModels
/// must not construct Windows directly, and the headless test host has no real window to parent to - a
/// fake implementation lets the workspace's play command be tested without a desktop.
/// </summary>
public interface IVideoPlayerWindowService
{
    /// <summary>Opens the player for <paramref name="filePath"/>. Returns false when no window could be
    /// opened (no desktop session, e.g. under the headless test host), so callers can report that
    /// honestly instead of assuming playback started.</summary>
    bool OpenPlayer(string filePath);
}
