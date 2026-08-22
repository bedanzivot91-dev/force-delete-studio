namespace NPVideoStudio.Domain;

/// <summary>Resolved video format for a project: aspect ratio, resolution and frame rate.</summary>
public sealed class ProjectFormat
{
    public AspectRatioPreset AspectRatio { get; set; } = AspectRatioPreset.Widescreen16x9;
    public ResolutionPreset Resolution { get; set; } = ResolutionPreset.FullHd1080;
    public FrameRatePreset FrameRate { get; set; } = FrameRatePreset.Fps30;

    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public double Fps { get; set; } = 30.0;

    public static double ResolveFps(FrameRatePreset preset) => preset switch
    {
        FrameRatePreset.Fps23_976 => 24000.0 / 1001.0,
        FrameRatePreset.Fps24 => 24.0,
        FrameRatePreset.Fps25 => 25.0,
        FrameRatePreset.Fps29_97 => 30000.0 / 1001.0,
        FrameRatePreset.Fps30 => 30.0,
        FrameRatePreset.Fps50 => 50.0,
        FrameRatePreset.Fps59_94 => 60000.0 / 1001.0,
        FrameRatePreset.Fps60 => 60.0,
        _ => 30.0
    };

    public static (int Width, int Height) ResolveResolution(ResolutionPreset preset, AspectRatioPreset aspect)
    {
        int longSide = preset switch
        {
            ResolutionPreset.Hd720 => 1280,
            ResolutionPreset.FullHd1080 => 1920,
            ResolutionPreset.Qhd1440 => 2560,
            ResolutionPreset.Uhd4K => 3840,
            _ => 1920
        };

        return aspect switch
        {
            AspectRatioPreset.Widescreen16x9 => (longSide, (int)Math.Round(longSide * 9.0 / 16.0)),
            AspectRatioPreset.Vertical9x16 => ((int)Math.Round(longSide * 9.0 / 16.0), longSide),
            AspectRatioPreset.Square1x1 => (longSide, longSide),
            AspectRatioPreset.Portrait4x5 => (longSide, (int)Math.Round(longSide * 5.0 / 4.0)),
            AspectRatioPreset.Ultrawide21x9 => (longSide, (int)Math.Round(longSide * 9.0 / 21.0)),
            _ => (longSide, (int)Math.Round(longSide * 9.0 / 16.0))
        };
    }

    public static ProjectFormat FromPresets(AspectRatioPreset aspect, ResolutionPreset resolution, FrameRatePreset frameRate)
    {
        var (w, h) = ResolveResolution(resolution, aspect);
        return new ProjectFormat
        {
            AspectRatio = aspect,
            Resolution = resolution,
            FrameRate = frameRate,
            Width = w,
            Height = h,
            Fps = ResolveFps(frameRate)
        };
    }

    public static ProjectFormat ForPlatform(TargetPlatform platform) => platform switch
    {
        TargetPlatform.YouTube => FromPresets(AspectRatioPreset.Widescreen16x9, ResolutionPreset.FullHd1080, FrameRatePreset.Fps30),
        TargetPlatform.YouTubeShorts => FromPresets(AspectRatioPreset.Vertical9x16, ResolutionPreset.FullHd1080, FrameRatePreset.Fps30),
        TargetPlatform.TikTok => FromPresets(AspectRatioPreset.Vertical9x16, ResolutionPreset.FullHd1080, FrameRatePreset.Fps30),
        TargetPlatform.InstagramReel => FromPresets(AspectRatioPreset.Vertical9x16, ResolutionPreset.FullHd1080, FrameRatePreset.Fps30),
        TargetPlatform.FacebookReel => FromPresets(AspectRatioPreset.Vertical9x16, ResolutionPreset.FullHd1080, FrameRatePreset.Fps30),
        _ => FromPresets(AspectRatioPreset.Widescreen16x9, ResolutionPreset.FullHd1080, FrameRatePreset.Fps30)
    };

    /// <summary>Human-readable orientation, used by the UI to clearly show vertical/horizontal/square.</summary>
    public string Orientation
    {
        get
        {
            if (Width == Height) return "Kvadratni";
            return Width > Height ? "Horizontalni" : "Vertikalni";
        }
    }
}
