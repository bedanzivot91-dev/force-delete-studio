using NPVideoStudio.AI;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Pure logic, no process/model involved.</summary>
public class PlayerStateMachineTests
{
    [Fact]
    public void Play_SetsStateToPlaying()
    {
        var player = new PlayerStateMachine(totalDurationSeconds: 10, frameRate: 30);

        player.Play();

        Assert.Equal(PlayerPlaybackState.Playing, player.State);
    }

    [Fact]
    public void Pause_FromPlaying_SetsStateToPaused()
    {
        var player = new PlayerStateMachine(10, 30);
        player.Play();

        player.Pause();

        Assert.Equal(PlayerPlaybackState.Paused, player.State);
    }

    [Fact]
    public void Stop_ResetsCurrentTimeToZero()
    {
        var player = new PlayerStateMachine(10, 30);
        player.Seek(5);

        player.Stop();

        Assert.Equal(PlayerPlaybackState.Stopped, player.State);
        Assert.Equal(0, player.CurrentTimeSeconds);
    }

    [Fact]
    public void Seek_ClampsToValidRange()
    {
        var player = new PlayerStateMachine(10, 30);

        player.Seek(-5);
        Assert.Equal(0, player.CurrentTimeSeconds);

        player.Seek(50);
        Assert.Equal(10, player.CurrentTimeSeconds);
    }

    [Fact]
    public void Advance_WhilePlaying_MovesCurrentTimeForward()
    {
        var player = new PlayerStateMachine(10, 30);
        player.Play();

        player.Advance(2.5);

        Assert.Equal(2.5, player.CurrentTimeSeconds);
        Assert.Equal(PlayerPlaybackState.Playing, player.State);
    }

    [Fact]
    public void Advance_PastEnd_StopsAtDurationAndSetsStateStopped()
    {
        var player = new PlayerStateMachine(10, 30);
        player.Play();

        player.Advance(15);

        Assert.Equal(10, player.CurrentTimeSeconds);
        Assert.Equal(PlayerPlaybackState.Stopped, player.State);
    }

    [Fact]
    public void Advance_WhileNotPlaying_DoesNothing()
    {
        var player = new PlayerStateMachine(10, 30);

        player.Advance(5);

        Assert.Equal(0, player.CurrentTimeSeconds);
    }

    [Fact]
    public void Play_AfterReachingEnd_RestartsFromZero()
    {
        var player = new PlayerStateMachine(10, 30);
        player.Play();
        player.Advance(15); // reaches end, becomes Stopped

        player.Play();

        Assert.Equal(0, player.CurrentTimeSeconds);
        Assert.Equal(PlayerPlaybackState.Playing, player.State);
    }

    [Fact]
    public void StepFrame_PausesAndMovesByOneFrameDuration()
    {
        var player = new PlayerStateMachine(10, frameRate: 25);
        player.Play();
        player.Seek(1.0);

        player.StepFrame(1);

        Assert.Equal(PlayerPlaybackState.Paused, player.State);
        Assert.Equal(1.0 + 1.0 / 25, player.CurrentTimeSeconds, precision: 5);
    }

    [Fact]
    public void StepFrame_Backward_MovesBeforeCurrentPosition()
    {
        var player = new PlayerStateMachine(10, frameRate: 25);
        player.Seek(1.0);

        player.StepFrame(-1);

        Assert.Equal(1.0 - 1.0 / 25, player.CurrentTimeSeconds, precision: 5);
    }

    [Fact]
    public void SetVolume_ClampsToZeroOneRange()
    {
        var player = new PlayerStateMachine(10, 30);

        player.SetVolume(1.5);
        Assert.Equal(1.0, player.Volume);

        player.SetVolume(-1);
        Assert.Equal(0.0, player.Volume);
    }

    [Fact]
    public void ToggleMute_FlipsMutedState()
    {
        var player = new PlayerStateMachine(10, 30);

        player.ToggleMute();
        Assert.True(player.IsMuted);

        player.ToggleMute();
        Assert.False(player.IsMuted);
    }

    [Fact]
    public void Seek_ToExactEnd_WhilePlaying_PausesInsteadOfStayingPlaying()
    {
        var player = new PlayerStateMachine(10, 30);
        player.Play();

        player.Seek(10);

        Assert.Equal(PlayerPlaybackState.Paused, player.State);
    }
}
