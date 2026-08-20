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

# The blend implementation no longer uses maskedmerge/alphaextract. Test the replacement path instead.
$path = 'tests/NPVideoStudio.UnitTests/CapCutP2MasksBlendTests.cs'
$t = Read-Utf8 $path
$t = Replace-Once $t `
'        Assert.Contains("maskedmerge", plan.FilterComplexArgument);' `
'        Assert.Contains("lutrgb=a=255", plan.FilterComplexArgument);' `
'updated neutral blend graph assertion'
$t = Replace-Once $t `
'        Assert.Contains("alphaextract", plan.FilterComplexArgument);' `
'        Assert.Contains("blend=all_mode=screen", plan.FilterComplexArgument);' `
'updated blend mode graph assertion'
Write-Utf8 $path $t

# Project defaults to 1920x1080. The source clips are 320x240 (4:3), so the old pixel (160,120)
# was in the LEFT BLACK PADDING after aspect-ratio-preserving scale/pad. It was never the rendered
# frame centre. Sample the actual 1920x1080 centre, and use a control point that is inside the blue
# 4:3 base image (x >= 240) but safely outside the 60% circular overlay mask.
$path = 'tests/NPVideoStudio.UnitTests/RenderServiceTests.cs'
$t = Read-Utf8 $path
$old = @'
        var centre = await ReadRgbPixelAsync(output, 160, 120);
        var corner = await ReadRgbPixelAsync(output, 10, 10);
        Assert.True(centre.R > 150 && centre.B > 150 && centre.G < 100,
            $"Screen-blended centre should be magenta-ish, got {centre}.");
        Assert.True(corner.B > 150 && corner.R < 100 && corner.G < 100,
            $"Outside circular mask should remain blue, got {corner}.");
'@
$new = @'
        var centre = await ReadRgbPixelAsync(output, 960, 540);
        var outsideMask = await ReadRgbPixelAsync(output, 300, 100);
        Assert.True(centre.R > 150 && centre.B > 150 && centre.G < 100,
            $"Screen-blended centre should be magenta-ish, got {centre}.");
        Assert.True(outsideMask.B > 150 && outsideMask.R < 100 && outsideMask.G < 100,
            $"Outside circular mask should remain blue, got {outsideMask}.");
'@
$t = Replace-Once $t $old $new 'render test true 1080p centre and control pixel'
Write-Utf8 $path $t

Write-Host 'P2 regression tests corrected for neutral blend graph and real 1920x1080 output coordinates.'
