using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.AI;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// Player transport (spec Phase 8): play/pause/stop/seek/frame-step/volume/mute/current-time/total-time,
/// driven by the pure/tested <see cref="PlayerStateMachine"/> via a real <see cref="DispatcherTimer"/>
/// tick loop. No real video decode/render - see PHASE_STATUS.md for why (this sandbox has no display to
/// verify a real decoder against). A future phase wires an actual frame renderer to <see cref="CurrentTimeSeconds"/>.
/// </summary>
public sealed partial class PlayerViewModel : ViewModelBase, IDisposable
{
    private PlayerStateMachine _state;
    private readonly DispatcherTimer _timer;
    private DateTime _lastTick;

    [ObservableProperty]
    private double _currentTimeSeconds;

    [ObservableProperty]
    private double _totalDurationSeconds;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private double _volume = 1.0;

    [ObservableProperty]
    private bool _isMuted;

    public string CurrentTimeLabel => FormatTime(CurrentTimeSeconds);
    public string TotalTimeLabel => FormatTime(TotalDurationSeconds);

    public PlayerViewModel(double totalDurationSeconds, double frameRate = 30)
    {
        _state = new PlayerStateMachine(totalDurationSeconds, frameRate);
        TotalDurationSeconds = _state.TotalDurationSeconds;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => OnTick();
    }

    /// <summary>Rebuilds the underlying state machine against a new total duration (e.g. the timeline just grew) while preserving current position/volume/mute.</summary>
    public void Retarget(double newTotalDurationSeconds, double frameRate = 30)
    {
        var wasPlaying = _state.State == PlayerPlaybackState.Playing;
        var currentTime = _state.CurrentTimeSeconds;

        _state = new PlayerStateMachine(newTotalDurationSeconds, frameRate);
        _state.Seek(currentTime);
        _state.SetVolume(Volume);
        _state.SetMuted(IsMuted);
        if (wasPlaying)
        {
            _state.Play();
        }

        TotalDurationSeconds = _state.TotalDurationSeconds;
        SyncFromState();
    }

    public void Seek(double seconds)
    {
        _state.Seek(seconds);
        SyncFromState();
    }

    [RelayCommand]
    private void Play()
    {
        _state.Play();
        _lastTick = DateTime.UtcNow;
        SyncFromState();
        if (_state.State == PlayerPlaybackState.Playing)
        {
            _timer.Start();
        }
    }

    [RelayCommand]
    private void Pause()
    {
        _state.Pause();
        SyncFromState();
        _timer.Stop();
    }

    [RelayCommand]
    private void Stop()
    {
        _state.Stop();
        SyncFromState();
        _timer.Stop();
    }

    [RelayCommand]
    private void StepForward()
    {
        _state.StepFrame(1);
        SyncFromState();
        _timer.Stop();
    }

    [RelayCommand]
    private void StepBackward()
    {
        _state.StepFrame(-1);
        SyncFromState();
        _timer.Stop();
    }

    [RelayCommand]
    private void ToggleMute()
    {
        _state.ToggleMute();
        IsMuted = _state.IsMuted;
    }

    partial void OnVolumeChanged(double value) => _state.SetVolume(value);

    private void OnTick()
    {
        var now = DateTime.UtcNow;
        var delta = (now - _lastTick).TotalSeconds;
        _lastTick = now;

        _state.Advance(delta);
        SyncFromState();

        if (_state.State != PlayerPlaybackState.Playing)
        {
            _timer.Stop();
        }
    }

    private void SyncFromState()
    {
        CurrentTimeSeconds = _state.CurrentTimeSeconds;
        IsPlaying = _state.State == PlayerPlaybackState.Playing;
        OnPropertyChanged(nameof(CurrentTimeLabel));
    }

    private static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}";
    }

    public void Dispose() => _timer.Stop();
}
