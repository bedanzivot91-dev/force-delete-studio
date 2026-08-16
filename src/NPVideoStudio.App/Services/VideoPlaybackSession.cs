using LibVLCSharp.Shared;

namespace NPVideoStudio.App.Services;

/// <summary>
/// All of the player's actual playback behaviour - loading a file, play/pause, stop, seek, volume,
/// mute - with no Avalonia control anywhere in it.
///
/// It lives apart from <see cref="Views.PlayerWindow"/> for one reason: while this logic sat in the
/// window's code-behind it could not be tested at all, so every "the player is fixed" claim was a
/// guess. A libvlc <see cref="MediaPlayer"/> does not need a window to decode - point it at memory
/// output callbacks and it will hand you real frames and real PCM - so pulled out here the whole
/// pipeline runs headless under xUnit, including on the Windows CI machine against the exact libvlc
/// build that ships to users.
///
/// The window keeps only what genuinely needs a window: handing <c>Player</c> to the VideoView.
/// </summary>
public sealed class VideoPlaybackSession : IDisposable
{
    private LibVLC? _libVlc;
    private bool _isDisposed;

    private VideoPlaybackSession(LibVLC? libVlc, MediaPlayer? player, string? failureReason)
    {
        _libVlc = libVlc;
        Player = player;
        FailureReason = failureReason;
    }

    /// <summary>The live player, or null when libvlc could not start on this machine.</summary>
    public MediaPlayer? Player { get; private set; }

    /// <summary>Serbian, user-facing explanation of why playback is unavailable; null when fine.</summary>
    public string? FailureReason { get; }

    public bool IsReady => Player is not null && !_isDisposed;

    /// <summary>
    /// Starts libvlc, returning a session that reports its own failure rather than throwing. A machine
    /// without a usable libvlc must still get a working window with the "open in your own player"
    /// button, not a crash - which is what the user hit repeatedly.
    /// </summary>
    public static VideoPlaybackSession Create()
    {
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            var libVlc = new LibVLC("--quiet");
            return new VideoPlaybackSession(libVlc, new MediaPlayer(libVlc), null);
        }
        catch (Exception ex)
        {
            return new VideoPlaybackSession(
                null,
                null,
                $"Ugrađeni plejer nije mogao da se pokrene: {ex.Message}\n" +
                "Koristite dugme „Otvori u mom plejeru“ - radi uvek.");
        }
    }

    /// <summary>
    /// Hands a file to libvlc and starts it. Returns false with a Serbian message when it cannot even
    /// be attempted; note that a true result only means playback STARTED - libvlc opens media on its
    /// own thread, so a file it cannot decode fails later, not here.
    /// </summary>
    public bool Open(string filePath, out string message)
    {
        if (!IsReady)
        {
            message = FailureReason ?? "Plejer nije spreman.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            message = "Nije izabran nijedan fajl.";
            return false;
        }

        if (!File.Exists(filePath))
        {
            message = $"Fajl ne postoji: {filePath}";
            return false;
        }

        try
        {
            using var media = new LibVLCSharp.Shared.Media(_libVlc!, filePath, FromType.FromPath);
            Player!.Play(media);
        }
        catch (Exception ex)
        {
            message = $"Nije moguće pustiti fajl: {ex.Message}";
            return false;
        }

        message = "Pušta se. Prozor možete povećati ili prevući u ceo ekran.";
        return true;
    }

    /// <summary>Toggles playback; returns true when the result is "playing".</summary>
    public bool TogglePlayPause()
    {
        if (!IsReady)
        {
            return false;
        }

        if (Player!.IsPlaying)
        {
            Player.Pause();
            return false;
        }

        Player.Play();
        return true;
    }

    public void Stop()
    {
        if (IsReady)
        {
            Player!.Stop();
        }
    }

    /// <summary>Toggles mute; returns the new mute state.</summary>
    public bool ToggleMute()
    {
        if (!IsReady)
        {
            return false;
        }

        Player!.Mute = !Player.Mute;
        return Player.Mute;
    }

    public int Volume
    {
        get => IsReady ? Player!.Volume : 0;
        set
        {
            if (IsReady)
            {
                Player!.Volume = Math.Clamp(value, 0, 100);
            }
        }
    }

    public long TimeMs => IsReady ? Math.Max(0, Player!.Time) : 0;

    public long LengthMs => IsReady ? Player!.Length : 0;

    /// <summary>Seeks, but only once libvlc knows the duration - seeking an unopened media is ignored
    /// by libvlc anyway and would just make the slider fight playback.</summary>
    public void SeekToSeconds(double seconds)
    {
        if (IsReady && Player!.Length > 0)
        {
            Player.Time = (long)(Math.Max(0, seconds) * 1000);
        }
    }

    /// <summary>Stop first, then release. Freeing a still-playing MediaPlayer is a native access
    /// violation that kills the process with no managed exception and nothing in the log - the same
    /// failure mode already fixed once in RealPreviewViewModel.</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        try { Player?.Stop(); } catch { /* already gone */ }

        var player = Player;
        Player = null;

        try { player?.Dispose(); } catch { /* ditto */ }
        try { _libVlc?.Dispose(); } catch { /* ditto */ }
        _libVlc = null;
    }
}
