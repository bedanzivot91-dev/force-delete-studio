using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Pure logic, no process/model involved.</summary>
public class CaptionFormatConverterTests
{
    private static List<CaptionWord> TwoLineTranscript() => new()
    {
        new CaptionWord { OriginalText = "Sve", Start = TimeSpan.FromSeconds(0), End = TimeSpan.FromSeconds(0.4) },
        new CaptionWord { OriginalText = "je", Start = TimeSpan.FromSeconds(0.4), End = TimeSpan.FromSeconds(0.6) },
        new CaptionWord { OriginalText = "bilo", Start = TimeSpan.FromSeconds(0.6), End = TimeSpan.FromSeconds(1.2), LineBreakAfter = true },
        new CaptionWord { OriginalText = "lepo", Start = TimeSpan.FromSeconds(2.0), End = TimeSpan.FromSeconds(2.8), LineBreakAfter = true }
    };

    [Fact]
    public void ToSrt_GroupsWordsIntoLinesWithCorrectTimestamps()
    {
        var srt = CaptionFormatConverter.ToSrt(TwoLineTranscript());

        Assert.Contains("1\n00:00:00,000 --> 00:00:01,200\nSve je bilo", srt);
        Assert.Contains("2\n00:00:02,000 --> 00:00:02,800\nlepo", srt);
    }

    [Fact]
    public void ToVtt_HasWebVttHeaderAndDotSeparatedMilliseconds()
    {
        var vtt = CaptionFormatConverter.ToVtt(TwoLineTranscript());

        Assert.StartsWith("WEBVTT\n\n", vtt);
        Assert.Contains("00:00:00.000 --> 00:00:01.200", vtt);
    }

    [Fact]
    public void ToPlainText_HasNoTimestampsOnePerLine()
    {
        var txt = CaptionFormatConverter.ToPlainText(TwoLineTranscript());

        Assert.Equal("Sve je bilo\nlepo", txt);
    }

    [Fact]
    public void ToLrc_OneTimestampedLinePerGroup()
    {
        var lrc = CaptionFormatConverter.ToLrc(TwoLineTranscript());

        Assert.Contains("[00:00.00]Sve je bilo", lrc);
        Assert.Contains("[00:02.00]lepo", lrc);
    }

    [Fact]
    public void ToAss_EscapesSpecialCharactersAndUsesDialogueLines()
    {
        var words = new List<CaptionWord>
        {
            new() { OriginalText = "{tag}\\test", Start = TimeSpan.FromSeconds(0), End = TimeSpan.FromSeconds(1), LineBreakAfter = true }
        };

        var ass = CaptionFormatConverter.ToAss(words);

        Assert.Contains("[Events]", ass);
        Assert.Contains(@"Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,\{tag\}\\test", ass);
    }

    [Fact]
    public void SrtRoundTrip_PreservesLineTextAndApproximateTiming()
    {
        var srt = CaptionFormatConverter.ToSrt(TwoLineTranscript());

        var reimported = CaptionFormatConverter.FromSrt(srt);
        var reexported = CaptionFormatConverter.ToSrt(reimported);

        Assert.Contains("Sve je bilo", reexported);
        Assert.Contains("lepo", reexported);
        Assert.Contains("00:00:00,000 --> 00:00:01,200", reexported);
        Assert.Contains("00:00:02,000 --> 00:00:02,800", reexported);
    }

    [Fact]
    public void FromSrt_DistributesWordsEvenlyAcrossTheLineSpan()
    {
        const string srt = "1\n00:00:00,000 --> 00:00:02,000\nsve je bilo lepo\n\n";

        var words = CaptionFormatConverter.FromSrt(srt);

        Assert.Equal(4, words.Count);
        Assert.Equal("sve", words[0].OriginalText);
        Assert.Equal(TimeSpan.Zero, words[0].Start);
        Assert.Equal("lepo", words[^1].OriginalText);
        Assert.Equal(TimeSpan.FromSeconds(2), words[^1].End);
        Assert.True(words[^1].LineBreakAfter);
        Assert.All(words, w => Assert.Equal(CaptionWordSource.Manual, w.Source));
    }

    [Fact]
    public void FromVtt_ParsesHeaderAndDotSeparatedTimestamps()
    {
        const string vtt = "WEBVTT\n\n00:00:00.000 --> 00:00:01.500\nzdravo svete\n\n";

        var words = CaptionFormatConverter.FromVtt(vtt);

        Assert.Equal(2, words.Count);
        Assert.Equal("zdravo", words[0].OriginalText);
        Assert.Equal(TimeSpan.FromSeconds(1.5), words[^1].End);
    }

    [Fact]
    public void LrcRoundTrip_PreservesLineTextAndSource()
    {
        var lrc = CaptionFormatConverter.ToLrc(TwoLineTranscript());

        var reimported = CaptionFormatConverter.FromLrc(lrc);

        Assert.Equal("Sve", reimported[0].OriginalText);
        Assert.Equal("lepo", reimported[^1].OriginalText);
        Assert.All(reimported, w => Assert.Equal(CaptionWordSource.Lrc, w.Source));
        Assert.Equal(TimeSpan.Zero, reimported[0].Start);
    }

    [Fact]
    public void JsonRoundTrip_PreservesFullFidelityIncludingSourceAndConfidence()
    {
        var original = new List<CaptionWord>
        {
            new()
            {
                OriginalText = "reč", NormalizedText = "rec", Start = TimeSpan.FromSeconds(1.25),
                End = TimeSpan.FromSeconds(1.75), Confidence = 0.42, Source = CaptionWordSource.WhisperX,
                VerificationStatus = CaptionVerificationStatus.NeedsReview, LineBreakAfter = true
            }
        };

        var json = CaptionFormatConverter.ToJson(original);
        var reimported = CaptionFormatConverter.FromJson(json);

        var word = Assert.Single(reimported);
        Assert.Equal("reč", word.OriginalText);
        Assert.Equal(TimeSpan.FromSeconds(1.25), word.Start);
        Assert.Equal(TimeSpan.FromSeconds(1.75), word.End);
        Assert.Equal(0.42, word.Confidence);
        Assert.Equal(CaptionWordSource.WhisperX, word.Source);
        Assert.Equal(CaptionVerificationStatus.NeedsReview, word.VerificationStatus);
        Assert.True(word.LineBreakAfter);
    }
}
