using NPVideoStudio.Domain;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Covers the platform export presets - TargetPlatform existed and was offered at project
/// creation, but export ignored it entirely, so "TikTok" and "YouTube" produced identical files.</summary>
public class PlatformExportPresetTests
{
    [Fact]
    public void EveryTargetPlatform_HasAPreset_SoNoChoiceSilentlyDoesNothing()
    {
        foreach (var platform in Enum.GetValues<TargetPlatform>())
        {
            var preset = PlatformExportPreset.For(platform);
            Assert.Equal(platform, preset.Platform);
            Assert.False(string.IsNullOrWhiteSpace(preset.DisplayName));
        }
    }

    [Theory]
    [InlineData(TargetPlatform.YouTubeShorts)]
    [InlineData(TargetPlatform.TikTok)]
    [InlineData(TargetPlatform.InstagramReel)]
    [InlineData(TargetPlatform.FacebookReel)]
    public void ShortFormPlatforms_AreVertical(TargetPlatform platform)
    {
        var preset = PlatformExportPreset.For(platform);

        Assert.True(preset.Height > preset.Width, $"{preset.DisplayName} mora biti vertikalan.");
    }

    [Fact]
    public void YouTube_IsWidescreen()
    {
        var preset = PlatformExportPreset.For(TargetPlatform.YouTube);

        Assert.True(preset.Width > preset.Height);
        Assert.Equal(1920, preset.Width);
        Assert.Equal(1080, preset.Height);
    }

    [Fact]
    public void ApplyTo_SetsTheRealExportValues()
    {
        var settings = new RenderSettings { OutputFilePath = "/tmp/izlaz.mp4", Crf = 18, AudioBitrateKbps = 999 };

        PlatformExportPreset.For(TargetPlatform.TikTok).ApplyTo(settings);

        Assert.Equal(21, settings.Crf);
        Assert.Equal(128, settings.AudioBitrateKbps);
        Assert.Equal("medium", settings.Preset);
    }

    [Fact]
    public void ApplyTo_NeverTouchesTheOutputPathOrOverwriteFlag_ThoseAreTheUsersChoice()
    {
        var settings = new RenderSettings { OutputFilePath = "/moj/put/video.mp4", OverwriteConfirmed = true };

        PlatformExportPreset.For(TargetPlatform.YouTube).ApplyTo(settings);

        Assert.Equal("/moj/put/video.mp4", settings.OutputFilePath);
        Assert.True(settings.OverwriteConfirmed);
    }

    [Fact]
    public void CustomPreset_LeavesEverythingExactlyAsTheUserSetIt()
    {
        var settings = new RenderSettings { OutputFilePath = "/tmp/a.mp4", Crf = 12, Preset = "veryslow", AudioBitrateKbps = 320 };

        PlatformExportPreset.Custom.ApplyTo(settings);

        Assert.Equal(12, settings.Crf);
        Assert.Equal("veryslow", settings.Preset);
        Assert.Equal(320, settings.AudioBitrateKbps);
    }

    [Fact]
    public void SummaryLabel_ShowsSizeFrameRateAndQuality()
    {
        Assert.Equal("1080x1920 · 30 fps · CRF 21", PlatformExportPreset.TikTok.SummaryLabel);
    }
}
