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

Write-Host "== 4/7: Preuzimanje FFmpeg/FFprobe/yt-dlp/Whisper modela da program radi odmah bez rucne instalacije ==" -ForegroundColor Cyan
# FfmpegLocator.cs resolves these from Tools\ffmpeg\{ffmpeg,ffprobe}.exe and Tools\yt-dlp\yt-dlp.exe next
# to the exe before falling back to PATH - placing them here (in $publishDir, BEFORE it's copied into the
# portable folder and BEFORE Inno Setup packages it) means both the portable ZIP and the installer ship
# with a working FFmpeg/FFprobe/yt-dlp out of the box. A release artifact must not be published when a
# required file is missing: that was how an installer could be green in CI but unusable on a clean PC.
$toolsDir = Join-Path $publishDir 'Tools'
$bundledToolsOk = $true
# This destination must exist before the network attempt. If gyan.dev fails before any code
# inside try assigns local variables, the fallback still needs a real, non-empty target.
$ffmpegToolsDir = Join-Path $toolsDir 'ffmpeg'
New-Item -ItemType Directory -Force -Path $ffmpegToolsDir | Out-Null

try {
    Write-Host "Preuzimam FFmpeg (gyan.dev 'essentials' GPLv3 build - vidi THIRD_PARTY_NOTICES.md)..." -ForegroundColor Cyan
    $ffmpegZip = Join-Path $env:TEMP 'npvs-ffmpeg-essentials.zip'
    $ffmpegExtractDir = Join-Path $env:TEMP 'npvs-ffmpeg-extract'
    if (Test-Path $ffmpegExtractDir) { Remove-Item $ffmpegExtractDir -Recurse -Force }
    Invoke-WebRequest -Uri 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip' -OutFile $ffmpegZip -UseBasicParsing
    Expand-Archive -Path $ffmpegZip -DestinationPath $ffmpegExtractDir -Force
    $ffmpegBinDir = Join-Path (Get-ChildItem -Path $ffmpegExtractDir -Directory | Select-Object -First 1).FullName 'bin'
    Copy-Item -Path (Join-Path $ffmpegBinDir 'ffmpeg.exe') -Destination $ffmpegToolsDir -Force
    Copy-Item -Path (Join-Path $ffmpegBinDir 'ffprobe.exe') -Destination $ffmpegToolsDir -Force
    Copy-Item -Path (Join-Path $ffmpegBinDir 'ffplay.exe') -Destination $ffmpegToolsDir -Force
    Remove-Item $ffmpegZip, $ffmpegExtractDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "FFmpeg i FFprobe spakovani u Tools\ffmpeg\." -ForegroundColor Green
} catch {
    $downloadError = $_
    Write-Host "UPOZORENJE: Preuzimanje FFmpeg-a nije uspelo ($downloadError). Pokusavam validirani lokalni fallback..." -ForegroundColor Yellow
    try {
        $fallbackScript = Join-Path $repoRoot 'scripts\copy-ffmpeg-from-path.ps1'
        if (-not (Test-Path $fallbackScript)) { throw "Fallback skripta ne postoji: $fallbackScript" }
        & $fallbackScript -Destination $ffmpegToolsDir
        if ($LASTEXITCODE -ne 0) { throw "Fallback skripta je vratila kod $LASTEXITCODE." }
        Write-Host "FFmpeg download je bio nedostupan, ali release koristi validirane lokalne FFmpeg binarne fajlove." -ForegroundColor Green
    } catch {
        $bundledToolsOk = $false
        throw "FFmpeg nije mogao da se spakuje. Download greska: $downloadError; lokalni fallback greska: $_"
    }
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

# FfmpegLocator.cs already resolves fpcalc/tesseract from Tools\fpcalc\ and Tools\tesseract\ next to the
# exe before falling back to PATH - these were the last two dependencies the user still had to install by
# hand, which meant song recognition and reading text off a picture silently did nothing on a clean machine.
# A missing copy is caught by the release gate below, so incomplete artifacts are never uploaded.
try {
    Write-Host "Preuzimam fpcalc (Chromaprint) za prepoznavanje pesme po zvuku..." -ForegroundColor Cyan
    $fpcalcToolsDir = Join-Path $toolsDir 'fpcalc'
    New-Item -ItemType Directory -Force -Path $fpcalcToolsDir | Out-Null
    $fpcalcZip = Join-Path $env:TEMP 'npvs-fpcalc.zip'
    $fpcalcExtract = Join-Path $env:TEMP 'npvs-fpcalc-extract'
    if (Test-Path $fpcalcExtract) { Remove-Item $fpcalcExtract -Recurse -Force }
    Invoke-WebRequest -Uri 'https://github.com/acoustid/chromaprint/releases/download/v1.5.1/chromaprint-fpcalc-1.5.1-windows-x86_64.zip' -OutFile $fpcalcZip -UseBasicParsing
    Expand-Archive -Path $fpcalcZip -DestinationPath $fpcalcExtract -Force
    $fpcalcExe = Get-ChildItem -Path $fpcalcExtract -Filter 'fpcalc.exe' -Recurse | Select-Object -First 1
    if ($null -eq $fpcalcExe) { throw "fpcalc.exe nije pronadjen u preuzetoj arhivi." }
    Copy-Item -Path $fpcalcExe.FullName -Destination $fpcalcToolsDir -Force
    Remove-Item $fpcalcZip, $fpcalcExtract -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "fpcalc spakovan u Tools\fpcalc\." -ForegroundColor Green
} catch {
    $bundledToolsOk = $false
    Write-Host "UPOZORENJE: Preuzimanje fpcalc-a nije uspelo ($_)." -ForegroundColor Yellow
    Write-Host "Prepoznavanje pesme po zvuku ce raditi tek kad korisnik sam instalira Chromaprint." -ForegroundColor Yellow
}

# Tesseract is copied from a local install rather than downloaded: it ships as a Windows installer, not a
# portable archive, and it needs its DLLs and its tessdata language files alongside the exe - copying the
# whole installed folder is the only way to get a working copy. The CI runner installs it via Chocolatey
# (see .github/workflows/windows-build.yml), so this finds it there; on a dev machine without it, this is
# skipped with a warning like everything else.
try {
    Write-Host "Pakujem Tesseract (citanje teksta sa slike)..." -ForegroundColor Cyan
    $tesseractSource = @(
        'C:\Program Files\Tesseract-OCR',
        'C:\Program Files (x86)\Tesseract-OCR'
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($null -eq $tesseractSource) { throw "Tesseract nije instaliran na ovoj masini." }

    $tesseractToolsDir = Join-Path $toolsDir 'tesseract'
    New-Item -ItemType Directory -Force -Path $tesseractToolsDir | Out-Null
    Copy-Item -Path (Join-Path $tesseractSource '*') -Destination $tesseractToolsDir -Recurse -Force
    Write-Host "Tesseract spakovan u Tools\tesseract\." -ForegroundColor Green
} catch {
    $bundledToolsOk = $false
    Write-Host "UPOZORENJE: Pakovanje Tesseract-a nije uspelo ($_)." -ForegroundColor Yellow
    Write-Host "Citanje teksta sa slike ce raditi tek kad korisnik sam instalira Tesseract." -ForegroundColor Yellow
}

# WhisperModelLocator.cs resolves the model from Tools\whisper-models\ggml-tiny.bin next to the exe
# before falling back to the per-user AppData download-on-demand path - bundling it here means titlovi/
# karaoke/"pronadji tekst u pesmi" all work immediately after install, with no separate "Preuzmi model"
# click needed (same reasoning and same best-effort-not-fatal handling as FFmpeg/yt-dlp above). Real
# source URL, not guessed: this is exactly what Whisper.net's own WhisperGgmlDownloader requests
# (huggingface.co/sandrohanea/whisper.net), confirmed by reading its source.
$whisperModelOk = $true
try {
    Write-Host "Preuzimam Whisper tiny model za prepoznavanje govora (~75 MB)..." -ForegroundColor Cyan
    $whisperModelsDir = Join-Path $toolsDir 'whisper-models'
    New-Item -ItemType Directory -Force -Path $whisperModelsDir | Out-Null
    Invoke-WebRequest -Uri 'https://huggingface.co/sandrohanea/whisper.net/resolve/v4/classic/ggml-tiny.bin' -OutFile (Join-Path $whisperModelsDir 'ggml-tiny.bin') -UseBasicParsing
    Write-Host "Whisper model spakovan u Tools\whisper-models\." -ForegroundColor Green
} catch {
    $whisperModelOk = $false
    Write-Host "UPOZORENJE: Preuzimanje Whisper modela nije uspelo ($_)." -ForegroundColor Yellow
    Write-Host "Titlovi/karaoke i dalje rade ako korisnik klikne 'Preuzmi model' unutar programa (jednom, uz internet)." -ForegroundColor Yellow
}

# Release completeness gate. Building the UI successfully is not sufficient: every clean-install
# feature requested by the product must have its executable/model inside the published payload.
$requiredReleaseFiles = @(
    'Tools\ffmpeg\ffmpeg.exe',
    'Tools\ffmpeg\ffprobe.exe',
    'Tools\ffmpeg\ffplay.exe',
    'Tools\yt-dlp\yt-dlp.exe',
    'Tools\fpcalc\fpcalc.exe',
    'Tools\tesseract\tesseract.exe',
    'Tools\whisper-models\ggml-tiny.bin',
    'Tools\ai-worker\ai_worker.py',
    'Tools\ai-worker\motion_tracker.py',
    'Tools\ai-worker\install-song-ai.ps1'
)
$missingReleaseFiles = @($requiredReleaseFiles | Where-Object {
    $file = Join-Path $publishDir $_
    -not (Test-Path $file) -or (Get-Item $file).Length -eq 0
})
if ($missingReleaseFiles.Count -gt 0) {
    throw "Release je nepotpun. Nedostaju obavezni fajlovi: $($missingReleaseFiles -join ', ')"
}
Write-Host "Release gate: svi video/OCR/govor alati i AI instalater su prisutni." -ForegroundColor Green

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
    "FFmpeg, FFprobe, FFplay, yt-dlp, fpcalc i Tesseract su ukljuceni u Tools\ - nisu potrebne rucne instalacije za video, YouTube, OCR i fingerprint."
} else {
    "FFmpeg/yt-dlp NISU uspeli da se preuzmu tokom pravljenja ovog build-a (nije bilo interneta ili je preuzimanje palo). Pokrenite scripts\check-dependencies.ps1 (nalazi se u ovom folderu) ili instalirajte alat rucno i podesite putanju u Podesavanja unutar programa."
}
$whisperNote = if ($whisperModelOk) {
    "Model za prepoznavanje govora (titlovi, karaoke, pronalazenje teksta u pesmi) je vec ukljucen - radi odmah, bez ikakvog preuzimanja."
} else {
    "Model za prepoznavanje govora NIJE uspeo da se preuzme tokom pravljenja ovog build-a. Otvorite alat 'Generisi titlove (SRT)' u programu i kliknite 'Preuzmi model' (~75 MB, jednom, uz internet)."
}
Set-Content -Path (Join-Path $portableDir 'README-FIRST.txt') -Value @"
NP Video Studio - Portable verzija $version

Zelite li da se program INSTALIRA (precica u Start meniju, uklanjanje kroz Windows Podesavanja)?
Pokrenite NPVideoStudioSetup.exe iz ovog foldera - ne treba mu ni internet ni admin prava.

Ili, bez instalacije: raspakujte ovaj ceo folder bilo gde na disku i pokrenite NPVideoStudio.exe
direktno.

$toolsNote

$whisperNote

Za stihove iz pevanja otvorite Podesavanja > Alati i modeli i jednom kliknite INSTALIRAJ AI ZA PESME.
Program tada pravi svoje odvojeno Python 3.12 okruzenje i instalira faster-whisper i Demucs.
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
