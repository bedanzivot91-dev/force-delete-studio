using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// Real, continuous audio+video playback of a rendered preview file, via LibVLC (bundled for win-x64 -
/// see NPVideoStudio.App.csproj's VideoLAN.LibVLC.Windows reference). Deliberately separate from
/// <see cref="PlayerViewModel"/>, which stays a frame-by-frame ffmpeg snapshot preview with no audio
/// (cheap, always available, no native player dependency) - this is the answer to the real, repeated
/// user request for actual playback with sound, at the real cost of a much larger bundled install
/// (~100MB of libvlc + plugins) and a render step before anything plays (see
/// <see cref="WorkspaceViewModel.RenderRealPreviewCommand"/>, which reuses the exact same
/// <c>IRenderService</c>/<c>FfmpegFilterGraphBuilder</c> pipeline export uses - not a separate, possibly-
/// inaccurate preview path).
///
/// <see cref="IsAvailable"/> is false whenever LibVLC's native library can't be loaded - true on a real
/// Windows install with the bundled libvlc.dll, but also the honest, expected outcome on this project's
/// Linux dev sandbox (no libvlc.so present there), so construction never throws even when native
/// playback genuinely isn't possible on the current machine.
/// </summary>
public sealed partial class RealPreviewViewModel : ViewModelBase, IDisposable
{
    private readonly LibVLC? _libVlc;
    private readonly DispatcherTimer _timer;
    private bool _isSyncingFromPlayer;

    public MediaPlayer? MediaPlayer { get; }

    [ObservableProperty]
    private bool _isAvailable;

    [ObservableProperty]
    private string? _unavailableReason;

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

    public string CurrentTimeLabel => FormatTime(CurrentTimeSeconds);
    public string TotalTimeLabel => FormatTime(TotalDurationSeconds);

    public RealPreviewViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => SyncFromPlayer();

        try
        {
            // Fully qualified: this project also has sibling namespaces literally named "Core" and
            // "Media" (NPVideoStudio.Core, NPVideoStudio.Media), which C#'s enclosing-namespace lookup
            // resolves before the "using LibVLCSharp.Shared" import - an unqualified "Core"/"Media" here
            // would silently bind to the wrong namespace instead of LibVLCSharp's types.
            LibVLCSharp.Shared.Core.Initialize();
            _libVlc = new LibVLC("--quiet");
            MediaPlayer = new MediaPlayer(_libVlc);
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            // Real, expected outcome on any machine without libvlc's native library available (this
            // sandbox included) - never let a missing native dependency crash the whole workspace, just
            // disable this one feature and say why.
            IsAvailable = false;
            UnavailableReason = $"Pravi plejer nije dostupan na ovom računaru (libvlc nije učitan): {ex.Message}";
        }
    }

    /// <summary>Loads a real rendered file (see <see cref="WorkspaceViewModel.RenderRealPreviewCommand"/>) and starts playing it immediately, with real audio.</summary>
    public void LoadAndPlay(string filePath)
    {
        if (!IsAvailable || _libVlc is null || MediaPlayer is null)
        {
            return;
        }

        using (var media = new LibVLCSharp.Shared.Media(_libVlc, filePath, FromType.FromPath))
        {
            MediaPlayer.Play(media);
        }

        MediaPlayer.Volume = Volume;
        MediaPlayer.Mute = IsMuted;
        HasLoadedFile = true;
        _timer.Start();
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (MediaPlayer is null)
        {
            return;
        }

        if (MediaPlayer.IsPlaying)
        {
            MediaPlayer.Pause();
        }
        else
        {
            MediaPlayer.Play();
        }
    }

    [RelayCommand]
    private void Stop()
    {
        MediaPlayer?.Stop();
        _timer.Stop();
        IsPlaying = false;
    }

    partial void OnVolumeChanged(int value)
    {
        if (MediaPlayer is not null)
        {
            MediaPlayer.Volume = value;
        }
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (MediaPlayer is not null)
        {
            MediaPlayer.Mute = value;
        }
    }

    /// <summary>Mirrors the same real-vs-external-seek guard pattern as <see cref="PlayerViewModel.OnCurrentTimeSecondsChanged"/> - the seek slider two-way binds straight to this property, so an externally-driven value (a user drag) needs to reach the real player, while a value we ourselves just set from <see cref="SyncFromPlayer"/> must not re-seek and fight the player's own natural playback advance.</summary>
    partial void OnCurrentTimeSecondsChanged(double value)
    {
        if (!_isSyncingFromPlayer && MediaPlayer is not null && HasLoadedFile)
        {
            MediaPlayer.Time = (long)(value * 1000);
        }
    }

    private void SyncFromPlayer()
    {
        if (MediaPlayer is null)
        {
            return;
        }

        _isSyncingFromPlayer = true;
        try
        {
            CurrentTimeSeconds = Math.Max(0, MediaPlayer.Time / 1000.0);
            var lengthMs = MediaPlayer.Length;
            if (lengthMs > 0)
            {
                TotalDurationSeconds = lengthMs / 1000.0;
            }
        }
        finally
        {
            _isSyncingFromPlayer = false;
        }

        IsPlaying = MediaPlayer.IsPlaying;
    }

    private static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}";
    }

    public void Dispose()
    {
        _timer.Stop();
        MediaPlayer?.Dispose();
        _libVlc?.Dispose();
    }
}
