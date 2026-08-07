using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.AI;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// Player transport (spec Phase 8): play/pause/stop/seek/frame-step/volume/mute/current-time/total-time,
/// driven by the pure/tested <see cref="PlayerStateMachine"/> via a real <see cref="DispatcherTimer"/>
/// tick loop. <see cref="CurrentFrameBitmap"/> (Phase 11 follow-up) is a real decoded preview frame -
/// this ViewModel doesn't know how to decode one itself (no Timeline/media-library access), it just
/// raises <see cref="TimeChanged"/> on every position change and exposes a settable bitmap slot;
/// <see cref="WorkspaceViewModel"/> is what actually resolves the timeline position to a source file and
/// timestamp and fills this in - kept out of this class to not couple pure transport state to the
/// timeline model.
/// </summary>
public sealed partial class PlayerViewModel : ViewModelBase, IDisposable
{
    private PlayerStateMachine _state;
    private readonly DispatcherTimer _timer;
    private DateTime _lastTick;
    private bool _isApplyingStateSync;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentTimeLabel))]
    private double _currentTimeSeconds;

    [ObservableProperty]
    private Bitmap? _currentFrameBitmap;

    /// <summary>Raised whenever <see cref="CurrentTimeSeconds"/> changes (seek/step/play-tick/stop), so
    /// the owner can refresh <see cref="CurrentFrameBitmap"/> for the new position.</summary>
    public event Action<double>? TimeChanged;

    /// <summary>NotifyPropertyChangedFor is required here - TotalTimeLabel is a plain computed property,
    /// not an [ObservableProperty] itself, so without this the UI's total-time text silently kept
    /// showing the old duration after Retarget() (e.g. after adding a clip that extends the timeline)
    /// even though TotalDurationSeconds itself had genuinely changed. A real bug, found by inspection,
    /// not previously caught by any test because no test asserts on the formatted label text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalTimeLabel))]
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

    /// <summary>
    /// The seek slider in WorkspaceView.axaml two-way binds directly to <see cref="CurrentTimeSeconds"/>
    /// (simpler XAML than routing every drag through a command) - this hook is what makes that actually
    /// seek: an externally-set value (the user dragging the slider) reaches here with
    /// <see cref="_isApplyingStateSync"/> false, so it gets routed through the real state machine's
    /// clamping instead of just changing the displayed number with nothing underneath tracking it. A
    /// real, previously-latent bug found while wiring up <see cref="TimeChanged"/> for frame preview:
    /// without this, dragging the slider never touched <see cref="_state"/> at all.
    /// </summary>
    partial void OnCurrentTimeSecondsChanged(double value)
    {
        if (!_isApplyingStateSync)
        {
            _state.Seek(value);
        }

        TimeChanged?.Invoke(value);
    }

    private void SyncFromState()
    {
        _isApplyingStateSync = true;
        try
        {
            CurrentTimeSeconds = _state.CurrentTimeSeconds;
        }
        finally
        {
            _isApplyingStateSync = false;
        }

        IsPlaying = _state.State == PlayerPlaybackState.Playing;
    }

    private static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}";
    }

    // Frequent replacement during scrubbing/playback (every seek/step/tick) - dispose the outgoing
    // bitmap's native handle immediately rather than waiting on the GC to catch up.
    partial void OnCurrentFrameBitmapChanging(Bitmap? oldValue, Bitmap? newValue)
    {
        if (!ReferenceEquals(oldValue, newValue))
        {
            oldValue?.Dispose();
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        CurrentFrameBitmap = null;
    }
}
