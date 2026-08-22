using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;

namespace NPVideoStudio.App.ViewModels;

/// <summary>One clip row in the timeline UI - wraps a live <see cref="TimelineClip"/> plus the per-clip commands the parent <see cref="TimelineViewModel"/> wires up.</summary>
public sealed class TimelineClipItemViewModel : ViewModelBase
{
    /// <summary>(clipId, fontChoice, fontSizePx, textColor, textPosition) - deliberately passed as plain
    /// values rather than mutating <see cref="Clip"/> directly here first: <see cref="Clip"/> is the same
    /// live object the owning <c>TimelineEditSession</c> holds internally, so mutating it before the
    /// session's own SetTextStyle call would make its undo snapshot capture the *new* value as if it were
    /// the "before" state, silently breaking undo for style edits.</summary>
    private readonly Action<string, CaptionFontChoice, int, string, CaptionTextPosition>? _onTextStyleChanged;
    private readonly Action<string, CaptionFontChoice, string?, string?>? _onTextFontChanged;

    /// <summary>(clipId, transitionType, durationSeconds) - same reasoning as <see cref="_onTextStyleChanged"/>
    /// above: goes through the owning session's SetTransition so undo captures the correct "before" state.</summary>
    private readonly Action<string, ClipTransitionType, double>? _onTransitionChanged;

    /// <summary>(clipId, newText) - same reasoning as the callbacks above.</summary>
    private readonly Action<string, string>? _onTextContentChanged;

    /// <summary>(clipId, style) - same reasoning as the callbacks above.</summary>
    private readonly Action<string, TextAdvancedStyle>? _onAdvancedStyleChanged;

    /// <summary>(clipId, scalePercent, xPercent, yPercent, opacity) - same reasoning as the callbacks
    /// above: routed through the owning session so one undo takes the whole placement change back.</summary>
    private readonly Action<string, double, double, double, double>? _onLayerPlacementChanged;

    /// <summary>(clipId, effect, brightness, contrast, saturation, speed) - routed through the session so
    /// one undo takes the whole effect change back.</summary>
    private readonly Action<string, ClipVideoEffect, double, double, double, double>? _onEffectsChanged;
    private readonly Action<string, SpeedCurvePreset>? _onSpeedCurvePresetChanged;
    private readonly Action<string, ClipTransformSettings>? _onTransformChanged;
    private readonly Action<string, ClipCompositingSettings>? _onCompositingChanged;
    private readonly Func<double>? _getPlayheadSeconds;
    private readonly Action<string, ClipKeyframeProperty, double, double, ClipKeyframeEasing>? _onKeyframeUpsert;
    private readonly Action<string, ClipKeyframeProperty, double>? _onKeyframeRemove;
    private readonly Action<string, double>? _onTrimInChanged;
    private readonly Action<string, double>? _onTrimOutChanged;

    public TimelineClip Clip { get; }
    public string TrackId { get; }

    /// <summary>True only for a clip on a Video-kind track - transitions only make sense between two
    /// video clips, never on caption/text/audio/image-overlay tracks.</summary>
    public bool IsVideoClip { get; }

    private readonly string _mediaLabel;
    public string Label => IsTextClip ? TextContent : _mediaLabel;
    public double StartSeconds => Clip.TimelineStartSeconds;
    public double DurationSeconds => Clip.TimelineDurationSeconds;

    /// <summary>How many pixels one second of timeline occupies - the lane's zoom level. Set by the owning
    /// track so every clip on screen uses the same scale.</summary>
    private double _pixelsPerSecond = 40;
    public double PixelsPerSecond
    {
        get => _pixelsPerSecond;
        set
        {
            if (Math.Abs(_pixelsPerSecond - value) < 0.001) return;
            _pixelsPerSecond = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PixelLeft));
            OnPropertyChanged(nameof(PixelWidth));
        }
    }

    /// <summary>Where this clip's left edge sits in the lane, in pixels.</summary>
    public double PixelLeft => StartSeconds * PixelsPerSecond;

    /// <summary>How wide this clip is drawn. Floored at a few pixels so a very short clip is still big
    /// enough to see and grab with the mouse instead of collapsing to an invisible sliver.</summary>
    public double PixelWidth => Math.Max(6, DurationSeconds * PixelsPerSecond);
    public string TimingLabel => $"{FormatTime(Clip.TimelineStartSeconds)} → {FormatTime(Clip.TimelineEndSeconds)}";
    public bool IsMuted => Clip.IsMuted;
    public bool HasFadeIn => Clip.FadeInSeconds > 0;
    public bool HasFadeOut => Clip.FadeOutSeconds > 0;

    /// <summary>True only when this clip points at a real media asset whose source duration is known.
    /// Text/caption clips do not expose source trim controls because they have no source media file.</summary>
    public bool HasSourceMedia => Clip.MediaAssetId is not null && SourceDurationSeconds > 0.05;

    /// <summary>Actual source-file duration supplied by the project media library. It bounds Trim Out so
    /// the UI cannot create an ffmpeg seek beyond the source file.</summary>
    public double SourceDurationSeconds { get; }
    public double MaxTrimInSeconds => Math.Max(0, Math.Min(TrimOutSeconds, SourceDurationSeconds) - 0.05);
    public double MinTrimOutSeconds => Math.Min(SourceDurationSeconds, TrimInSeconds + 0.05);

    public double TrimInSeconds
    {
        get => Clip.SourceTrimInSeconds;
        set
        {
            if (!HasSourceMedia) return;
            var clamped = Math.Clamp(value, 0, MaxTrimInSeconds);
            if (Math.Abs(Clip.SourceTrimInSeconds - clamped) < 1e-6) return;
            _onTrimInChanged?.Invoke(Clip.Id, clamped);
        }
    }

    public double TrimOutSeconds
    {
        get => Clip.SourceTrimOutSeconds;
        set
        {
            if (!HasSourceMedia) return;
            var clamped = Math.Clamp(value, MinTrimOutSeconds, SourceDurationSeconds);
            if (Math.Abs(Clip.SourceTrimOutSeconds - clamped) < 1e-6) return;
            _onTrimOutChanged?.Invoke(Clip.Id, clamped);
        }
    }

    /// <summary>True for a Caption/Text clip - the font/size/color/position controls below only make
    /// sense (and are only shown in the UI) for these.</summary>
    public bool IsTextClip => Clip.TextContent is not null;

    /// <summary>True for a clip that is laid OVER the video underneath (an ImageOverlay track). Only these
    /// get the size/position/transparency controls - on the background video track those values are
    /// meaningless, since the base layer always fills the frame.</summary>
    public bool IsOverlayClip { get; }
    public bool IsPictureClip => IsVideoClip || IsOverlayClip;
    public bool IsAudioClip { get; }
    public bool SupportsKeyframes => IsPictureClip || IsTextClip;

    private bool _isSelected;

    /// <summary>Drives the lane highlight so the user can see which clip the keyboard will act on.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        private set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Called by the owning timeline when the selection changes.</summary>
    public void RefreshSelection(string? selectedClipId) => IsSelected = Clip.Id == selectedClipId;

    /// <summary>Overlay width as a percentage of the finished frame's width.</summary>
    public double ScalePercent
    {
        get => Clip.ScalePercent;
        set { if (Math.Abs(Clip.ScalePercent - value) < 1e-6) return; _onLayerPlacementChanged?.Invoke(Clip.Id, value, PositionXPercent, PositionYPercent, LayerOpacity); }
    }

    /// <summary>Horizontal position of the overlay's centre: 0 = left edge, 50 = middle, 100 = right edge.</summary>
    public double PositionXPercent
    {
        get => Clip.PositionXPercent;
        set { if (Math.Abs(Clip.PositionXPercent - value) < 1e-6) return; _onLayerPlacementChanged?.Invoke(Clip.Id, ScalePercent, value, PositionYPercent, LayerOpacity); }
    }

    /// <summary>Vertical position of the overlay's centre: 0 = top edge, 50 = middle, 100 = bottom edge.</summary>
    public double PositionYPercent
    {
        get => Clip.PositionYPercent;
        set { if (Math.Abs(Clip.PositionYPercent - value) < 1e-6) return; _onLayerPlacementChanged?.Invoke(Clip.Id, ScalePercent, PositionXPercent, value, LayerOpacity); }
    }

    /// <summary>Shown as a 0-100 percentage in the UI; stored as 0-1 on the clip, which is what ffmpeg's
    /// alpha channel expects.</summary>
    public int LayerOpacityPercent
    {
        get => (int)Math.Round(Clip.Opacity * 100);
        set
        {
            var opacity = Math.Clamp(value, 0, 100) / 100.0;
            if (Math.Abs(Clip.Opacity - opacity) < 1e-6) return;
            _onLayerPlacementChanged?.Invoke(Clip.Id, ScalePercent, PositionXPercent, PositionYPercent, opacity);
        }
    }

    private double LayerOpacity => Clip.Opacity;

    // --- Picture effects (available on every picture clip, layer or background) --------------------

    public IReadOnlyList<ClipVideoEffect> AvailableEffects { get; } = Enum.GetValues<ClipVideoEffect>();

    public ClipVideoEffect Effect
    {
        get => Clip.Effect;
        set { if (Clip.Effect == value) return; _onEffectsChanged?.Invoke(Clip.Id, value, Brightness, Contrast, Saturation, SpeedMultiplier); }
    }

    public double Brightness
    {
        get => Clip.Brightness;
        set { if (Math.Abs(Clip.Brightness - value) < 1e-6) return; _onEffectsChanged?.Invoke(Clip.Id, Effect, value, Contrast, Saturation, SpeedMultiplier); }
    }

    public double Contrast
    {
        get => Clip.Contrast;
        set { if (Math.Abs(Clip.Contrast - value) < 1e-6) return; _onEffectsChanged?.Invoke(Clip.Id, Effect, Brightness, value, Saturation, SpeedMultiplier); }
    }

    public double Saturation
    {
        get => Clip.Saturation;
        set { if (Math.Abs(Clip.Saturation - value) < 1e-6) return; _onEffectsChanged?.Invoke(Clip.Id, Effect, Brightness, Contrast, value, SpeedMultiplier); }
    }

    /// <summary>0.5 = slow motion, 2 = double speed. Changing it explicitly disables a velocity curve.</summary>
    public double SpeedMultiplier
    {
        get => Clip.SpeedMultiplier;
        set { if (Math.Abs(Clip.SpeedMultiplier - value) < 1e-6) return; _onEffectsChanged?.Invoke(Clip.Id, Effect, Brightness, Contrast, Saturation, value); }
    }

    public IReadOnlyList<SpeedCurvePreset> AvailableSpeedCurvePresets { get; } = Enum.GetValues<SpeedCurvePreset>();
    public bool CanUseSpeedCurve => HasSourceMedia && !IsTextClip && !Clip.IsReversed && !Clip.IsFreezeFrame;
    public SpeedCurvePreset SpeedCurvePreset
    {
        get => Clip.SpeedCurvePreset;
        set
        {
            if (Clip.SpeedCurvePreset == value) return;
            _onSpeedCurvePresetChanged?.Invoke(Clip.Id, value);
        }
    }

    private ClipTransformSettings CurrentTransform() => new(
        RotationDegrees, FlipHorizontal, FlipVertical,
        CropLeftPercent, CropTopPercent, CropRightPercent, CropBottomPercent,
        IsReversed, IsFreezeFrame, ChromaKeyEnabled, ChromaKeyColor,
        ChromaKeySimilarity, ChromaKeyBlend);

    private void PushTransform(Func<ClipTransformSettings, ClipTransformSettings> mutate) =>
        _onTransformChanged?.Invoke(Clip.Id, mutate(CurrentTransform()));

    public double RotationDegrees
    {
        get => Clip.RotationDegrees;
        set { if (Math.Abs(Clip.RotationDegrees - value) < 1e-6) return; PushTransform(s => s with { RotationDegrees = value }); }
    }
    public bool FlipHorizontal
    {
        get => Clip.FlipHorizontal;
        set { if (Clip.FlipHorizontal == value) return; PushTransform(s => s with { FlipHorizontal = value }); }
    }
    public bool FlipVertical
    {
        get => Clip.FlipVertical;
        set { if (Clip.FlipVertical == value) return; PushTransform(s => s with { FlipVertical = value }); }
    }
    public double CropLeftPercent
    {
        get => Clip.CropLeftPercent;
        set { if (Math.Abs(Clip.CropLeftPercent - value) < 1e-6) return; PushTransform(s => s with { CropLeftPercent = value }); }
    }
    public double CropTopPercent
    {
        get => Clip.CropTopPercent;
        set { if (Math.Abs(Clip.CropTopPercent - value) < 1e-6) return; PushTransform(s => s with { CropTopPercent = value }); }
    }
    public double CropRightPercent
    {
        get => Clip.CropRightPercent;
        set { if (Math.Abs(Clip.CropRightPercent - value) < 1e-6) return; PushTransform(s => s with { CropRightPercent = value }); }
    }
    public double CropBottomPercent
    {
        get => Clip.CropBottomPercent;
        set { if (Math.Abs(Clip.CropBottomPercent - value) < 1e-6) return; PushTransform(s => s with { CropBottomPercent = value }); }
    }
    public bool IsReversed
    {
        get => Clip.IsReversed;
        set { if (Clip.IsReversed == value) return; PushTransform(s => s with { IsReversed = value }); }
    }
    public bool IsFreezeFrame
    {
        get => Clip.IsFreezeFrame;
        set { if (Clip.IsFreezeFrame == value) return; PushTransform(s => s with { IsFreezeFrame = value }); }
    }
    public bool ChromaKeyEnabled
    {
        get => Clip.ChromaKeyEnabled;
        set { if (Clip.ChromaKeyEnabled == value) return; PushTransform(s => s with { ChromaKeyEnabled = value }); }
    }
    public string ChromaKeyColor
    {
        get => Clip.ChromaKeyColor;
        set { if (Clip.ChromaKeyColor == value || string.IsNullOrWhiteSpace(value)) return; PushTransform(s => s with { ChromaKeyColor = value }); }
    }
    public double ChromaKeySimilarity
    {
        get => Clip.ChromaKeySimilarity;
        set { if (Math.Abs(Clip.ChromaKeySimilarity - value) < 1e-6) return; PushTransform(s => s with { ChromaKeySimilarity = value }); }
    }
    public double ChromaKeyBlend
    {
        get => Clip.ChromaKeyBlend;
        set { if (Math.Abs(Clip.ChromaKeyBlend - value) < 1e-6) return; PushTransform(s => s with { ChromaKeyBlend = value }); }
    }
    public IReadOnlyList<ClipMaskType> AvailableMaskTypes { get; } = Enum.GetValues<ClipMaskType>();
    public IReadOnlyList<ClipBlendMode> AvailableBlendModes { get; } = Enum.GetValues<ClipBlendMode>();

    private ClipCompositingSettings CurrentCompositing() => new(
        MaskType, MaskCenterXPercent, MaskCenterYPercent,
        MaskWidthPercent, MaskHeightPercent, MaskFeatherPercent,
        MaskRotationDegrees, MaskInvert, BlendMode);

    private void PushCompositing(Func<ClipCompositingSettings, ClipCompositingSettings> mutate) =>
        _onCompositingChanged?.Invoke(Clip.Id, mutate(CurrentCompositing()));

    public ClipMaskType MaskType
    {
        get => Clip.MaskType;
        set { if (Clip.MaskType == value) return; PushCompositing(s => s with { MaskType = value }); }
    }
    public double MaskCenterXPercent
    {
        get => Clip.MaskCenterXPercent;
        set { if (Math.Abs(Clip.MaskCenterXPercent - value) < 1e-6) return; PushCompositing(s => s with { MaskCenterXPercent = value }); }
    }
    public double MaskCenterYPercent
    {
        get => Clip.MaskCenterYPercent;
        set { if (Math.Abs(Clip.MaskCenterYPercent - value) < 1e-6) return; PushCompositing(s => s with { MaskCenterYPercent = value }); }
    }
    public double MaskWidthPercent
    {
        get => Clip.MaskWidthPercent;
        set { if (Math.Abs(Clip.MaskWidthPercent - value) < 1e-6) return; PushCompositing(s => s with { MaskWidthPercent = value }); }
    }
    public double MaskHeightPercent
    {
        get => Clip.MaskHeightPercent;
        set { if (Math.Abs(Clip.MaskHeightPercent - value) < 1e-6) return; PushCompositing(s => s with { MaskHeightPercent = value }); }
    }
    public double MaskFeatherPercent
    {
        get => Clip.MaskFeatherPercent;
        set { if (Math.Abs(Clip.MaskFeatherPercent - value) < 1e-6) return; PushCompositing(s => s with { MaskFeatherPercent = value }); }
    }
    public double MaskRotationDegrees
    {
        get => Clip.MaskRotationDegrees;
        set { if (Math.Abs(Clip.MaskRotationDegrees - value) < 1e-6) return; PushCompositing(s => s with { MaskRotationDegrees = value }); }
    }
    public bool MaskInvert
    {
        get => Clip.MaskInvert;
        set { if (Clip.MaskInvert == value) return; PushCompositing(s => s with { MaskInvert = value }); }
    }
    public ClipBlendMode BlendMode
    {
        get => Clip.BlendMode;
        set { if (Clip.BlendMode == value) return; PushCompositing(s => s with { BlendMode = value }); }
    }
    private static readonly ClipKeyframeProperty[] PictureKeyframeProperties = Enum.GetValues<ClipKeyframeProperty>();
    private static readonly ClipKeyframeProperty[] TextKeyframeProperties =
    {
        ClipKeyframeProperty.PositionX,
        ClipKeyframeProperty.PositionY,
        ClipKeyframeProperty.Scale,
        ClipKeyframeProperty.Opacity
    };

    public IReadOnlyList<ClipKeyframeProperty> AvailableKeyframeProperties =>
        IsTextClip ? TextKeyframeProperties : PictureKeyframeProperties;
    public IReadOnlyList<ClipKeyframeEasing> AvailableKeyframeEasings { get; } = Enum.GetValues<ClipKeyframeEasing>();

    private ClipKeyframeProperty _selectedKeyframeProperty = ClipKeyframeProperty.PositionX;
    public ClipKeyframeProperty SelectedKeyframeProperty
    {
        get => _selectedKeyframeProperty;
        set
        {
            if (_selectedKeyframeProperty == value) return;
            _selectedKeyframeProperty = value;
            OnPropertyChanged();
            _keyframeValue = CurrentKeyframeValue(value);
            OnPropertyChanged(nameof(KeyframeValue));
            OnPropertyChanged(nameof(KeyframeValueLabel));
            OnPropertyChanged(nameof(KeyframeValueMinimum));
            OnPropertyChanged(nameof(KeyframeValueMaximum));
            OnPropertyChanged(nameof(KeyframeValueIncrement));
        }
    }

    private ClipKeyframeEasing _selectedKeyframeEasing = ClipKeyframeEasing.EaseInOut;
    public ClipKeyframeEasing SelectedKeyframeEasing
    {
        get => _selectedKeyframeEasing;
        set { if (_selectedKeyframeEasing == value) return; _selectedKeyframeEasing = value; OnPropertyChanged(); }
    }

    private double _keyframeValue = 50;
    public double KeyframeValue
    {
        get => _keyframeValue;
        set
        {
            var clamped = ClipKeyframeEvaluator.ClampValue(SelectedKeyframeProperty, value);
            if (Math.Abs(_keyframeValue - clamped) < 1e-9) return;
            _keyframeValue = clamped;
            OnPropertyChanged();
        }
    }

    public string KeyframeValueLabel => SelectedKeyframeProperty switch
    {
        ClipKeyframeProperty.PositionX => "X pozicija (%)",
        ClipKeyframeProperty.PositionY => "Y pozicija (%)",
        ClipKeyframeProperty.Scale => "Veličina (%)",
        ClipKeyframeProperty.Rotation => "Rotacija (°)",
        ClipKeyframeProperty.Opacity => "Providnost (0-1)",
        _ => "Vrednost"
    };
    public double KeyframeValueMinimum => SelectedKeyframeProperty switch
    {
        ClipKeyframeProperty.PositionX or ClipKeyframeProperty.PositionY => -200,
        ClipKeyframeProperty.Scale => 1,
        ClipKeyframeProperty.Rotation => -3600,
        ClipKeyframeProperty.Opacity => 0,
        _ => -10000
    };
    public double KeyframeValueMaximum => SelectedKeyframeProperty switch
    {
        ClipKeyframeProperty.PositionX or ClipKeyframeProperty.PositionY => 300,
        ClipKeyframeProperty.Scale => 1000,
        ClipKeyframeProperty.Rotation => 3600,
        ClipKeyframeProperty.Opacity => 1,
        _ => 10000
    };
    public double KeyframeValueIncrement => SelectedKeyframeProperty == ClipKeyframeProperty.Opacity ? 0.05 : 1;
    public string KeyframeSummary => Clip.Keyframes.Count == 0
        ? "Nema keyframe-ova"
        : $"{Clip.Keyframes.Count} keyframe tačaka";

    public ICommand AddKeyframeAtPlayheadCommand { get; }
    public ICommand RemoveKeyframeAtPlayheadCommand { get; }

    private double CurrentKeyframeValue(ClipKeyframeProperty property)
    {
        if (IsTextClip)
        {
            return property switch
            {
                ClipKeyframeProperty.PositionX => HorizontalAlign switch
                {
                    TextHorizontalAlign.Left => 10,
                    TextHorizontalAlign.Right => 90,
                    _ => 50
                },
                ClipKeyframeProperty.PositionY => TextPosition switch
                {
                    CaptionTextPosition.Top => 10,
                    CaptionTextPosition.Middle => 50,
                    _ => 85
                },
                ClipKeyframeProperty.Scale => 100,
                ClipKeyframeProperty.Opacity => 1,
                _ => 0
            };
        }

        return ClipKeyframeEvaluator.StaticValue(Clip, property);
    }

    private void AddKeyframeAtPlayhead()
    {
        if (_getPlayheadSeconds is null || _onKeyframeUpsert is null)
        {
            return;
        }

        var local = Math.Clamp(_getPlayheadSeconds() - Clip.TimelineStartSeconds, 0, Clip.TimelineDurationSeconds);
        _onKeyframeUpsert(Clip.Id, SelectedKeyframeProperty, local, KeyframeValue, SelectedKeyframeEasing);
        OnPropertyChanged(nameof(KeyframeSummary));
    }

    private void RemoveKeyframeAtPlayhead()
    {
        if (_getPlayheadSeconds is null || _onKeyframeRemove is null)
        {
            return;
        }

        var local = Math.Clamp(_getPlayheadSeconds() - Clip.TimelineStartSeconds, 0, Clip.TimelineDurationSeconds);
        _onKeyframeRemove(Clip.Id, SelectedKeyframeProperty, local);
        OnPropertyChanged(nameof(KeyframeSummary));
    }

    /// <summary>The clip's own words, editable - real fix for "how do I check/correct what Whisper
    /// heard": before this, an auto-generated caption's text could only be deleted and retyped from
    /// scratch as a brand new Text-track clip, with no way to just fix a misheard word in place.</summary>
    public string TextContent
    {
        get => Clip.TextContent ?? string.Empty;
        set
        {
            if (Clip.TextContent is null || Clip.TextContent == value) return;
            _onTextContentChanged?.Invoke(Clip.Id, value);
        }
    }

    /// <summary>
    /// Refreshes only the edited text. Rebuilding the complete timeline after every key press destroys
    /// the focused TextBox, which made continuous typing impossible.
    /// </summary>
    public void NotifyTextContentChanged()
    {
        OnPropertyChanged(nameof(TextContent));
        OnPropertyChanged(nameof(Label));
    }

    /// <summary>These four are real, working per-clip text style controls - unlike the 24 "Stilovi
    /// titlova" gallery presets (color-swatch preview only), changing these actually changes what
    /// <c>FfmpegFilterGraphBuilder</c> burns into the exported video for this exact clip.</summary>
    private static readonly Lazy<IReadOnlyList<object>> FontPickerChoices = new(() =>
        Enum.GetValues<CaptionFontChoice>().Cast<object>()
            .Concat(SystemFontCatalog.ListFontsUsableForSerbian().Cast<object>())
            .ToList());

    /// <summary>The same property name keeps the existing inspector binding intact, but it now accepts
    /// both legacy enum presets and real InstalledFont entries from SystemFontCatalog.</summary>
    public object FontChoice
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Clip.TextFontFilePath))
            {
                var exact = AvailableFontChoices.OfType<InstalledFont>().FirstOrDefault(f =>
                    string.Equals(f.FilePath, Clip.TextFontFilePath, StringComparison.OrdinalIgnoreCase));
                if (exact is not null) return exact;
            }

            if (!string.IsNullOrWhiteSpace(Clip.TextFontFamilyName))
            {
                var family = AvailableFontChoices.OfType<InstalledFont>().FirstOrDefault(f =>
                    string.Equals(f.FamilyName, Clip.TextFontFamilyName, StringComparison.OrdinalIgnoreCase));
                if (family is not null) return family;
            }

            return Clip.FontChoice;
        }
        set
        {
            if (value is CaptionFontChoice legacy)
            {
                if (Clip.FontChoice == legacy && Clip.TextFontFamilyName is null && Clip.TextFontFilePath is null) return;
                if (_onTextFontChanged is not null)
                    _onTextFontChanged(Clip.Id, legacy, null, null);
                else
                    _onTextStyleChanged?.Invoke(Clip.Id, legacy, FontSizePx, TextColor, TextPosition);
                return;
            }

            if (value is InstalledFont installed)
            {
                if (string.Equals(Clip.TextFontFilePath, installed.FilePath, StringComparison.OrdinalIgnoreCase)) return;
                _onTextFontChanged?.Invoke(Clip.Id, CaptionFontChoice.Default, installed.FamilyName, installed.FilePath);
            }
        }
    }

    public int FontSizePx
    {
        get => Clip.FontSizePx;
        set
        {
            if (Clip.FontSizePx == value) return;
            _onTextStyleChanged?.Invoke(Clip.Id, Clip.FontChoice, value, TextColor, TextPosition);
        }
    }

    public string TextColor
    {
        get => Clip.TextColor;
        set
        {
            if (Clip.TextColor == value || string.IsNullOrWhiteSpace(value)) return;
            _onTextStyleChanged?.Invoke(Clip.Id, Clip.FontChoice, FontSizePx, value, TextPosition);
        }
    }

    public CaptionTextPosition TextPosition
    {
        get => Clip.TextPosition;
        set
        {
            if (Clip.TextPosition == value) return;
            _onTextStyleChanged?.Invoke(Clip.Id, Clip.FontChoice, FontSizePx, TextColor, value);
        }
    }

    public IReadOnlyList<object> AvailableFontChoices => FontPickerChoices.Value;
    public IReadOnlyList<CaptionTextPosition> AvailablePositions { get; } = Enum.GetValues<CaptionTextPosition>();
    public IReadOnlyList<TextHorizontalAlign> AvailableHorizontalAligns { get; } = Enum.GetValues<TextHorizontalAlign>();
    public IReadOnlyList<TextCaseTransform> AvailableTextCases { get; } = Enum.GetValues<TextCaseTransform>();

    /// <summary>Builds the full style record from the current getters with one field overridden, and
    /// pushes it through the same undo-safe session callback every other style setter below uses.</summary>
    private void PushAdvancedStyle(Func<TextAdvancedStyle, TextAdvancedStyle> mutate)
    {
        var current = new TextAdvancedStyle(
            HasOutline ? OutlineColor : null, OutlineWidthPx,
            HasShadow ? ShadowColor : null, ShadowOffsetPx,
            HasBackground, BackgroundColor, BackgroundOpacity,
            HorizontalAlign, IsBold, IsItalic, TextCase, LineSpacingPx);
        _onAdvancedStyleChanged?.Invoke(Clip.Id, mutate(current));
    }

    public bool HasOutline
    {
        get => Clip.TextOutlineColor is not null;
        set { if (HasOutline == value) return; PushAdvancedStyle(s => s with { OutlineColor = value ? OutlineColor : null }); }
    }

    public string OutlineColor
    {
        get => Clip.TextOutlineColor ?? "#000000";
        set { if (Clip.TextOutlineColor == value || string.IsNullOrWhiteSpace(value)) return; PushAdvancedStyle(s => s with { OutlineColor = value }); }
    }

    public int OutlineWidthPx
    {
        get => Clip.TextOutlineWidthPx;
        set { if (Clip.TextOutlineWidthPx == value) return; PushAdvancedStyle(s => s with { OutlineWidthPx = value }); }
    }

    public bool HasShadow
    {
        get => Clip.TextShadowColor is not null;
        set { if (HasShadow == value) return; PushAdvancedStyle(s => s with { ShadowColor = value ? ShadowColor : null }); }
    }

    public string ShadowColor
    {
        get => Clip.TextShadowColor ?? "#000000";
        set { if (Clip.TextShadowColor == value || string.IsNullOrWhiteSpace(value)) return; PushAdvancedStyle(s => s with { ShadowColor = value }); }
    }

    public int ShadowOffsetPx
    {
        get => Clip.TextShadowOffsetPx;
        set { if (Clip.TextShadowOffsetPx == value) return; PushAdvancedStyle(s => s with { ShadowOffsetPx = value }); }
    }

    /// <summary>Defaults to on (matches the always-drawn box every Caption/Text clip had before this
    /// became a real toggle) - turning it off is what makes a plain outline-only or shadow-only style
    /// possible for the first time.</summary>
    public bool HasBackground
    {
        get => Clip.HasTextBackground;
        set { if (Clip.HasTextBackground == value) return; PushAdvancedStyle(s => s with { HasBackground = value }); }
    }

    public string BackgroundColor
    {
        get => Clip.TextBackgroundColor;
        set { if (Clip.TextBackgroundColor == value || string.IsNullOrWhiteSpace(value)) return; PushAdvancedStyle(s => s with { BackgroundColor = value }); }
    }

    /// <summary>0-100 for the UI slider - converted to/from the underlying 0.0-1.0 domain value.</summary>
    public int BackgroundOpacityPercent
    {
        get => (int)Math.Round(BackgroundOpacity * 100);
        set { var opacity = Math.Clamp(value, 0, 100) / 100.0; if (Math.Abs(BackgroundOpacity - opacity) < 1e-6) return; PushAdvancedStyle(s => s with { BackgroundOpacity = opacity }); }
    }

    private double BackgroundOpacity => Clip.TextBackgroundOpacity;

    public TextHorizontalAlign HorizontalAlign
    {
        get => Clip.TextHorizontalAlign;
        set { if (Clip.TextHorizontalAlign == value) return; PushAdvancedStyle(s => s with { HorizontalAlign = value }); }
    }

    public bool IsBold
    {
        get => Clip.IsTextBold;
        set { if (Clip.IsTextBold == value) return; PushAdvancedStyle(s => s with { IsBold = value }); }
    }

    public bool IsItalic
    {
        get => Clip.IsTextItalic;
        set { if (Clip.IsTextItalic == value) return; PushAdvancedStyle(s => s with { IsItalic = value }); }
    }

    public TextCaseTransform TextCase
    {
        get => Clip.TextCase;
        set { if (Clip.TextCase == value) return; PushAdvancedStyle(s => s with { TextCase = value }); }
    }

    public int LineSpacingPx
    {
        get => Clip.LineSpacingPx;
        set { if (Clip.LineSpacingPx == value) return; PushAdvancedStyle(s => s with { LineSpacingPx = value }); }
    }

    /// <summary>"Primeni na sve titlove na ovoj traci" - copies this clip's complete text style (font/
    /// size/color/position + everything above) onto every other Caption/Text clip on the same track, so
    /// styling a batch of auto-generated captions doesn't mean re-clicking the same settings on each one.</summary>
    public ICommand ApplyStyleToAllOnTrackCommand { get; }

    /// <summary>Real transition into this clip from whichever Video-track clip is right before it - burnt
    /// into the exported video via ffmpeg's own <c>xfade</c>/<c>acrossfade</c> filters, not a placeholder.
    /// Has no visible effect on the very first clip on a track (nothing to transition from) or when there's
    /// a real gap before this clip - both cases are handled gracefully by the render pipeline rather than
    /// erroring, so leaving this set doesn't break anything if the clip before it is later moved/deleted.</summary>
    public ClipTransitionType TransitionInType
    {
        get => Clip.TransitionInType;
        set
        {
            if (Clip.TransitionInType == value) return;
            _onTransitionChanged?.Invoke(Clip.Id, value, TransitionInDurationSeconds);
        }
    }

    public double TransitionInDurationSeconds
    {
        get => Clip.TransitionInDurationSeconds;
        set
        {
            if (Math.Abs(Clip.TransitionInDurationSeconds - value) < 1e-9) return;
            _onTransitionChanged?.Invoke(Clip.Id, TransitionInType, value);
        }
    }

    public IReadOnlyList<ClipTransitionType> AvailableTransitions { get; } = Enum.GetValues<ClipTransitionType>();

    public ICommand SplitAtPlayheadCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand NudgeEarlierCommand { get; }
    public ICommand NudgeLaterCommand { get; }
    public ICommand ToggleMuteCommand { get; }
    public ICommand ToggleFadeInCommand { get; }
    public ICommand ToggleFadeOutCommand { get; }

    public TimelineClipItemViewModel(
        TimelineClip clip,
        string trackId,
        string label,
        bool isVideoClip,
        ICommand splitAtPlayheadCommand,
        ICommand deleteCommand,
        ICommand duplicateCommand,
        ICommand nudgeEarlierCommand,
        ICommand nudgeLaterCommand,
        ICommand toggleMuteCommand,
        ICommand toggleFadeInCommand,
        ICommand toggleFadeOutCommand,
        ICommand applyStyleToAllOnTrackCommand,
        Action<string, CaptionFontChoice, int, string, CaptionTextPosition>? onTextStyleChanged = null,
        Action<string, ClipTransitionType, double>? onTransitionChanged = null,
        Action<string, string>? onTextContentChanged = null,
        Action<string, TextAdvancedStyle>? onAdvancedStyleChanged = null,
        Action<string, double, double, double, double>? onLayerPlacementChanged = null,
        bool isOverlayClip = false,
        Action<string, ClipVideoEffect, double, double, double, double>? onEffectsChanged = null,
        Action<string, ClipTransformSettings>? onTransformChanged = null,
        Action<string, ClipCompositingSettings>? onCompositingChanged = null,
        bool isAudioClip = false,
        Func<double>? getPlayheadSeconds = null,
        Action<string, ClipKeyframeProperty, double, double, ClipKeyframeEasing>? onKeyframeUpsert = null,
        Action<string, ClipKeyframeProperty, double>? onKeyframeRemove = null,
        Action<string, CaptionFontChoice, string?, string?>? onTextFontChanged = null,
        double sourceMediaDurationSeconds = 0,
        Action<string, double>? onTrimInChanged = null,
        Action<string, double>? onTrimOutChanged = null,
        Action<string, SpeedCurvePreset>? onSpeedCurvePresetChanged = null)
    {
        _onEffectsChanged = onEffectsChanged;
        _onSpeedCurvePresetChanged = onSpeedCurvePresetChanged;
        _onTransformChanged = onTransformChanged;
        _onCompositingChanged = onCompositingChanged;
        _onLayerPlacementChanged = onLayerPlacementChanged;
        _getPlayheadSeconds = getPlayheadSeconds;
        _onKeyframeUpsert = onKeyframeUpsert;
        _onKeyframeRemove = onKeyframeRemove;
        _onTrimInChanged = onTrimInChanged;
        _onTrimOutChanged = onTrimOutChanged;
        SourceDurationSeconds = Math.Max(0, sourceMediaDurationSeconds);
        IsOverlayClip = isOverlayClip;
        Clip = clip;
        TrackId = trackId;
        _mediaLabel = label;
        IsVideoClip = isVideoClip;
        IsAudioClip = isAudioClip;
        SplitAtPlayheadCommand = splitAtPlayheadCommand;
        DeleteCommand = deleteCommand;
        DuplicateCommand = duplicateCommand;
        NudgeEarlierCommand = nudgeEarlierCommand;
        NudgeLaterCommand = nudgeLaterCommand;
        ToggleMuteCommand = toggleMuteCommand;
        ToggleFadeInCommand = toggleFadeInCommand;
        ToggleFadeOutCommand = toggleFadeOutCommand;
        ApplyStyleToAllOnTrackCommand = applyStyleToAllOnTrackCommand;
        _onTextStyleChanged = onTextStyleChanged;
        _onTextFontChanged = onTextFontChanged;
        _onTransitionChanged = onTransitionChanged;
        _onTextContentChanged = onTextContentChanged;
        _onAdvancedStyleChanged = onAdvancedStyleChanged;
        _keyframeValue = CurrentKeyframeValue(_selectedKeyframeProperty);
        AddKeyframeAtPlayheadCommand = new RelayCommand(AddKeyframeAtPlayhead);
        RemoveKeyframeAtPlayheadCommand = new RelayCommand(RemoveKeyframeAtPlayhead);
    }

    private static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}.{t.Milliseconds / 100:D1}";
    }
}
