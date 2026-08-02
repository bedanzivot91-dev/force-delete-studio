using System.Globalization;
using Avalonia.Data.Converters;
using NPVideoStudio.Domain;

namespace NPVideoStudio.App.Converters;

/// <summary>Maps domain enums to Serbian labels for display. Falls back to the raw enum name for anything not listed.</summary>
public sealed class EnumLabelConverter : IValueConverter
{
    public static readonly EnumLabelConverter Instance = new();

    private static readonly Dictionary<object, string> Labels = new()
    {
        [AspectRatioPreset.Widescreen16x9] = "16:9 (Horizontalni)",
        [AspectRatioPreset.Vertical9x16] = "9:16 (Vertikalni)",
        [AspectRatioPreset.Square1x1] = "1:1 (Kvadratni)",
        [AspectRatioPreset.Portrait4x5] = "4:5 (Instagram)",
        [AspectRatioPreset.Ultrawide21x9] = "21:9 (Široki)",
        [AspectRatioPreset.Custom] = "Ručno",

        [ResolutionPreset.Hd720] = "720p",
        [ResolutionPreset.FullHd1080] = "1080p (Full HD)",
        [ResolutionPreset.Qhd1440] = "1440p (QHD)",
        [ResolutionPreset.Uhd4K] = "4K (UHD)",
        [ResolutionPreset.Custom] = "Ručno",

        [FrameRatePreset.Fps23_976] = "23.976 fps",
        [FrameRatePreset.Fps24] = "24 fps",
        [FrameRatePreset.Fps25] = "25 fps",
        [FrameRatePreset.Fps29_97] = "29.97 fps",
        [FrameRatePreset.Fps30] = "30 fps",
        [FrameRatePreset.Fps50] = "50 fps",
        [FrameRatePreset.Fps59_94] = "59.94 fps",
        [FrameRatePreset.Fps60] = "60 fps",
        [FrameRatePreset.Custom] = "Ručno",

        [AppTheme.DarkCinematic] = "Dark Cinematic",
        [AppTheme.MinimalLight] = "Minimal Light",
        [AppTheme.ProfessionalStudio] = "Professional Studio",
        [AppTheme.ObsidianNeon] = "Obsidian Neon",
        [AppTheme.ArcticGlass] = "Arctic Glass",
        [AppTheme.CrimsonCyber] = "Crimson Cyber",
        [AppTheme.MidnightPro] = "Midnight Pro",
        [AppTheme.OceanGlass] = "Ocean Glass",
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return null;
        }

        return Labels.TryGetValue(value, out var label) ? label : value.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
