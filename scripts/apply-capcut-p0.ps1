$ErrorActionPreference = 'Stop'

function Read-Utf8([string]$Path) {
    return [System.IO.File]::ReadAllText((Resolve-Path $Path), [System.Text.Encoding]::UTF8)
}

function Write-Utf8([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText((Resolve-Path $Path), $Text, (New-Object System.Text.UTF8Encoding($false)))
}

function Replace-Once([string]$Text, [string]$Old, [string]$New, [string]$Label) {
    $index = $Text.IndexOf($Old, [StringComparison]::Ordinal)
    if ($index -lt 0) { throw "Anchor not found: $Label" }
    if ($Text.IndexOf($Old, $index + $Old.Length, [StringComparison]::Ordinal) -ge 0) { throw "Anchor not unique: $Label" }
    return $Text.Substring(0, $index) + $New + $Text.Substring($index + $Old.Length)
}

# -----------------------------------------------------------------------------
# Domain model: persisted transform/chroma/reverse/freeze state.
# -----------------------------------------------------------------------------
$path = 'src/NPVideoStudio.Domain/Timeline.cs'
$t = Read-Utf8 $path

$anchor = @'
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
'@
$replacement = $anchor + @'

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
'@
$t = Replace-Once $t $anchor $replacement 'Timeline ClipTransformSettings record'

$anchor = @'
    /// <summary>Playback speed, 0.25..4. 1 = normal, 0.5 = slow motion, 2 = double speed.</summary>
    public double SpeedMultiplier { get; set; } = 1.0;
'@
$replacement = $anchor + @'

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
'@
$t = Replace-Once $t $anchor $replacement 'Timeline transform fields'
Write-Utf8 $path $t

# -----------------------------------------------------------------------------
# Edit session: undo-safe setter + snapshot persistence.
# -----------------------------------------------------------------------------
$path = 'src/NPVideoStudio.AI/TimelineEditSession.cs'
$t = Read-Utf8 $path
$anchor = @'
    public void SetClipMute(string clipId, bool muted)
'@
$method = @'
    public void SetClipTransform(string clipId, ClipTransformSettings settings)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null)
        {
            return;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        liveClip.RotationDegrees = Math.Clamp(settings.RotationDegrees, -360, 360);
        liveClip.FlipHorizontal = settings.FlipHorizontal;
        liveClip.FlipVertical = settings.FlipVertical;
        liveClip.CropLeftPercent = Math.Clamp(settings.CropLeftPercent, 0, 45);
        liveClip.CropTopPercent = Math.Clamp(settings.CropTopPercent, 0, 45);
        liveClip.CropRightPercent = Math.Clamp(settings.CropRightPercent, 0, 45);
        liveClip.CropBottomPercent = Math.Clamp(settings.CropBottomPercent, 0, 45);
        liveClip.IsReversed = settings.IsReversed;
        liveClip.IsFreezeFrame = settings.IsFreezeFrame;
        liveClip.ChromaKeyEnabled = settings.ChromaKeyEnabled;
        liveClip.ChromaKeyColor = string.IsNullOrWhiteSpace(settings.ChromaKeyColor) ? "#00FF00" : settings.ChromaKeyColor;
        liveClip.ChromaKeySimilarity = Math.Clamp(settings.ChromaKeySimilarity, 0.01, 1.0);
        liveClip.ChromaKeyBlend = Math.Clamp(settings.ChromaKeyBlend, 0, 1.0);
    }

'@
$t = Replace-Once $t $anchor ($method + $anchor) 'TimelineEditSession SetClipTransform'

$anchor = @'
        SpeedMultiplier = clip.SpeedMultiplier
'@
$replacement = @'
        SpeedMultiplier = clip.SpeedMultiplier,
        RotationDegrees = clip.RotationDegrees,
        FlipHorizontal = clip.FlipHorizontal,
        FlipVertical = clip.FlipVertical,
        CropLeftPercent = clip.CropLeftPercent,
        CropTopPercent = clip.CropTopPercent,
        CropRightPercent = clip.CropRightPercent,
        CropBottomPercent = clip.CropBottomPercent,
        IsReversed = clip.IsReversed,
        IsFreezeFrame = clip.IsFreezeFrame,
        ChromaKeyEnabled = clip.ChromaKeyEnabled,
        ChromaKeyColor = clip.ChromaKeyColor,
        ChromaKeySimilarity = clip.ChromaKeySimilarity,
        ChromaKeyBlend = clip.ChromaKeyBlend
'@
$t = Replace-Once $t $anchor $replacement 'TimelineEditSession clone transform fields'
Write-Utf8 $path $t

# -----------------------------------------------------------------------------
# Clip ViewModel: expose controls and route them through session callback.
# -----------------------------------------------------------------------------
$path = 'src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs'
$t = Read-Utf8 $path
$anchor = @'
    private readonly Action<string, ClipVideoEffect, double, double, double, double>? _onEffectsChanged;
'@
$replacement = $anchor + @'
    private readonly Action<string, ClipTransformSettings>? _onTransformChanged;
'@
$t = Replace-Once $t $anchor $replacement 'TimelineClipItemViewModel transform callback field'

$anchor = @'
    /// <summary>The clip's own words, editable - real fix for "how do I check/correct what Whisper
'@
$properties = @'
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

'@
$t = Replace-Once $t $anchor ($properties + $anchor) 'TimelineClipItemViewModel transform properties'

$anchor = @'
        Action<string, ClipVideoEffect, double, double, double, double>? onEffectsChanged = null)
'@
$replacement = @'
        Action<string, ClipVideoEffect, double, double, double, double>? onEffectsChanged = null,
        Action<string, ClipTransformSettings>? onTransformChanged = null)
'@
$t = Replace-Once $t $anchor $replacement 'TimelineClipItemViewModel constructor signature'

$anchor = @'
        _onEffectsChanged = onEffectsChanged;
'@
$replacement = $anchor + @'
        _onTransformChanged = onTransformChanged;
'@
$t = Replace-Once $t $anchor $replacement 'TimelineClipItemViewModel constructor assignment'
Write-Utf8 $path $t

# -----------------------------------------------------------------------------
# Timeline ViewModel: connect transform UI changes to edit session.
# -----------------------------------------------------------------------------
$path = 'src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs'
$t = Read-Utf8 $path
$anchor = @'
        void OnEffectsChanged(string clipId, ClipVideoEffect effect, double brightness, double contrast, double saturation, double speed)
        {
            _session.SetClipEffects(clipId, effect, brightness, contrast, saturation, speed);
            RefreshFromSession();
        }
'@
$replacement = $anchor + @'
        void OnTransformChanged(string clipId, ClipTransformSettings settings)
        {
            _session.SetClipTransform(clipId, settings);
            RefreshFromSession();
        }
'@
$t = Replace-Once $t $anchor $replacement 'TimelineViewModel transform callback'

$anchor = @'
            OnLayerPlacementChanged, track.Kind == TimelineTrackKind.ImageOverlay, OnEffectsChanged)
'@
$replacement = @'
            OnLayerPlacementChanged, track.Kind == TimelineTrackKind.ImageOverlay, OnEffectsChanged, OnTransformChanged)
'@
$t = Replace-Once $t $anchor $replacement 'TimelineViewModel pass transform callback'
Write-Utf8 $path $t

# -----------------------------------------------------------------------------
# FFmpeg renderer: crop/rotate/flip, reverse, freeze-frame and chroma key.
# -----------------------------------------------------------------------------
$path = 'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs'
$t = Read-Utf8 $path

$anchor = @'
            videoFilter.Append(BuildSpeedFilter(clip));
            videoFilter.Append(FormattableString.Invariant(
'@
$replacement = @'
            videoFilter.Append(BuildTemporalVideoFilters(clip, duration));
            videoFilter.Append(BuildSpeedFilter(clip));
            videoFilter.Append(BuildTransformFilters(clip));
            videoFilter.Append(FormattableString.Invariant(
'@
$t = Replace-Once $t $anchor $replacement 'base video transform pipeline'

$anchor = @'
                $"[{inputIndex}:a]atrim=start={clip.SourceTrimInSeconds}:end={clip.SourceTrimOutSeconds},asetpts=PTS-STARTPTS,volume={volume}"));
'@
$replacement = @'
                $"[{inputIndex}:a]atrim=start={clip.SourceTrimInSeconds}:end={clip.SourceTrimOutSeconds},asetpts=PTS-STARTPTS,volume={(clip.IsFreezeFrame ? 0 : volume)}"));
            if (clip.IsReversed && !clip.IsFreezeFrame)
            {
                audioFilter.Append(",areverse");
            }
'@
$t = Replace-Once $t $anchor $replacement 'reverse/freeze base audio'

$anchor = @'
            prepared.Append(BuildSpeedFilter(clip));
            prepared.Append(FormattableString.Invariant($",scale={overlayWidth}:-1"));
            prepared.Append(BuildEffectFilters(clip));
            // colorchannelmixer needs an alpha channel to write into, hence format=rgba first.
            prepared.Append(FormattableString.Invariant($",format=rgba,colorchannelmixer=aa={opacity}"));
'@
$replacement = @'
            prepared.Append(BuildTemporalVideoFilters(clip, clip.TimelineDurationSeconds));
            prepared.Append(BuildSpeedFilter(clip));
            prepared.Append(BuildTransformFilters(clip));
            prepared.Append(FormattableString.Invariant($",scale={overlayWidth}:-1"));
            prepared.Append(BuildEffectFilters(clip));
            // colorchannelmixer/chromakey need an alpha-capable pixel format.
            prepared.Append(",format=rgba");
            prepared.Append(BuildChromaKeyFilter(clip));
            prepared.Append(FormattableString.Invariant($",colorchannelmixer=aa={opacity}"));
'@
$t = Replace-Once $t $anchor $replacement 'overlay transform/chroma pipeline'

$anchor = @'
    public static string BuildEffectFilters(TimelineClip clip)
'@
$helpers = @'
    public static string BuildTemporalVideoFilters(TimelineClip clip, double durationSeconds)
    {
        var parts = new List<string>();
        if (clip.IsReversed)
        {
            parts.Add("reverse");
        }
        if (clip.IsFreezeFrame)
        {
            var hold = Math.Max(0.01, durationSeconds - 0.04);
            parts.Add("trim=start=0:end=0.04");
            parts.Add("setpts=PTS-STARTPTS");
            parts.Add(FormattableString.Invariant($"tpad=stop_mode=clone:stop_duration={hold}"));
        }
        return parts.Count == 0 ? string.Empty : "," + string.Join(",", parts);
    }

    public static string BuildTransformFilters(TimelineClip clip)
    {
        var parts = new List<string>();
        var left = Math.Clamp(clip.CropLeftPercent, 0, 45) / 100.0;
        var top = Math.Clamp(clip.CropTopPercent, 0, 45) / 100.0;
        var right = Math.Clamp(clip.CropRightPercent, 0, 45) / 100.0;
        var bottom = Math.Clamp(clip.CropBottomPercent, 0, 45) / 100.0;
        if (left + top + right + bottom > 1e-8)
        {
            var width = Math.Max(0.1, 1 - left - right);
            var height = Math.Max(0.1, 1 - top - bottom);
            parts.Add(FormattableString.Invariant($"crop=iw*{width}:ih*{height}:iw*{left}:ih*{top}"));
        }
        if (clip.FlipHorizontal) parts.Add("hflip");
        if (clip.FlipVertical) parts.Add("vflip");

        var rotation = clip.RotationDegrees % 360.0;
        if (Math.Abs(rotation) > 1e-6)
        {
            parts.Add(FormattableString.Invariant(
                $"rotate={rotation}*PI/180:ow=rotw({rotation}*PI/180):oh=roth({rotation}*PI/180):c=black"));
        }
        return parts.Count == 0 ? string.Empty : "," + string.Join(",", parts);
    }

    public static string BuildChromaKeyFilter(TimelineClip clip)
    {
        if (!clip.ChromaKeyEnabled)
        {
            return string.Empty;
        }
        var color = string.IsNullOrWhiteSpace(clip.ChromaKeyColor) ? "00FF00" : clip.ChromaKeyColor.Trim().TrimStart('#');
        if (color.Length != 6 || color.Any(c => !Uri.IsHexDigit(c)))
        {
            color = "00FF00";
        }
        var similarity = Math.Clamp(clip.ChromaKeySimilarity, 0.01, 1.0);
        var blend = Math.Clamp(clip.ChromaKeyBlend, 0, 1.0);
        return FormattableString.Invariant($",chromakey=0x{color}:{similarity}:{blend}");
    }

'@
$t = Replace-Once $t $anchor ($helpers + $anchor) 'FFmpeg transform helper methods'

$anchor = @'
        SpeedMultiplier = clip.SpeedMultiplier
'@
$replacement = @'
        SpeedMultiplier = clip.SpeedMultiplier,
        RotationDegrees = clip.RotationDegrees,
        FlipHorizontal = clip.FlipHorizontal,
        FlipVertical = clip.FlipVertical,
        CropLeftPercent = clip.CropLeftPercent,
        CropTopPercent = clip.CropTopPercent,
        CropRightPercent = clip.CropRightPercent,
        CropBottomPercent = clip.CropBottomPercent,
        IsReversed = clip.IsReversed,
        IsFreezeFrame = clip.IsFreezeFrame,
        ChromaKeyEnabled = clip.ChromaKeyEnabled,
        ChromaKeyColor = clip.ChromaKeyColor,
        ChromaKeySimilarity = clip.ChromaKeySimilarity,
        ChromaKeyBlend = clip.ChromaKeyBlend
'@
$t = Replace-Once $t $anchor $replacement 'range clone transform fields'
Write-Utf8 $path $t

# -----------------------------------------------------------------------------
# Workspace inspector UI. This is intentionally visible only for non-text clips.
# -----------------------------------------------------------------------------
$path = 'src/NPVideoStudio.App/Views/WorkspaceView.axaml'
$t = Read-Utf8 $path
$anchor = @'
              <TextBlock Text="Brzina" Classes="subtle"/><NumericUpDown Value="{Binding SpeedMultiplier}" Minimum="0.25" Maximum="4" Increment="0.25"/>
'@
$replacement = $anchor + @'
              <TextBlock Text="TRANSFORMACIJA" Classes="eyebrow" Margin="0,8,0,0" />
              <TextBlock Text="Rotacija (stepeni)" Classes="subtle"/><NumericUpDown Value="{Binding RotationDegrees}" Minimum="-360" Maximum="360" Increment="1"/>
              <WrapPanel Orientation="Horizontal">
                <ToggleButton Content="Flip horizontalno" IsChecked="{Binding FlipHorizontal}" Margin="0,0,8,4"/>
                <ToggleButton Content="Flip vertikalno" IsChecked="{Binding FlipVertical}" Margin="0,0,8,4"/>
              </WrapPanel>
              <TextBlock Text="Crop (%) — levo / gore / desno / dole" Classes="subtle"/>
              <Grid ColumnDefinitions="*,*,*,*" ColumnSpacing="6">
                <NumericUpDown Grid.Column="0" Value="{Binding CropLeftPercent}" Minimum="0" Maximum="45" Increment="1"/>
                <NumericUpDown Grid.Column="1" Value="{Binding CropTopPercent}" Minimum="0" Maximum="45" Increment="1"/>
                <NumericUpDown Grid.Column="2" Value="{Binding CropRightPercent}" Minimum="0" Maximum="45" Increment="1"/>
                <NumericUpDown Grid.Column="3" Value="{Binding CropBottomPercent}" Minimum="0" Maximum="45" Increment="1"/>
              </Grid>
              <WrapPanel Orientation="Horizontal">
                <ToggleButton Content="Reverse" IsChecked="{Binding IsReversed}" Margin="0,0,8,4"/>
                <ToggleButton Content="Freeze frame" IsChecked="{Binding IsFreezeFrame}" Margin="0,0,8,4"/>
              </WrapPanel>
              <TextBlock Text="GREEN SCREEN / CHROMA KEY" Classes="eyebrow" Margin="0,8,0,0" />
              <ToggleButton Content="Uključi Chroma Key" IsChecked="{Binding ChromaKeyEnabled}"/>
              <TextBlock Text="Boja (#RRGGBB)" Classes="subtle"/><TextBox Text="{Binding ChromaKeyColor}" Watermark="#00FF00"/>
              <TextBlock Text="Sličnost" Classes="subtle"/><Slider Minimum="0.01" Maximum="1" Value="{Binding ChromaKeySimilarity}"/>
              <TextBlock Text="Feather / blend" Classes="subtle"/><Slider Minimum="0" Maximum="1" Value="{Binding ChromaKeyBlend}"/>
'@
$t = Replace-Once $t $anchor $replacement 'Workspace inspector CapCut P0 controls'
Write-Utf8 $path $t

# -----------------------------------------------------------------------------
# Regression tests for persisted state and FFmpeg graph output.
# -----------------------------------------------------------------------------
$testPath = 'tests/NPVideoStudio.UnitTests/CapCutP0TransformTests.cs'
$test = @'
using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class CapCutP0TransformTests
{
    [Fact]
    public void Session_SetClipTransform_PersistsAndUndoRestores()
    {
        var clip = new TimelineClip { Id = "c", MediaAssetId = "m", SourceTrimOutSeconds = 5 };
        var track = new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } };
        var session = new TimelineEditSession(new[] { track });
        session.SetClipTransform("c", new ClipTransformSettings(90, true, true, 10, 5, 7, 3, true, false, true, "#00FF00", .2, .05));

        var changed = session.Tracks.Single().Clips.Single();
        Assert.Equal(90, changed.RotationDegrees, 6);
        Assert.True(changed.FlipHorizontal);
        Assert.True(changed.FlipVertical);
        Assert.Equal(10, changed.CropLeftPercent, 6);
        Assert.True(changed.IsReversed);
        Assert.True(changed.ChromaKeyEnabled);

        session.Undo();
        var restored = session.Tracks.Single().Clips.Single();
        Assert.Equal(0, restored.RotationDegrees, 6);
        Assert.False(restored.FlipHorizontal);
        Assert.False(restored.IsReversed);
        Assert.False(restored.ChromaKeyEnabled);
    }

    [Fact]
    public void BuildTransformFilters_EmitsCropFlipAndRotation()
    {
        var clip = new TimelineClip
        {
            CropLeftPercent = 10,
            CropRightPercent = 5,
            CropTopPercent = 3,
            CropBottomPercent = 2,
            FlipHorizontal = true,
            FlipVertical = true,
            RotationDegrees = 90
        };
        var filters = FfmpegFilterGraphBuilder.BuildTransformFilters(clip);
        Assert.Contains("crop=", filters);
        Assert.Contains("hflip", filters);
        Assert.Contains("vflip", filters);
        Assert.Contains("rotate=90*PI/180", filters);
    }

    [Fact]
    public void BuildChromaKeyFilter_EmitsRealChromakeyFilter()
    {
        var clip = new TimelineClip { ChromaKeyEnabled = true, ChromaKeyColor = "#00FF00", ChromaKeySimilarity = .18, ChromaKeyBlend = .04 };
        var filter = FfmpegFilterGraphBuilder.BuildChromaKeyFilter(clip);
        Assert.Contains("chromakey=0x00FF00", filter);
        Assert.Contains("0.18", filter);
        Assert.Contains("0.04", filter);
    }

    [Fact]
    public void BuildTemporalVideoFilters_EmitsReverseAndFreeze()
    {
        var clip = new TimelineClip { IsReversed = true, IsFreezeFrame = true };
        var filters = FfmpegFilterGraphBuilder.BuildTemporalVideoFilters(clip, 3);
        Assert.Contains("reverse", filters);
        Assert.Contains("tpad=stop_mode=clone", filters);
    }

    [Fact]
    public void ExtractRangeTimeline_PreservesCapCutP0State()
    {
        var clip = new TimelineClip
        {
            Id = "c", MediaAssetId = "m", SourceTrimOutSeconds = 8, TimelineStartSeconds = 2,
            RotationDegrees = 33, FlipHorizontal = true, CropLeftPercent = 12,
            IsReversed = true, IsFreezeFrame = true, ChromaKeyEnabled = true,
            ChromaKeyColor = "#12AB34", ChromaKeySimilarity = .3, ChromaKeyBlend = .1
        };
        var timeline = new Timeline { Tracks = new List<TimelineTrack> { new() { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } } } };
        var range = FfmpegFilterGraphBuilder.ExtractRangeTimeline(timeline, 3, 5);
        var copied = range.Tracks.Single().Clips.Single();
        Assert.Equal(33, copied.RotationDegrees, 6);
        Assert.True(copied.FlipHorizontal);
        Assert.Equal(12, copied.CropLeftPercent, 6);
        Assert.True(copied.IsReversed);
        Assert.True(copied.IsFreezeFrame);
        Assert.True(copied.ChromaKeyEnabled);
        Assert.Equal("#12AB34", copied.ChromaKeyColor);
    }
}
'@
[System.IO.File]::WriteAllText((Join-Path (Get-Location) $testPath), $test, (New-Object System.Text.UTF8Encoding($false)))

Write-Host 'CapCut P0 patch applied successfully.'
