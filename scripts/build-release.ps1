#Requires -Version 5.1
<#
    NP Video Studio - pravi release build: self-contained publish + portable ZIP + Windows installer.

    Mora da se pokrene na Windows racunaru sa instaliranim .NET 8 SDK i (opciono, za installer)
    Inno Setup 6 (https://jrsoftware.org/isinfo.php) dostupnim kao ISCC.exe na PATH-u.

    Upotreba:
        powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
#>

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot 'publish\win-x64'
$distDir = Join-Path $repoRoot 'dist'
$portableDir = Join-Path $distDir 'NPVideoStudio-Portable-x64'

$versionMatch = Select-String -Path (Join-Path $repoRoot 'Directory.Build.props') -Pattern '<Version>(.*?)</Version>'
$version = if ($versionMatch) { $versionMatch.Matches[0].Groups[1].Value } else { '0.0.0' }
Write-Host "Verzija: $version" -ForegroundColor Cyan

Write-Host "== 1/7: Cišćenje prethodnog build-a ==" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $portableDir) { Remove-Item $portableDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

Write-Host "== 2/7: dotnet publish (self-contained, win-x64) ==" -ForegroundColor Cyan
dotnet publish (Join-Path $repoRoot 'src\NPVideoStudio.App\NPVideoStudio.App.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish nije uspeo (kod $LASTEXITCODE)." }

Write-Host "== 3/7: Ciscenje release paketa (PDB fajlovi, native biblioteke za druge platforme) ==" -ForegroundColor Cyan
Get-ChildItem -Path $publishDir -Filter '*.pdb' -Recurse | Remove-Item -Force
$runtimesDir = Join-Path $publishDir 'runtimes'
if (Test-Path $runtimesDir) {
    Get-ChildItem -Path $runtimesDir -Directory | Where-Object { $_.Name -ne 'win-x64' } | Remove-Item -Recurse -Force
}
Write-Host "Uklonjeni PDB fajlovi i runtime folderi osim win-x64." -ForegroundColor Green

Write-Host "== 4/7: Preuzimanje FFmpeg/FFprobe/yt-dlp da program radi odmah bez rucne instalacije ==" -ForegroundColor Cyan
# FfmpegLocator.cs resolves these from Tools\ffmpeg\{ffmpeg,ffprobe}.exe and Tools\yt-dlp\yt-dlp.exe next
# to the exe before falling back to PATH - placing them here (in $publishDir, BEFORE it's copied into the
# portable folder and BEFORE Inno Setup packages it) means both the portable ZIP and the installer ship
# with a working FFmpeg/FFprobe/yt-dlp out of the box. Best-effort: a failed download here is a warning,
# not a fatal error - the app still runs and falls back to PATH/manual install (see FfmpegLocator), same
# as it always has, so a machine with no internet access can still produce a runnable (if dependency-less)
# build rather than the whole release failing.
$toolsDir = Join-Path $publishDir 'Tools'
$bundledToolsOk = $true

try {
    Write-Host "Preuzimam FFmpeg (gyan.dev 'essentials' GPLv3 build - vidi THIRD_PARTY_NOTICES.md)..." -ForegroundColor Cyan
    $ffmpegZip = Join-Path $env:TEMP 'npvs-ffmpeg-essentials.zip'
    $ffmpegExtractDir = Join-Path $env:TEMP 'npvs-ffmpeg-extract'
    if (Test-Path $ffmpegExtractDir) { Remove-Item $ffmpegExtractDir -Recurse -Force }
    Invoke-WebRequest -Uri 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip' -OutFile $ffmpegZip -UseBasicParsing
    Expand-Archive -Path $ffmpegZip -DestinationPath $ffmpegExtractDir -Force
    $ffmpegBinDir = Join-Path (Get-ChildItem -Path $ffmpegExtractDir -Directory | Select-Object -First 1).FullName 'bin'
    $ffmpegToolsDir = Join-Path $toolsDir 'ffmpeg'
    New-Item -ItemType Directory -Force -Path $ffmpegToolsDir | Out-Null
    Copy-Item -Path (Join-Path $ffmpegBinDir 'ffmpeg.exe') -Destination $ffmpegToolsDir -Force
    Copy-Item -Path (Join-Path $ffmpegBinDir 'ffprobe.exe') -Destination $ffmpegToolsDir -Force
    Remove-Item $ffmpegZip, $ffmpegExtractDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "FFmpeg i FFprobe spakovani u Tools\ffmpeg\." -ForegroundColor Green
} catch {
    $bundledToolsOk = $false
    Write-Host "UPOZORENJE: Preuzimanje FFmpeg-a nije uspelo ($_)." -ForegroundColor Yellow
    Write-Host "Program ce i dalje raditi ako korisnik sam instalira FFmpeg (scripts\check-dependencies.ps1)." -ForegroundColor Yellow
}

try {
    Write-Host "Preuzimam yt-dlp..." -ForegroundColor Cyan
    $ytDlpToolsDir = Join-Path $toolsDir 'yt-dlp'
    New-Item -ItemType Directory -Force -Path $ytDlpToolsDir | Out-Null
    Invoke-WebRequest -Uri 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe' -OutFile (Join-Path $ytDlpToolsDir 'yt-dlp.exe') -UseBasicParsing
    Write-Host "yt-dlp spakovan u Tools\yt-dlp\." -ForegroundColor Green
} catch {
    $bundledToolsOk = $false
    Write-Host "UPOZORENJE: Preuzimanje yt-dlp-a nije uspelo ($_)." -ForegroundColor Yellow
    Write-Host "Program ce i dalje raditi ako korisnik sam instalira yt-dlp (scripts\check-dependencies.ps1)." -ForegroundColor Yellow
}

Write-Host "== 5/7: Pravljenje ugradjenog instalatera (NPVideoStudioSetup.exe) ==" -ForegroundColor Cyan
# A real, self-contained alternative to the Inno Setup installer below, for machines that don't have
# Inno Setup and can't reach jrsoftware.org to get it - see src/NPVideoStudio.Installer's doc comment.
# Published straight into $publishDir so it ends up both in the portable ZIP and inside the Inno Setup
# installer's payload (harmless there - "Setup.exe within Setup" is just an extra file, never run
# automatically).
dotnet publish (Join-Path $repoRoot 'src\NPVideoStudio.Installer\NPVideoStudio.Installer.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:PublishTrimmed=true -p:TrimMode=full `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "Pravljenje NPVideoStudioSetup.exe nije uspelo (kod $LASTEXITCODE)." }
Remove-Item (Join-Path $publishDir 'NPVideoStudioSetup.pdb') -Force -ErrorAction SilentlyContinue
Write-Host "NPVideoStudioSetup.exe napravljen." -ForegroundColor Green

Write-Host "== 6/7: Pravljenje portable ZIP verzije ==" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $portableDir | Out-Null
Copy-Item -Path (Join-Path $publishDir '*') -Destination $portableDir -Recurse -Force
# README-FIRST.txt below tells the user to run scripts\check-dependencies.ps1 - that script has to
# actually be in the portable folder for that instruction to be followable (it previously only existed
# in the full source checkout, which someone who only downloaded the portable ZIP never has).
New-Item -ItemType Directory -Force -Path (Join-Path $portableDir 'scripts') | Out-Null
Copy-Item -Path (Join-Path $repoRoot 'scripts\check-dependencies.ps1') -Destination (Join-Path $portableDir 'scripts\check-dependencies.ps1') -Force
Set-Content -Path (Join-Path $portableDir 'VERSION.txt') -Value $version
$toolsNote = if ($bundledToolsOk) {
    "FFmpeg, FFprobe i yt-dlp su vec ukljuceni u ovaj folder (Tools\) - ne treba nista dodatno da instalirate za osnovne funkcije (plejer, export, YouTube preuzimanje)."
} else {
    "FFmpeg/yt-dlp NISU uspeli da se preuzmu tokom pravljenja ovog build-a (nije bilo interneta ili je preuzimanje palo). Pokrenite scripts\check-dependencies.ps1 (nalazi se u ovom folderu) ili instalirajte alat rucno i podesite putanju u Podesavanja unutar programa."
}
Set-Content -Path (Join-Path $portableDir 'README-FIRST.txt') -Value @"
NP Video Studio - Portable verzija $version

Zelite li da se program INSTALIRA (precica u Start meniju, uklanjanje kroz Windows Podesavanja)?
Pokrenite NPVideoStudioSetup.exe iz ovog foldera - ne treba mu ni internet ni admin prava.

Ili, bez instalacije: raspakujte ovaj ceo folder bilo gde na disku i pokrenite NPVideoStudio.exe
direktno.

$toolsNote

Za OCR (prepoznavanje teksta u kadru) i prepoznavanje pesama (fingerprint) i dalje su potrebni Tesseract
i fpcalc - ti alati nisu ukljuceni u ovaj build, instalirajte ih rucno ili preko scripts\check-dependencies.ps1.
"@

$zipPath = Join-Path $distDir "NPVideoStudio-Portable-x64-$version.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $portableDir '*') -DestinationPath $zipPath
Write-Host "Portable verzija: $zipPath" -ForegroundColor Green
Write-Host "Portable folder (za CI artifact, izbegava ZIP-u-ZIP-u): $portableDir" -ForegroundColor Green

Write-Host "== 7/7: Pravljenje Windows instalacije (Inno Setup) ==" -ForegroundColor Cyan
$iscc = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
if ($null -eq $iscc) {
    Write-Host "ISCC.exe (Inno Setup) nije pronadjen na PATH-u." -ForegroundColor Yellow
    Write-Host "Instalirajte Inno Setup 6 sa https://jrsoftware.org/isinfo.php i pokrenite skript ponovo," -ForegroundColor Yellow
    Write-Host "ili rucno kompajlirajte installer\NPVideoStudio.iss u Inno Setup Compiler-u." -ForegroundColor Yellow
} else {
    & $iscc.Path (Join-Path $repoRoot 'installer\NPVideoStudio.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup kompajliranje nije uspelo (kod $LASTEXITCODE)." }
    Write-Host "Instalacioni fajl je napravljen u: $distDir" -ForegroundColor Green
}

Write-Host ""
Write-Host "Gotovo. Rezultati u: $distDir" -ForegroundColor Green
