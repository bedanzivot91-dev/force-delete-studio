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

Write-Host "== 1/5: Cišćenje prethodnog build-a ==" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $portableDir) { Remove-Item $portableDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

Write-Host "== 2/5: dotnet publish (self-contained, win-x64) ==" -ForegroundColor Cyan
dotnet publish (Join-Path $repoRoot 'src\NPVideoStudio.App\NPVideoStudio.App.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish nije uspeo (kod $LASTEXITCODE)." }

Write-Host "== 3/5: Ciscenje release paketa (PDB fajlovi, native biblioteke za druge platforme) ==" -ForegroundColor Cyan
Get-ChildItem -Path $publishDir -Filter '*.pdb' -Recurse | Remove-Item -Force
$runtimesDir = Join-Path $publishDir 'runtimes'
if (Test-Path $runtimesDir) {
    Get-ChildItem -Path $runtimesDir -Directory | Where-Object { $_.Name -ne 'win-x64' } | Remove-Item -Recurse -Force
}
Write-Host "Uklonjeni PDB fajlovi i runtime folderi osim win-x64." -ForegroundColor Green

Write-Host "== 4/5: Pravljenje portable ZIP verzije ==" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $portableDir | Out-Null
Copy-Item -Path (Join-Path $publishDir '*') -Destination $portableDir -Recurse -Force
# README-FIRST.txt below tells the user to run scripts\check-dependencies.ps1 - that script has to
# actually be in the portable folder for that instruction to be followable (it previously only existed
# in the full source checkout, which someone who only downloaded the portable ZIP never has).
New-Item -ItemType Directory -Force -Path (Join-Path $portableDir 'scripts') | Out-Null
Copy-Item -Path (Join-Path $repoRoot 'scripts\check-dependencies.ps1') -Destination (Join-Path $portableDir 'scripts\check-dependencies.ps1') -Force
Set-Content -Path (Join-Path $portableDir 'VERSION.txt') -Value $version
Set-Content -Path (Join-Path $portableDir 'README-FIRST.txt') -Value @"
NP Video Studio - Portable verzija $version

Ovo je portable (bez instalacije) verzija programa - raspakujte ovaj folder bilo gde na disku i
pokrenite NPVideoStudio.exe direktno.

Ako neki alat (FFmpeg, FFprobe, yt-dlp) nije pronadjen, pokrenite scripts\check-dependencies.ps1 (nalazi
se u ovom folderu) ili instalirajte alat rucno i podesite putanju u Podesavanja unutar programa.
"@

$zipPath = Join-Path $distDir "NPVideoStudio-Portable-x64-$version.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $portableDir '*') -DestinationPath $zipPath
Write-Host "Portable verzija: $zipPath" -ForegroundColor Green
Write-Host "Portable folder (za CI artifact, izbegava ZIP-u-ZIP-u): $portableDir" -ForegroundColor Green

Write-Host "== 5/5: Pravljenje Windows instalacije (Inno Setup) ==" -ForegroundColor Cyan
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
