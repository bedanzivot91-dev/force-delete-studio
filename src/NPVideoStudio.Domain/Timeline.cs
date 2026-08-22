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
/// Legacy built-in font choices retained for backward compatibility with existing project files.
/// New clips may additionally select a real installed font through <see cref="TimelineClip.TextFontFamilyName"/>
/// and <see cref="TimelineClip.TextFontFilePath"/>.
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

/// <summary>
/// Persisted libvidstab controls for one real media clip. Detection and transformation are deliberately
/// separate because FFmpeg/libvidstab is a genuine two-pass stabilizer: pass 1 measures motion and writes
/// a transform file, pass 2 consumes that file while the normal render graph applies every other edit.
/// </summary>
public readonly record struct ClipStabilizationSettings(
    bool Enabled,
    int Shakiness,
    int Accuracy,
    int Smoothing,
    double ZoomPercent,
    int OptimalZoom);

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

    /// <summary>Darkened corners, draws the eye to the middle (<c>vignette</c>).</summary>
    Vignette,

    /// <summary>Crisper edges (<c>unsharp</c>).</summary>
    Sharpen,

    /// <summary>Colour negative (<c>negate</c>).</summary>
    Invert,

    /// <summary>Mirrored left-to-right (<c>hflip</c>) - the usual fix for selfie-camera footage.</summary>
    Mirror
}

/// <summary>
/// One clip placed on a timeline track. Non-destructive: <see cref="SourceTrimInSeconds"/>/
/// <see cref="SourceTrimOutSeconds"/> only change which slice of the original source plays - the
/// underlying <see cref="MediaAssetId"/> file is never modified (spec Phase 8: "non-destructive").
/// </summary>
public sealed class TimelineClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Links to a <see cref="Project.MediaLibrary"/> entry via <see cref="MediaAsset.Id"/> - null for a Text-overlay clip, which has no underlying media file.</summary>
    public string? MediaAssetId { get; set; }

    /// <summary>Plain text for a Text-overlay clip - null for every other track kind.</summary>
    public string? TextContent { get; set; }

    /// <summary>Only meaningful when <see cref="TextContent"/> is set - real, per-clip text styling that
    /// actually reaches the exported video via <c>FfmpegFilterGraphBuilder</c> (unlike the 24 "Stilovi
    /// titlova" gallery presets, which are a preview-only color swatch today and do not affect export).</summary>
    public CaptionFontChoice FontChoice { get; set; } = CaptionFontChoice.Default;

    /// <summary>Family name of a real installed font selected from <c>SystemFontCatalog</c>. Kept alongside
    /// the exact file path so a project copied to another PC can resolve the same family there.</summary>
    public string? TextFontFamilyName { get; set; }

    /// <summary>Exact installed font file selected by the user. If it no longer exists, the renderer falls
    /// back to <see cref="TextFontFamilyName"/> and then the legacy <see cref="FontChoice"/>.</summary>
    public string? TextFontFilePath { get; set; }

    public int FontSizePx { get; set; } = 36;

    /// <summary>Hex color, e.g. "#FFFFFF" - passed straight through to ffmpeg's drawtext fontcolor.</summary>
    public string TextColor { get; set; } = "#FFFFFF";
    public CaptionTextPosition TextPosition { get; set; } = CaptionTextPosition.Bottom;
    public TextHorizontalAlign TextHorizontalAlign { get; set; } = TextHorizontalAlign.Center;

    /// <summary>Null = no outline drawn (ffmpeg drawtext's own default). Real per-clip readability control
    /// for text over busy/light backgrounds, independent of the background box below.</summary>
    public string? TextOutlineColor { get; set; }
    public int TextOutlineWidthPx { get; set; } = 2;

    /// <summary>Null = no drop shadow.</summary>
    public string? TextShadowColor { get; set; }
    public int TextShadowOffsetPx { get; set; } = 2;

    /// <summary>Defaults to true/black/0.5 opacity - preserves the exact hardcoded look every Caption/Text
    /// clip had before this became a real per-clip toggle (a solid semi-transparent box was always drawn
    /// with no way to turn it off).</summary>
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
    public double TimelineDurationSeconds => IsFreezeFrame
        ? Math.Max(0, SourceTrimOutSeconds - SourceTrimInSeconds)
        : SpeedCurveMath.OutputDuration(this);
    public double TimelineEndSeconds => TimelineStartSeconds + TimelineDurationSeconds;

    public double FadeInSeconds { get; set; }
    public double FadeOutSeconds { get; set; }

    /// <summary>Real transition FROM the previous Video-track clip INTO this one (only meaningful on a
    /// Video-track clip that isn't the first, and only takes effect when this clip starts exactly where
    /// the previous one ends - a real gap between them means there's nothing to transition from/to, so it
    /// falls back to a hard cut same as <see cref="ClipTransitionType.None"/>).</summary>
    public ClipTransitionType TransitionInType { get; set; } = ClipTransitionType.None;
    public double TransitionInDurationSeconds { get; set; } = 0.5;

    public bool IsMuted { get; set; }
    public double Volume { get; set; } = 1.0;

    // --- Layer compositing (CapCut-style picture-in-picture / stickers / logo) -------------------
    // Only meaningful for clips on an overlay layer - i.e. any Video track after the first, or an
    // ImageOverlay track. The base (bottom) video track always fills the frame and ignores these, the
    // same way every layer-based editor treats its background layer.
    //
    // Percentages rather than pixels on purpose: a project can be re-rendered at 1080p or 4K, or switched
    // between 16:9 and 9:16, and an overlay pinned at "70% across, 20% down, 30% of frame width" stays
    // where the user put it, while a pixel offset would silently drift off-frame.

    /// <summary>Overlay width as a percentage of the finished frame's width. Height follows from the
    /// source's own aspect ratio, so an overlay is never stretched.</summary>
    public double ScalePercent { get; set; } = 100;

    /// <summary>Horizontal position of the overlay's CENTER, 0 = left edge, 50 = centered, 100 = right edge.</summary>
    public double PositionXPercent { get; set; } = 50;

    /// <summary>Vertical position of the overlay's CENTER, 0 = top edge, 50 = centered, 100 = bottom edge.</summary>
    public double PositionYPercent { get; set; } = 50;

    /// <summary>1.0 = fully opaque, 0 = invisible.</summary>
    public double Opacity { get; set; } = 1.0;

    // --- Picture effects -------------------------------------------------------------------------
    // Applied to the clip's own picture before it is placed on the timeline, so an effect on an overlay
    // affects only that overlay, not the video underneath it.

    /// <summary>A ready-made look, or <see cref="ClipVideoEffect.None"/>.</summary>
    public ClipVideoEffect Effect { get; set; } = ClipVideoEffect.None;

    /// <summary>Manual brightness, -1..1, 0 = unchanged. Stacks on top of <see cref="Effect"/>.</summary>
    public double Brightness { get; set; }

    /// <summary>Manual contrast, 0..3, 1 = unchanged.</summary>
    public double Contrast { get; set; } = 1.0;

    /// <summary>Manual colour saturation, 0..3, 1 = unchanged. 0 is fully grey.</summary>
    public double Saturation { get; set; } = 1.0;

    /// <summary>Playback speed, 0.25..4. Used when no velocity curve is active.</summary>
    public double SpeedMultiplier { get; set; } = 1.0;

    /// <summary>Optional CapCut-style variable velocity preset. None keeps constant speed.</summary>
    public SpeedCurvePreset SpeedCurvePreset { get; set; } = SpeedCurvePreset.None;

    /// <summary>Absolute source-time control points for the active velocity curve.</summary>
    public List<SpeedCurvePoint> SpeedCurvePoints { get; set; } = new();

    // --- Transform / temporal / green-screen ----------------------------------------------------
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

    // --- Two-pass video stabilization ------------------------------------------------------------
    /// <summary>Runs FFmpeg/libvidstab analysis for this clip before export/real preview, then applies the
    /// resulting transforms in the normal render graph. Originals are never rewritten.</summary>
    public bool StabilizationEnabled { get; set; }
    public int StabilizationShakiness { get; set; } = 5;
    public int StabilizationAccuracy { get; set; } = 15;
    public int StabilizationSmoothing { get; set; } = 15;
    public double StabilizationZoomPercent { get; set; }
    /// <summary>libvidstab optzoom: 0=off, 1=static optimal zoom, 2=adaptive optimal zoom.</summary>
    public int StabilizationOptimalZoom { get; set; } = 1;

    // --- Overlay mask + blend -------------------------------------------------------------------
    public ClipMaskType MaskType { get; set; } = ClipMaskType.None;
    public double MaskCenterXPercent { get; set; } = 50;
    public double MaskCenterYPercent { get; set; } = 50;
    public double MaskWidthPercent { get; set; } = 80;
    public double MaskHeightPercent { get; set; } = 80;
    public double MaskFeatherPercent { get; set; } = 5;
    public double MaskRotationDegrees { get; set; }
    public bool MaskInvert { get; set; }
    public ClipBlendMode BlendMode { get; set; } = ClipBlendMode.Normal;

    // --- Keyframe animation ----------------------------------------------------------------------
    // Times are local to this rendered clip (0 = first visible frame), not absolute project time.
    // Moving the clip therefore never changes the animation authored inside it.
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

/// <summary>
/// The project's non-destructive timeline (spec Phase 8) - persisted as part of <see cref="Project"/>,
/// not just transient UI-control state. Playhead/zoom are session UI state that happens to be worth
/// persisting too (so re-opening a project restores where you left off), not editing data.
/// </summary>
public sealed class Timeline
{
    public List<TimelineTrack> Tracks { get; set; } = new();
    public double PlayheadSeconds { get; set; }
    public double ZoomPixelsPerSecond { get; set; } = 40;
}
