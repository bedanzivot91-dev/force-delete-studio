using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Covers the per-clip picture effects and speed. Filters chosen from ffmpeg's own documented filter set
/// (eq / hue / gblur / vignette / unsharp / negate / hflip), cross-checked against how real open-source
/// colour-grading front-ends drive them (e.g. mifi/VideoGrader, IORoot/Video_FFMPEG-Scriptflow use the
/// same eq brightness/contrast/saturation parameters).
/// </summary>
public class ClipVideoEffectTests
{
    private static TimelineClip Clip() => new()
    {
        MediaAssetId = "a",
        TimelineStartSeconds = 0,
        SourceTrimInSeconds = 0,
        SourceTrimOutSeconds = 5
    };

    [Fact]
    public void UntouchedClip_ProducesNoEffectFiltersAtAll()
    {
        Assert.Equal(string.Empty, FfmpegFilterGraphBuilder.BuildEffectFilters(Clip()));
        Assert.Equal(string.Empty, FfmpegFilterGraphBuilder.BuildSpeedFilter(Clip()));
    }

    [Theory]
    [InlineData(ClipVideoEffect.Grayscale, "hue=s=0")]
    [InlineData(ClipVideoEffect.Blur, "gblur=sigma=8")]
    [InlineData(ClipVideoEffect.Vignette, "vignette")]
    [InlineData(ClipVideoEffect.Sharpen, "unsharp=")]
    [InlineData(ClipVideoEffect.Invert, "negate")]
    [InlineData(ClipVideoEffect.Mirror, "hflip")]
    [InlineData(ClipVideoEffect.Sepia, "colorchannelmixer=")]
    public void EachNamedEffect_MapsToARealFfmpegFilter(ClipVideoEffect effect, string expectedFilter)
    {
        var clip = Clip();
        clip.Effect = effect;

        Assert.Contains(expectedFilter, FfmpegFilterGraphBuilder.BuildEffectFilters(clip));
    }

    [Fact]
    public void ManualAdjustments_ProduceASingleEqFilter()
    {
        var clip = Clip();
        clip.Brightness = 0.2;
        clip.Contrast = 1.3;
        clip.Saturation = 0.8;

        var filters = FfmpegFilterGraphBuilder.BuildEffectFilters(clip);

        Assert.Contains("eq=brightness=0.2:contrast=1.3:saturation=0.8", filters);
        Assert.Equal(1, filters.Split("eq=").Length - 1);
    }

    [Fact]
    public void NamedEffectPlusManualAdjustment_AppliesTheLookFirstSoTheAdjustmentIsNotUndone()
    {
        var clip = Clip();
        clip.Effect = ClipVideoEffect.Grayscale;
        clip.Brightness = 0.3;

        var filters = FfmpegFilterGraphBuilder.BuildEffectFilters(clip);

        Assert.True(
            filters.IndexOf("hue=s=0", StringComparison.Ordinal) < filters.IndexOf("eq=", StringComparison.Ordinal),
            "Izgled mora da ide pre ručnog podešavanja, inače bi ga poništio.");
    }

    [Fact]
    public void OutOfRangeAdjustments_AreClampedToWhatFfmpegAccepts()
    {
        var clip = Clip();
        clip.Brightness = 99;
        clip.Contrast = -5;
        clip.Saturation = 99;

        var filters = FfmpegFilterGraphBuilder.BuildEffectFilters(clip);

        Assert.Contains("brightness=1", filters);
        Assert.Contains("contrast=0", filters);
        Assert.Contains("saturation=3", filters);
    }

    [Theory]
    [InlineData(2.0, ",setpts=PTS/2")]
    [InlineData(0.5, ",setpts=PTS/0.5")]
    public void Speed_ProducesTheSetptsFilter(double speed, string expected)
    {
        var clip = Clip();
        clip.SpeedMultiplier = speed;

        Assert.Equal(expected, FfmpegFilterGraphBuilder.BuildSpeedFilter(clip));
    }

    [Fact]
    public void Speed_IsClampedToASaneRange()
    {
        var clip = Clip();
        clip.SpeedMultiplier = 100;

        Assert.Equal(",setpts=PTS/4", FfmpegFilterGraphBuilder.BuildSpeedFilter(clip));
    }

    [Fact]
    public void EffectOnATimelineClip_ActuallyReachesTheRenderedFilterGraph()
    {
        var asset = new MediaAsset
        {
            Id = "a",
            FilePath = "/tmp/a.mp4",
            Kind = MediaKind.Video,
            Duration = TimeSpan.FromSeconds(10),
            Width = 1920,
            Height = 1080
        };

        var clip = Clip();
        clip.Effect = ClipVideoEffect.Sepia;
        clip.SpeedMultiplier = 2;

        var timeline = new Timeline
        {
            Tracks = { new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = { clip } } }
        };

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { asset });

        Assert.Contains("colorchannelmixer=", plan.FilterComplexArgument);
        Assert.Contains("setpts=PTS/2", plan.FilterComplexArgument);
    }
}
