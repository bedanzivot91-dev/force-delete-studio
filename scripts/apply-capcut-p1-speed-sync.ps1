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

# Domain: playback speed changes authored timeline duration, not just encoded video timestamps.
$path = 'src/NPVideoStudio.Domain/Timeline.cs'
$t = Read-Utf8 $path
$t = Replace-Once $t `
'    public double TimelineDurationSeconds => Math.Max(0, SourceTrimOutSeconds - SourceTrimInSeconds);' `
'    public double TimelineDurationSeconds => Math.Max(0, SourceTrimOutSeconds - SourceTrimInSeconds) / (IsFreezeFrame ? 1.0 : Math.Clamp(SpeedMultiplier, 0.25, 4));' `
'Timeline duration speed'
Write-Utf8 $path $t

# Editing math: timeline seconds and source-media seconds differ when speed != 1.
$path = 'src/NPVideoStudio.AI/TimelineEditSession.cs'
$t = Read-Utf8 $path
$t = Replace-Once $t `
'        var splitSourcePoint = clip.SourceTrimInSeconds + offsetIntoClip;' `
'        var splitSourcePoint = clip.SourceTrimInSeconds + offsetIntoClip * (clip.IsFreezeFrame ? 1.0 : Math.Clamp(clip.SpeedMultiplier, 0.25, 4));' `
'speed-aware split'
$t = Replace-Once $t `
'        liveClip.TimelineStartSeconds = Math.Max(0, liveClip.TimelineStartSeconds + delta);' `
'        liveClip.TimelineStartSeconds = Math.Max(0, liveClip.TimelineStartSeconds + delta / (liveClip.IsFreezeFrame ? 1.0 : Math.Clamp(liveClip.SpeedMultiplier, 0.25, 4)));' `
'speed-aware trim in'
Write-Utf8 $path $t

# Expose speed on Audio clips too. Video/image already has the same control in its inspector.
$path = 'src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs'
$t = Read-Utf8 $path
$t = Replace-Once $t `
'    public bool IsPictureClip => IsVideoClip || IsOverlayClip;' `
"    public bool IsPictureClip => IsVideoClip || IsOverlayClip;`r`n    public bool IsAudioClip { get; }" `
'audio clip property'
$t = Replace-Once $t `
'        Action<string, ClipTransformSettings>? onTransformChanged = null)' `
"        Action<string, ClipTransformSettings>? onTransformChanged = null,`r`n        bool isAudioClip = false)" `
'audio constructor argument'
$t = Replace-Once $t `
'        IsVideoClip = isVideoClip;' `
"        IsVideoClip = isVideoClip;`r`n        IsAudioClip = isAudioClip;" `
'audio constructor assignment'
Write-Utf8 $path $t

$path = 'src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs'
$t = Read-Utf8 $path
$t = Replace-Once $t `
'            OnLayerPlacementChanged, track.Kind == TimelineTrackKind.ImageOverlay || (track.Kind == TimelineTrackKind.Video && _session.Tracks.Where(t => t.Kind == TimelineTrackKind.Video).FirstOrDefault()?.Id != track.Id), OnEffectsChanged, OnTransformChanged)' `
'            OnLayerPlacementChanged, track.Kind == TimelineTrackKind.ImageOverlay || (track.Kind == TimelineTrackKind.Video && _session.Tracks.Where(t => t.Kind == TimelineTrackKind.Video).FirstOrDefault()?.Id != track.Id), OnEffectsChanged, OnTransformChanged, track.Kind == TimelineTrackKind.Audio)' `
'pass audio clip classification'
Write-Utf8 $path $t

$path = 'src/NPVideoStudio.App/Views/WorkspaceView.axaml'
$t = Read-Utf8 $path
$anchor = '            <StackPanel Spacing="8" IsVisible="{Binding IsPictureClip}">'
$audioPanel = @'
            <StackPanel Spacing="8" IsVisible="{Binding IsAudioClip}">
              <TextBlock Text="AUDIO" Classes="eyebrow" />
              <TextBlock Text="Brzina" Classes="subtle"/>
              <NumericUpDown Value="{Binding SpeedMultiplier}" Minimum="0.25" Maximum="4" Increment="0.25"/>
            </StackPanel>

'@
$t = Replace-Once $t $anchor ($audioPanel + $anchor) 'audio speed inspector'
Write-Utf8 $path $t

# FFmpeg: keep audio tempo synchronized with video speed, and range preview source math speed-aware.
$path = 'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs'
$t = Read-Utf8 $path

$anchor = @'
            if (clip.IsReversed && !clip.IsFreezeFrame)
            {
                audioFilter.Append(",areverse");
            }
'@
$replacement = $anchor + @'
            audioFilter.Append(BuildAudioSpeedFilter(clip));
'@
$t = Replace-Once $t $anchor $replacement 'base video embedded audio speed'

$oldStandaloneVolume = '                chain.Append(FormattableString.Invariant($",volume={volume}"));'
$newStandaloneVolume = @'
                chain.Append(BuildAudioSpeedFilter(clip));
                chain.Append(FormattableString.Invariant($",volume={volume}"));
'@.TrimEnd("`r", "`n")
$t = Replace-Once $t $oldStandaloneVolume $newStandaloneVolume 'standalone audio speed'

$oldRange = @'
                var newClip = CloneClipForRange(clip);
                newClip.TimelineStartSeconds = overlapStart - rangeStartSeconds;
                newClip.SourceTrimInSeconds = clip.SourceTrimInSeconds + trimmedFromStart;
                newClip.SourceTrimOutSeconds = clip.SourceTrimOutSeconds - trimmedFromEnd;
'@
$newRange = @'
                var newClip = CloneClipForRange(clip);
                newClip.TimelineStartSeconds = overlapStart - rangeStartSeconds;
                var sourceRate = clip.IsFreezeFrame ? 1.0 : Math.Clamp(clip.SpeedMultiplier, 0.25, 4);
                if (clip.IsFreezeFrame)
                {
                    var visibleDuration = Math.Max(0.05, overlapEnd - overlapStart);
                    if (clip.IsReversed)
                    {
                        newClip.SourceTrimOutSeconds = clip.SourceTrimOutSeconds;
                        newClip.SourceTrimInSeconds = Math.Max(clip.SourceTrimInSeconds, clip.SourceTrimOutSeconds - visibleDuration);
                    }
                    else
                    {
                        newClip.SourceTrimInSeconds = clip.SourceTrimInSeconds;
                        newClip.SourceTrimOutSeconds = Math.Min(clip.SourceTrimOutSeconds, clip.SourceTrimInSeconds + visibleDuration);
                    }
                }
                else if (clip.IsReversed)
                {
                    newClip.SourceTrimInSeconds = clip.SourceTrimInSeconds + trimmedFromEnd * sourceRate;
                    newClip.SourceTrimOutSeconds = clip.SourceTrimOutSeconds - trimmedFromStart * sourceRate;
                }
                else
                {
                    newClip.SourceTrimInSeconds = clip.SourceTrimInSeconds + trimmedFromStart * sourceRate;
                    newClip.SourceTrimOutSeconds = clip.SourceTrimOutSeconds - trimmedFromEnd * sourceRate;
                }
'@
$t = Replace-Once $t $oldRange $newRange 'speed-aware range extraction'

$anchor = @'
    private static string TransitionName(ClipTransitionType type) => type switch
'@
$helper = @'
    /// <summary>FFmpeg audio-tempo chain matching <see cref="BuildSpeedFilter"/>. Chained 0.5..2.0
    /// stages work across the full UI range 0.25x..4x without pitch-shifting the audio.</summary>
    public static string BuildAudioSpeedFilter(TimelineClip clip)
    {
        if (clip.IsFreezeFrame)
        {
            return string.Empty;
        }

        var remaining = Math.Clamp(clip.SpeedMultiplier, 0.25, 4);
        if (Math.Abs(remaining - 1) < 1e-6)
        {
            return string.Empty;
        }

        var stages = new List<double>();
        while (remaining < 0.5 - 1e-9)
        {
            stages.Add(0.5);
            remaining /= 0.5;
        }
        while (remaining > 2.0 + 1e-9)
        {
            stages.Add(2.0);
            remaining /= 2.0;
        }
        if (Math.Abs(remaining - 1) > 1e-6)
        {
            stages.Add(remaining);
        }

        return stages.Count == 0
            ? string.Empty
            : "," + string.Join(",", stages.Select(s => FormattableString.Invariant($"atempo={s}")));
    }

'@
$t = Replace-Once $t $anchor ($helper + $anchor) 'audio speed helper'

$t = Replace-Once $t `
'        var speed = Math.Clamp(clip.SpeedMultiplier, 0.25, 4);' `
'        var speed = clip.IsFreezeFrame ? 1.0 : Math.Clamp(clip.SpeedMultiplier, 0.25, 4);' `
'freeze ignores speed filter'
Write-Utf8 $path $t

# Regression tests.
$testPath = 'tests/NPVideoStudio.UnitTests/CapCutSpeedSyncTests.cs'
$test = @'
using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class CapCutSpeedSyncTests
{
    [Fact]
    public void TimelineDuration_ChangesWithSpeed()
    {
        var clip = new TimelineClip { SourceTrimInSeconds = 2, SourceTrimOutSeconds = 12, SpeedMultiplier = 2 };
        Assert.Equal(5, clip.TimelineDurationSeconds, 6);
        clip.SpeedMultiplier = 0.5;
        Assert.Equal(20, clip.TimelineDurationSeconds, 6);
    }

    [Theory]
    [InlineData(2.0, ",atempo=2")]
    [InlineData(0.5, ",atempo=0.5")]
    [InlineData(4.0, ",atempo=2,atempo=2")]
    [InlineData(0.25, ",atempo=0.5,atempo=0.5")]
    public void AudioSpeedFilter_CoversFullUiRange(double speed, string expected)
    {
        var clip = new TimelineClip { SpeedMultiplier = speed };
        Assert.Equal(expected, FfmpegFilterGraphBuilder.BuildAudioSpeedFilter(clip));
    }

    [Fact]
    public void Build_SpeedChangesVideoAudioAndOutputDurationTogether()
    {
        var asset = new MediaAsset { Id = "m", FilePath = "/media/m.mp4" };
        var clip = new TimelineClip { MediaAssetId = asset.Id, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 10, SpeedMultiplier = 2 };
        var timeline = new Timeline { Tracks = new List<TimelineTrack> { new() { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } } } };
        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { asset });
        Assert.Equal(5, plan.TotalDurationSeconds, 6);
        Assert.Contains("setpts=PTS/2", plan.FilterComplexArgument);
        Assert.Contains("atempo=2", plan.FilterComplexArgument);
    }

    [Fact]
    public void SplitClip_MapsTimelineOffsetBackToSourceAtSpeed()
    {
        var clip = new TimelineClip { Id = "c", MediaAssetId = "m", SourceTrimInSeconds = 0, SourceTrimOutSeconds = 10, SpeedMultiplier = 2 };
        var session = new TimelineEditSession(new[] { new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } } });
        session.SplitClip("c", 2);
        var clips = session.Tracks.Single().Clips.OrderBy(c => c.TimelineStartSeconds).ToArray();
        Assert.Equal(4, clips[0].SourceTrimOutSeconds, 6);
        Assert.Equal(4, clips[1].SourceTrimInSeconds, 6);
    }

    [Fact]
    public void TrimIn_ShiftsTimelineBySourceDeltaDividedBySpeed()
    {
        var clip = new TimelineClip { Id = "c", MediaAssetId = "m", SourceTrimInSeconds = 0, SourceTrimOutSeconds = 10, TimelineStartSeconds = 5, SpeedMultiplier = 2 };
        var session = new TimelineEditSession(new[] { new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } } });
        session.TrimIn("c", 2);
        var edited = session.Tracks.Single().Clips.Single();
        Assert.Equal(6, edited.TimelineStartSeconds, 6);
        Assert.Equal(4, edited.TimelineDurationSeconds, 6);
    }

    [Fact]
    public void ExtractRange_UsesSpeedWhenMappingBackToSource()
    {
        var clip = new TimelineClip { MediaAssetId = "m", SourceTrimInSeconds = 0, SourceTrimOutSeconds = 10, TimelineStartSeconds = 0, SpeedMultiplier = 2 };
        var timeline = new Timeline { Tracks = new List<TimelineTrack> { new() { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { clip } } } };
        var range = FfmpegFilterGraphBuilder.ExtractRangeTimeline(timeline, 1, 3);
        var ranged = range.Tracks.Single().Clips.Single();
        Assert.Equal(2, ranged.SourceTrimInSeconds, 6);
        Assert.Equal(6, ranged.SourceTrimOutSeconds, 6);
        Assert.Equal(2, ranged.TimelineDurationSeconds, 6);
    }
}
'@
[System.IO.File]::WriteAllText((Join-Path (Get-Location) $testPath), $test, (New-Object System.Text.UTF8Encoding($false)))

Write-Host 'CapCut P1 speed sync patch applied.'
