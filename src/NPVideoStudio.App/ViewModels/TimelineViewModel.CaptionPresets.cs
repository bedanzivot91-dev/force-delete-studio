using NPVideoStudio.App.Services;
using NPVideoStudio.Domain;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// Connects the existing 24 caption-style presets to the real timeline/export path.
/// Before this file the gallery was intentionally preview-only: choosing a card could not change a
/// project clip. This partial keeps the existing renderer model intact and applies every visual preset
/// field that the renderer can honestly represent today (text colour, outline/shadow and panel).
/// Animation/accent-word behaviour is reported as a remaining gap instead of being falsely advertised.
/// </summary>
public sealed partial class TimelineViewModel
{
    public sealed record CaptionPresetChoice(CaptionStylePreset Preset)
    {
        public override string ToString() => $"{Preset.Name} — {Preset.Theme}";
    }

    public IReadOnlyList<CaptionPresetChoice> CaptionPresetChoices { get; } =
        CaptionStylePresetCatalog.All.Select(p => new CaptionPresetChoice(p)).ToList();

    private CaptionPresetChoice? _selectedCaptionPresetChoice =
        CaptionStylePresetCatalog.All.Count > 0 ? new CaptionPresetChoice(CaptionStylePresetCatalog.All[0]) : null;

    public CaptionPresetChoice? SelectedCaptionPresetChoice
    {
        get => _selectedCaptionPresetChoice;
        set
        {
            if (Equals(_selectedCaptionPresetChoice, value))
            {
                return;
            }

            _selectedCaptionPresetChoice = value;
            OnPropertyChanged();
        }
    }

    private string _captionPresetStatusMessage = string.Empty;
    public string CaptionPresetStatusMessage
    {
        get => _captionPresetStatusMessage;
        private set
        {
            if (_captionPresetStatusMessage == value)
            {
                return;
            }

            _captionPresetStatusMessage = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Applies the selected preset to the selected Caption/Text clip through TimelineEditSession so the
    /// change is persisted and reaches FfmpegFilterGraphBuilder. Nothing is changed when a picture/audio
    /// clip is selected. The preset's named animation is NOT marked as implemented here because the
    /// current renderer has no caption-animation/keyframe model yet.
    /// </summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void ApplySelectedCaptionPreset()
    {
        var choice = SelectedCaptionPresetChoice;
        var selected = SelectedClip;
        if (choice is null)
        {
            CaptionPresetStatusMessage = "Izaberi stil titla.";
            return;
        }

        if (selected is null || selected.Clip.TextContent is null)
        {
            CaptionPresetStatusMessage = "Prvo klikni na tekst/titl klip na timeline-u.";
            return;
        }

        var preset = choice.Preset;
        var clipId = selected.Clip.Id;
        var clip = selected.Clip;

        _session.SetTextStyle(
            clipId,
            clip.FontChoice,
            clip.FontSizePx,
            preset.TextColorHex,
            clip.TextPosition);

        var (hasPanel, panelColor, panelOpacity) = ParsePanel(preset.PanelColorHex, clip);
        var shadowOnly = preset.Animation == CaptionAnimationKind.Shadow;

        _session.SetTextAdvancedStyle(
            clipId,
            new TextAdvancedStyle(
                OutlineColor: shadowOnly ? null : preset.OutlineOrShadowColorHex,
                OutlineWidthPx: shadowOnly ? 0 : Math.Max(2, clip.TextOutlineWidthPx),
                ShadowColor: shadowOnly ? preset.OutlineOrShadowColorHex : null,
                ShadowOffsetPx: shadowOnly ? Math.Max(2, clip.TextShadowOffsetPx) : clip.TextShadowOffsetPx,
                HasBackground: hasPanel,
                BackgroundColor: panelColor,
                BackgroundOpacity: panelOpacity,
                HorizontalAlign: clip.TextHorizontalAlign,
                IsBold: clip.IsTextBold,
                IsItalic: clip.IsTextItalic,
                TextCase: clip.TextCase,
                LineSpacingPx: clip.LineSpacingPx));

        RefreshFromSession();
        SelectedClipId = clipId;

        var missingParts = new List<string>();
        if (preset.Animation is not CaptionAnimationKind.Fade and not CaptionAnimationKind.Shadow and not CaptionAnimationKind.Outline)
        {
            missingParts.Add($"animacija {preset.Animation}");
        }
        if (preset.Granularity is CaptionGranularity.WordByWord or CaptionGranularity.Karaoke)
        {
            missingParts.Add($"granularnost {preset.Granularity}");
        }

        CaptionPresetStatusMessage = missingParts.Count == 0
            ? $"Stil „{preset.Name}” je primenjen na izabrani titl i ide u export."
            : $"Vizuelni deo stila „{preset.Name}” ide u export. Još nije implementirano: {string.Join(", ", missingParts)}.";
    }

    private static (bool HasPanel, string Color, double Opacity) ParsePanel(string? panelHex, TimelineClip clip)
    {
        if (string.IsNullOrWhiteSpace(panelHex))
        {
            return (false, clip.TextBackgroundColor, clip.TextBackgroundOpacity);
        }

        // Catalog uses #AARRGGBB for its translucent theme panels. ffmpeg drawtext receives RGB plus a
        // separate opacity value, so split the alpha rather than handing it an ambiguous 8-digit colour.
        if (panelHex.Length == 9 && panelHex[0] == '#' &&
            byte.TryParse(panelHex.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var alpha))
        {
            return (true, "#" + panelHex[3..], alpha / 255.0);
        }

        return (true, panelHex, 0.5);
    }
}
