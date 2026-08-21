$ErrorActionPreference = 'Stop'

function Read-Utf8([string]$Path) {
    [System.IO.File]::ReadAllText((Resolve-Path $Path), [System.Text.Encoding]::UTF8)
}
function Write-Utf8([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText((Resolve-Path $Path), $Text, (New-Object System.Text.UTF8Encoding($false)))
}
function Replace-Once([string]$Text, [string]$Old, [string]$New, [string]$Label) {
    $i = $Text.IndexOf($Old, [StringComparison]::Ordinal)
    if ($i -lt 0) { throw "Patch-script anchor not found: $Label" }
    if ($Text.IndexOf($Old, $i + $Old.Length, [StringComparison]::Ordinal) -ge 0) { throw "Patch-script anchor not unique: $Label" }
    $Text.Substring(0, $i) + $New + $Text.Substring($i + $Old.Length)
}

# Fix the model/UI patch so it works with GitHub's Windows checkout (CRLF) instead of assuming LF.
$path = 'scripts/apply-capcut-p3-keyframes-model-ui.ps1'
$t = Read-Utf8 $path
$old = '$t = Replace-Once $t "using System.Windows.Input;`n" "using System.Windows.Input;`nusing CommunityToolkit.Mvvm.Input;`n" ''RelayCommand import'''
$new = @'
$newLine = if ($t.Contains("`r`n")) { "`r`n" } else { "`n" }
$t = Replace-Once $t "using System.Windows.Input;" ("using System.Windows.Input;" + $newLine + "using CommunityToolkit.Mvvm.Input;") 'RelayCommand import'
'@.TrimEnd()
$t = Replace-Once $t $old $new 'CRLF-safe RelayCommand import'
Write-Utf8 $path $t

# Keep the exact historical FFmpeg strings for clips that have no keyframes. This prevents keyframe support
# from changing existing static text/overlay rendering and preserves the already-tested behavior byte-for-byte.
$path = 'scripts/apply-capcut-p3-keyframes-renderer.ps1'
$t = Read-Utf8 $path

$old = @'
            // Centre-anchored. overlay() runs on the global rendered clock, so local clip keyframe time is
            // global t minus this clip's rendered start.
            var localOverlayTime = FormattableString.Invariant($"(t-{start})");
            var xPercent = BuildKeyframeValueExpression(clip, ClipKeyframeProperty.PositionX, localOverlayTime, clip.PositionXPercent);
            var yPercent = BuildKeyframeValueExpression(clip, ClipKeyframeProperty.PositionY, localOverlayTime, clip.PositionYPercent);
            var centreX = $"(main_w*(({xPercent})/100))-(overlay_w/2)";
            var centreY = $"(main_h*(({yPercent})/100))-(overlay_h/2)";
'@
$new = @'
            // Centre-anchored. Static clips keep the exact old expressions; animated clips use the global
            // rendered clock minus this clip's rendered start as their local keyframe time.
            var localOverlayTime = FormattableString.Invariant($"(t-{start})");
            var centreX = HasKeyframes(clip, ClipKeyframeProperty.PositionX)
                ? $"(main_w*(({BuildKeyframeValueExpression(clip, ClipKeyframeProperty.PositionX, localOverlayTime, clip.PositionXPercent)})/100))-(overlay_w/2)"
                : FormattableString.Invariant($"(main_w*{clip.PositionXPercent / 100.0})-(overlay_w/2)");
            var centreY = HasKeyframes(clip, ClipKeyframeProperty.PositionY)
                ? $"(main_h*(({BuildKeyframeValueExpression(clip, ClipKeyframeProperty.PositionY, localOverlayTime, clip.PositionYPercent)})/100))-(overlay_h/2)"
                : FormattableString.Invariant($"(main_h*{clip.PositionYPercent / 100.0})-(overlay_h/2)");
'@
$t = Replace-Once $t $old $new 'static overlay expression compatibility'

$old = @'
            filterLines.Add(FormattableString.Invariant(
                $"{currentTextVideoLabel}drawtext=text='{escapedText}':enable='between(t,{renderedStart},{renderedEnd})':x='{x}':y='{y}':fontsize={fontSize}:fontcolor={clip.TextColor}{fontFileArgument}{extraArguments}{nextLabel}"));
'@
$new = @'
            var drawTextX = HasKeyframes(clip, ClipKeyframeProperty.PositionX) ? $"'{x}'" : x;
            var drawTextY = HasKeyframes(clip, ClipKeyframeProperty.PositionY) ? $"'{y}'" : y;
            filterLines.Add(FormattableString.Invariant(
                $"{currentTextVideoLabel}drawtext=text='{escapedText}':enable='between(t,{renderedStart},{renderedEnd})':x={drawTextX}:y={drawTextY}:fontsize={fontSize}:fontcolor={clip.TextColor}{fontFileArgument}{extraArguments}{nextLabel}"));
'@
$t = Replace-Once $t $old $new 'static text expression compatibility'
Write-Utf8 $path $t
Write-Host 'P3 patch scripts fixed for CRLF and static-render compatibility.'
