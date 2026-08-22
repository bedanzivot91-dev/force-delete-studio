namespace NPVideoStudio.Domain;

public enum TimelineTrackKind
{
    Video,
    Audio,
    Caption,
    Text,
    ImageOverlay
}

/// <summary>Where a Caption/Text clip's <see cref="TimelineClip.TextContent"/> is drawn on the frame.</summary>
public enum CaptionTextPosition
{
    Top,
    Middle,
    Bottom
}

/// <summary>
/// Legacy built-in font choices kept for backwards compatibility with existing project files. New projects
/// can additionally persist a real installed font through <see cref="TimelineClip.TextFontFamilyName"/> and
/// <see cref="TimelineClip.TextFontFilePath"/>.
/// </summary>
public enum CaptionFontChoice
{
    Default,
    Arial,
    ArialBold,
    Impact,
    ComicSansBold,
    Georgia
}

/// <summary>Horizontal placement of a Caption/Text clip's text within the frame - independent of
/// <see cref="CaptionTextPosition"/> (vertical), so combined they give a real 9-zone grid instead of just
/// 3 vertical bands always centered horizontally.</summary>
public enum TextHorizontalAlign
{
    Left,
    Center,
    Right
}

/// <summary>Case transform applied to a Caption/Text clip's displayed text - the underlying
/// <see cref="TimelineClip.TextContent"/> (and any transcription behind it) is never modified, this only
/// changes what gets burned into the exported video.</summary>
public enum TextCaseTransform
{
    Normal,
    UpperCase,
    LowerCase,
    TitleCase
}

/// <summary>
/// The extra per-clip text styling fields beyond font/size/color/position (outline, shadow, background,
/// horizontal alignment, bold/italic, case transform, line spacing) - bundled into one record so
/// <c>TimelineEditSession.SetTextAdvancedStyle</c>/<c>TimelineClipItemViewModel</c> don't need an
/// ever-growing positional parameter list every time one more style knob is added.
/// </summary>
public readonly record struct TextAdvancedStyle(
    string? OutlineColor,
    int OutlineWidthPx,
    string? ShadowColor,
    int ShadowOffsetPx,
    bool HasBackground,
    string BackgroundColor,
    double BackgroundOpacity,
    TextHorizontalAlign HorizontalAlign,
    bool IsBold,
    bool IsItalic,
    TextCaseTransform TextCase,
    int LineSpacingPx);
/// <summary>
/// Persisted picture-transform controls shared by base video and overlay/image clips.
/// Percent values keep projects resolution-independent; all values are rendered by FFmpeg, not UI-only.
/// </summary>
public readonly record struct ClipTransformSettings(
    double RotationDegrees,
    bool FlipHorizontal,
    bool FlipVertical,
    double CropLeftPercent,
    double CropTopPercent,
    double CropRightPercent,
    double CropBottomPercent,
    bool IsReversed,
    bool IsFreezeFrame,
    bool ChromaKeyEnabled,
    string ChromaKeyColor,
    double ChromaKeySimilarity,
    double ChromaKeyBlend);

public enum ClipMaskType
{
    None,
    Rectangle,
    Circle,
    Linear
}

public enum ClipBlendMode
{
    Normal,
    Multiply,
    Screen,
    Overlay,
    Add,
    Difference
}

/// <summary>Persisted overlay compositing controls. Masks are evaluated in the overlay's own rendered
/// coordinates before placement; blend mode is applied only while compositing the overlay over the base.</summary>
public readonly record struct ClipCompositingSettings(
    ClipMaskType MaskType,
    double MaskCenterXPercent,
    double MaskCenterYPercent,
    double MaskWidthPercent,
    double MaskHeightPercent,
    double MaskFeatherPercent,
    double MaskRotationDegrees,
    bool MaskInvert,
    ClipBlendMode BlendMode);

/// <summary>
/// A real transition between a Video-track clip and the one right before it, rendered via ffmpeg's own
/// <c>xfade</c>/<c>acrossfade</c> filters (each enum name here is exactly the matching <c>xfade</c>
/// transition name ffmpeg expects, lowercased) - not a fade-to-black substitute. <see cref="None"/> keeps
/// the old hard-cut behavior.
/// </summary>
public enum ClipTransitionType
{
    None,
    Fade,
    WipeLeft,
    WipeRight,
    SlideLeft,
    SlideRight,
    Dissolve,
    ZoomIn
}

/// <summary>
/// A ready-made look applied to a whole clip's picture. Each maps to a real, standard ffmpeg video
/// filter (see <c>FfmpegFilterGraphBuilder.BuildEffectFilters</c>) - these are not decorative names with
/// nothing behind them.
/// </summary>
public enum ClipVideoEffect
{
    /// <summary>Untouched picture.</summary>
    None,

    /// <summary>Black and white (<c>hue=s=0</c>).</summary>
    Grayscale,

    /// <summary>Warm brown old-photo look (<c>colorchannelmixer</c>).</summary>
    Sepia,

    /// <summary>Soft focus (<c>gblur</c>).</summary>
    Blur,

    /// <summary>Darkened corners, draws the eye to the middle.</summary>
    Vignette,

    /// <summary>Crisper edges (<c>unsharp</c>).</summary>
    Sharpen,

    /// <summary>Colour negative (<c>negate</c>).</summary>
    Invert,

    /// <summary>Mirrored left-to-right (<c>hflip</c>).</summary>
    Mirror
}

/// <summary>
/// One clip placed on a timeline track. Non-destructive: <see cref="SourceTrimInSeconds"/>/
/// <see cref="SourceTrimOutSeconds"/> only change which slice of the original source plays - the
/// underlying <see cref="MediaAssetId"/> file is never modified.
/// </summary>
public sealed class TimelineClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Links to a <see cref="Project.MediaLibrary"/> entry via <see cref="MediaAsset.Id"/> - null for a Text-overlay clip.</summary>
    public string? MediaAssetId { get; set; }

    /// <summary>Plain text for a Text-overlay clip - null for every other track kind.</summary>
    public string? TextContent { get; set; }

    /// <summary>Legacy preset retained so old .npvsproject files continue to render exactly as before.</summary>
    public CaptionFontChoice FontChoice { get; set; } = CaptionFontChoice.Default;

    /// <summary>Family name of the real installed font explicitly selected by the user. Null means use the
    /// legacy <see cref="FontChoice"/> path. Saved alongside the file path so a project moved to another
    /// Windows PC can locate the same family even when its physical font-file path differs.</summary>
    public string? TextFontFamilyName { get; set; }

    /// <summary>Exact font file chosen from <c>SystemFontCatalog</c>. The renderer uses it when it still
    /// exists, otherwise falls back to <see cref="TextFontFamilyName"/> and finally the legacy preset.</summary>
    public string? TextFontFilePath { get; set; }

    public int FontSizePx { get; set; } = 36;

    /// <summary>Hex color, e.g. "#FFFFFF" - passed straight through to ffmpeg's drawtext fontcolor.</summary>
    public string TextColor { get; set; } = "#FFFFFF";
    public CaptionTextPosition TextPosition { get; set; } = CaptionTextPosition.Bottom;
    public TextHorizontalAlign TextHorizontalAlign { get; set; } = TextHorizontalAlign.Center;

    public string? TextOutlineColor { get; set; }
    public int TextOutlineWidthPx { get; set; } = 2;

    public string? TextShadowColor { get; set; }
    public int TextShadowOffsetPx { get; set; } = 2;

    public bool HasTextBackground { get; set; } = true;
    public string TextBackgroundColor { get; set; } = "#000000";
    public double TextBackgroundOpacity { get; set; } = 0.5;

    public bool IsTextBold { get; set; }
    public bool IsTextItalic { get; set; }
    public TextCaseTransform TextCase { get; set; } = TextCaseTransform.Normal;

    /// <summary>0 = ffmpeg drawtext's own default line spacing (only matters for multi-line text).</summary>
    public int LineSpacingPx { get; set; }

    public double SourceTrimInSeconds { get; set; }
    public double SourceTrimOutSeconds { get; set; }

    public double TimelineStartSeconds { get; set; }
    public double TimelineDurationSeconds => Math.Max(0, SourceTrimOutSeconds - SourceTrimInSeconds) / (IsFreezeFrame ? 1.0 : Math.Clamp(SpeedMultiplier, 0.25, 4));
    public double TimelineEndSeconds => TimelineStartSeconds + TimelineDurationSeconds;

    public double FadeInSeconds { get; set; }
    public double FadeOutSeconds { get; set; }

    public ClipTransitionType TransitionInType { get; set; } = ClipTransitionType.None;
    public double TransitionInDurationSeconds { get; set; } = 0.5;

    public bool IsMuted { get; set; }
    public double Volume { get; set; } = 1.0;

    public double ScalePercent { get; set; } = 100;
    public double PositionXPercent { get; set; } = 50;
    public double PositionYPercent { get; set; } = 50;
    public double Opacity { get; set; } = 1.0;

    public ClipVideoEffect Effect { get; set; } = ClipVideoEffect.None;
    public double Brightness { get; set; }
    public double Contrast { get; set; } = 1.0;
    public double Saturation { get; set; } = 1.0;

    public double SpeedMultiplier { get; set; } = 1.0;
    public double RotationDegrees { get; set; }
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }
    public double CropLeftPercent { get; set; }
    public double CropTopPercent { get; set; }
    public double CropRightPercent { get; set; }
    public double CropBottomPercent { get; set; }
    public bool IsReversed { get; set; }
    public bool IsFreezeFrame { get; set; }
    public bool ChromaKeyEnabled { get; set; }
    public string ChromaKeyColor { get; set; } = "#00FF00";
    public double ChromaKeySimilarity { get; set; } = 0.12;
    public double ChromaKeyBlend { get; set; } = 0.02;

    public ClipMaskType MaskType { get; set; } = ClipMaskType.None;
    public double MaskCenterXPercent { get; set; } = 50;
    public double MaskCenterYPercent { get; set; } = 50;
    public double MaskWidthPercent { get; set; } = 80;
    public double MaskHeightPercent { get; set; } = 80;
    public double MaskFeatherPercent { get; set; } = 5;
    public double MaskRotationDegrees { get; set; }
    public bool MaskInvert { get; set; }
    public ClipBlendMode BlendMode { get; set; } = ClipBlendMode.Normal;

    // Times are local to this rendered clip (0 = first visible frame), not absolute project time.
    public List<ClipKeyframe> Keyframes { get; set; } = new();
}

/// <summary>One track (a lane of non-overlapping clips) in the timeline.</summary>
public sealed class TimelineTrack
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required TimelineTrackKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;

    public List<TimelineClip> Clips { get; set; } = new();

    public bool IsLocked { get; set; }
    public bool IsHidden { get; set; }
    public bool IsMuted { get; set; }
    public bool IsSolo { get; set; }
    public double Volume { get; set; } = 1.0;
}

/// <summary>The project's persisted non-destructive timeline.</summary>
public sealed class Timeline
{
    public List<TimelineTrack> Tracks { get; set; } = new();
    public double PlayheadSeconds { get; set; }
    public double ZoomPixelsPerSecond { get; set; } = 40;
}
