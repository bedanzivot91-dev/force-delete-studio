namespace NPVideoStudio.AI;

public enum PlayerPlaybackState
{
    Stopped,
    Playing,
    Paused
}

/// <summary>
/// Pure playback state/arithmetic (spec Phase 8's player: play/pause/stop/seek/frame-step/volume/mute/
/// current-time/total-time). No real video decode/render here - see PHASE_STATUS.md for why that's out
/// of scope this pass (this sandbox has no display to verify a real decoder against). A real player UI
/// drives this state machine and is responsible for actually rendering a frame at <see cref="CurrentTimeSeconds"/>.
/// </summary>
public sealed class PlayerStateMachine
{
    public double TotalDurationSeconds { get; }
    public double FrameRate { get; }

    public PlayerPlaybackState State { get; private set; } = PlayerPlaybackState.Stopped;
    public double CurrentTimeSeconds { get; private set; }
    public double Volume { get; private set; } = 1.0;
    public bool IsMuted { get; private set; }

    public PlayerStateMachine(double totalDurationSeconds, double frameRate)
    {
        TotalDurationSeconds = Math.Max(0, totalDurationSeconds);
        FrameRate = frameRate > 0 ? frameRate : 30;
    }

    public void Play()
    {
        if (TotalDurationSeconds <= 0)
        {
            return;
        }

        if (State == PlayerPlaybackState.Stopped && CurrentTimeSeconds >= TotalDurationSeconds)
        {
            CurrentTimeSeconds = 0; // Playing again after reaching the end restarts from zero.
        }

        State = PlayerPlaybackState.Playing;
    }

    public void Pause()
    {
        if (State == PlayerPlaybackState.Playing)
        {
            State = PlayerPlaybackState.Paused;
        }
    }

    public void Stop()
    {
        State = PlayerPlaybackState.Stopped;
        CurrentTimeSeconds = 0;
    }

    public void Seek(double seconds)
    {
        CurrentTimeSeconds = Math.Clamp(seconds, 0, TotalDurationSeconds);
        if (CurrentTimeSeconds >= TotalDurationSeconds && State == PlayerPlaybackState.Playing)
        {
            State = PlayerPlaybackState.Paused; // Seeking to the very end stops advancing, doesn't keep "playing" past it.
        }
    }

    /// <summary>Advances playback by <paramref name="deltaSeconds"/> of wall-clock time (a real player UI calls this from its render/tick loop) - stops automatically at the end.</summary>
    public void Advance(double deltaSeconds)
    {
        if (State != PlayerPlaybackState.Playing || deltaSeconds <= 0)
        {
            return;
        }

        var next = CurrentTimeSeconds + deltaSeconds;
        if (next >= TotalDurationSeconds)
        {
            CurrentTimeSeconds = TotalDurationSeconds;
            State = PlayerPlaybackState.Stopped;
        }
        else
        {
            CurrentTimeSeconds = next;
        }
    }

    public void StepFrame(int frameDelta)
    {
        if (frameDelta == 0)
        {
            return;
        }

        Pause();
        Seek(CurrentTimeSeconds + frameDelta / FrameRate);
    }

    public void SetVolume(double volume) => Volume = Math.Clamp(volume, 0, 1.0);

    public void SetMuted(bool muted) => IsMuted = muted;

    public void ToggleMute() => IsMuted = !IsMuted;
}
