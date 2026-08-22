using NPVideoStudio.AI;
using Xunit;

namespace NPVideoStudio.UnitTests;

public class SrtWriterTests
{
    [Theory]
    [InlineData(0, 0, 0, 0, "00:00:00,000")]
    [InlineData(1, 2, 3, 4, "01:02:03,004")]
    [InlineData(0, 0, 59, 999, "00:00:59,999")]
    public void FormatTimestamp_ProducesSrtFormat(int hours, int minutes, int seconds, int milliseconds, string expected)
    {
        var time = new TimeSpan(0, hours, minutes, seconds, milliseconds);
        Assert.Equal(expected, SrtWriter.FormatTimestamp(time));
    }

    [Fact]
    public void FormatTimestamp_NegativeTime_ClampsToZero()
    {
        Assert.Equal("00:00:00,000", SrtWriter.FormatTimestamp(TimeSpan.FromSeconds(-5)));
    }

    [Fact]
    public void Write_MultipleSegments_ProducesSequentiallyNumberedBlocks()
    {
        var segments = new[]
        {
            new TranscribedSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Zdravo svima"),
            new TranscribedSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4.5), "Ovo je pesma")
        };

        var srt = SrtWriter.Write(segments);

        var expected =
            "1\n00:00:00,000 --> 00:00:02,000\nZdravo svima\n\n" +
            "2\n00:00:02,000 --> 00:00:04,500\nOvo je pesma\n\n";
        Assert.Equal(expected, srt);
    }

    [Fact]
    public void Write_EmptyOrWhitespaceText_IsSkippedAndDoesNotBreakNumbering()
    {
        var segments = new[]
        {
            new TranscribedSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "   "),
            new TranscribedSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "Prva prava linija"),
            new TranscribedSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "Druga prava linija")
        };

        var srt = SrtWriter.Write(segments);

        Assert.StartsWith("1\n00:00:01,000 --> 00:00:02,000\nPrva prava linija\n\n", srt);
        Assert.Contains("2\n00:00:02,000 --> 00:00:03,000\nDruga prava linija\n\n", srt);
    }

    [Fact]
    public void Write_ZeroOrNegativeDurationSegment_IsSkipped()
    {
        var segments = new[]
        {
            new TranscribedSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), "Nulto trajanje"),
            new TranscribedSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "Ispravna linija")
        };

        var srt = SrtWriter.Write(segments);

        Assert.DoesNotContain("Nulto trajanje", srt);
        Assert.Contains("Ispravna linija", srt);
    }

    [Fact]
    public void Write_NoSegments_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, SrtWriter.Write(Array.Empty<TranscribedSegment>()));
    }

    [Fact]
    public void Write_TrimsSurroundingWhitespaceFromText()
    {
        var segments = new[] { new TranscribedSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "  tekst sa razmacima  ") };

        var srt = SrtWriter.Write(segments);

        Assert.Contains("tekst sa razmacima\n\n", srt);
        Assert.DoesNotContain("  tekst sa razmacima  ", srt);
    }
}
