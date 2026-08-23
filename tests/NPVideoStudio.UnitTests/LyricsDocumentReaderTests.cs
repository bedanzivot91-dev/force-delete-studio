using NPVideoStudio.App.Services;
using Xunit;

namespace NPVideoStudio.UnitTests;

public class LyricsDocumentReaderTests
{
    [Fact]
    public void WindowsRtf_IsConvertedToSerbianPlainLyrics()
    {
        const string rtf = "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Calibri;}}\\f0 " +
                           "Tvoje ime vi\\'9ae ne izgovaram\\line ne zato \\'9ato sam te preboleo\\line " +
                           "nego \\'9ato ne\\'e6u da vidi\\'9a\\par}";

        var text = LyricsDocumentReader.ExtractRtf(rtf);

        Assert.Contains("Tvoje ime više ne izgovaram", text);
        Assert.Contains("neću da vidiš", text);
        Assert.DoesNotContain("fonttbl", text);
    }
}
