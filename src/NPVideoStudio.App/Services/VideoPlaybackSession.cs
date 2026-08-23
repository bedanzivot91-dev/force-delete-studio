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

    /// <summary>
    /// Guards every touch of the native player. Not paranoia - a real segfault: the volume re-apply and
    /// the stop both run on pool threads now (libvlc events and Stop must not run on the UI thread), so
    /// without this a queued call could reach the MediaPlayer after Dispose had already freed it. That
    /// is a use-after-free, it raises SIGSEGV rather than an exception, and it kills the process with
    /// nothing in the log - exactly the "program se sam gasi" the user keeps reporting. It was caught
    /// here as a crashed test host, not guessed at.
    ///
    /// The discipline is: every user of the player takes this lock and re-checks _isDisposed INSIDE it.
    /// Dispose flips the flag and clears the references inside the same lock, and only then releases the
    /// native objects - outside the lock, which is safe precisely because no one can still be inside a
    /// critical section holding a live reference, and which keeps a hanging Stop from blocking anyone.
    /// </summary>
    private readonly object _nativeLock = new();

    private int _desiredVolume = 100;
    private int _audioDelayMilliseconds;
    private VlcVideoFrameBuffer? _frameBuffer;

    private VideoPlaybackSession(LibVLC? libVlc, MediaPlayer? player, string? failureReason)
    {
        _libVlc = libVlc;
        Player = player;
        FailureReason = failureReason;

        if (player is not null)
        {
            // THE reason sound could go missing. libvlc does not create its audio output module until
            // playback has actually begun, so a volume set any earlier - which is exactly what the
            // window did, right after Play() returned - is written to a player that has no audio output
            // yet and is silently dropped. Windows CI proved it: set 40, read back 0.
            // Re-applying once playback really starts is what makes the volume stick.
            //
            // The thread switch is mandatory, not defensive. LibVLCSharp's own best-practices document
            // says "Do not call LibVLC from a LibVLC event without switching thread first" and that doing
            // so "might freeze your app" - and Playing is a LibVLC event, so setting the volume directly
            // in this handler is precisely the pattern they warn against. That is a real hang, and it is
            // the app freezing that the user reported after the volume fix landed.
            player.Playing += (_, _) => ThreadPool.QueueUserWorkItem(_ => ApplyVolumeToPlayer());
        }
    }

    /// <summary>The live player, or null when libvlc could not start on this machine.</summary>
    public MediaPlayer? Player { get; private set; }

    /// <summary>Serbian, user-facing explanation of why playback is unavailable; null when fine.</summary>
    public string? FailureReason { get; }

    public bool IsReady => Player is not null && !_isDisposed;

    private static LibVLC? _sharedLibVlc;
    private static bool? _playbackSupported;
    private static string? _supportFailureReason;
    private static readonly object SupportProbeLock = new();

    /// <summary>
    /// Whether libvlc can be loaded on this machine, answered without creating a player.
    ///
    /// The distinction matters: screens used to answer "is playback available?" by constructing a whole
    /// LibVLC + MediaPlayer up front and keeping it alive forever. That left a second, unused native
    /// player running alongside the real one, and VideoLAN documents deadlocks on play and stop when a
    /// single application holds several media players. This probe touches only the native loader.
    /// </summary>
    public static bool IsPlaybackSupported
    {
        get
        {
            EnsureSupportProbed();
            return _playbackSupported!.Value;
        }
    }

    /// <summary>Serbian explanation for the user when <see cref="IsPlaybackSupported"/> is false.</summary>
    public static string? PlaybackUnavailableReason
    {
        get
        {
            EnsureSupportProbed();
            return _supportFailureReason;
        }
    }

    private static void EnsureSupportProbed()
    {
        lock (SupportProbeLock)
        {
            if (_playbackSupported.HasValue)
            {
                return;
            }

            try
            {
                LibVLCSharp.Shared.Core.Initialize();
                _playbackSupported = true;
                _supportFailureReason = null;
            }
            catch (Exception ex)
            {
                _playbackSupported = false;
                _supportFailureReason =
                    $"Pravi plejer nije dostupan na ovom računaru (libvlc nije učitan): {ex.Message}";
            }
        }
    }

    /// <summary>
    /// Starts libvlc, returning a session that reports its own failure rather than throwing. A machine
    /// without a usable libvlc must still get a working window with the "open in your own player"
    /// button, not a crash - which is what the user hit repeatedly.
    /// </summary>
    public static VideoPlaybackSession Create()
    {
        try
        {
            return new VideoPlaybackSession(SharedLibVlc, new MediaPlayer(SharedLibVlc), null);
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
    /// One LibVLC for the whole process, shared by every session.
    ///
    /// This is a crash fix with a reproduction, not a tidy-up. Giving each session its own LibVLC means
    /// several libvlc engines are alive whenever the player window is open over a workspace that has a
    /// preview loaded, and VideoLAN document deadlocks and crashes for an application holding several
    /// media players. Running this project's libvlc-backed test classes in parallel - which is exactly
    /// that situation - segfaulted the test host reliably (SIGSEGV, no managed exception, nothing
    /// logged); with a single shared engine it stops. That is the same failure the user sees as the app
    /// vanishing.
    ///
    /// Never disposed: it lives as long as the process, so there is no window in which one screen frees
    /// the engine another screen is still playing through.
    /// </summary>
    private static LibVLC SharedLibVlc
    {
        get
        {
            lock (SupportProbeLock)
            {
                if (_sharedLibVlc is null)
                {
                    LibVLCSharp.Shared.Core.Initialize();
                    _sharedLibVlc = new LibVLC("--quiet");
                }

                return _sharedLibVlc;
            }
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
            lock (_nativeLock)
            {
                if (_isDisposed || Player is null || _libVlc is null)
                {
                    message = "Plejer je u međuvremenu zatvoren.";
                    return false;
                }

                using var media = new LibVLCSharp.Shared.Media(_libVlc, filePath, FromType.FromPath);
                Player.Play(media);
            }
        }
        catch (Exception ex)
        {
            message = $"Nije moguće pustiti fajl: {ex.Message}";
            return false;
        }

        message = "Pušta se. Prozor možete povećati ili prevući u ceo ekran.";
        return true;
    }

    /// <summary>
    /// Switches this session to decoding video into memory instead of into a native child window, and
    /// returns the buffer the frames land in. Must be called before <see cref="Open"/>, because libvlc
    /// builds its video output chain when playback starts.
    ///
    /// This is what removes the whole class of failure the user kept hitting: with no native window
    /// there is no handle to fail to attach, nothing that can only live directly inside a Window, and no
    /// native crash path that kills the process without an exception.
    /// </summary>
    public VlcVideoFrameBuffer? UseMemoryVideoOutput(int nativeWidth, int nativeHeight)
    {
        if (!IsReady)
        {
            return null;
        }

        var (width, height) = VlcVideoFrameBuffer.ChooseDecodeSize(nativeWidth, nativeHeight);

        lock (_nativeLock)
        {
            if (_isDisposed || Player is null)
            {
                return null;
            }

            _frameBuffer?.Dispose();
            _frameBuffer = new VlcVideoFrameBuffer(width, height);
            _frameBuffer.AttachTo(Player);

            return _frameBuffer;
        }
    }

    /// <summary>Toggles playback; returns true when the result is "playing".</summary>
    public bool TogglePlayPause()
    {
        lock (_nativeLock)
        {
            if (_isDisposed || Player is null)
            {
                return false;
            }

            if (Player.IsPlaying)
            {
                Player.Pause();
                return false;
            }

            Player.Play();
            return true;
        }
    }

    /// <summary>
    /// Stops playback without blocking the caller. The thread switch is the point: VideoLAN's issue #214
    /// ("MediaPlayer.Stop hangs when the player is connecting to another media") documents Stop blocking
    /// until the media is playing or a timeout expires, so calling it straight from a button click
    /// freezes the whole window for as long as libvlc feels like taking.
    /// </summary>
    public void Stop()
    {
        MediaPlayer player;

        lock (_nativeLock)
        {
            if (_isDisposed || Player is null)
            {
                return;
            }

            player = Player;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            lock (_nativeLock)
            {
                // Re-checked inside the lock: Dispose may have won the race and freed this player
                // between the queue and the callback. Touching it then is a segfault, not an exception.
                if (_isDisposed)
                {
                    return;
                }

                try { player.Stop(); } catch { /* torn down underneath us */ }
            }
        });
    }

    /// <summary>Toggles mute; returns the new mute state.</summary>
    public bool ToggleMute()
    {
        lock (_nativeLock)
        {
            if (_isDisposed || Player is null)
            {
                return false;
            }

            Player.Mute = !Player.Mute;
            return Player.Mute;
        }
    }

    /// <summary>
    /// The volume the user asked for. Reads back what was set rather than what libvlc currently reports,
    /// because libvlc reports a meaningless value whenever no audio output module exists yet - so
    /// trusting its getter would make the slider jump to 0 the moment the window opened.
    /// The value is pushed to libvlc now if possible, and again when playback starts.
    /// </summary>
    public int Volume
    {
        get => _desiredVolume;
        set
        {
            _desiredVolume = Math.Clamp(value, 0, 100);
            ApplyVolumeToPlayer();
        }
    }

    /// <summary>Manual A/V correction supported by libvlc itself. Negative values move sound earlier
    /// (the reported case: picture runs ahead); positive values move sound later.</summary>
    public int AudioDelayMilliseconds
    {
        get => _audioDelayMilliseconds;
        set
        {
            _audioDelayMilliseconds = Math.Clamp(value, -2000, 2000);
            lock (_nativeLock)
            {
                if (!_isDisposed && Player is not null)
                {
                    Player.SetAudioDelay(_audioDelayMilliseconds * 1000L);
                }
            }
        }
    }

    /// <summary>What libvlc itself currently reports, purely for diagnostics and tests. Negative or
    /// stale values here are normal before an audio output exists.</summary>
    public int ActualPlayerVolume
    {
        get
        {
            lock (_nativeLock)
            {
                return _isDisposed || Player is null ? -1 : Player.Volume;
            }
        }
    }

    private void ApplyVolumeToPlayer()
    {
        lock (_nativeLock)
        {
            if (_isDisposed || Player is null)
            {
                return;
            }

            try { Player.Volume = _desiredVolume; } catch { /* no audio output yet; retried on Playing */ }
        }
    }

    public long TimeMs
    {
        get
        {
            lock (_nativeLock)
            {
                return _isDisposed || Player is null ? 0 : Math.Max(0, Player.Time);
            }
        }
    }

    public long LengthMs
    {
        get
        {
            lock (_nativeLock)
            {
                return _isDisposed || Player is null ? 0 : Player.Length;
            }
        }
    }

    /// <summary>Seeks, but only once libvlc knows the duration - seeking an unopened media is ignored
    /// by libvlc anyway and would just make the slider fight playback.</summary>
    public void SeekToSeconds(double seconds)
    {
        lock (_nativeLock)
        {
            if (!_isDisposed && Player is { Length: > 0 })
            {
                Player.Time = (long)(Math.Max(0, seconds) * 1000);
            }
        }
    }

    /// <summary>
    /// Releases everything, in an order that matters, and without blocking the caller.
    ///
    /// Two separate hazards are being avoided here. Ordering: the player must be stopped and freed
    /// before the frame buffers, because libvlc writes into those buffers from its own decoder thread
    /// and freeing memory it is about to touch is a native access violation that kills the process with
    /// no managed exception and nothing in the log. Threading: Stop is documented to hang
    /// (VideoLAN/LibVLCSharp#214), so running this teardown inline would freeze the UI while a window is
    /// closing - which looks exactly like the app locking up.
    ///
    /// The state flips to disposed immediately, so nothing can touch the player after this returns even
    /// though the native release finishes a moment later on a pool thread.
    /// </summary>
    public void Dispose()
    {
        MediaPlayer? player;
        VlcVideoFrameBuffer? frameBuffer;

        lock (_nativeLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            player = Player;
            frameBuffer = _frameBuffer;

            Player = null;
            _libVlc = null;
            _frameBuffer = null;
        }

        // Released outside the lock on purpose. Nothing can still be inside a critical section holding a
        // live reference - every user re-checks _isDisposed inside the same lock - so this cannot race
        // with a use, while keeping a possibly-hanging Stop from blocking anyone waiting on the lock.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { player?.Stop(); } catch { /* already gone */ }
            try { player?.Dispose(); } catch { /* ditto */ }
            // The LibVLC engine is deliberately NOT disposed here - it is shared process-wide, and
            // freeing it would pull the ground out from under any other session still playing.
            try { frameBuffer?.Dispose(); } catch { /* ditto */ }
        });
    }
}
