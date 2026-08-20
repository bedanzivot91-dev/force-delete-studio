$ErrorActionPreference = 'Stop'

$path = Join-Path $PSScriptRoot 'setup\main.go'
$text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)

function Replace-One([string]$name, [string]$pattern, [string]$replacement) {
    $rx = [regex]::new($pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    $matches = $rx.Matches($script:text)
    if ($matches.Count -ne 1) {
        throw "${name}: expected exactly one legacy block, found $($matches.Count)"
    }
    $script:text = $rx.Replace($script:text, $replacement, 1)
    Write-Host "Normalized: $name"
}

function Install-CIChromaprintFFmpeg {
    if ($env:GITHUB_ACTIONS -ne 'true') {
        return
    }

    $repoRoot = Split-Path $PSScriptRoot -Parent
    $ffDir = Join-Path $repoRoot 'tools\ffmpeg\bin'
    $ffmpegExe = Join-Path $ffDir 'ffmpeg.exe'
    $ffprobeExe = Join-Path $ffDir 'ffprobe.exe'

    if ((Test-Path $ffmpegExe -PathType Leaf) -and (Test-Path $ffprobeExe -PathType Leaf)) {
        $muxers = & $ffmpegExe -hide_banner -muxers 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0 -and $muxers -match '(?i)chromaprint') {
            Write-Host 'CI FFmpeg with Chromaprint is already present.'
            return
        }
    }

    $fileName = 'ffmpeg-master-latest-win64-gpl.zip'
    $releaseBase = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest'
    $zipUrl = "$releaseBase/$fileName"
    $checksumsUrl = "$releaseBase/checksums.sha256"
    $cacheRoot = if ($env:RUNNER_TEMP) { Join-Path $env:RUNNER_TEMP 'suno-chromaprint-ffmpeg' } else { Join-Path ([System.IO.Path]::GetTempPath()) 'suno-chromaprint-ffmpeg' }
    $zipPath = Join-Path $cacheRoot $fileName
    $checksumsPath = Join-Path $cacheRoot 'checksums.sha256'
    $extractRoot = Join-Path $cacheRoot 'expanded'

    New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
    Remove-Item -Recurse -Force $extractRoot -ErrorAction SilentlyContinue

    Write-Host 'Downloading BtbN checksum manifest for the same full GPL FFmpeg used by the installer...'
    Invoke-WebRequest -UseBasicParsing -Uri $checksumsUrl -OutFile $checksumsPath
    $checksumLine = Get-Content $checksumsPath | Where-Object { $_ -match "\s+$([regex]::Escape($fileName))$" } | Select-Object -First 1
    if (-not $checksumLine) {
        throw "BtbN checksum entry was not found for $fileName"
    }
    $expected = (($checksumLine -split '\s+')[0]).Trim().ToLowerInvariant()
    if ($expected -notmatch '^[0-9a-f]{64}$') {
        throw "Invalid BtbN SHA-256 value for ${fileName}: $expected"
    }

    Write-Host 'Downloading BtbN full GPL FFmpeg with Chromaprint...'
    Invoke-WebRequest -UseBasicParsing -Uri $zipUrl -OutFile $zipPath
    $actual = (Get-FileHash -Algorithm SHA256 -Path $zipPath).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        throw "BtbN FFmpeg SHA-256 mismatch. Expected $expected, got $actual"
    }

    Expand-Archive -Path $zipPath -DestinationPath $extractRoot -Force
    $srcFFmpeg = Get-ChildItem $extractRoot -Filter 'ffmpeg.exe' -File -Recurse | Select-Object -First 1
    $srcFFprobe = Get-ChildItem $extractRoot -Filter 'ffprobe.exe' -File -Recurse | Select-Object -First 1
    if (-not $srcFFmpeg -or -not $srcFFprobe) {
        throw 'BtbN archive does not contain ffmpeg.exe and ffprobe.exe.'
    }

    New-Item -ItemType Directory -Force -Path $ffDir | Out-Null
    Copy-Item -Force $srcFFmpeg.FullName $ffmpegExe
    Copy-Item -Force $srcFFprobe.FullName $ffprobeExe

    & $ffprobeExe -version | Select-Object -First 1 | Write-Host
    if ($LASTEXITCODE -ne 0) {
        throw 'Staged ffprobe.exe does not run.'
    }
    $muxers = & $ffmpegExe -hide_banner -muxers 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -or $muxers -notmatch '(?i)chromaprint') {
        throw 'Staged BtbN FFmpeg does not expose the Chromaprint muxer.'
    }
    Write-Host "Verified CI FFmpeg with Chromaprint: $ffmpegExe"
}

$pythonReplacement = @'
	pythonDir := filepath.Join(stage, "python")
	if !fileReady(filepath.Join(pythonDir, "python.exe"), 100000) || !fileReady(filepath.Join(pythonDir, "pythonw.exe"), 100000) {
		if err := stageCurrentPython(stage, log); err != nil {
			return fmt.Errorf("Python: %w", err)
		}
	}
'@
Replace-One 'Python runtime fallback' '(?s)\tpythonDir := filepath\.Join\(stage, "python"\).*?(?=\n\tffDir := filepath\.Join\(stage, "tools", "ffmpeg", "bin"\))' $pythonReplacement

$ffmpegReplacement = @'
	ffDir := filepath.Join(stage, "tools", "ffmpeg", "bin")
	ffmpegExe := filepath.Join(ffDir, "ffmpeg.exe")
	ffprobeExe := filepath.Join(ffDir, "ffprobe.exe")
	if !fileReady(ffmpegExe, 1000000) || !fileReady(ffprobeExe, 1000000) {
		if err := stageChromaprintFFmpeg(stage, log); err != nil {
			return fmt.Errorf("FFmpeg: %w", err)
		}
	}
'@
Replace-One 'FFmpeg legacy essentials fallback' '(?s)\tffDir := filepath\.Join\(stage, "tools", "ffmpeg", "bin"\).*?(?=\n\tytdlp := filepath\.Join\(stage, "tools", "yt-dlp", "yt-dlp\.exe"\))' $ffmpegReplacement

$denoReplacement = @'
	denoDir := filepath.Join(stage, "tools", "deno")
	denoExe := filepath.Join(denoDir, "deno.exe")
	if !fileReady(denoExe, 1000000) {
		if err := stageCurrentDeno(stage, log); err != nil {
			return fmt.Errorf("Deno: %w", err)
		}
	}
'@
Replace-One 'Deno legacy fallback' '(?s)\tdenoDir := filepath\.Join\(stage, "tools", "deno"\).*?(?=\n\tchecks := \[\]struct \{)' $denoReplacement

$forbidden = @(
    '3.13.14',
    '3.14.6',
    'ffmpeg-8.1.2-essentials_build',
    'ffmpeg-release-essentials',
    '/v2.8.1/'
)
foreach ($token in $forbidden) {
    if ($text.Contains($token)) {
        throw "Obsolete component reference still present in setup/main.go after normalization: $token"
    }
}

$required = @('stageCurrentPython(stage, log)', 'stageChromaprintFFmpeg(stage, log)', 'stageCurrentDeno(stage, log)')
foreach ($token in $required) {
    if (-not $text.Contains($token)) {
        throw "Required current component path missing after normalization: $token"
    }
}

[System.IO.File]::WriteAllText($path, $text, [System.Text.UTF8Encoding]::new($false))
Write-Host 'setup/main.go contains current component paths only.'

# The Windows workflow used winget only as a CI convenience. Hosted runner
# source metadata can disappear temporarily, so stage the exact verified BtbN
# full build that the offline installer itself uses. The later workflow check
# still refuses to continue unless the real chromaprint muxer is present.
Install-CIChromaprintFFmpeg
