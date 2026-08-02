from __future__ import annotations
import json, os, subprocess, sys, tempfile, time, urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PORT = 8893
BASE = f'http://127.0.0.1:{PORT}'


def req(path):
    with urllib.request.urlopen(BASE + path, timeout=10) as r:
        return dict(r.headers), r.read()


def main():
    checks = []

    def check(name, cond, detail=''):
        if not cond:
            raise AssertionError(f'{name}: {detail}')
        checks.append(name)

    with tempfile.TemporaryDirectory(prefix='sps-cache-hdr-') as raw:
        base = Path(raw)
        env = os.environ.copy()
        env.update({
            'SUNO_STUDIO_USER_DIR': str(base / 'user'), 'SUNO_STUDIO_DATA_DIR': str(base / 'data'),
            'SUNO_STUDIO_DOWNLOAD_DIR': str(base / 'dl'), 'SUNO_STUDIO_EXPORT_DIR': str(base / 'ex'),
            'SUNO_STUDIO_PORT': str(PORT), 'SUNO_AUTO_OPEN': '0', 'PYTHONUTF8': '1',
        })
        proc = subprocess.Popen([sys.executable, str(ROOT / 'app/server.py')], cwd=str(ROOT), env=env,
                                 stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        try:
            for _ in range(100):
                try:
                    with urllib.request.urlopen(BASE + '/api/health', timeout=1) as r:
                        if json.loads(r.read().decode()).get('version') == '3.3.2':
                            break
                except Exception:
                    time.sleep(0.1)
            else:
                raise RuntimeError('server did not start: ' + (proc.stdout.read() if proc.stdout else ''))

            # -- This is the exact bug a real user hit: WebView2's own HTTP
            # cache (stored outside the app's install folder, so an
            # uninstall/reinstall never clears it) kept serving old
            # index.html/app.js/style.css after a real update, because the
            # server sent no Cache-Control header on these routes at all. --
            for path in ('/', '/assets/app.js', '/assets/style.css'):
                headers, body = req(path)
                cache_control = headers.get('Cache-Control', '')
                check(f'{path} sends no-store Cache-Control', 'no-store' in cache_control, f'got: {cache_control!r}')
                check(f'{path} actually returns content', len(body) > 100, f'len={len(body)}')

            # -- index.html served both via "/" and "/index.html" must both be uncached. --
            headers2, _ = req('/index.html')
            check('/index.html also sends no-store Cache-Control', 'no-store' in headers2.get('Cache-Control', ''))
        finally:
            proc.terminate()
            try:
                proc.wait(timeout=10)
            except Exception:
                proc.kill()

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
