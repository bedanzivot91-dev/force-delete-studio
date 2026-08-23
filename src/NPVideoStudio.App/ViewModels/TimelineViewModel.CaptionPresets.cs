using NPVideoStudio.App.Services;
using NPVideoStudio.Domain;

namespace NPVideoStudio.App.ViewModels;

/// <summary>
/// Connects the existing 24 caption-style presets to the real timeline/export path.
/// Before this file the gallery was intentionally preview-only: choosing a card could not change a
/// project clip. This partial keeps the existing renderer model intact and applies every visual preset
/// field that the renderer can honestly represent today (text colour, outline/shadow and panel).
/// Animation and word/karaoke granularity are persisted on the clip and consumed by the FFmpeg renderer.
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
    /// renderer applies the temporal animation and selected word granularity during export.
    /// </summary>
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

        if (!_session.ApplyCaptionStylePreset(clipId, preset))
        {
            CaptionPresetStatusMessage = "Stil nije mogao da se primeni na izabrani klip.";
            return;
        }

        RefreshFromSession();
        SelectedClipId = clipId;

        CaptionPresetStatusMessage = $"Stil „{preset.Name}” je primenjen sa animacijom i ide u export.";
    }
}
