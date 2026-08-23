using Avalonia.Headless.XUnit;
using NPVideoStudio.App.ViewModels;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Regression tests for real bugs found while wiring up the frame-preview feature: TotalTimeLabel and
/// CurrentTimeLabel are plain computed properties (not [ObservableProperty] themselves), so without
/// [NotifyPropertyChangedFor] on the underlying seconds properties, the UI's formatted time text could
/// silently keep showing a stale value even though the underlying seconds value had genuinely changed -
/// nothing asserted on the label text before, only the raw seconds properties.
/// </summary>
public class PlayerViewModelTests
{
    [AvaloniaFact]
    public void Retarget_ChangesTotalDuration_RaisesChangeNotificationForTotalTimeLabel()
    {
        var player = new PlayerViewModel(totalDurationSeconds: 5);
        Assert.Equal("00:05", player.TotalTimeLabel);

        var raised = false;
        player.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(PlayerViewModel.TotalTimeLabel);

        player.Retarget(newTotalDurationSeconds: 42);

        Assert.True(raised);
        Assert.Equal("00:42", player.TotalTimeLabel);
    }

    [AvaloniaFact]
    public void Seek_ChangesCurrentTime_RaisesChangeNotificationForCurrentTimeLabel()
    {
        var player = new PlayerViewModel(totalDurationSeconds: 60);

        var raised = false;
        player.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(PlayerViewModel.CurrentTimeLabel);

        player.Seek(37);

        Assert.True(raised);
        Assert.Equal("00:37", player.CurrentTimeLabel);
    }
}
