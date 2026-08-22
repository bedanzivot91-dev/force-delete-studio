param(
    [Parameter(Mandatory = $true)]
    [string]$SetupPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$setup = (Resolve-Path $SetupPath).Path
$installDir = Join-Path $env:RUNNER_TEMP 'np-suno-unified-real-install'
$unifiedExe = 'NP Suno Unified Studio.exe'

$expectedPayload = @(
    $unifiedExe,
    'Modules\Suno\Suno Pesme Studio.exe',
    'Modules\Suno\app\server.py',
    'Modules\Suno\app\youtube_match_recovery.py',
    'Modules\Suno\app\web\modern_2026_shell_extension.js',
    'Modules\Suno\tools\ffmpeg\bin\ffmpeg.exe',
    'Modules\Suno\tools\webview2\MicrosoftEdgeWebView2RuntimeInstallerX64.exe',
    'Modules\Video\NPVideoStudio.exe',
    'Modules\Video\Tools\ffmpeg\ffmpeg.exe',
    'Modules\Video\Tools\yt-dlp\yt-dlp.exe',
    'Modules\Video\Tools\fpcalc\fpcalc.exe',
    'Modules\Video\Tools\tesseract\tesseract.exe',
    'Modules\Video\Tools\ai-worker\ai_worker.py'
)

function Assert-File([string]$relative) {
    $path = Join-Path $installDir $relative
    if (-not (Test-Path $path -PathType Leaf)) { throw "Nedostaje instalirani unified payload: $relative" }
    if ((Get-Item $path).Length -le 0) { throw "Prazan instalirani unified payload: $relative" }
}

function Get-ProcessByExactPath([string]$path) {
    $target = [IO.Path]::GetFullPath($path)
    foreach ($p in Get-Process -ErrorAction SilentlyContinue) {
        try {
            if ($p.Path -and [IO.Path]::GetFullPath($p.Path) -eq $target) { return $p }
        } catch {}
    }
    return $null
}

function Wait-ResponsiveWindow([System.Diagnostics.Process]$process, [int]$seconds, [string]$label) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        if ($process.HasExited) { throw "$label se srušio pre otvaranja prozora. Exit=$($process.ExitCode)" }
    } while ($process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)

    $process.Refresh()
    if ($process.MainWindowHandle -eq 0) { throw "$label nije otvorio pravi Windows GUI prozor u roku od $seconds s." }
    if (-not $process.Responding) { throw "$label GUI ne odgovara na Windows message pump." }
    Write-Host "$label: PID=$($process.Id), HWND=$($process.MainWindowHandle), Responding=$($process.Responding)"
}

function Invoke-Install([int]$pass) {
    Write-Host "== UNIFIED real install pass $pass =="
    if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Force }
    $args = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER',"/DIR=$installDir",'/TASKS=associate')
    $p = Start-Process -FilePath $setup -ArgumentList $args -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Unified setup pass $pass je završio kodom $($p.ExitCode)." }
    foreach ($relative in $expectedPayload) { Assert-File $relative }

    $assoc = Get-ItemPropertyValue -Path 'Registry::HKEY_CURRENT_USER\Software\Classes\.npvsproject' -Name '(default)' -ErrorAction SilentlyContinue
    if ($assoc -ne 'NPVideoStudioProject') { throw "Unified install pass $pass nije registrovao .npvsproject." }
}

function Assert-UnifiedSelfTest([int]$pass) {
    Write-Host "== Unified launcher self-test pass $pass =="
    $exe = Join-Path $installDir $unifiedExe
    $p = Start-Process -FilePath $exe -ArgumentList @('--self-test') -WorkingDirectory $installDir -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Unified --self-test pass $pass nije prošao, exit=$($p.ExitCode)." }
}

function Assert-UnifiedGui([int]$pass) {
    Write-Host "== Unified shell GUI pass $pass =="
    $exe = Join-Path $installDir $unifiedExe
    $p = Start-Process -FilePath $exe -WorkingDirectory $installDir -PassThru
    try { Wait-ResponsiveWindow $p 45 "Unified shell pass $pass" }
    finally {
        if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue; $p.WaitForExit(10000) | Out-Null }
    }
}

function Assert-VideoModule([int]$pass) {
    Write-Host "== Unified -> NP Video Studio pass $pass =="
    $launcher = Join-Path $installDir $unifiedExe
    $videoExe = Join-Path $installDir 'Modules\Video\NPVideoStudio.exe'
    $launch = Start-Process -FilePath $launcher -ArgumentList @('--launch-video') -WorkingDirectory $installDir -Wait -PassThru
    if ($launch.ExitCode -ne 0) { throw "Unified --launch-video pass $pass nije uspeo, exit=$($launch.ExitCode)." }
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    $video = $null
    do { Start-Sleep -Milliseconds 500; $video = Get-ProcessByExactPath $videoExe } while (-not $video -and [DateTime]::UtcNow -lt $deadline)
    if (-not $video) { throw "NP Video Studio proces nije pokrenut kroz unified launcher (pass $pass)." }
    try { Wait-ResponsiveWindow $video 45 "NP Video Studio pass $pass" }
    finally {
        if (-not $video.HasExited) { Stop-Process -Id $video.Id -Force -ErrorAction SilentlyContinue; $video.WaitForExit(10000) | Out-Null }
    }
}

function Assert-SunoModule([int]$pass) {
    Write-Host "== Unified -> Suno Studio pass $pass =="
    $launcher = Join-Path $installDir $unifiedExe
    $sunoExe = Join-Path $installDir 'Modules\Suno\Suno Pesme Studio.exe'
    $launch = Start-Process -FilePath $launcher -ArgumentList @('--launch-suno') -WorkingDirectory $installDir -Wait -PassThru
    if ($launch.ExitCode -ne 0) { throw "Unified --launch-suno pass $pass nije uspeo, exit=$($launch.ExitCode)." }
    $deadline = [DateTime]::UtcNow.AddSeconds(100)
    $suno = $null
    do { Start-Sleep -Milliseconds 500; $suno = Get-ProcessByExactPath $sunoExe } while (-not $suno -and [DateTime]::UtcNow -lt $deadline)
    if (-not $suno) { throw "Suno Studio proces nije pokrenut kroz unified launcher (pass $pass)." }
    try { Wait-ResponsiveWindow $suno 100 "Suno Studio pass $pass" }
    finally {
        try { Invoke-WebRequest -UseBasicParsing -Method POST -Uri 'http://127.0.0.1:8765/api/shutdown' -TimeoutSec 3 | Out-Null } catch {}
        Start-Sleep -Seconds 1
        if (-not $suno.HasExited) { Stop-Process -Id $suno.Id -Force -ErrorAction SilentlyContinue; $suno.WaitForExit(10000) | Out-Null }
    }
}

function Invoke-Uninstall([int]$pass) {
    Write-Host "== UNIFIED real uninstall pass $pass =="
    $uninstaller = Get-ChildItem $installDir -Filter 'unins*.exe' | Select-Object -First 1
    if (-not $uninstaller) { throw "Unified pass $pass nema Inno uninstaller." }
    $p = Start-Process -FilePath $uninstaller.FullName -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Unified uninstall pass $pass je završio kodom $($p.ExitCode)." }
    Start-Sleep -Seconds 2
    if (Test-Path (Join-Path $installDir $unifiedExe)) { throw "Unified uninstall pass $pass je ostavio glavni EXE." }
    if (Test-Path 'Registry::HKEY_CURRENT_USER\Software\Classes\.npvsproject') { throw "Unified uninstall pass $pass je ostavio .npvsproject registraciju." }
    if (Test-Path 'Registry::HKEY_CURRENT_USER\Software\Classes\NPVideoStudioProject') { throw "Unified uninstall pass $pass je ostavio NPVideoStudioProject ProgID." }
}

foreach ($pass in 1..2) {
    Invoke-Install $pass
    Assert-UnifiedSelfTest $pass
    Assert-UnifiedGui $pass
    Assert-VideoModule $pass
    Assert-SunoModule $pass
    Invoke-Uninstall $pass
}

Write-Host 'UNIFIED REAL INSTALL GATE PASSED: two complete install -> unified self-test -> unified GUI -> NP Video GUI -> Suno GUI/backend -> uninstall cycles.'
