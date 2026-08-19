$ErrorActionPreference = 'Stop'

$path = Join-Path $PSScriptRoot 'setup\main.go'
$text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)

function Replace-One([string]$name, [string]$pattern, [string]$replacement) {
    $rx = [regex]::new($pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    $matches = $rx.Matches($script:text)
    if ($matches.Count -ne 1) {
        throw "$name: expected exactly one legacy block, found $($matches.Count)"
    }
    $script:text = $rx.Replace($script:text, $replacement, 1)
    Write-Host "Normalized: $name"
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
