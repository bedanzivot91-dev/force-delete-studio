namespace NPVideoStudio.Media;

public interface IBeatDetectionService
{
    IReadOnlyList<double> DetectNormalizedPositions(IReadOnlyList<double> peaks);
}

/// <summary>Detects strong audio onsets from the reduced timeline waveform without changing source media.</summary>
public sealed class BeatDetectionService : IBeatDetectionService
{
    public IReadOnlyList<double> DetectNormalizedPositions(IReadOnlyList<double> peaks)
    {
        if (peaks.Count < 8) return Array.Empty<double>();
        var beats = new List<double>();
        var lastIndex = -8;
        for (var i = 2; i < peaks.Count - 2; i++)
        {
            var start = Math.Max(0, i - 12);
            var localAverage = 0d;
            for (var j = start; j < i; j++) localAverage += peaks[j];
            localAverage /= Math.Max(1, i - start);

            var onset = peaks[i] - Math.Max(peaks[i - 1], peaks[i - 2]);
            var isLocalMaximum = peaks[i] >= peaks[i - 1] && peaks[i] > peaks[i + 1];
            var threshold = Math.Max(0.12, localAverage * 1.35);
            if (isLocalMaximum && peaks[i] >= threshold && onset >= 0.06 && i - lastIndex >= 6)
            {
                beats.Add((i + 0.5) / peaks.Count);
                lastIndex = i;
            }
        }
        return beats;
    }
}
