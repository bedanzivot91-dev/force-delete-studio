$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$runtime = Join-Path $env:LOCALAPPDATA 'NPVideoStudio\ai-runtime'
$modelCache = Join-Path $env:LOCALAPPDATA 'NPVideoStudio\ai-models'
$python = $null

$drive = Get-PSDrive -Name ($env:SystemDrive.TrimEnd(':'))
if ($drive.Free -lt 8GB) {
    throw 'Za AI modele je potrebno najmanje 8 GB slobodnog prostora na sistemskom disku.'
}

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
$env:HF_HOME = $modelCache
$env:PYTHONUTF8 = '1'

Write-Output 'Ažuriram instalacioni sistem...'
& $managedPython -m pip install --disable-pip-version-check --upgrade pip setuptools wheel
if ($LASTEXITCODE -ne 0) { throw 'Ažuriranje pip sistema nije uspelo.' }
Write-Output 'Instaliram faster-whisper za tačno prepoznavanje...'
& $managedPython -m pip install --disable-pip-version-check --upgrade faster-whisper
if ($LASTEXITCODE -ne 0) { throw 'Instalacija faster-whisper paketa nije uspela.' }
Write-Output 'Instaliram Demucs za izdvajanje vokala iz pesme...'
& $managedPython -m pip install --disable-pip-version-check --upgrade demucs
if ($LASTEXITCODE -ne 0) { throw 'Instalacija Demucs paketa nije uspela.' }
Write-Output 'Instaliram poravnanje poznatog teksta pesme...'
& $managedPython -m pip install --disable-pip-version-check --upgrade "lyric-align[asr,separate]"
if ($LASTEXITCODE -ne 0) { throw 'Instalacija lyric-align paketa nije uspela.' }
Write-Output 'Instaliram OpenCV CSRT za Motion Tracking i Auto Reframe...'
& $managedPython -m pip install --disable-pip-version-check --upgrade opencv-contrib-python-headless
if ($LASTEXITCODE -ne 0) { throw 'Instalacija OpenCV tracking paketa nije uspela.' }
Write-Output 'Instaliram AI uklanjanje pozadine bez zelenog platna...'
& $managedPython -m pip install --disable-pip-version-check --upgrade rembg onnxruntime
if ($LASTEXITCODE -ne 0) { throw 'Instalacija AI uklanjanja pozadine nije uspela.' }
Write-Output 'Instaliram lokalni prevod titlova...'
& $managedPython -m pip install --disable-pip-version-check --upgrade argostranslate
if ($LASTEXITCODE -ne 0) { throw 'Instalacija lokalnog prevoda titlova nije uspela.' }
Write-Output 'Proveravam AI instalaciju...'
& $managedPython -c "import importlib.metadata as m; import faster_whisper, demucs, lyric_align, cv2, rembg, onnxruntime, argostranslate; tracker = getattr(cv2, 'TrackerCSRT_create', None) or getattr(getattr(cv2, 'legacy', None), 'TrackerCSRT_create', None); assert tracker is not None; print('AI alati su spremni. lyric-align ' + m.version('lyric-align') + ', OpenCV ' + cv2.__version__ + ', rembg ' + m.version('rembg') + ', Argos ' + m.version('argostranslate'))"
if ($LASTEXITCODE -ne 0) { throw 'AI paketi su instalirani, ali završna provera importa/CSRT trackera nije uspela.' }

Write-Output 'Preuzimam model large-v3 za stihove (ovo je veliko i radi se samo prvi put)...'
& $managedPython -c "from faster_whisper import WhisperModel; WhisperModel('large-v3', device='cpu', compute_type='int8'); print('Whisper large-v3 model je spreman.')"
if ($LASTEXITCODE -ne 0) { throw 'Preuzimanje ili učitavanje Whisper large-v3 modela nije uspelo.' }

Write-Output 'Svi AI alati, Motion Tracking i model za pesme su instalirani.'
