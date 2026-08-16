using Avalonia.Controls;
using NPVideoStudio.App.Views;

namespace NPVideoStudio.App.Services;

public sealed class VideoPlayerWindowService : IVideoPlayerWindowService
{
    private readonly Func<Window?> _getMainWindow;

    public VideoPlayerWindowService(Func<Window?> getMainWindow)
    {
        _getMainWindow = getMainWindow;
    }

    public bool OpenPlayer(string filePath)
    {
        var owner = _getMainWindow();
        if (owner is null)
        {
            return false;
        }

        // Show (not ShowDialog): the user should be able to keep editing the timeline while watching,
        // which is the whole point of having the player in its own window.
        new PlayerWindow(filePath).Show(owner);
        return true;
    }
}
