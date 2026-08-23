using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class BeatDetectionServiceTests
{
    [Fact]
    public void Detects_spaced_strong_onsets_and_returns_normalized_positions()
    {
        var peaks = Enumerable.Repeat(0.08, 64).ToArray();
        peaks[10] = 0.85; peaks[26] = 0.92; peaks[45] = 0.78;
        var beats = new BeatDetectionService().DetectNormalizedPositions(peaks);
        Assert.Equal(3, beats.Count);
        Assert.All(beats, value => Assert.InRange(value, 0, 1));
    }

    [Fact]
    public void Ignores_silence_and_nearby_duplicate_peaks()
    {
        var peaks = Enumerable.Repeat(0.02, 40).ToArray();
        peaks[15] = 0.8; peaks[18] = 0.9;
        var beats = new BeatDetectionService().DetectNormalizedPositions(peaks);
        Assert.Single(beats);
    }
}
