#Requires -Version 5.1
<#
    NP Video Studio - provera i instalacija potrebnih zavisnosti.

    Ovaj skript proverava da li su na Windows racunaru instalirane sve komponente
    potrebne za rad programa (.NET 8 Desktop Runtime, FFmpeg/FFprobe) i nudi da ih
    automatski instalira. Ne menja nista bez potvrde korisnika, osim ako je prosledjen
    parametar -Force.

    Upotreba:
        powershell -ExecutionPolicy Bypass -File check-dependencies.ps1
        powershell -ExecutionPolicy Bypass -File check-dependencies.ps1 -Force
#>

[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$logPath = Join-Path $env:LOCALAPPDATA 'NP Video Studio\Logs\dependency-check.log'
New-Item -ItemType Directory -Force -Path (Split-Path $logPath) | Out-Null

function Write-Log {
    param([string]$Message, [string]$Level = 'INFO')
    $line = "{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}" -f (Get-Date), $Level, $Message
    Add-Content -Path $logPath -Value $line
    switch ($Level) {
        'ERROR' { Write-Host $Message -ForegroundColor Red }
        'WARN'  { Write-Host $Message -ForegroundColor Yellow }
        'OK'    { Write-Host $Message -ForegroundColor Green }
        default { Write-Host $Message }
    }
}

Write-Log "===== NP Video Studio - provera zavisnosti ====="

# 1. Windows verzija
$os = Get-CimInstance Win32_OperatingSystem
$buildNumber = [int]$os.BuildNumber
Write-Log "Windows: $($os.Caption) (build $buildNumber)"
if ($buildNumber -lt 17763) {
    Write-Log "Upozorenje: NP Video Studio je testiran na Windows 10 (1809+) i Windows 11. Vasa verzija moze biti nepodrzana." 'WARN'
} else {
    Write-Log "Verzija Windows-a je podrzana." 'OK'
}

# 2. .NET 8 Desktop Runtime
function Test-DotNet8Desktop {
    try {
        $runtimes = & dotnet --list-runtimes 2>$null
        return ($runtimes -match 'Microsoft\.WindowsDesktop\.App 8\.')
    } catch {
        return $false
    }
}

if (Test-DotNet8Desktop) {
    Write-Log ".NET 8 Desktop Runtime je instaliran." 'OK'
} else {
    Write-Log ".NET 8 Desktop Runtime NIJE pronadjen." 'WARN'
    $install = $Force -or (Read-Host "Da li zelite da ga sada instaliram preko winget-a? (d/n)") -eq 'd'
    if ($install) {
        try {
            winget install --id Microsoft.DotNet.DesktopRuntime.8 -e --accept-source-agreements --accept-package-agreements
            Write-Log ".NET 8 Desktop Runtime instaliran preko winget-a." 'OK'
        } catch {
            Write-Log "Automatska instalacija nije uspela: $($_.Exception.Message)" 'ERROR'
            Write-Log "Preuzmite ga rucno sa: https://dotnet.microsoft.com/download/dotnet/8.0" 'ERROR'
        }
    }
}

# 3. FFmpeg / FFprobe
function Test-Tool {
    param([string]$Name)
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    return $null -ne $cmd
}

$appDir = Split-Path -Parent $PSScriptRoot
$bundledToolsDir = Join-Path $appDir 'Tools\ffmpeg'

$ffmpegOk = (Test-Tool 'ffmpeg.exe') -or (Test-Path (Join-Path $bundledToolsDir 'ffmpeg.exe'))
$ffprobeOk = (Test-Tool 'ffprobe.exe') -or (Test-Path (Join-Path $bundledToolsDir 'ffprobe.exe'))

if ($ffmpegOk -and $ffprobeOk) {
    Write-Log "FFmpeg i FFprobe su dostupni." 'OK'
} else {
    Write-Log "FFmpeg/FFprobe nisu pronadjeni na sistemu ni u instalacionom folderu." 'WARN'
    $install = $Force -or (Read-Host "Da li zelite da ih sada preuzmem (winget, paket gyan.dev)? (d/n)") -eq 'd'
    if ($install) {
        try {
            winget install --id Gyan.FFmpeg -e --accept-source-agreements --accept-package-agreements
            Write-Log "FFmpeg instaliran preko winget-a. Mozda je potrebno ponovo pokrenuti terminal da PATH bude azuriran." 'OK'
        } catch {
            Write-Log "Automatska instalacija nije uspela: $($_.Exception.Message)" 'ERROR'
            Write-Log "Preuzmite rucno sa: https://www.gyan.dev/ffmpeg/builds/ i raspakujte ffmpeg.exe/ffprobe.exe u: $bundledToolsDir" 'ERROR'
        }
    }
}

# 4. yt-dlp (opcioni alat, samo za "Preuzmi sa YouTube-a")
$ytDlpBundled = Join-Path $appDir 'Tools\yt-dlp'
$ytDlpOk = (Test-Tool 'yt-dlp.exe') -or (Test-Path (Join-Path $ytDlpBundled 'yt-dlp.exe'))

if ($ytDlpOk) {
    Write-Log "yt-dlp je dostupan." 'OK'
} else {
    Write-Log "yt-dlp nije pronadjen - alat 'Preuzmi sa YouTube-a' nece raditi dok se ne instalira (ostatak programa radi normalno)." 'WARN'
    $install = $Force -or (Read-Host "Da li zelite da ga sada instaliram preko winget-a? (d/n)") -eq 'd'
    if ($install) {
        try {
            winget install --id yt-dlp.yt-dlp -e --accept-source-agreements --accept-package-agreements
            Write-Log "yt-dlp instaliran preko winget-a. Mozda je potrebno ponovo pokrenuti terminal da PATH bude azuriran." 'OK'
        } catch {
            Write-Log "Automatska instalacija nije uspela: $($_.Exception.Message)" 'ERROR'
            Write-Log "Preuzmite rucno sa: https://github.com/yt-dlp/yt-dlp/releases i stavite yt-dlp.exe u: $ytDlpBundled" 'ERROR'
        }
    }
}

# 5. Whisper tiny model (titlovi, karaoke, "pronadji tekst u pesmi")
$whisperModelBundled = Join-Path $appDir 'Tools\whisper-models\ggml-tiny.bin'
$whisperModelAppData = Join-Path $env:LOCALAPPDATA 'NP Video Studio\Models\ggml-tiny.bin'
$whisperModelOk = (Test-Path $whisperModelBundled) -or (Test-Path $whisperModelAppData)

if ($whisperModelOk) {
    Write-Log "Model za prepoznavanje govora (Whisper) je dostupan." 'OK'
} else {
    Write-Log "Model za prepoznavanje govora NIJE pronadjen - titlovi/karaoke/pronalazenje teksta u pesmi nece raditi dok se ne preuzme." 'WARN'
    $install = $Force -or (Read-Host "Da li zelite da ga sada preuzmem (~75 MB, jednokratno)? (d/n)") -eq 'd'
    if ($install) {
        try {
            New-Item -ItemType Directory -Force -Path (Split-Path $whisperModelBundled) | Out-Null
            Invoke-WebRequest -Uri 'https://huggingface.co/sandrohanea/whisper.net/resolve/v4/classic/ggml-tiny.bin' -OutFile $whisperModelBundled -UseBasicParsing
            Write-Log "Whisper model preuzet u: $whisperModelBundled" 'OK'
        } catch {
            Write-Log "Automatsko preuzimanje nije uspelo: $($_.Exception.Message)" 'ERROR'
            Write-Log "Mozete ga preuzeti i klikom na 'Preuzmi model' unutar programa (alat 'Generisi titlove (SRT)')." 'ERROR'
        }
    }
}

# 6. Slobodan prostor na disku (C:)
$systemDrive = Get-PSDrive -Name ($env:SystemDrive.TrimEnd(':'))
$freeGb = [math]::Round($systemDrive.Free / 1GB, 1)
if ($freeGb -lt 2) {
    Write-Log "Slobodno je samo $freeGb GB na disku $($env:SystemDrive) - ovo moze prekinuti instalaciju ili render." 'ERROR'
} elseif ($freeGb -lt 10) {
    Write-Log "Slobodno je $freeGb GB na disku $($env:SystemDrive) - preporucuje se vise prostora za rad sa video fajlovima." 'WARN'
} else {
    Write-Log "Slobodan prostor na disku: $freeGb GB." 'OK'
}

Write-Log "===== Provera zavrsena. Log: $logPath ====="
Write-Host ""
Write-Host "Detaljan log sacuvan u: $logPath"
