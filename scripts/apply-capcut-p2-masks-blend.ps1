$ErrorActionPreference = 'Stop'

function Read-Utf8([string]$Path) {
    return [System.IO.File]::ReadAllText((Resolve-Path $Path), [System.Text.Encoding]::UTF8)
}
function Write-Utf8([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText((Resolve-Path $Path), $Text, (New-Object System.Text.UTF8Encoding($false)))
}
function Replace-Once([string]$Text, [string]$Old, [string]$New, [string]$Label) {
    $i = $Text.IndexOf($Old, [StringComparison]::Ordinal)
    if ($i -lt 0) { throw "Anchor not found: $Label" }
    if ($Text.IndexOf($Old, $i + $Old.Length, [StringComparison]::Ordinal) -ge 0) { throw "Anchor not unique: $Label" }
    return $Text.Substring(0, $i) + $New + $Text.Substring($i + $Old.Length)
}

# -----------------------------------------------------------------------------
# Domain: persisted mask + blend state.
# -----------------------------------------------------------------------------
$path = 'src/NPVideoStudio.Domain/Timeline.cs'
$t = Read-Utf8 $path
$old = @'
    double ChromaKeyBlend);

/// <summary>
/// A real transition between a Video-track clip and the one right before it, rendered via ffmpeg's own
'@
$new = @'
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
'@
$t = Replace-Once $t $old $new 'domain compositing types'

$old = @'
    public double ChromaKeySimilarity { get; set; } = 0.12;
    public double ChromaKeyBlend { get; set; } = 0.02;
}
'@
$new = @'
    public double ChromaKeySimilarity { get; set; } = 0.12;
    public double ChromaKeyBlend { get; set; } = 0.02;

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
}
'@
$t = Replace-Once $t $old $new 'timeline clip mask blend fields'
Write-Utf8 $path $t

# -----------------------------------------------------------------------------
# Session: undo-safe compositing setter + clone state.
# -----------------------------------------------------------------------------
$path = 'src/NPVideoStudio.AI/TimelineEditSession.cs'
$t = Read-Utf8 $path
$anchor = @'
    public void SetClipMute(string clipId, bool muted)
'@
$insert = @'
    public void SetClipCompositing(string clipId, ClipCompositingSettings settings)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        liveClip.MaskType = settings.MaskType;
        liveClip.MaskCenterXPercent = Math.Clamp(settings.MaskCenterXPercent, 0, 100);
        liveClip.MaskCenterYPercent = Math.Clamp(settings.MaskCenterYPercent, 0, 100);
        liveClip.MaskWidthPercent = Math.Clamp(settings.MaskWidthPercent, 1, 100);
        liveClip.MaskHeightPercent = Math.Clamp(settings.MaskHeightPercent, 1, 100);
        liveClip.MaskFeatherPercent = Math.Clamp(settings.MaskFeatherPercent, 0, 50);
        liveClip.MaskRotationDegrees = Math.Clamp(settings.MaskRotationDegrees, -180, 180);
        liveClip.MaskInvert = settings.MaskInvert;
        liveClip.BlendMode = settings.BlendMode;
    }

'@
$t = Replace-Once $t $anchor ($insert + $anchor) 'session compositing setter'

$old = @'
        ChromaKeyColor = clip.ChromaKeyColor,
        ChromaKeySimilarity = clip.ChromaKeySimilarity,
        ChromaKeyBlend = clip.ChromaKeyBlend
'@
$new = @'
        ChromaKeyColor = clip.ChromaKeyColor,
        ChromaKeySimilarity = clip.ChromaKeySimilarity,
        ChromaKeyBlend = clip.ChromaKeyBlend,
        MaskType = clip.MaskType,
        MaskCenterXPercent = clip.MaskCenterXPercent,
        MaskCenterYPercent = clip.MaskCenterYPercent,
        MaskWidthPercent = clip.MaskWidthPercent,
        MaskHeightPercent = clip.MaskHeightPercent,
        MaskFeatherPercent = clip.MaskFeatherPercent,
        MaskRotationDegrees = clip.MaskRotationDegrees,
        MaskInvert = clip.MaskInvert,
        BlendMode = clip.BlendMode
'@
$t = Replace-Once $t $old $new 'session clone compositing state'
Write-Utf8 $path $t

# -----------------------------------------------------------------------------
# Selected clip VM: real inspector properties routed through the session.
# -----------------------------------------------------------------------------
$path = 'src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs'
$t = Read-Utf8 $path
$t = Replace-Once $t `
'    private readonly Action<string, ClipTransformSettings>? _onTransformChanged;' `
"    private readonly Action<string, ClipTransformSettings>? _onTransformChanged;`r`n    private readonly Action<string, ClipCompositingSettings>? _onCompositingChanged;" `
'vm compositing callback field'

$anchor = @'
    /// <summary>The clip's own words, editable - real fix for "how do I check/correct what Whisper
'@
$insert = @'
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

'@
$t = Replace-Once $t $anchor ($insert + $anchor) 'vm compositing properties'

$t = Replace-Once $t `
'        Action<string, ClipTransformSettings>? onTransformChanged = null,
        bool isAudioClip = false)' `
"        Action<string, ClipTransformSettings>? onTransformChanged = null,`r`n        Action<string, ClipCompositingSettings>? onCompositingChanged = null,`r`n        bool isAudioClip = false)" `
'vm constructor compositing argument'

$t = Replace-Once $t `
'        _onTransformChanged = onTransformChanged;' `
"        _onTransformChanged = onTransformChanged;`r`n        _onCompositingChanged = onCompositingChanged;" `
'vm constructor compositing assignment'
Write-Utf8 $path $t

# -----------------------------------------------------------------------------
# Parent timeline VM wires the compositing callback to the edit session.
# -----------------------------------------------------------------------------
$path = 'src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs'
$t = Read-Utf8 $path
$anchor = @'
        return new TimelineClipItemViewModel(clip, track.Id, ResolveClipLabel(clip), track.Kind == TimelineTrackKind.Video,
'@
$insert = @'
        void OnCompositingChanged(string clipId, ClipCompositingSettings settings)
        {
            _session.SetClipCompositing(clipId, settings);
            RefreshFromSession();
        }

'@
$t = Replace-Once $t $anchor ($insert + $anchor) 'timeline vm compositing callback'

$old = @'
            OnLayerPlacementChanged, track.Kind == TimelineTrackKind.ImageOverlay || (track.Kind == TimelineTrackKind.Video && _session.Tracks.Where(t => t.Kind == TimelineTrackKind.Video).FirstOrDefault()?.Id != track.Id), OnEffectsChanged, OnTransformChanged, track.Kind == TimelineTrackKind.Audio)
'@
$new = @'
            OnLayerPlacementChanged, track.Kind == TimelineTrackKind.ImageOverlay || (track.Kind == TimelineTrackKind.Video && _session.Tracks.Where(t => t.Kind == TimelineTrackKind.Video).FirstOrDefault()?.Id != track.Id), OnEffectsChanged, OnTransformChanged, OnCompositingChanged, track.Kind == TimelineTrackKind.Audio)
'@
$t = Replace-Once $t $old $new 'timeline vm pass compositing callback'
Write-Utf8 $path $t

# -----------------------------------------------------------------------------
# Inspector UI: masks and blend controls only on real overlay layers.
# -----------------------------------------------------------------------------
$path = 'src/NPVideoStudio.App/Views/WorkspaceView.axaml'
$t = Read-Utf8 $path
$old = @'
                <TextBlock Text="Sličnost" Classes="subtle"/><Slider Minimum="0.01" Maximum="1" Value="{Binding ChromaKeySimilarity}"/>
                <TextBlock Text="Feather / blend" Classes="subtle"/><Slider Minimum="0" Maximum="1" Value="{Binding ChromaKeyBlend}"/>
              </StackPanel>
'@
$new = @'
                <TextBlock Text="Sličnost" Classes="subtle"/><Slider Minimum="0.01" Maximum="1" Value="{Binding ChromaKeySimilarity}"/>
                <TextBlock Text="Feather / blend" Classes="subtle"/><Slider Minimum="0" Maximum="1" Value="{Binding ChromaKeyBlend}"/>

                <TextBlock Text="MASKA" Classes="eyebrow" Margin="0,10,0,0" />
                <TextBlock Text="Oblik" Classes="subtle"/><ComboBox ItemsSource="{Binding AvailableMaskTypes}" SelectedItem="{Binding MaskType}"/>
                <Grid ColumnDefinitions="*,*">
                  <StackPanel Spacing="4"><TextBlock Text="Centar X (%)" Classes="subtle"/><NumericUpDown Value="{Binding MaskCenterXPercent}" Minimum="0" Maximum="100" Increment="1"/></StackPanel>
                  <StackPanel Grid.Column="1" Spacing="4" Margin="8,0,0,0"><TextBlock Text="Centar Y (%)" Classes="subtle"/><NumericUpDown Value="{Binding MaskCenterYPercent}" Minimum="0" Maximum="100" Increment="1"/></StackPanel>
                </Grid>
                <Grid ColumnDefinitions="*,*">
                  <StackPanel Spacing="4"><TextBlock Text="Širina (%)" Classes="subtle"/><NumericUpDown Value="{Binding MaskWidthPercent}" Minimum="1" Maximum="100" Increment="1"/></StackPanel>
                  <StackPanel Grid.Column="1" Spacing="4" Margin="8,0,0,0"><TextBlock Text="Visina (%)" Classes="subtle"/><NumericUpDown Value="{Binding MaskHeightPercent}" Minimum="1" Maximum="100" Increment="1"/></StackPanel>
                </Grid>
                <TextBlock Text="Feather maske (%)" Classes="subtle"/><Slider Minimum="0" Maximum="50" Value="{Binding MaskFeatherPercent}"/>
                <TextBlock Text="Ugao maske (stepeni)" Classes="subtle"/><NumericUpDown Value="{Binding MaskRotationDegrees}" Minimum="-180" Maximum="180" Increment="1"/>
                <ToggleButton Content="Invertuj masku" IsChecked="{Binding MaskInvert}"/>

                <TextBlock Text="BLEND MODE" Classes="eyebrow" Margin="0,10,0,0" />
                <ComboBox ItemsSource="{Binding AvailableBlendModes}" SelectedItem="{Binding BlendMode}"/>
              </StackPanel>
'@
$t = Replace-Once $t $old $new 'workspace mask blend controls'
Write-Utf8 $path $t

# -----------------------------------------------------------------------------
# FFmpeg: mask alpha, true blend modes, and correct delayed-overlay PTS.
# -----------------------------------------------------------------------------
$path = 'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs'
$t = Read-Utf8 $path

$old = @'
        ChromaKeyColor = clip.ChromaKeyColor,
        ChromaKeySimilarity = clip.ChromaKeySimilarity,
        ChromaKeyBlend = clip.ChromaKeyBlend
    };
'@
$new = @'
        ChromaKeyColor = clip.ChromaKeyColor,
        ChromaKeySimilarity = clip.ChromaKeySimilarity,
        ChromaKeyBlend = clip.ChromaKeyBlend,
        MaskType = clip.MaskType,
        MaskCenterXPercent = clip.MaskCenterXPercent,
        MaskCenterYPercent = clip.MaskCenterYPercent,
        MaskWidthPercent = clip.MaskWidthPercent,
        MaskHeightPercent = clip.MaskHeightPercent,
        MaskFeatherPercent = clip.MaskFeatherPercent,
        MaskRotationDegrees = clip.MaskRotationDegrees,
        MaskInvert = clip.MaskInvert,
        BlendMode = clip.BlendMode
    };
'@
$t = Replace-Once $t $old $new 'range clone compositing state'

$old = @'
            var inputIndex = inputs.Count;
            inputs.Add(asset.FilePath);

            var scale = Math.Clamp(clip.ScalePercent, 1, 1000) / 100.0;
            var overlayWidth = Math.Max(1, (int)Math.Round(targetWidth * scale));
            var opacity = Math.Clamp(clip.Opacity, 0, 1);

            var preparedLabel = $"[ovl{i}]";
            var prepared = new StringBuilder();
            prepared.Append(FormattableString.Invariant(
                $"[{inputIndex}:v]trim=start={clip.SourceTrimInSeconds}:end={clip.SourceTrimOutSeconds},setpts=PTS-STARTPTS"));
            // -1 keeps the source aspect ratio; the overlay is sized by width only.
            prepared.Append(BuildTemporalVideoFilters(clip, clip.TimelineDurationSeconds));
            prepared.Append(BuildSpeedFilter(clip));
            prepared.Append(BuildTransformFilters(clip));
            prepared.Append(FormattableString.Invariant($",scale={overlayWidth}:-1"));
            prepared.Append(BuildEffectFilters(clip));
            // colorchannelmixer/chromakey need an alpha-capable pixel format.
            prepared.Append(",format=rgba");
            prepared.Append(BuildChromaKeyFilter(clip));
            prepared.Append(FormattableString.Invariant($",colorchannelmixer=aa={opacity}"));
            prepared.Append(preparedLabel);
            filterLines.Add(prepared.ToString());

            // Centre-anchored: shift left/up by half the overlay's own rendered size. main_w/overlay_w are
            // ffmpeg's own variables for the base and overlay sizes, so this stays correct even though the
            // overlay's height is only known to ffmpeg (scale=-1 above).
            var centreX = FormattableString.Invariant($"(main_w*{clip.PositionXPercent / 100.0})-(overlay_w/2)");
            var centreY = FormattableString.Invariant($"(main_h*{clip.PositionYPercent / 100.0})-(overlay_h/2)");

            var start = mapToRenderedTime(clip.TimelineStartSeconds);
            var end = mapToRenderedTime(clip.TimelineEndSeconds);

            var outLabel = i == overlayClips.Count - 1 ? "[vlayered]" : $"[vlay{i}]";
            filterLines.Add(FormattableString.Invariant(
                $"{currentLabel}{preparedLabel}overlay=x='{centreX}':y='{centreY}':enable='between(t,{start},{end})'{outLabel}"));

            currentLabel = outLabel;
'@
$new = @'
            var inputIndex = inputs.Count;
            inputs.Add(asset.FilePath);

            var scale = Math.Clamp(clip.ScalePercent, 1, 1000) / 100.0;
            var overlayWidth = Math.Max(1, (int)Math.Round(targetWidth * scale));
            var opacity = Math.Clamp(clip.Opacity, 0, 1);
            var start = mapToRenderedTime(clip.TimelineStartSeconds);
            var end = mapToRenderedTime(clip.TimelineEndSeconds);

            var preparedLabel = $"[ovl{i}]";
            var prepared = new StringBuilder();
            prepared.Append(FormattableString.Invariant(
                $"[{inputIndex}:v]trim=start={clip.SourceTrimInSeconds}:end={clip.SourceTrimOutSeconds},setpts=PTS-STARTPTS"));
            // -1 keeps the source aspect ratio; the overlay is sized by width only.
            prepared.Append(BuildTemporalVideoFilters(clip, clip.TimelineDurationSeconds));
            prepared.Append(BuildSpeedFilter(clip));
            prepared.Append(BuildTransformFilters(clip));
            prepared.Append(FormattableString.Invariant($",scale={overlayWidth}:-1"));
            prepared.Append(BuildEffectFilters(clip));
            // colorchannelmixer/chromakey/masks need an alpha-capable pixel format.
            prepared.Append(",format=rgba");
            prepared.Append(BuildChromaKeyFilter(clip));
            prepared.Append(BuildMaskFilter(clip));
            prepared.Append(FormattableString.Invariant($",colorchannelmixer=aa={opacity}"));
            // Overlay streams used to stay at t=0 regardless of where the clip lived on the timeline.
            // That makes a delayed overlay repeat its last decoded frame when enable() finally turns on.
            // Shift the prepared overlay into the same rendered clock as the base before compositing it.
            prepared.Append(FormattableString.Invariant($",setpts=PTS+{start}/TB"));
            prepared.Append(preparedLabel);
            filterLines.Add(prepared.ToString());

            // Centre-anchored: shift left/up by half the overlay's own rendered size.
            var centreX = FormattableString.Invariant($"(main_w*{clip.PositionXPercent / 100.0})-(overlay_w/2)");
            var centreY = FormattableString.Invariant($"(main_h*{clip.PositionYPercent / 100.0})-(overlay_h/2)");

            var outLabel = i == overlayClips.Count - 1 ? "[vlayered]" : $"[vlay{i}]";
            if (clip.BlendMode == ClipBlendMode.Normal)
            {
                filterLines.Add(FormattableString.Invariant(
                    $"{currentLabel}{preparedLabel}overlay=x='{centreX}':y='{centreY}':enable='between(t,{start},{end})':format=auto{outLabel}"));
            }
            else
            {
                // Build a transparent full-frame layer, place the masked overlay on it, then blend only
                // inside that layer's alpha. maskedmerge prevents multiply/screen/etc. from changing the
                // base outside the overlay's actual visible pixels.
                var canvas = $"[blendcanvas{i}]";
                var overlayCanvas = $"[blendovlcanvas{i}]";
                var overlayColor = $"[blendovlcolor{i}]";
                var overlayMaskSource = $"[blendmasksrc{i}]";
                var mask = $"[blendmask{i}]";
                var baseBlend = $"[blendbase{i}]";
                var baseKeep = $"[blendkeep{i}]";
                var candidate = $"[blendcandidate{i}]";

                filterLines.Add($"color=c=black@0.0:s={targetWidth}x{targetHeight},format=rgba{canvas}");
                filterLines.Add(FormattableString.Invariant(
                    $"{canvas}{preparedLabel}overlay=x='{centreX}':y='{centreY}':enable='between(t,{start},{end})':format=auto{overlayCanvas}"));
                filterLines.Add($"{overlayCanvas}split=2{overlayColor}{overlayMaskSource}");
                filterLines.Add($"{overlayMaskSource}alphaextract,format=rgba{mask}");
                filterLines.Add($"{currentLabel}format=rgba,split=2{baseBlend}{baseKeep}");
                filterLines.Add($"{overlayColor}{baseBlend}blend=all_mode={BlendModeName(clip.BlendMode)}:shortest=1{candidate}");
                filterLines.Add($"{baseKeep}{candidate}{mask}maskedmerge{outLabel}");
            }

            currentLabel = outLabel;
'@
$t = Replace-Once $t $old $new 'overlay masks blend compositing graph'

$anchor = @'
    public static string BuildChromaKeyFilter(TimelineClip clip)
'@
$insert = @'
    public static string BuildMaskFilter(TimelineClip clip)
    {
        if (clip.MaskType == ClipMaskType.None)
        {
            return string.Empty;
        }

        var cx = Math.Clamp(clip.MaskCenterXPercent, 0, 100) / 100.0;
        var cy = Math.Clamp(clip.MaskCenterYPercent, 0, 100) / 100.0;
        var width = Math.Clamp(clip.MaskWidthPercent, 1, 100) / 100.0;
        var height = Math.Clamp(clip.MaskHeightPercent, 1, 100) / 100.0;
        var feather = Math.Clamp(clip.MaskFeatherPercent, 0, 50) / 100.0;
        var radians = Math.Clamp(clip.MaskRotationDegrees, -180, 180) * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        var dx = FormattableString.Invariant($"(X-W*{cx})");
        var dy = FormattableString.Invariant($"(Y-H*{cy})");
        var rx = FormattableString.Invariant($"(({dx})*{cos}+({dy})*{sin})");
        var ry = FormattableString.Invariant($"(-({dx})*{sin}+({dy})*{cos})");

        string mask;
        switch (clip.MaskType)
        {
            case ClipMaskType.Rectangle:
            {
                var halfW = FormattableString.Invariant($"W*{width / 2.0}");
                var halfH = FormattableString.Invariant($"H*{height / 2.0}");
                mask = feather <= 1e-8
                    ? $"between({rx},-{halfW},{halfW})*between({ry},-{halfH},{halfH})"
                    : FormattableString.Invariant($"clip(min({halfW}-abs({rx}),{halfH}-abs({ry}))/(min(W,H)*{feather}),0,1)");
                break;
            }
            case ClipMaskType.Circle:
            {
                var radius = Math.Min(width, height) / 2.0;
                var distance = $"sqrt(({dx})^2+({dy})^2)";
                var radiusExpr = FormattableString.Invariant($"min(W,H)*{radius}");
                mask = feather <= 1e-8
                    ? $"lte({distance},{radiusExpr})"
                    : FormattableString.Invariant($"clip(({radiusExpr}-{distance})/(min(W,H)*{feather}),0,1)");
                break;
            }
            case ClipMaskType.Linear:
            {
                var projection = FormattableString.Invariant($"(({dx})*{cos}+({dy})*{sin})");
                mask = feather <= 1e-8
                    ? $"gte({projection},0)"
                    : FormattableString.Invariant($"clip(0.5+({projection})/(min(W,H)*{feather}*2),0,1)");
                break;
            }
            default:
                return string.Empty;
        }

        if (clip.MaskInvert)
        {
            mask = $"1-({mask})";
        }

        return $",geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='alpha(X,Y)*({mask})'";
    }

    private static string BlendModeName(ClipBlendMode mode) => mode switch
    {
        ClipBlendMode.Multiply => "multiply",
        ClipBlendMode.Screen => "screen",
        ClipBlendMode.Overlay => "overlay",
        ClipBlendMode.Add => "addition",
        ClipBlendMode.Difference => "difference",
        _ => "normal"
    };

'@
$t = Replace-Once $t $anchor ($insert + $anchor) 'mask and blend helpers'
Write-Utf8 $path $t

# -----------------------------------------------------------------------------
# Unit/regression tests for session, clone, graph, PTS and mask expressions.
# -----------------------------------------------------------------------------
$testPath = 'tests/NPVideoStudio.UnitTests/CapCutP2MasksBlendTests.cs'
$test = @'
using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class CapCutP2MasksBlendTests
{
    [Fact]
    public void Session_SetClipCompositing_PersistsAndUndoRestores()
    {
        var clip = new TimelineClip { Id = "c", MediaAssetId = "m", SourceTrimOutSeconds = 5 };
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } };
        var session = new TimelineEditSession(new[] { track });

        session.SetClipCompositing("c", new ClipCompositingSettings(
            ClipMaskType.Circle, 40, 60, 70, 65, 12, 25, true, ClipBlendMode.Screen));

        var changed = session.Tracks.Single().Clips.Single();
        Assert.Equal(ClipMaskType.Circle, changed.MaskType);
        Assert.Equal(40, changed.MaskCenterXPercent, 6);
        Assert.Equal(12, changed.MaskFeatherPercent, 6);
        Assert.True(changed.MaskInvert);
        Assert.Equal(ClipBlendMode.Screen, changed.BlendMode);

        session.Undo();
        var restored = session.Tracks.Single().Clips.Single();
        Assert.Equal(ClipMaskType.None, restored.MaskType);
        Assert.False(restored.MaskInvert);
        Assert.Equal(ClipBlendMode.Normal, restored.BlendMode);
    }

    [Theory]
    [InlineData(ClipMaskType.Rectangle)]
    [InlineData(ClipMaskType.Circle)]
    [InlineData(ClipMaskType.Linear)]
    public void BuildMaskFilter_EmitsRealAlphaGeq(ClipMaskType type)
    {
        var clip = new TimelineClip
        {
            MaskType = type,
            MaskCenterXPercent = 50,
            MaskCenterYPercent = 50,
            MaskWidthPercent = 60,
            MaskHeightPercent = 70,
            MaskFeatherPercent = 8,
            MaskRotationDegrees = 20
        };
        var filter = FfmpegFilterGraphBuilder.BuildMaskFilter(clip);
        Assert.Contains("geq=", filter);
        Assert.Contains("alpha(X,Y)", filter);
        Assert.Contains("clip(", filter);
    }

    [Fact]
    public void Build_OverlayMaskBlendAndDelayedPts_AreInRealGraph()
    {
        var baseAsset = new MediaAsset { Id = "base", FilePath = "/media/base.mp4" };
        var overlayAsset = new MediaAsset { Id = "ov", FilePath = "/media/ov.mp4" };
        var timeline = new Timeline
        {
            Tracks = new List<TimelineTrack>
            {
                new()
                {
                    Kind = TimelineTrackKind.Video,
                    Clips = new List<TimelineClip>
                    {
                        new() { MediaAssetId = baseAsset.Id, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 10, TimelineStartSeconds = 0 }
                    }
                },
                new()
                {
                    Kind = TimelineTrackKind.Video,
                    Clips = new List<TimelineClip>
                    {
                        new()
                        {
                            MediaAssetId = overlayAsset.Id, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 2, TimelineStartSeconds = 5,
                            MaskType = ClipMaskType.Circle, MaskWidthPercent = 60, MaskHeightPercent = 60,
                            MaskFeatherPercent = 5, BlendMode = ClipBlendMode.Screen
                        }
                    }
                }
            }
        };

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { baseAsset, overlayAsset }, 320, 240);
        Assert.Contains("geq=", plan.FilterComplexArgument);
        Assert.Contains("blend=all_mode=screen", plan.FilterComplexArgument);
        Assert.Contains("maskedmerge", plan.FilterComplexArgument);
        Assert.Contains("alphaextract", plan.FilterComplexArgument);
        Assert.Contains("setpts=PTS+5/TB", plan.FilterComplexArgument);
    }

    [Fact]
    public void ExtractRangeTimeline_PreservesMaskAndBlendState()
    {
        var clip = new TimelineClip
        {
            MediaAssetId = "m", SourceTrimOutSeconds = 8, TimelineStartSeconds = 2,
            MaskType = ClipMaskType.Rectangle, MaskCenterXPercent = 30, MaskCenterYPercent = 70,
            MaskWidthPercent = 55, MaskHeightPercent = 45, MaskFeatherPercent = 9,
            MaskRotationDegrees = -15, MaskInvert = true, BlendMode = ClipBlendMode.Multiply
        };
        var timeline = new Timeline
        {
            Tracks = new List<TimelineTrack> { new() { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } } }
        };

        var range = FfmpegFilterGraphBuilder.ExtractRangeTimeline(timeline, 3, 5);
        var copied = range.Tracks.Single().Clips.Single();
        Assert.Equal(ClipMaskType.Rectangle, copied.MaskType);
        Assert.Equal(30, copied.MaskCenterXPercent, 6);
        Assert.Equal(9, copied.MaskFeatherPercent, 6);
        Assert.Equal(-15, copied.MaskRotationDegrees, 6);
        Assert.True(copied.MaskInvert);
        Assert.Equal(ClipBlendMode.Multiply, copied.BlendMode);
    }
}
'@
[System.IO.File]::WriteAllText((Join-Path (Get-Location) $testPath), $test, (New-Object System.Text.UTF8Encoding($false)))

# -----------------------------------------------------------------------------
# Real ffmpeg render test: full-frame red overlay, circular mask + Screen blend.
# Corner must remain blue (mask), centre must become magenta (blend).
# -----------------------------------------------------------------------------
$path = 'tests/NPVideoStudio.UnitTests/RenderServiceTests.cs'
$t = Read-Utf8 $path
$anchor = @'
    [Fact]
    public async Task RenderAsync_OutputAlreadyExistsWithoutConfirmation_ThrowsWithoutOverwriting()
'@
$insert = @'
    private static async Task<(byte R, byte G, byte B)> ReadRgbPixelAsync(string videoPath, int x, int y)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-v");
        process.StartInfo.ArgumentList.Add("error");
        process.StartInfo.ArgumentList.Add("-ss");
        process.StartInfo.ArgumentList.Add("0.2");
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(videoPath);
        process.StartInfo.ArgumentList.Add("-vf");
        process.StartInfo.ArgumentList.Add($"format=rgb24,crop=1:1:{x}:{y}");
        process.StartInfo.ArgumentList.Add("-frames:v");
        process.StartInfo.ArgumentList.Add("1");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("rawvideo");
        process.StartInfo.ArgumentList.Add("pipe:1");

        process.Start();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var bytes = new byte[3];
        var read = 0;
        while (read < bytes.Length)
        {
            var n = await process.StandardOutput.BaseStream.ReadAsync(bytes.AsMemory(read, bytes.Length - read));
            if (n == 0) break;
            read += n;
        }
        await process.WaitForExitAsync();
        await stderrTask;
        Assert.Equal(3, read);
        return (bytes[0], bytes[1], bytes[2]);
    }

    [Fact]
    public async Task RenderAsync_CircleMaskWithScreenBlend_ChangesOnlyMaskedArea()
    {
        var basePath = await CreateSolidColorClipAsync("mask-base.mp4", "blue", 1, 440);
        var overlayPath = await CreateSolidColorClipAsync("mask-overlay.mp4", "red", 1, 660);
        var baseAsset = new MediaAsset { Id = "base", FilePath = basePath, Duration = TimeSpan.FromSeconds(1) };
        var overlayAsset = new MediaAsset { Id = "overlay", FilePath = overlayPath, Duration = TimeSpan.FromSeconds(1) };
        var project = new Project { Name = "Mask blend render", MediaLibrary = { baseAsset, overlayAsset } };

        project.Timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Video,
            Clips =
            {
                new TimelineClip { MediaAssetId = baseAsset.Id, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 1, TimelineStartSeconds = 0 }
            }
        });
        project.Timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Video,
            Clips =
            {
                new TimelineClip
                {
                    MediaAssetId = overlayAsset.Id, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 1, TimelineStartSeconds = 0,
                    ScalePercent = 100, PositionXPercent = 50, PositionYPercent = 50,
                    MaskType = ClipMaskType.Circle, MaskCenterXPercent = 50, MaskCenterYPercent = 50,
                    MaskWidthPercent = 60, MaskHeightPercent = 60, MaskFeatherPercent = 5,
                    BlendMode = ClipBlendMode.Screen
                }
            }
        });

        var job = new RenderJob
        {
            ProjectName = project.Name,
            Settings = new RenderSettings { OutputFilePath = Path.Combine(_tempDir, "mask-blend.mp4"), OverwriteConfirmed = true }
        };
        var output = await _service.RenderAsync(project, job);

        var centre = await ReadRgbPixelAsync(output, 160, 120);
        var corner = await ReadRgbPixelAsync(output, 10, 10);
        Assert.True(centre.R > 150 && centre.B > 150 && centre.G < 100,
            $"Screen-blended centre should be magenta-ish, got {centre}.");
        Assert.True(corner.B > 150 && corner.R < 100 && corner.G < 100,
            $"Outside circular mask should remain blue, got {corner}.");
    }

'@
$t = Replace-Once $t $anchor ($insert + $anchor) 'real mask blend render test'
Write-Utf8 $path $t

Write-Host 'CapCut P2 masks + blend modes patch applied.'
