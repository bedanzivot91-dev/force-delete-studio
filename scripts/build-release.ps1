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

Write-Host "== 1/4: Cišćenje prethodnog build-a ==" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

Write-Host "== 2/4: dotnet publish (self-contained, win-x64) ==" -ForegroundColor Cyan
dotnet publish (Join-Path $repoRoot 'src\NPVideoStudio.App\NPVideoStudio.App.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish nije uspeo (kod $LASTEXITCODE)." }

Write-Host "== 3/4: Pravljenje portable ZIP verzije ==" -ForegroundColor Cyan
$zipPath = Join-Path $distDir 'NPVideoStudio-Portable.zip'
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath
Write-Host "Portable verzija: $zipPath" -ForegroundColor Green

Write-Host "== 4/4: Pravljenje Windows instalacije (Inno Setup) ==" -ForegroundColor Cyan
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
