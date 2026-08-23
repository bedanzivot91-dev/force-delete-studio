using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public class YouTubeDownloadHelpersTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc123XYZ_")]
    [InlineData("https://youtube.com/watch?v=abc123XYZ_")]
    [InlineData("https://m.youtube.com/watch?v=abc123XYZ_")]
    [InlineData("https://music.youtube.com/watch?v=abc123XYZ_")]
    [InlineData("https://youtu.be/abc123XYZ_")]
    public void IsYouTubeUrl_ValidYouTubeHost_ReturnsTrue(string url)
    {
        Assert.True(YouTubeDownloadHelpers.IsYouTubeUrl(url));
    }

    [Theory]
    [InlineData("https://vimeo.com/12345")]
    [InlineData("https://example.com/video")]
    [InlineData("not a url")]
    [InlineData("ftp://youtube.com/watch?v=abc123")]
    public void IsYouTubeUrl_NonYouTubeOrInvalid_ReturnsFalse(string url)
    {
        Assert.False(YouTubeDownloadHelpers.IsYouTubeUrl(url));
    }

    [Fact]
    public void ValidateYouTubeUrl_ValidUrl_DoesNotThrow()
    {
        var exception = Record.Exception(() => YouTubeDownloadHelpers.ValidateYouTubeUrl("https://www.youtube.com/watch?v=abc123"));
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateYouTubeUrl_NonYouTubeHost_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => YouTubeDownloadHelpers.ValidateYouTubeUrl("https://vimeo.com/12345"));
    }

    [Fact]
    public void ValidateYouTubeUrl_MalformedUrl_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => YouTubeDownloadHelpers.ValidateYouTubeUrl("definitely not a url"));
    }

    [Theory]
    [InlineData("Moja Pesma", "Moja Pesma")]
    [InlineData("Naslov Deo 1/2", "Naslov Deo 1_2")]
    [InlineData("   ", "preuzeta_pesma")]
    public void SanitizeFileName_RemovesInvalidCharsAndHandlesBlank(string input, string expected)
    {
        // '/' is invalid on every platform Path.GetInvalidFileNameChars() targets; other characters
        // (e.g. ':') are only invalid on Windows, so asserting on those would be flaky on Linux.
        Assert.Equal(expected, YouTubeDownloadHelpers.SanitizeFileName(input));
    }

    [Fact]
    public void MakeUnique_NoCollision_ReturnsSamePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"npvs_yt_test_{Guid.NewGuid():N}.mp3");
        Assert.Equal(path, YouTubeDownloadHelpers.MakeUnique(path));
    }

    [Fact]
    public void MakeUnique_PathAlreadyExists_AppendsCounter()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"npvs_yt_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "pesma.mp3");
            File.WriteAllText(path, "x");

            var unique = YouTubeDownloadHelpers.MakeUnique(path);

            Assert.Equal(Path.Combine(directory, "pesma (1).mp3"), unique);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
