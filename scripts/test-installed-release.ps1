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
    'Tools\ai-worker\install-song-ai.ps1',
    'libvlc\win-x64\libvlc.dll',
    'libvlc\win-x64\libvlccore.dll',
    'runtimes\win-x64\whisper.dll'
)

function Invoke-SetupInstall([int]$pass) {
    Write-Host "== Real install pass $pass =="
    if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Force }
    $args = @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER',
        "/DIR=$installDir", '/TASKS=resetstate,associate'
    )
    $p = Start-Process -FilePath $setup.FullName -ArgumentList $args -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Setup pass $pass je završio kodom $($p.ExitCode)." }

    foreach ($relative in $expectedPayload) {
        $full = Join-Path $installDir $relative
        if (-not (Test-Path $full -PathType Leaf)) { throw "Install pass $pass nema obavezni payload: $relative" }
    }

    $assoc = Get-ItemPropertyValue -Path 'Registry::HKEY_CURRENT_USER\Software\Classes\.npvsproject' -Name '(default)' -ErrorAction SilentlyContinue
    if ($assoc -ne 'NPVideoStudioProject') { throw "Install pass $pass nije registrovao .npvsproject za trenutnog korisnika." }
}

function Assert-BundledTool([int]$pass, [string]$relativePath, [string[]]$arguments) {
    $exe = Join-Path $installDir $relativePath
    Write-Host "== Tool smoke pass ${pass}: $relativePath $($arguments -join ' ') =="
    $p = Start-Process -FilePath $exe -ArgumentList $arguments -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0) {
        throw "Bundled alat $relativePath nije funkcionalan nakon instalacije (pass $pass), exit=$($p.ExitCode)."
    }
}

function Assert-BundledTools([int]$pass) {
    Assert-BundledTool $pass 'Tools\ffmpeg\ffmpeg.exe' @('-version')
    Assert-BundledTool $pass 'Tools\ffmpeg\ffprobe.exe' @('-version')
    Assert-BundledTool $pass 'Tools\ffmpeg\ffplay.exe' @('-version')
    Assert-BundledTool $pass 'Tools\yt-dlp\yt-dlp.exe' @('--version')
    Assert-BundledTool $pass 'Tools\fpcalc\fpcalc.exe' @('-version')
    Assert-BundledTool $pass 'Tools\tesseract\tesseract.exe' @('--version')
}

function Assert-GuiLaunch([int]$pass) {
    Write-Host "== GUI launch/responding pass $pass =="
    $exe = Join-Path $installDir 'NPVideoStudio.exe'
    $p = Start-Process -FilePath $exe -WorkingDirectory $installDir -PassThru
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
        Write-Host "GUI pass ${pass}: PID=$($p.Id), HWND=$($p.MainWindowHandle), Responding=$($p.Responding)"
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
Assert-GuiLaunch 1
Invoke-Uninstall 1
Invoke-SetupInstall 2
Assert-BundledTools 2
Assert-GuiLaunch 2
Invoke-Uninstall 2

Write-Host 'REAL INSTALL GATE PASSED: install -> bundled tools/runtime payload -> GUI window/responding -> uninstall -> second clean install -> bundled tools/runtime payload -> GUI window/responding -> uninstall.'
