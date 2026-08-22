from pathlib import Path

root = Path(__file__).resolve().parents[1]
path = root / 'scripts' / 'build-release.ps1'
text = path.read_text(encoding='utf-8')
old = '''} catch {\n    $bundledToolsOk = $false\n    Write-Host "UPOZORENJE: Preuzimanje FFmpeg-a nije uspelo ($_)." -ForegroundColor Yellow\n    Write-Host "Program ce i dalje raditi ako korisnik sam instalira FFmpeg (scripts\\check-dependencies.ps1)." -ForegroundColor Yellow\n}\n\ntry {\n    Write-Host "Preuzimam yt-dlp..." -ForegroundColor Cyan'''
new = '''} catch {\n    $downloadError = $_\n    Write-Host "UPOZORENJE: Preuzimanje FFmpeg-a nije uspelo ($downloadError). Pokusavam validirani lokalni fallback..." -ForegroundColor Yellow\n    try {\n        $fallbackScript = Join-Path $repoRoot 'scripts\\copy-ffmpeg-from-path.ps1'\n        if (-not (Test-Path $fallbackScript)) { throw "Fallback skripta ne postoji: $fallbackScript" }\n        & $fallbackScript -Destination $ffmpegToolsDir\n        if ($LASTEXITCODE -ne 0) { throw "Fallback skripta je vratila kod $LASTEXITCODE." }\n        Write-Host "FFmpeg download je bio nedostupan, ali release koristi validirane lokalne FFmpeg binarne fajlove." -ForegroundColor Green\n    } catch {\n        $bundledToolsOk = $false\n        throw "FFmpeg nije mogao da se spakuje. Download greska: $downloadError; lokalni fallback greska: $_"\n    }\n}\n\ntry {\n    Write-Host "Preuzimam yt-dlp..." -ForegroundColor Cyan'''
count = text.count(old)
if count != 1:
    raise SystemExit(f'Expected one FFmpeg catch anchor, found {count}')
path.write_text(text.replace(old, new, 1), encoding='utf-8')
print('Release FFmpeg PATH fallback integrated.')
