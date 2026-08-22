using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Pure logic tests for the Chromaprint raw-fingerprint parser and Hamming-distance comparer -
/// no process, no audio, fully deterministic (spec Phase 4 fingerprint matching core).</summary>
public class FingerprintMatcherTests
{
    [Fact]
    public void ParseRaw_ValidCsv_ReturnsArray()
    {
        var result = FingerprintMatcher.ParseRaw("1,2,3,4294967295");

        Assert.Equal(new uint[] { 1, 2, 3, 4294967295 }, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseRaw_EmptyOrWhitespace_ReturnsEmptyArray(string raw)
    {
        Assert.Empty(FingerprintMatcher.ParseRaw(raw));
    }

    [Fact]
    public void Compare_IdenticalArrays_ReturnsSimilarityOne()
    {
        var a = Enumerable.Range(0, 40).Select(i => (uint)(i * 7919)).ToArray();

        var (similarity, offset) = FingerprintMatcher.Compare(a, a);

        Assert.Equal(1.0, similarity, precision: 6);
        Assert.Equal(0, offset);
    }

    [Fact]
    public void Compare_InvertedBitsAtZeroOffset_ProducesZeroSimilarityAtThatOffset()
    {
        // a[i] ^ ~a[i] is always all-ones (similarity 0 at the *same* index), but Compare searches every
        // offset in range and unrelated sequences can coincidentally correlate better at some other
        // offset - so this only asserts the zero-offset comparison directly, not Compare's overall best.
        var a = Enumerable.Range(0, 40).Select(i => (uint)(i * 7919)).ToArray();
        var b = a.Select(v => ~v).ToArray();

        var (similarityAtZero, _) = FingerprintMatcher.Compare(a[..20], b[..20]);

        // With only 20 items (below the offset search's own overlap floor at nonzero shifts within a
        // 20-length array), the only viable alignment is offset 0, which must be exactly 0 similarity.
        Assert.Equal(0.0, similarityAtZero, precision: 6);
    }

    [Fact]
    public void Compare_ShiftedCopy_FindsCorrectOffsetWithHighSimilarity()
    {
        var source = Enumerable.Range(0, 60).Select(i => (uint)(i * 104729)).ToArray();
        const int shift = 5;

        // b is `source` shifted right by `shift` items: b[shift + i] = source[i]. Compare(a, b) aligns
        // a[i] with b[i + offset], so a[i] == b[i + shift] == source[i] - the correct offset is +shift.
        var b = new uint[source.Length + shift];
        Array.Copy(source, 0, b, shift, source.Length);

        var (similarity, offset) = FingerprintMatcher.Compare(source, b);

        Assert.True(similarity > 0.99, $"Expected near-perfect alignment, got {similarity}");
        Assert.Equal(shift, offset);
    }

    [Fact]
    public void Compare_TooShortToOverlap_ReturnsZero()
    {
        var a = new uint[] { 1, 2, 3 };
        var b = new uint[] { 1, 2, 3 };

        var (similarity, _) = FingerprintMatcher.Compare(a, b);

        Assert.Equal(0.0, similarity);
    }

    [Fact]
    public void Compare_EmptyArray_ReturnsZero()
    {
        var (similarity, offset) = FingerprintMatcher.Compare(Array.Empty<uint>(), new uint[] { 1, 2, 3 });

        Assert.Equal(0.0, similarity);
        Assert.Equal(0, offset);
    }
}
