using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Captures libvlc's decoded audio samples so a test can measure whether there is really sound, on a
/// machine with no sound card.
///
/// It exists as a class with fields, rather than lambdas written inline in a test, for the same reason
/// <c>VlcVideoFrameBuffer</c> does: a delegate handed to libvlc and then left unreferenced is collectable,
/// and libvlc will happily call the freed thunk from its own thread. That is not theoretical - tests here
/// did exactly that, and the test host died with SIGSEGV and no managed exception once allocation
/// patterns changed enough to make the GC actually run during playback. Holding the delegate in a field
/// keeps it alive for as long as this object is.
/// </summary>
public sealed class VlcAudioProbe
{
    private readonly MediaPlayer.LibVLCAudioPlayCb _playCb;
    private readonly object _sync = new();

    private long _frames;
    private double _peak;
    private double _sumSquares;
    private long _samples;

    public VlcAudioProbe()
    {
        _playCb = (IntPtr _, IntPtr samples, uint count, long _) =>
        {
            var shorts = (int)count * 2;   // stereo S16

            lock (_sync)
            {
                _frames += count;

                for (var i = 0; i < shorts; i++)
                {
                    var value = Marshal.ReadInt16(samples, i * 2) / 32768.0;
                    _peak = Math.Max(_peak, Math.Abs(value));
                    _sumSquares += value * value;
                    _samples++;
                }
            }
        };
    }

    public long Frames { get { lock (_sync) { return _frames; } } }

    public double Peak { get { lock (_sync) { return _peak; } } }

    public double Rms
    {
        get
        {
            lock (_sync)
            {
                return _samples > 0 ? Math.Sqrt(_sumSquares / _samples) : 0;
            }
        }
    }

    public double RmsDb => 20 * Math.Log10(Math.Max(Rms, 1e-9));

    /// <summary>Routes the player's audio here. Must be called before playback starts.</summary>
    public void AttachTo(MediaPlayer player)
    {
        player.SetAudioFormat("S16N", 48000, 2);
        player.SetAudioCallbacks(_playCb, null, null, null, null);
    }
}
