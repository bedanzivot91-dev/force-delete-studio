$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$setup = Get-ChildItem -Path (Join-Path $PSScriptRoot '..\dist') -Filter 'NPVideoStudio-Setup-*.exe' | Select-Object -First 1
if (-not $setup) { throw 'Windows Setup EXE nije pronađen u dist folderu.' }

$installDir = Join-Path $env:RUNNER_TEMP 'npvs-real-install'
$expectedPayload = @(
    'NPVideoStudio.exe',
    'Tools\ffmpeg\ffmpeg.exe',
    'Tools\ffmpeg\ffprobe.exe',
    'Tools\ffmpeg\ffplay.exe',
    'Tools\yt-dlp\yt-dlp.exe',
    'Tools\fpcalc\fpcalc.exe',
    'Tools\tesseract\tesseract.exe',
    'Tools\whisper-models\ggml-tiny.bin',
    'Tools\ai-worker\ai_worker.py',
    'Tools\ai-worker\motion_tracker.py',
    'Tools\ai-worker\install-song-ai.ps1',
    'libvlc\win-x64\libvlc.dll',
    'libvlc\win-x64\libvlccore.dll',
    'runtimes\win-x64\whisper.dll',
    'THIRD_PARTY_NOTICES.md',
    'Licenses\Apache-2.0-Serilog.txt',
    'Licenses\GPLv3-FFmpeg.txt',
    'Licenses\LGPL-2.1-LibVLC.txt',
    'Licenses\MIT-Avalonia.txt',
    'Licenses\MIT-CommunityToolkit.Mvvm.txt',
    'Licenses\MIT-Microsoft.Data.Sqlite.txt',
    'Licenses\MIT-Microsoft.Extensions.DependencyInjection.txt',
    'Licenses\MIT-Whisper.net.txt',
    'Licenses\MIT-whisper.cpp.txt',
    'Licenses\PublicDomain-SQLite.txt',
    'Licenses\Unlicense-yt-dlp.txt'
)

function Invoke-SetupInstall([int]$pass) {
    Write-Host "== Real install pass $pass =="
    if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Force }
    $args = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER',"/DIR=$installDir",'/TASKS=resetstate,associate')
    $p = Start-Process -FilePath $setup.FullName -ArgumentList $args -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Setup pass $pass je završio kodom $($p.ExitCode)." }
    foreach ($relative in $expectedPayload) {
        $full = Join-Path $installDir $relative
        if (-not (Test-Path $full -PathType Leaf)) { throw "Install pass $pass nema obavezni payload: $relative" }
        if ((Get-Item $full).Length -eq 0) { throw "Install pass $pass ima prazan obavezni payload: $relative" }
    }
    $assoc = Get-ItemPropertyValue -Path 'Registry::HKEY_CURRENT_USER\Software\Classes\.npvsproject' -Name '(default)' -ErrorAction SilentlyContinue
    if ($assoc -ne 'NPVideoStudioProject') { throw "Install pass $pass nije registrovao .npvsproject za trenutnog korisnika." }
}

function Assert-BundledTool([int]$pass, [string]$relativePath, [string[]]$arguments) {
    $exe = Join-Path $installDir $relativePath
    Write-Host "== Tool smoke pass ${pass}: $relativePath $($arguments -join ' ') =="
    $p = Start-Process -FilePath $exe -ArgumentList $arguments -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0) { throw "Bundled alat $relativePath nije funkcionalan nakon instalacije (pass $pass), exit=$($p.ExitCode)." }
}

function Assert-BundledTools([int]$pass) {
    Assert-BundledTool $pass 'Tools\ffmpeg\ffmpeg.exe' @('-version')
    Assert-BundledTool $pass 'Tools\ffmpeg\ffprobe.exe' @('-version')
    Assert-BundledTool $pass 'Tools\ffmpeg\ffplay.exe' @('-version')
    Assert-BundledTool $pass 'Tools\yt-dlp\yt-dlp.exe' @('--version')
    Assert-BundledTool $pass 'Tools\fpcalc\fpcalc.exe' @('-version')
    Assert-BundledTool $pass 'Tools\tesseract\tesseract.exe' @('--version')
}

function Assert-FunctionalMediaRender([int]$pass) {
    Write-Host "== Functional installed FFmpeg render + FFprobe pass $pass =="
    $ffmpeg = Join-Path $installDir 'Tools\ffmpeg\ffmpeg.exe'
    $ffprobe = Join-Path $installDir 'Tools\ffmpeg\ffprobe.exe'
    $output = Join-Path $env:RUNNER_TEMP "npvs-installed-render-pass-$pass.mp4"
    Remove-Item $output -Force -ErrorAction SilentlyContinue
    $renderArgs = @('-hide_banner','-loglevel','error','-y','-f','lavfi','-i','color=c=blue:s=320x180:r=30:d=1.2','-f','lavfi','-i','sine=frequency=440:sample_rate=44100:duration=1.2','-shortest','-c:v','libx264','-pix_fmt','yuv420p','-c:a','aac',$output)
    $render = Start-Process -FilePath $ffmpeg -ArgumentList $renderArgs -Wait -PassThru -NoNewWindow
    if ($render.ExitCode -ne 0 -or -not (Test-Path $output -PathType Leaf)) { throw "Instalirani FFmpeg nije napravio funkcionalni MP4 (pass $pass), exit=$($render.ExitCode)." }
    if ((Get-Item $output).Length -lt 1000) { throw "FFmpeg output je sumnjivo mali (pass $pass)." }
    $probeJson = & $ffprobe -v error -show_entries stream=codec_type -of json $output | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Instalirani FFprobe nije mogao da pročita render (pass $pass)." }
    $types = @((($probeJson | ConvertFrom-Json).streams) | ForEach-Object { $_.codec_type })
    if ('video' -notin $types -or 'audio' -notin $types) { throw "Funkcionalni render pass $pass nema i video i audio stream. Dobijeno: $($types -join ', ')" }
    Write-Host "Functional media render pass ${pass}: $((Get-Item $output).Length) bytes; streams=$($types -join ',')"
}

function Assert-InstalledAppProjectRender([int]$pass) {
    Write-Host "== Installed NPVideoStudio project save -> reload -> production render pass $pass =="
    $exe = Join-Path $installDir 'NPVideoStudio.exe'
    $ffprobe = Join-Path $installDir 'Tools\ffmpeg\ffprobe.exe'
    $workDir = Join-Path $env:RUNNER_TEMP "npvs-installed-app-e2e-$pass"
    if (Test-Path $workDir) { Remove-Item $workDir -Recurse -Force }
    New-Item -ItemType Directory -Path $workDir -Force | Out-Null
    $p = Start-Process -FilePath $exe -WorkingDirectory $installDir -ArgumentList @('--self-test-project-render', $workDir) -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Instalirani NPVideoStudio.exe production project E2E je pao (pass $pass), exit=$($p.ExitCode)." }

    $project = Join-Path $workDir 'npvs-installed-e2e.npvsproject'
    $output = Join-Path $workDir 'npvs-installed-app-render.mp4'
    $marker = Join-Path $workDir 'npvs-installed-e2e.success.txt'
    foreach ($required in @($project,$output,$marker)) {
        if (-not (Test-Path $required -PathType Leaf)) { throw "NP app E2E pass $pass nema rezultat: $required" }
    }
    if ((Get-Item $output).Length -lt 1000) { throw "NP app production render je sumnjivo mali (pass $pass)." }
    $projectJson = Get-Content -Raw -Path $project | ConvertFrom-Json
    if ($projectJson.Name -ne 'NP Installed E2E' -or @($projectJson.Timeline.Tracks).Count -ne 2) { throw "Sačuvani/ponovo učitani NP projekat nema očekivanu strukturu (pass $pass)." }
    $probeJson = & $ffprobe -v error -show_entries stream=codec_type -of json $output | Out-String
    if ($LASTEXITCODE -ne 0) { throw "FFprobe nije mogao da pročita NP app render (pass $pass)." }
    $types = @((($probeJson | ConvertFrom-Json).streams) | ForEach-Object { $_.codec_type })
    if ('video' -notin $types -or 'audio' -notin $types) { throw "NP app production render pass $pass nema video+audio. Dobijeno: $($types -join ', ')" }
    Write-Host "NP app project E2E pass ${pass}: project=$project; render=$((Get-Item $output).Length) bytes; streams=$($types -join ',')"
    return $project
}

function Assert-GuiLaunch([int]$pass, [string]$projectPath) {
    Write-Host "== GUI launch/responding + project-open pass $pass =="
    $exe = Join-Path $installDir 'NPVideoStudio.exe'
    $arguments = @()
    if (-not [string]::IsNullOrWhiteSpace($projectPath)) { $arguments = @($projectPath) }
    $p = Start-Process -FilePath $exe -WorkingDirectory $installDir -ArgumentList $arguments -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(35)
    try {
        do {
            Start-Sleep -Milliseconds 500
            $p.Refresh()
            if ($p.HasExited) { throw "Instalirana aplikacija se srušila pri startu (pass $pass), exit=$($p.ExitCode)." }
        } while ($p.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
        $p.Refresh()
        if ($p.MainWindowHandle -eq 0) { throw "Instalirana aplikacija nije otvorila pravi Windows GUI prozor u roku od 35 s (pass $pass)." }
        if (-not $p.Responding) { throw "Instalirani GUI ne odgovara na Windows message pump (pass $pass)." }
        Write-Host "GUI/project-open pass ${pass}: PID=$($p.Id), HWND=$($p.MainWindowHandle), Responding=$($p.Responding), project=$projectPath"
    }
    finally {
        if (-not $p.HasExited) {
            Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
            $p.WaitForExit(10000) | Out-Null
        }
    }
}

function Invoke-Uninstall([int]$pass) {
    Write-Host "== Real uninstall pass $pass =="
    $uninstaller = Get-ChildItem -Path $installDir -Filter 'unins*.exe' | Select-Object -First 1
    if (-not $uninstaller) { throw "Pass $pass nema Inno uninstaller u instalacionom folderu." }
    $p = Start-Process -FilePath $uninstaller.FullName -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Uninstall pass $pass je završio kodom $($p.ExitCode)." }
    Start-Sleep -Seconds 2
    if (Test-Path (Join-Path $installDir 'NPVideoStudio.exe')) { throw "Uninstall pass $pass je ostavio NPVideoStudio.exe na disku." }
    if (Test-Path 'Registry::HKEY_CURRENT_USER\Software\Classes\.npvsproject') { throw "Uninstall pass $pass je ostavio .npvsproject registraciju." }
}

Invoke-SetupInstall 1
Assert-BundledTools 1
Assert-FunctionalMediaRender 1
$project1 = Assert-InstalledAppProjectRender 1
Assert-GuiLaunch 1 $project1
Invoke-Uninstall 1
Invoke-SetupInstall 2
Assert-BundledTools 2
Assert-FunctionalMediaRender 2
$project2 = Assert-InstalledAppProjectRender 2
Assert-GuiLaunch 2 $project2
Invoke-Uninstall 2

Write-Host 'REAL INSTALL GATE PASSED: install -> bundled tools/runtime/legal payload -> FFmpeg infrastructure render -> NPVideoStudio.exe save/reload real .npvsproject -> production RenderService video+audio export -> GUI opens that project/responds -> uninstall -> second clean install -> repeat full NP project render/open gate -> uninstall.'
