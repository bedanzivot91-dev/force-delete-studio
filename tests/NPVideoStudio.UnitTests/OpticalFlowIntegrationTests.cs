using System.Diagnostics;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class OpticalFlowIntegrationTests
{
    [Fact]
    public void SmoothSlowMotion_IsVisibleThroughExistingEffectsCatalog()
    {
        Assert.Contains(ClipVideoEffect.SmoothSlowMotion, Enum.GetValues<ClipVideoEffect>());
    }

    [Fact]
    public void SmoothSlowMotion_BuildsMotionCompensatedInterpolationFilter()
    {
        var clip = new TimelineClip
        {
            MediaAssetId = "video",
            SourceTrimOutSeconds = 2,
            SpeedMultiplier = 0.5,
            Effect = ClipVideoEffect.SmoothSlowMotion
        };

        var filter = FfmpegFilterGraphBuilder.BuildEffectFilters(clip);

        Assert.Contains("minterpolate=fps=60", filter, StringComparison.Ordinal);
        Assert.Contains("mi_mode=mci", filter, StringComparison.Ordinal);
        Assert.Contains("mc_mode=aobmc", filter, StringComparison.Ordinal);
        Assert.Contains("me_mode=bidir", filter, StringComparison.Ordinal);
    }

    [Fact]
    public void SmoothSlowMotion_RunsInRealWindowsFfmpeg()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in new[]
        {
            "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=12:duration=1",
            "-vf", "minterpolate=fps=30:mi_mode=mci:mc_mode=aobmc:me_mode=bidir:me=epzs:vsbmc=1",
            "-frames:v", "20", "-f", "null", "-"
        })
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi);
        Assert.NotNull(process);
        var stderr = process!.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, stderr);
    }
}
