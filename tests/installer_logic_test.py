from pathlib import Path
import hashlib
ROOT=Path(__file__).resolve().parents[1]
setup=(ROOT/'windows_build/setup/main.go').read_text(encoding='utf-8')
progress=(ROOT/'windows_build/progress/main.go').read_text(encoding='utf-8')
checks=[]
def ok(name, cond):
    assert cond, name
    checks.append(name)
ok('temporary stage before download', 'os.TempDir(), "SunoPesmeStudio-stage-"' in setup)
ok('version stage created only after components', setup.index('prepareComponents(stage') < setup.index('versionStage := filepath.Join(versionsRoot'))
ok('cross-volume handled by copy', 'copyTree(stage, versionStage)' in setup)
ok('final self-test on selected disk', 'finalTest := exec.Command(filepath.Join(versionStage' in setup)
ok('python actually executed', '{filepath.Join(pythonDir, "python.exe"), []string{"--version"}}' in setup)
ok('pythonw actually executed', '{filepath.Join(pythonDir, "pythonw.exe"), []string{"-c", "import sys; sys.exit(0)"}}' in setup)
ok('ffmpeg actually executed', '{ffmpegExe, []string{"-version"}}' in setup)
ok('ffprobe actually executed', '{ffprobeExe, []string{"-version"}}' in setup)
ok('yt-dlp actually executed', '{ytdlp, []string{"--version"}}' in setup)
ok('deno actually executed', '{denoExe, []string{"--version"}}' in setup)
ok('download retries', 'for attempt := 1; attempt <= 3; attempt++' in setup)
ok('Python official fallback', 'python-3.13.14-embed-amd64.zip' in setup)
ok('FFmpeg fallback', 'BtbN/FFmpeg-Builds' in setup)
ok('yt-dlp fallback', 'releases/latest/download/yt-dlp.exe' in setup)
ok('Deno fallback', 'releases/latest/download/deno-x86_64-pc-windows-msvc.zip' in setup)
ok('native progress window', 'SunoPesmeStudioInstallerProgress' in progress)
orig=Path('/mnt/data/work_orig/Suno-Pesme-Studio-v3.2.0-KOMPLETAN/app/web/style.css')
new=ROOT/'app/web/style.css'
if orig.exists():
    ok('original CSS unchanged', hashlib.sha256(orig.read_bytes()).digest()==hashlib.sha256(new.read_bytes()).digest())
print({'ok':True,'passed':len(checks),'checks':checks})
