using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.App.Services;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// Real, continuous audio+video playback of a rendered preview file, inside the workspace.
///
/// Two things changed here after the user reported the app freezing and closing itself, and both were
/// real defects rather than tuning:
///
/// 1. This class used to call <c>Core.Initialize()</c> and build a whole LibVLC + MediaPlayer in its
///    constructor, which runs whenever a workspace opens - so the app permanently held a SECOND native
///    player beside the one in the player window, used or not. VideoLAN documents deadlocks on play and
///    stop when one application holds several media players. Now nothing native exists until the user
///    actually renders a preview, and <see cref="IsAvailable"/> answers the "can this machine play?"
///    question through a loader probe that creates no player at all.
///
/// 2. That MediaPlayer had no video output attached at all once the workspace's VideoView was removed,
///    which means libvlc would have opened its own bare window to put the picture in. It now decodes
///    into <see cref="Frames"/> and the workspace draws it with the same VideoSurface the player window
///    uses - one playback path, one set of tests, no native windows anywhere.
/// </summary>
public sealed partial class RealPreviewViewModel : ViewModelBase, IDisposable
{
    private readonly DispatcherTimer _timer;
    private VideoPlaybackSession? _session;
    private bool _isSyncingFromPlayer;
    private bool _isDisposed;

    /// <summary>Raised once decoded frames start landing somewhere, so the view can point a
    /// VideoSurface at them. An event rather than a binding because the surface is a control that owns a
    /// bitmap, not a value to display.</summary>
    public event Action<VlcVideoFrameBuffer>? FramesReady;

    [ObservableProperty]
    private bool _hasLoadedFile;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentTimeLabel))]
    private double _currentTimeSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalTimeLabel))]
    private double _totalDurationSeconds;

    [ObservableProperty]
    private int _volume = 100;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AudioSyncLabel))]
    private int _audioSyncMilliseconds;

    public string AudioSyncLabel => AudioSyncMilliseconds == 0
        ? "A/V 0 ms"
        : $"A/V {AudioSyncMilliseconds:+0;-0} ms";

    public string CurrentTimeLabel => FormatTime(CurrentTimeSeconds);
    public string TotalTimeLabel => FormatTime(TotalDurationSeconds);

    /// <summary>True when libvlc can be loaded here. Costs a loader probe, not a player.</summary>
    public bool IsAvailable => VideoPlaybackSession.IsPlaybackSupported;

    public string? UnavailableReason => VideoPlaybackSession.PlaybackUnavailableReason;

    /// <summary>
    /// Whether a native player exists yet. Public so the "no second media player until the feature is
    /// actually used" guarantee is something a test can assert, rather than a claim in a comment - the
    /// old eager constructor is precisely the kind of regression that would otherwise creep back.
    /// </summary>
    public bool IsPlayerCreated => _session is not null;

    public RealPreviewViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => SyncFromPlayer();
    }

    /// <summary>
    /// Loads a rendered preview file and starts playing it with sound. Async because the frame size is
    /// read from the file first, so a vertical Shorts render is not decoded into a landscape buffer.
    /// </summary>
    public async Task LoadAndPlayAsync(string filePath)
    {
        if (_isDisposed || !IsAvailable)
        {
            return;
        }

        // One session per loaded file: reusing a player across media is where LibVLCSharp's documented
        // "Stop hangs while connecting to another media" (#214) bites.
        _session?.Dispose();
        _session = VideoPlaybackSession.Create();

        if (!_session.IsReady)
        {
            return;
        }

        var (width, height) = await ProbeFrameSizeAsync(filePath);

        var frames = _session.UseMemoryVideoOutput(width, height);
        if (frames is not null)
        {
            FramesReady?.Invoke(frames);
        }

        _session.Volume = Volume;
        _session.AudioDelayMilliseconds = AudioSyncMilliseconds;
        if (IsMuted)
        {
            // The session owns the native-player lock. Never reach through .Player from this ViewModel;
            // doing so can race its asynchronous Dispose and become a native use-after-free crash.
            _session.ToggleMute();
        }

        if (!_session.Open(filePath, out _))
        {
            return;
        }

        HasLoadedFile = true;
        IsPlaying = true;
        _timer.Start();
    }

    private static async Task<(int Width, int Height)> ProbeFrameSizeAsync(string filePath)
    {
        try
        {
            var asset = await new NPVideoStudio.Media.FfprobeService().ProbeAsync(filePath);
            return (asset.Width, asset.Height);
        }
        catch
        {
            // Falls back to a default decode size; never a reason to refuse to play.
            return (0, 0);
        }
    }

    [RelayCommand]
    private void TogglePlayPause() => IsPlaying = _session?.TogglePlayPause() ?? false;

    [RelayCommand]
    private void Stop()
    {
        _session?.Stop();
        _timer.Stop();
        IsPlaying = false;
    }

    partial void OnVolumeChanged(int value)
    {
        if (!_isDisposed && _session is not null)
        {
            _session.Volume = value;
        }
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (_isDisposed || _session is null || !_session.IsReady)
        {
            return;
        }

        // This callback runs only when the bound bool actually changes, so one safe session toggle maps
        // exactly to one UI toggle. The initial true state is applied once in LoadAndPlayAsync above.
        _session.ToggleMute();
    }

    partial void OnAudioSyncMillisecondsChanged(int value)
    {
        if (!_isDisposed && _session is not null)
        {
            _session.AudioDelayMilliseconds = value;
        }
    }

    /// <summary>Mirrors the same real-vs-external-seek guard pattern as
    /// <see cref="PlayerViewModel.OnCurrentTimeSecondsChanged"/> - the seek slider two-way binds straight
    /// to this property, so a user drag needs to reach the real player, while a value this class just
    /// wrote from <see cref="SyncFromPlayer"/> must not re-seek and fight playback.</summary>
    partial void OnCurrentTimeSecondsChanged(double value)
    {
        if (!_isSyncingFromPlayer && !_isDisposed && HasLoadedFile)
        {
            _session?.SeekToSeconds(value);
        }
    }

    private void SyncFromPlayer()
    {
        // _isDisposed matters as much as the null check: a DispatcherTimer Tick can already be queued
        // when Dispose() runs, and reading from a freed native player is an access violation that takes
        // the whole process down with no managed exception to log.
        if (_isDisposed || _session is null || !_session.IsReady)
        {
            return;
        }

        _isSyncingFromPlayer = true;
        try
        {
            CurrentTimeSeconds = _session.TimeMs / 1000.0;

            var lengthMs = _session.LengthMs;
            if (lengthMs > 0)
            {
                TotalDurationSeconds = lengthMs / 1000.0;

                // Do not read MediaPlayer.IsPlaying directly here: VideoPlaybackSession deliberately
                // serializes every native access behind its own lock. Natural EOF is observable from the
                // same safe time/length API, while play/pause/stop commands already own the interactive state.
                if (CurrentTimeSeconds >= TotalDurationSeconds - 0.05)
                {
                    IsPlaying = false;
                    _timer.Stop();
                }
            }
        }
        finally
        {
            _isSyncingFromPlayer = false;
        }
    }

    private static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}";
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _timer.Stop();

        // The session handles the ordering and the off-UI-thread teardown; both matter, and both are
        // explained where they happen.
        _session?.Dispose();
        _session = null;
    }
}
