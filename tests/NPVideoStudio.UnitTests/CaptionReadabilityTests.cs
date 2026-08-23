using NPVideoStudio.AI;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Covers the line-breaking/reading-speed rules ported from the user's other project
/// (PROGRAM-ZA-TEKST-U-VIDEO, lyrics-line-breaker.js).</summary>
public class CaptionReadabilityTests
{
    [Fact]
    public void BreakIntoLines_ShortText_StaysOnOneLine()
    {
        var lines = CaptionReadability.BreakIntoLines("Volim te draga moja");

        Assert.Single(lines);
        Assert.Equal("Volim te draga moja", lines[0]);
    }

    [Fact]
    public void BreakIntoLines_LongText_WrapsWithoutEverSplittingAWord()
    {
        var text = "Jos se nisam navikao da te nemam pored sebe svako jutro";

        var lines = CaptionReadability.BreakIntoLines(text, maxCharactersPerLine: 20, maxLines: 4);

        Assert.True(lines.Count > 1);
        // Nothing may be lost or invented by wrapping - the words must survive exactly.
        Assert.Equal(
            text.Split(' '),
            lines.SelectMany(l => l.Split(' ')).ToArray());
        Assert.All(lines.Take(lines.Count - 1), line => Assert.True(line.Length <= 20, $"'{line}' je duži od 20"));
    }

    [Fact]
    public void BreakIntoLines_MoreTextThanMaxLinesAllows_KeepsEveryWordOnTheLastLineRatherThanDroppingIt()
    {
        var text = "prva rec druga rec treca rec cetvrta rec peta rec sesta rec";

        var lines = CaptionReadability.BreakIntoLines(text, maxCharactersPerLine: 10, maxLines: 2);

        Assert.Equal(2, lines.Count);
        Assert.Equal(
            text.Split(' '),
            lines.SelectMany(l => l.Split(' ')).ToArray());
    }

    [Fact]
    public void BreakIntoLines_EmptyText_ReturnsNoLines()
    {
        Assert.Empty(CaptionReadability.BreakIntoLines("   "));
    }

    [Fact]
    public void Assess_ComfortablePace_ReportedAsComfortable()
    {
        // 20 characters over 4 seconds = 5 chars/sec, well under the 15 limit.
        var assessment = CaptionReadability.Assess("Volim te draga moja.", TimeSpan.FromSeconds(4));

        Assert.Equal(ReadingSpeedVerdict.Comfortable, assessment.Verdict);
        Assert.Equal(5.0, assessment.CharactersPerSecond, precision: 2);
    }

    [Fact]
    public void Assess_FarTooMuchTextForTheTime_ReportedAsTooFast()
    {
        var longLine = new string('a', 90);

        var assessment = CaptionReadability.Assess(longLine, TimeSpan.FromSeconds(1));

        Assert.Equal(ReadingSpeedVerdict.TooFast, assessment.Verdict);
        Assert.Equal(90, assessment.CharacterCount);
    }

    [Fact]
    public void BuildWarnings_ReadableLine_ProducesNoWarningsAtAll()
    {
        var warnings = CaptionReadability.BuildWarnings("Volim te draga moja", TimeSpan.FromSeconds(3));

        Assert.Empty(warnings);
    }

    [Fact]
    public void BuildWarnings_TooFastAndTooShort_ReportsBothProblemsInSerbian()
    {
        var warnings = CaptionReadability.BuildWarnings(new string('a', 90), TimeSpan.FromMilliseconds(500));

        Assert.Contains(warnings, w => w.Contains("prekratko"));
        Assert.Contains(warnings, w => w.Contains("prebrzo"));
    }

    [Fact]
    public void BuildWarnings_EmptyText_ReportsMissingTextAndNothingElse()
    {
        var warnings = CaptionReadability.BuildWarnings("", TimeSpan.FromSeconds(3));

        Assert.Single(warnings);
        Assert.Contains("nema tekst", warnings[0]);
    }
}
