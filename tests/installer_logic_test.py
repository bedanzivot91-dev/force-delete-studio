from pathlib import Path
import hashlib

ROOT = Path(__file__).resolve().parents[1]
SETUP_DIR = ROOT / 'windows_build' / 'setup'
setup_main = (SETUP_DIR / 'main.go').read_text(encoding='utf-8')
setup_all = '\n'.join(
    p.read_text(encoding='utf-8')
    for p in sorted(SETUP_DIR.glob('*.go'))
)
ci_install = (SETUP_DIR / 'ci_install.go').read_text(encoding='utf-8')
ci_remove = (ROOT / 'windows_build/uninstaller/ci_remove.go').read_text(encoding='utf-8')
native_remove_guard = (ROOT / 'windows_build/uninstaller/00_remove_target_guard.go').read_text(encoding='utf-8')
progress = (ROOT / 'windows_build/progress/main.go').read_text(encoding='utf-8')
workflow = (ROOT / '.github/workflows/windows-build.yml').read_text(encoding='utf-8')
checks = []


def ok(name, cond):
    assert cond, name
    checks.append(name)


# Core installer transaction/safety behavior remains in setup/main.go.
ok('temporary stage before download', 'os.TempDir(), "SunoPesmeStudio-stage-"' in setup_main)
ok('version stage created only after components', setup_main.index('prepareComponents(stage') < setup_main.index('versionStage := filepath.Join(versionsRoot')))
ok('cross-volume handled by copy', 'copyTree(stage, versionStage)' in setup_main)
ok('final self-test on selected disk', 'finalTest := exec.Command(filepath.Join(versionStage' in setup_main)
ok('python actually executed', '{filepath.Join(pythonDir, "python.exe"), []string{"--version"}}' in setup_main)
ok('pythonw actually executed', '{filepath.Join(pythonDir, "pythonw.exe"), []string{"-c", "import sys; sys.exit(0)"}}' in setup_main)
ok('ffmpeg actually executed', '{ffmpegExe, []string{"-version"}}' in setup_main)
ok('ffprobe actually executed', '{ffprobeExe, []string{"-version"}}' in setup_main)
ok('yt-dlp actually executed', '{ytdlp, []string{"--version"}}' in setup_main)
ok('deno actually executed', '{denoExe, []string{"--version"}}' in setup_main)
ok('copy checks source byte count', 'n != srcInfo.Size()' in setup_main)
ok('copy checks destination byte count', 'dstInfo.Size() != srcInfo.Size()' in setup_main)
ok('manifest checked after initial copy', 'verifyProgramManifest(stage, manifest)' in setup_main)
ok('manifest checked after final disk copy', 'verifyProgramManifest(versionStage, manifest)' in setup_main)
ok('download retries', 'for attempt := 1; attempt <= 3; attempt++' in setup_main)

# Current components are split into focused setup/*.go modules. Test the real
# current design, not obsolete URLs/hashes that were deliberately removed.
ok('current embedded Python is 3.13.15', 'currentPythonVersion = "3.13.15"' in setup_all)
ok('Python 3.13.15 archive is SHA-256 pinned', 'd1f04d990aee1253d8569e8e5104e30fa9f5fa830899f14843448872d936a2cf' in setup_all)
ok('current Deno is 2.9.5', 'currentDenoVersion = "2.9.5"' in setup_all)
ok('Deno x64 Windows asset is explicit', 'deno-x86_64-pc-windows-msvc.zip' in setup_all)
ok('Deno uses official per-asset sha256sum', 'currentDenoSHAURL  = currentDenoURL + ".sha256sum"' in setup_all)
ok('Deno checksum parser accepts the sidecar wrapper but only a real 64-hex digest', 'strictSHA256Token = regexp.MustCompile(`(?i)\\b[0-9a-f]{64}\\b`)' in setup_all)
ok('Deno checksum parser rejects ambiguous sidecars', 'if len(unique) != 1' in setup_all)
ok('Deno uses the strict per-asset checksum parser', 'checksumFromPerAssetSidecar(shaPath)' in setup_all)
ok('Deno downloaded ZIP is SHA-256 verified', 'verifyFileSHA(zipPath, expected)' in setup_all)
ok('Chromaprint-capable BtbN full FFmpeg is staged', 'ffmpeg-master-latest-win64-gpl.zip' in setup_all)
ok('FFmpeg is rejected when Chromaprint is absent', 'ffmpegHasChromaprintBinary' in setup_all and 'nema Chromaprint muxer' in setup_all)
ok('yt-dlp current pinned release remains available', 'releases/download/2026.07.04/yt-dlp.exe' in setup_main)
ok('legacy yt-dlp latest URL cannot execute without verification', 'unverified yt-dlp latest fallback is blocked' in setup_all and '/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe' in setup_all)
ok('WebView2 Evergreen Standalone is bundled for offline install', 'MicrosoftEdgeWebView2RuntimeInstallerX64.exe' in setup_all)

# The shipped EXEs contain headless regression entry points, but those switches
# are destructive and must be usable only by the GitHub Actions environment.
ok('ci installer destructive switch is GitHub-Actions-only', 'GITHUB_ACTIONS' in ci_install and '--ci-install is available only inside GitHub Actions' in ci_install)
ok('ci uninstaller destructive switch is GitHub-Actions-only', 'GITHUB_ACTIONS' in ci_remove and '--ci-remove is available only inside GitHub Actions' in ci_remove)
ok('native uninstaller rejects non-Suno target directories', 'validateNativeRemoveTarget' in native_remove_guard and 'AKTIVNA_VERZIJA.txt' in native_remove_guard and 'Versions' in native_remove_guard)
ok('native uninstaller rejects drive-root deletion', 'odbijeno brisanje korena diska' in native_remove_guard)
ok('native uninstaller requires both installed executables', 'Deinstaliraj "+appName+".exe' in native_remove_guard and 'appName+".exe' in native_remove_guard)

# Obsolete component references must not survive the pre-build normalizer.
for obsolete in (
    '3.13.14',
    '3.14.6',
    'ffmpeg-8.1.2-essentials_build',
    'ffmpeg-release-essentials',
    '/v2.8.1/',
):
    ok(f'obsolete component reference removed: {obsolete}', obsolete not in setup_main)

ok('workflow verifies final manifest', 'Verify final release folder and ZIP against MANIFEST_SHA256.txt' in workflow)
ok('workflow expands and verifies the actual ZIP', 'Expand-Archive -LiteralPath "$rel.zip"' in workflow)
ok('workflow audits obsolete and duplicate packaged content', 'Audit Program for obsolete or duplicate packaged content' in workflow)
ok('native progress window', 'SunoPesmeStudioInstallerProgress' in progress)

orig = Path('/mnt/data/work_orig/Suno-Pesme-Studio-v3.2.0-KOMPLETAN/app/web/style.css')
new = ROOT / 'app/web/style.css'
if orig.exists():
    ok('original CSS unchanged', hashlib.sha256(orig.read_bytes()).digest() == hashlib.sha256(new.read_bytes()).digest())

print({'ok': True, 'passed': len(checks), 'checks': checks})
