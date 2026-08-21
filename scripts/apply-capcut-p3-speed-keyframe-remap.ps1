$ErrorActionPreference = 'Stop'

function Read-Utf8([string]$Path) { [System.IO.File]::ReadAllText((Resolve-Path $Path), [System.Text.Encoding]::UTF8) }
function Write-Utf8([string]$Path, [string]$Text) { [System.IO.File]::WriteAllText((Resolve-Path $Path), $Text, (New-Object System.Text.UTF8Encoding($false))) }
function Replace-Once([string]$Text, [string]$Old, [string]$New, [string]$Label) {
    $i = $Text.IndexOf($Old, [StringComparison]::Ordinal)
    if ($i -lt 0) { throw "Anchor not found: $Label" }
    if ($Text.IndexOf($Old, $i + $Old.Length, [StringComparison]::Ordinal) -ge 0) { throw "Anchor not unique: $Label" }
    $Text.Substring(0, $i) + $New + $Text.Substring($i + $Old.Length)
}

$path = 'src/NPVideoStudio.AI/TimelineEditSession.cs'
$t = Read-Utf8 $path
$old = @'
        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        liveClip.Effect = effect;
        liveClip.Brightness = Math.Clamp(brightness, -1, 1);
        liveClip.Contrast = Math.Clamp(contrast, 0, 3);
        liveClip.Saturation = Math.Clamp(saturation, 0, 3);
        liveClip.SpeedMultiplier = Math.Clamp(speed, 0.25, 4);
        ClampKeyframesToDuration(liveClip);
'@
$new = @'
        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        var previousTimelineDuration = liveClip.TimelineDurationSeconds;
        liveClip.Effect = effect;
        liveClip.Brightness = Math.Clamp(brightness, -1, 1);
        liveClip.Contrast = Math.Clamp(contrast, 0, 3);
        liveClip.Saturation = Math.Clamp(saturation, 0, 3);
        liveClip.SpeedMultiplier = Math.Clamp(speed, 0.25, 4);
        RescaleKeyframesForDurationChange(liveClip, previousTimelineDuration);
'@
$t = Replace-Once $t $old $new 'speed change keyframe remap call'

$old = @'
    private static void ClampKeyframesToDuration(TimelineClip clip)
    {
        var duration = Math.Max(0, clip.TimelineDurationSeconds);
        foreach (var keyframe in clip.Keyframes)
        {
            keyframe.TimeSeconds = Math.Clamp(keyframe.TimeSeconds, 0, duration);
        }
        clip.Keyframes = clip.Keyframes.OrderBy(k => k.Property).ThenBy(k => k.TimeSeconds).ToList();
    }
'@
$new = @'
    private static void RescaleKeyframesForDurationChange(TimelineClip clip, double previousDuration)
    {
        var duration = Math.Max(0, clip.TimelineDurationSeconds);
        if (clip.Keyframes.Count == 0)
        {
            return;
        }

        if (previousDuration <= 1e-9)
        {
            foreach (var keyframe in clip.Keyframes)
            {
                keyframe.TimeSeconds = Math.Clamp(keyframe.TimeSeconds, 0, duration);
            }
        }
        else
        {
            var timeScale = duration / previousDuration;
            foreach (var keyframe in clip.Keyframes)
            {
                keyframe.TimeSeconds = Math.Clamp(keyframe.TimeSeconds * timeScale, 0, duration);
            }
        }

        clip.Keyframes = clip.Keyframes.OrderBy(k => k.Property).ThenBy(k => k.TimeSeconds).ToList();
    }
'@
$t = Replace-Once $t $old $new 'speed keyframe remap helper'
Write-Utf8 $path $t
Write-Host 'Keyframe times now preserve their relative position when clip speed changes.'
