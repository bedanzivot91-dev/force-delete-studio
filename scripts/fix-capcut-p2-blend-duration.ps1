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
                var baseBlend = $"[blendbase{i}]";

                // A blend mode needs a mathematically neutral value outside the visible overlay:
                //   screen/add/difference -> black (0), multiply -> white (255), overlay -> middle grey (128).
                // We derive that canvas from the finite base stream, so it has the exact same duration and
                // cadence and cannot keep framesync alive forever like an unbounded `color` source can.
                var neutral = clip.BlendMode switch
                {
                    ClipBlendMode.Multiply => 255,
                    ClipBlendMode.Overlay => 128,
                    _ => 0
                };

                filterLines.Add($"{currentLabel}format=rgba,split=2{baseBlend}{canvasSource}");
                filterLines.Add($"{canvasSource}lutrgb=r={neutral}:g={neutral}:b={neutral}{canvas}");

                // preparedLabel already contains chroma/mask/opacity in its alpha channel. Normal overlaying
                // it over a neutral canvas naturally applies feather/invert/opacity. Outside the mask the
                // neutral colour remains, which is a no-op for the selected mathematical blend mode.
                filterLines.Add(FormattableString.Invariant(
                    $"{canvas}{preparedLabel}overlay=x='{centreX}':y='{centreY}':enable='between(t,{start},{end})':eof_action=pass:format=auto{overlayCanvas}"));

                // blend outputs planar GBR(A). Force it back to packed RGBA before later filters/encoding.
                // Also restore an opaque alpha plane: `difference` applied through all_mode would otherwise
                // calculate abs(255-255)=0 for alpha and make the entire composited frame transparent.
                filterLines.Add($"{baseBlend}{overlayCanvas}blend=all_mode={BlendModeName(clip.BlendMode)}:shortest=1,format=rgba,lutrgb=a=255{outLabel}");
'@
$t = Replace-Once $t $old $new 'finite neutral blend canvas'
Write-Utf8 $path $t
Write-Host 'P2 neutral blend canvas + RGBA/alpha output fix applied.'
