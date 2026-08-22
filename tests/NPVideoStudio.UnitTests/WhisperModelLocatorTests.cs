using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Pure logic, no process/download involved.</summary>
public class WhisperModelLocatorTests
{
    [Fact]
    public void ResolveModelPath_OverrideExists_ReturnsOverride()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var result = WhisperModelLocator.ResolveModelPath(tempFile, "/some/default/path/ggml-tiny.bin");
            Assert.Equal(tempFile, result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ResolveModelPath_OverrideDoesNotExist_FallsBackToDefault()
    {
        var result = WhisperModelLocator.ResolveModelPath("/does/not/exist/ggml-tiny.bin", "/some/default/path/ggml-tiny.bin");

        // No Tools\whisper-models\ggml-tiny.bin exists next to this test binary, so it must fall back to
        // the given default AppData path rather than a nonexistent override or a bundled copy.
        Assert.Equal("/some/default/path/ggml-tiny.bin", result);
    }

    [Fact]
    public void ResolveModelPath_NoOverrideNoBundledCopy_ReturnsDefault()
    {
        var result = WhisperModelLocator.ResolveModelPath(null, "/some/default/path/ggml-tiny.bin");

        Assert.Equal("/some/default/path/ggml-tiny.bin", result);
    }
}
