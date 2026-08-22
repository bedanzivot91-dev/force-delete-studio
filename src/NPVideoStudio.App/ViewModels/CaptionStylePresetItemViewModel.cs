using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Domain;

namespace NPVideoStudio.App.ViewModels;

/// <summary>One caption-preset card plus an optional real-project apply command.</summary>
public sealed class CaptionStylePresetItemViewModel
{
    public CaptionStylePreset Preset { get; }

    public string Name => Preset.Name;
    public IBrush TextBrush { get; }
    public IBrush AccentBrush { get; }
    public IBrush OutlineOrShadowBrush { get; }
    public IBrush? PanelBrush { get; }
    public bool HasPanel => PanelBrush is not null;
    public ICommand? ApplyCommand { get; }
    public bool CanApply => ApplyCommand is not null;

    public string GranularityLabel => Preset.Granularity switch
    {
        CaptionGranularity.WordByWord => "Reč po reč",
        CaptionGranularity.Karaoke => "Karaoke (aktivna reč)",
        _ => "Red po red"
    };

    public string AnimationLabel => Preset.Animation switch
    {
        CaptionAnimationKind.Pop => "Pop",
        CaptionAnimationKind.Scale => "Uvećanje",
        CaptionAnimationKind.Slide => "Klizanje",
        CaptionAnimationKind.Fade => "Iščezavanje",
        CaptionAnimationKind.Bounce => "Otkucaj",
        CaptionAnimationKind.Glow => "Sjaj",
        CaptionAnimationKind.Outline => "Kontura",
        CaptionAnimationKind.Shadow => "Senka",
        CaptionAnimationKind.BlurPanel => "Stakleni panel",
        CaptionAnimationKind.GradientPanel => "Gradijent panel",
        _ => Preset.Animation.ToString()
    };

    public CaptionStylePresetItemViewModel(CaptionStylePreset preset, Func<Task>? applyAsync = null)
    {
        Preset = preset;
        TextBrush = Brush.Parse(preset.TextColorHex);
        AccentBrush = Brush.Parse(preset.AccentColorHex);
        OutlineOrShadowBrush = Brush.Parse(preset.OutlineOrShadowColorHex);
        PanelBrush = preset.PanelColorHex is null ? null : Brush.Parse(preset.PanelColorHex);
        ApplyCommand = applyAsync is null ? null : new AsyncRelayCommand(applyAsync);
    }
}
