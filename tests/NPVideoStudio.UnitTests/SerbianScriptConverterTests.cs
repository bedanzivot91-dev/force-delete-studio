using NPVideoStudio.AI;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Pure logic, no process/model involved. Covers the specific edge cases the spec calls out by name:
/// đ vs dž (single letter vs digraph) and č vs ć (both single letters, easy to conflate), plus
/// digraph case handling (lj/nj/dž collapsing to one Cyrillic letter each way).
/// </summary>
public class SerbianScriptConverterTests
{
    [Fact]
    public void ToLatin_PlainSentence_ConvertsEveryLetter()
    {
        Assert.Equal("Ovo je test", SerbianScriptConverter.ToLatin("Ово је тест"));
    }

    [Fact]
    public void ToCyrillic_PlainSentence_ConvertsEveryLetter()
    {
        Assert.Equal("Ово је тест", SerbianScriptConverter.ToCyrillic("Ovo je test"));
    }

    [Fact]
    public void ToLatin_TitleCaseDigraph_ProducesOnlyFirstLetterCapitalized()
    {
        Assert.Equal("Ljubav", SerbianScriptConverter.ToLatin("Љубав"));
    }

    [Fact]
    public void ToLatin_AllCapsDigraph_ProducesFullyCapitalizedDigraph()
    {
        Assert.Equal("LJUBAV", SerbianScriptConverter.ToLatin("ЉУБАВ"));
    }

    [Fact]
    public void ToCyrillic_TitleCaseDigraph_MapsToSingleCyrillicLetter()
    {
        Assert.Equal("Љубав", SerbianScriptConverter.ToCyrillic("Ljubav"));
    }

    [Fact]
    public void ToCyrillic_AllCapsDigraph_MapsToSingleCyrillicLetter()
    {
        Assert.Equal("ЉУБАВ", SerbianScriptConverter.ToCyrillic("LJUBAV"));
    }

    [Theory]
    [InlineData("Đorđe", "Ђорђе")] // đ: single letter, no digraph
    [InlineData("Džanan", "Џанан")] // dž: digraph
    [InlineData("Ćevapčići", "Ћевапчићи")] // ć vs č together
    [InlineData("Žižak", "Жижак")]
    [InlineData("Šuška", "Шушка")]
    public void RoundTrip_LatinToCyrillicAndBack_PreservesOriginal(string latin, string expectedCyrillic)
    {
        var toCyrillic = SerbianScriptConverter.ToCyrillic(latin);
        Assert.Equal(expectedCyrillic, toCyrillic);

        var backToLatin = SerbianScriptConverter.ToLatin(toCyrillic);
        Assert.Equal(latin, backToLatin);
    }

    [Fact]
    public void ContainsCyrillic_DetectsCyrillicText()
    {
        Assert.True(SerbianScriptConverter.ContainsCyrillic("Ово је тест"));
        Assert.False(SerbianScriptConverter.ContainsCyrillic("Ovo je test"));
    }

    [Fact]
    public void ToLatin_NonSerbianCharacters_PassThroughUnchanged()
    {
        Assert.Equal("Test 123 !? — Hello", SerbianScriptConverter.ToLatin("Test 123 !? — Hello"));
    }
}
