namespace NPVideoStudio.App.Services;

/// <summary>
/// The text tools the player window offers for the file it is playing. Supplied by the workspace, which
/// already owns the real transcription/lyric pipelines - the player window itself stays a player and
/// never grows its own copy of that logic.
///
/// Every action is optional (null = the button is hidden), so the player can also be opened from a place
/// that has no project to add captions to.
/// </summary>
/// <param name="AddCaptionsFromVideo">Transcribes the spoken/sung audio and lands it on the caption track.</param>
/// <param name="AddKaraokeCaptions">Same, but word by word, so words appear one at a time in time with the audio.</param>
/// <param name="FindLyricsInSong">Opens the "find this phrase in the song" tool for the same file.</param>
public sealed record PlayerTextActions(
    Func<Task>? AddCaptionsFromVideo = null,
    Func<Task>? AddKaraokeCaptions = null,
    Func<Task>? FindLyricsInSong = null);

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
    bool OpenPlayer(string filePath, PlayerTextActions? textActions = null);
}
