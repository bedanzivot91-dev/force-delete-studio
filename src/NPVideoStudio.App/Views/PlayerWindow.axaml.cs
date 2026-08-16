using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using NPVideoStudio.App.Services;

namespace NPVideoStudio.App.Views;

/// <summary>
/// A real, resizable video player window with continuous playback and sound.
///
/// All playback behaviour lives in <see cref="VideoPlaybackSession"/>, which is covered by tests that
/// really decode a file and measure the frames and audio samples that come out. This class is only the
/// window around it: native handle lifetime, the controls, and full screen.
///
/// Deliberately owns its OWN session rather than sharing the workspace's: two VideoView controls bound
/// to one MediaPlayer is a known-broken configuration (donandren/vlcsharpavalonia#13, "Multiple
/// VideoView Controls Behavior"), and this window is opened while the workspace screen is still alive
/// behind it.
/// </summary>
public partial class PlayerWindow : Window
{
    private VideoPlaybackSession? _session;
    private readonly DispatcherTimer _timer;
    private readonly string _filePath;
    private readonly PlayerTextActions? _textActions;

    private bool _isSyncingFromPlayer;
    private bool _isDisposed;

    public PlayerWindow() : this(string.Empty)
    {
    }

    public PlayerWindow(string filePath, PlayerTextActions? textActions = null)
    {
        InitializeComponent();
        _filePath = filePath;
        _textActions = textActions;
        WireTextTools();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => SyncFromPlayer();

        PlayPauseButton.Click += OnPlayPause;
        StopButton.Click += OnStop;
        MuteButton.Click += OnToggleMute;
        FullScreenButton.Click += (_, _) => ToggleFullScreen();
        OpenExternalButton.Click += (_, _) => OpenInSystemPlayer();
        SeekSlider.PropertyChanged += OnSeekSliderChanged;
        VolumeSlider.PropertyChanged += OnVolumeSliderChanged;

        // Two standard ways out of full screen, both of which users expect.
        Video.DoubleTapped += (_, _) => ToggleFullScreen();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && WindowState == WindowState.FullScreen)
            {
                ToggleFullScreen();
            }
        };

        // Everything native happens only after the window (and therefore the VideoView's native window
        // handle) actually exists. Creating LibVLC and assigning VideoView.MediaPlayer in the constructor -
        // which is what this class did before - hands libvlc a control that has no handle yet, and libvlc
        // then crashes the whole PROCESS from native code: no exception, no log, the app just vanishes.
        // That is exactly what the user kept reporting when pressing "Pusti moj video".
        Opened += (_, _) => InitialisePlayerAndStart();
        Closed += (_, _) => Cleanup();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Shows only the text tools the caller actually provided, and runs each one without blocking
    /// playback - transcription takes real seconds to minutes, and freezing the video the user is
    /// watching while it runs would be worse than not offering it here at all.
    /// </summary>
    private void WireTextTools()
    {
        AddCaptionsButton.IsVisible = _textActions?.AddCaptionsFromVideo is not null;
        AddKaraokeButton.IsVisible = _textActions?.AddKaraokeCaptions is not null;
        FindLyricsButton.IsVisible = _textActions?.FindLyricsInSong is not null;

        TextToolsPanel.IsVisible =
            AddCaptionsButton.IsVisible || AddKaraokeButton.IsVisible || FindLyricsButton.IsVisible;

        AddCaptionsButton.Click += (_, _) => RunTextTool(_textActions?.AddCaptionsFromVideo, "Prepoznajem tekst iz videa...");
        AddKaraokeButton.Click += (_, _) => RunTextTool(_textActions?.AddKaraokeCaptions, "Pravim karaoke titlove, reč po reč...");
        FindLyricsButton.Click += (_, _) => RunTextTool(_textActions?.FindLyricsInSong, "Tražim tekst pesme...");
    }

    private async void RunTextTool(Func<Task>? action, string runningMessage)
    {
        if (action is null)
        {
            return;
        }

        SetTextToolsEnabled(false);
        TextToolsStatus.Text = runningMessage;

        try
        {
            await action();
            TextToolsStatus.Text = "Gotovo - rezultat je u radnom prostoru iza ovog prozora.";
        }
        catch (Exception ex)
        {
            // Reported here rather than swallowed: this runs on a button click in a window with no
            // logger of its own, so this line is the only place the user would ever see it.
            TextToolsStatus.Text = $"Nije uspelo: {ex.Message}";
        }
        finally
        {
            SetTextToolsEnabled(true);
        }
    }

    private void SetTextToolsEnabled(bool enabled)
    {
        AddCaptionsButton.IsEnabled = enabled;
        AddKaraokeButton.IsEnabled = enabled;
        FindLyricsButton.IsEnabled = enabled;
    }

    private void InitialisePlayerAndStart()
    {
        _session = VideoPlaybackSession.Create();

        if (!_session.IsReady)
        {
            StatusText.Text = _session.FailureReason;
            return;
        }

        Video.MediaPlayer = _session.Player;

        var started = _session.Open(_filePath, out var message);
        StatusText.Text = message;

        if (!started)
        {
            return;
        }

        _session.Volume = (int)VolumeSlider.Value;
        _timer.Start();

        Title = $"NP Video Studio - {Path.GetFileName(_filePath)}";
        PlayPauseButton.Content = "⏸ Pauza";
    }

    private void OnPlayPause(object? sender, RoutedEventArgs e) =>
        PlayPauseButton.Content = _session?.TogglePlayPause() == true ? "⏸ Pauza" : "▶ Pusti";

    private void OnStop(object? sender, RoutedEventArgs e)
    {
        _session?.Stop();
        PlayPauseButton.Content = "▶ Pusti";
    }

    private void OnToggleMute(object? sender, RoutedEventArgs e) =>
        MuteButton.Content = _session?.ToggleMute() == true ? "🔇" : "🔊";

    /// <summary>
    /// Hands the file to whatever video player Windows already uses. Deliberately here as a guaranteed
    /// escape hatch: the embedded player depends on libvlc loading and attaching a native window handle
    /// correctly, which has failed on this user's machine more than once, and when it fails natively there
    /// is nothing this code can catch. Launching the system player is a plain ShellExecute - it cannot
    /// take this process down, and it gives full size, sound and full screen for free.
    /// </summary>
    private void OpenInSystemPlayer()
    {
        if (!File.Exists(_filePath))
        {
            StatusText.Text = $"Fajl ne postoji: {_filePath}";
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_filePath)
            {
                UseShellExecute = true
            });

            StatusText.Text = "Otvoreno u vašem plejeru.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Nije moguće otvoriti fajl: {ex.Message}";
        }
    }

    private void ToggleFullScreen() =>
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

    /// <summary>Same guard pattern the embedded preview uses: a value this class just wrote from
    /// <see cref="SyncFromPlayer"/> must not be treated as a user seek and fight playback.</summary>
    private void OnSeekSliderChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Slider.ValueProperty || _isSyncingFromPlayer || _isDisposed)
        {
            return;
        }

        _session?.SeekToSeconds(SeekSlider.Value);
    }

    private void OnVolumeSliderChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Slider.ValueProperty || _isDisposed || _session is null)
        {
            return;
        }

        _session.Volume = (int)VolumeSlider.Value;
    }

    private void SyncFromPlayer()
    {
        if (_session is null || !_session.IsReady || _isDisposed)
        {
            return;
        }

        var lengthMs = _session.LengthMs;
        var timeMs = _session.TimeMs;

        _isSyncingFromPlayer = true;
        try
        {
            if (lengthMs > 0)
            {
                SeekSlider.Maximum = lengthMs / 1000.0;
                TotalTimeText.Text = Format(lengthMs / 1000.0);
            }

            SeekSlider.Value = timeMs / 1000.0;
            CurrentTimeText.Text = Format(timeMs / 1000.0);
        }
        finally
        {
            _isSyncingFromPlayer = false;
        }
    }

    private void Cleanup()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _timer.Stop();

        // Detach before disposing: the VideoView must not be left pointing at a freed MediaPlayer.
        try { Video.MediaPlayer = null; } catch { /* window already tearing down */ }
        _session?.Dispose();
        _session = null;
    }

    private static string Format(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}";
    }
}
