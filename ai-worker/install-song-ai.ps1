$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$runtime = Join-Path $env:LOCALAPPDATA 'NPVideoStudio\ai-runtime'
$python = $null

Write-Output 'Tražim kompatibilan Python 3.12...'
try {
    $python = (& py -3.12 -c "import sys; print(sys.executable)" 2>$null | Select-Object -First 1)
} catch { }

if (-not $python -or -not (Test-Path $python)) {
    Write-Output 'Python 3.12 nije pronađen. Instaliram ga preko Windows Package Manager-a...'
    winget install --id Python.Python.3.12 -e --scope user --silent --accept-source-agreements --accept-package-agreements
    if ($LASTEXITCODE -ne 0) { throw "Windows Package Manager nije uspeo da instalira Python 3.12 (kod $LASTEXITCODE)." }
    try { $python = (& py -3.12 -c "import sys; print(sys.executable)" 2>$null | Select-Object -First 1) } catch { }
    if (-not $python -or -not (Test-Path $python)) {
        $python = Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'
    }
}

if (-not $python -or -not (Test-Path $python)) {
    throw 'Python 3.12 nije mogao da se instalira ili pronađe.'
}

Write-Output 'Pravim odvojeno AI okruženje za NP Video Studio...'
if (-not (Test-Path (Join-Path $runtime 'Scripts\python.exe'))) {
    & $python -m venv $runtime
}
$managedPython = Join-Path $runtime 'Scripts\python.exe'

Write-Output 'Ažuriram instalacioni sistem...'
& $managedPython -m pip install --disable-pip-version-check --upgrade pip setuptools wheel
if ($LASTEXITCODE -ne 0) { throw 'Ažuriranje pip sistema nije uspelo.' }
Write-Output 'Instaliram faster-whisper za tačno prepoznavanje...'
& $managedPython -m pip install --disable-pip-version-check faster-whisper
if ($LASTEXITCODE -ne 0) { throw 'Instalacija faster-whisper paketa nije uspela.' }
Write-Output 'Instaliram Demucs za izdvajanje vokala iz pesme...'
& $managedPython -m pip install --disable-pip-version-check demucs
if ($LASTEXITCODE -ne 0) { throw 'Instalacija Demucs paketa nije uspela.' }
Write-Output 'Proveravam AI instalaciju...'
& $managedPython -c "import faster_whisper, demucs; print('AI za pesme je spreman.')"
