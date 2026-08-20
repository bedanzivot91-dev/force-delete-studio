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

$path = 'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs'
$t = Read-Utf8 $path
$old = @'
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
'@
$new = @'
                var canvasSource = $"[blendcanvassrc{i}]";
                var canvas = $"[blendcanvas{i}]";
                var overlayCanvas = $"[blendovlcanvas{i}]";
                var overlayColor = $"[blendovlcolor{i}]";
                var overlayMaskSource = $"[blendmasksrc{i}]";
                var mask = $"[blendmask{i}]";
                var baseBlend = $"[blendbase{i}]";
                var baseKeep = $"[blendkeep{i}]";
                var candidate = $"[blendcandidate{i}]";

                // Derive the transparent canvas from the finite base stream itself. Using a raw `color`
                // source here creates an infinite stream and can keep framesync/maskedmerge alive forever.
                // Splitting the base gives the canvas exactly the same duration and frame cadence.
                filterLines.Add($"{currentLabel}format=rgba,split=3{baseBlend}{baseKeep}{canvasSource}");
                filterLines.Add($"{canvasSource}colorchannelmixer=rr=0:gg=0:bb=0:aa=0{canvas}");
                filterLines.Add(FormattableString.Invariant(
                    $"{canvas}{preparedLabel}overlay=x='{centreX}':y='{centreY}':enable='between(t,{start},{end})':eof_action=pass:format=auto{overlayCanvas}"));
                filterLines.Add($"{overlayCanvas}split=2{overlayColor}{overlayMaskSource}");
                filterLines.Add($"{overlayMaskSource}alphaextract{mask}");
                filterLines.Add($"{overlayColor}{baseBlend}blend=all_mode={BlendModeName(clip.BlendMode)}:shortest=1{candidate}");
                filterLines.Add($"{baseKeep}{candidate}{mask}maskedmerge{outLabel}");
'@
$t = Replace-Once $t $old $new 'finite blend canvas and mask format'
Write-Utf8 $path $t
Write-Host 'P2 finite blend canvas fix applied.'
