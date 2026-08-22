using System.Runtime.InteropServices;
using NPVideoStudio.App.Services;
using Xunit;
using Xunit.Abstractions;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// The player's real behaviour, measured rather than assumed.
///
/// Every earlier "the player is fixed" claim in this repo was unverified, because the playback code sat
/// inside a Window and nothing could drive it. libvlc does not actually need a window to decode: given
/// memory-output callbacks it writes decoded frames and PCM straight into buffers we own, so these tests
/// play a real file and then assert on the pixels and the audio samples that came out of it.
///
/// On Windows CI this runs against the libvlc build that is bundled into the shipped app (the
/// VideoLAN.LibVLC.Windows reference in this test project is the same version the app references), so a
/// green run here is evidence about the binary the user actually installs - not about some other libvlc.
///
/// Where libvlc cannot start at all (a bare Linux sandbox with no VLC installed), the tests assert the
/// graceful-failure branch instead of silently passing. Both branches are real assertions; neither is a
/// skip that could hide a regression. The test output states which branch ran.
/// </summary>
public class VideoPlaybackSessionTests
{
    private readonly ITestOutputHelper _output;

    public VideoPlaybackSessionTests(ITestOutputHelper output) => _output = output;

    /// <summary>A 3 s, 320x240 @25fps clip of solid DodgerBlue (R=30 G=144 B=255) with a 440 Hz sine
    /// tone. Both the colour and the tone are chosen so a decoded frame and a decoded sample can be
    /// checked against a known-correct value instead of just "something arrived".</summary>
    private static string ProbeVideo =>
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "player_probe.mp4");

    /// <summary>
    /// Windows is the platform this app ships on, and it ships libvlc bundled beside the exe. So on
    /// Windows "libvlc could not start" is never an acceptable environment quirk to tolerate - it means
    /// the bundled payload is broken and every user would get a dead player. Without this, an unavailable
    /// libvlc would quietly route these tests down the failure branch and CI would stay green while the
    /// shipped player was broken, which is exactly the kind of blind spot that produced the unverified
    /// "player is fixed" claims in the first place.
    /// </summary>
    private static void RequireLibVlcOnWindows(VideoPlaybackSession session)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.True(
                session.IsReady,
                "libvlc is bundled with the Windows build, so it must start here. " +
                $"It did not: {session.FailureReason}");
        }
    }

    [Fact]
    public void Create_NeverThrows_AndReportsWhyWhenPlaybackIsUnavailable()
    {
        using var session = VideoPlaybackSession.Create();
        RequireLibVlcOnWindows(session);

        if (session.IsReady)
        {
            Assert.Null(session.FailureReason);
            Assert.NotNull(session.Player);
        }
        else
        {
            Assert.NotNull(session.FailureReason);
            Assert.Contains("Otvori u mom plejeru", session.FailureReason);
        }
    }

    [Fact]
    public void Open_MissingFile_ReportsItAndDoesNotThrow()
    {
        using var session = VideoPlaybackSession.Create();
        var started = session.Open(Path.Combine(Path.GetTempPath(), $"nema-{Guid.NewGuid():N}.mp4"), out var message);
        Assert.False(started);
        Assert.Contains("ne postoji", message);
    }

    [Fact]
    public void Open_EmptyPath_ReportsItAndDoesNotThrow()
    {
        using var session = VideoPlaybackSession.Create();
        var started = session.Open("   ", out var message);
        Assert.False(started);
        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Fact]
    public void Dispose_IsIdempotent_AndSafeWhilePlaying()
    {
        var session = VideoPlaybackSession.Create();
        session.Open(ProbeVideo, out _);
        session.Dispose();
        session.Dispose();
        Assert.False(session.IsReady);
    }

    [Fact]
    public void Playing_RealFile_ProducesRealPictureAndRealSound()
    {
        Assert.True(File.Exists(ProbeVideo), $"Test asset missing: {ProbeVideo}");

        using var session = VideoPlaybackSession.Create();
        RequireLibVlcOnWindows(session);

        if (!session.IsReady)
        {
            _output.WriteLine($"libvlc unavailable on this machine - asserting the failure branch. {session.FailureReason}");
            Assert.NotNull(session.FailureReason);
            return;
        }

        var frames = session.UseMemoryVideoOutput(320, 240);
        Assert.NotNull(frames);

        var audio = new VlcAudioProbe();
        audio.AttachTo(session.Player!);

        try
        {
            var started = session.Open(ProbeVideo, out var message);
            Assert.True(started, message);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            var maxTimeMs = 0L;
            while (DateTime.UtcNow < deadline)
            {
                maxTimeMs = Math.Max(maxTimeMs, session.TimeMs);
                if (session.Player!.State is LibVLCSharp.Shared.VLCState.Ended or LibVLCSharp.Shared.VLCState.Error)
                {
                    break;
                }
                Thread.Sleep(100);
            }

            var (r, g, b) = frames!.ReadPixel(frames.Width / 2, frames.Height / 2);

            _output.WriteLine($"state              : {session.Player!.State}");
            _output.WriteLine($"length             : {session.LengthMs} ms, reached {maxTimeMs} ms");
            _output.WriteLine($"frames decoded     : {frames.FramesDisplayed}");
            _output.WriteLine($"centre pixel R,G,B : {r}, {g}, {b}");
            _output.WriteLine($"audio frames       : {audio.Frames} ({audio.Frames / 48000.0:F2} s @48kHz)");
            _output.WriteLine($"audio peak         : {audio.Peak:F4}, RMS {audio.Rms:F4} ({audio.RmsDb:F1} dB)");

            Assert.NotEqual(LibVLCSharp.Shared.VLCState.Error, session.Player.State);
            Assert.True(frames.FramesDisplayed >= 50,
                $"Only {frames.FramesDisplayed} frames decoded - picture is not really playing.");
            Assert.InRange(r, 20, 40);
            Assert.InRange(g, 134, 154);
            Assert.InRange(b, 245, 255);
            Assert.True(audio.Frames >= 48000 * 2, $"Only {audio.Frames} audio frames - sound is not really playing.");
            Assert.True(audio.Peak > 0.01, $"Audio peak {audio.Peak:F4} - this is silence, not sound.");
            Assert.True(audio.Rms > 0.005, $"Audio RMS {audio.Rms:F4} - this is silence, not sound.");
        }
        finally
        {
            session.Stop();
        }
    }

    [Fact]
    public void Volume_SetBeforePlaybackStarts_SurvivesIntoPlayback()
    {
        using var session = VideoPlaybackSession.Create();
        RequireLibVlcOnWindows(session);

        if (!session.IsReady)
        {
            Assert.NotNull(session.FailureReason);
            return;
        }

        Assert.NotNull(session.UseMemoryVideoOutput(64, 64));

        try
        {
            session.Volume = 55;
            Assert.True(session.Open(ProbeVideo, out var message), message);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (session.TimeMs <= 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
            }

            Assert.True(session.TimeMs > 0, "playback never started, so the re-apply could not be observed");
            Assert.Equal(55, session.Volume);
            _output.WriteLine($"volume held across playback start; libvlc itself reports {session.ActualPlayerVolume}");
        }
        finally
        {
            session.Stop();
        }
    }

    [Fact]
    public void SeekAndVolume_MoveTheRealPlayer()
    {
        using var session = VideoPlaybackSession.Create();
        RequireLibVlcOnWindows(session);

        if (!session.IsReady)
        {
            _output.WriteLine("libvlc unavailable - asserting the failure branch.");
            Assert.NotNull(session.FailureReason);
            return;
        }

        Assert.NotNull(session.UseMemoryVideoOutput(64, 64));

        try
        {
            Assert.True(session.Open(ProbeVideo, out var message), message);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (session.LengthMs <= 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
            }

            Assert.True(session.LengthMs > 0, "libvlc never reported a duration for the probe file.");
            _output.WriteLine($"length reported: {session.LengthMs} ms");

            session.Volume = 40;
            Assert.Equal(40, session.Volume);
            _output.WriteLine($"volume 40 -> libvlc itself reports {session.ActualPlayerVolume} " +
                              "(negative or 0 is normal with no audio output module)");

            session.Volume = 500;
            Assert.Equal(100, session.Volume);

            session.Volume = -20;
            Assert.Equal(0, session.Volume);

            session.Volume = 80;
            Assert.Equal(80, session.Volume);

            var muted = session.ToggleMute();
            Assert.Equal(muted, session.Player!.Mute);
            Assert.False(session.ToggleMute());

            session.SeekToSeconds(2);
            var seekDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            var reached = 0L;
            while (DateTime.UtcNow < seekDeadline)
            {
                reached = Math.Max(reached, session.TimeMs);
                if (reached >= 1500)
                {
                    break;
                }
                Thread.Sleep(100);
            }

            _output.WriteLine($"after seek to 2 s, player time reached {reached} ms");
            Assert.True(reached >= 1500, $"Seek did not move the player - time only reached {reached} ms.");
        }
        finally
        {
            session.Stop();
        }
    }
}
