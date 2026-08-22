from pathlib import Path

root = Path(__file__).resolve().parents[2]
script = root / 'scripts' / 'test-installed-release.ps1'
text = script.read_text(encoding='utf-8-sig')
anchor = "    'runtimes\\win-x64\\whisper.dll'\n)"
replacement = """    'runtimes\\win-x64\\whisper.dll',
    'THIRD_PARTY_NOTICES.md',
    'Licenses\\Apache-2.0-Serilog.txt',
    'Licenses\\GPLv3-FFmpeg.txt',
    'Licenses\\LGPL-2.1-LibVLC.txt',
    'Licenses\\MIT-Avalonia.txt',
    'Licenses\\MIT-CommunityToolkit.Mvvm.txt',
    'Licenses\\MIT-Microsoft.Data.Sqlite.txt',
    'Licenses\\MIT-Microsoft.Extensions.DependencyInjection.txt',
    'Licenses\\MIT-Whisper.net.txt',
    'Licenses\\MIT-whisper.cpp.txt',
    'Licenses\\PublicDomain-SQLite.txt',
    'Licenses\\Unlicense-yt-dlp.txt'
)"""
if anchor not in text:
    raise SystemExit('installed expectedPayload anchor not found')
text = text.replace(anchor, replacement, 1)
script.write_text(text, encoding='utf-8')

Path(__file__).unlink()
workflow = root / '.github' / 'workflows' / 'materialize-installed-legal-payload-gate.yml'
if workflow.exists():
    workflow.unlink()
