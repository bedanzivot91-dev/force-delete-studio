using System.Globalization;

namespace NPVideoStudio.Media;

/// <summary>
/// Pure Chromaprint fingerprint comparison: parses fpcalc's "-raw" uint32 CSV format and compares two
/// fingerprints via best-alignment average Hamming distance (the same bit-population-count technique
/// Chromaprint-based matchers use), searching a bounded offset range since two recordings of the same
/// song rarely start at byte-identical positions.
///
/// Licensing note (spec Phase 4 "check AGPL licensing before adding any matcher library"): this class
/// only compares fingerprints already computed locally - there is no AcoustID web-service client here at
/// all. The only external tool involved is `fpcalc` (Chromaprint, LGPL-2.1), and it is shelled out to as
/// a separate process (same pattern as ffmpeg/yt-dlp), never linked into this assembly, so LGPL's
/// linking clause does not apply.
/// </summary>
public static class FingerprintMatcher
{
    private const int MaxOffset = 40;
    private const int MinOverlap = 20;

    public static uint[] ParseRaw(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<uint>();
        }

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => uint.Parse(s, CultureInfo.InvariantCulture))
            .ToArray();
    }

    /// <summary>
    /// Best-alignment similarity in [0, 1] between two raw fingerprints (1 = identical), and the item
    /// offset that produced it. Returns 0 similarity when the fingerprints are too short to overlap
    /// meaningfully at any offset.
    /// </summary>
    public static (double Similarity, int Offset) Compare(uint[] a, uint[] b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return (0, 0);
        }

        var bestSimilarity = 0.0;
        var bestOffset = 0;

        for (var offset = -MaxOffset; offset <= MaxOffset; offset++)
        {
            var start = Math.Max(0, -offset);
            var end = Math.Min(a.Length, b.Length - offset);
            var overlap = end - start;
            if (overlap < MinOverlap)
            {
                continue;
            }

            long differingBits = 0;
            for (var i = start; i < end; i++)
            {
                differingBits += System.Numerics.BitOperations.PopCount(a[i] ^ b[i + offset]);
            }

            var similarity = 1.0 - (double)differingBits / (overlap * 32.0);
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestOffset = offset;
            }
        }

        return (bestSimilarity, bestOffset);
    }
}
