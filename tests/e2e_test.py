from __future__ import annotations
import json, os, shutil, subprocess, sys, tempfile, time, urllib.request
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
PORT=8876


def get_json(path: str):
    with urllib.request.urlopen(f'http://127.0.0.1:{PORT}{path}', timeout=5) as r:
        return json.loads(r.read().decode())


def browser_candidates() -> list[tuple[str,str]]:
    candidates: list[tuple[str,str]]=[]
    if os.name=='nt':
        env=os.environ
        roots=[Path(env.get('PROGRAMFILES','')),Path(env.get('PROGRAMFILES(X86)','')),Path(env.get('LOCALAPPDATA',''))]
        rels=[
            ('Chrome','Google/Chrome/Application/chrome.exe'),
            ('Edge','Microsoft/Edge/Application/msedge.exe'),
            ('Brave','BraveSoftware/Brave-Browser/Application/brave.exe'),
        ]
        for name,rel in rels:
            for base in roots:
                path=base/rel
                if path.is_file(): candidates.append((name,str(path))); break
    else:
        for name,exe in [('Chromium','chromium'),('Chrome','google-chrome'),('Brave','brave-browser'),('Edge','microsoft-edge')]:
            path=shutil.which(exe)
            if path: candidates.append((name,path))
    return candidates


def main() -> int:
    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        print(
            'Playwright nije instaliran u ovom Python okruženju. '
            'Instaliraj zaključane osnovne zavisnosti komandom: '
            f'{sys.executable} -m pip install -r requirements-core.txt'
        )
        return 2
    candidates=browser_candidates()
    if not candidates:
        print('Nije pronađen Chrome, Edge, Brave ili Chromium za E2E test.')
        return 3
    with tempfile.TemporaryDirectory(prefix='suno-e2e-') as temp:
        env=os.environ.copy(); env.update({'SUNO_STUDIO_DATA_DIR':str(Path(temp)/'data'),'SUNO_STUDIO_DOWNLOAD_DIR':str(Path(temp)/'downloads'),'SUNO_STUDIO_EXPORT_DIR':str(Path(temp)/'exports'),'SUNO_STUDIO_PORT':str(PORT),'SUNO_AUTO_OPEN':'0','PYTHONUTF8':'1'})
        proc=subprocess.Popen([sys.executable,str(ROOT/'app'/'server.py')],cwd=str(ROOT),env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
        results=[]
        try:
            for _ in range(100):
                try:
                    if get_json('/api/health').get('version')=='3.0.0': break
                except Exception: time.sleep(.2)
            else: raise RuntimeError('Test server nije pokrenut.')
            with sync_playwright() as pw:
                for browser_name,executable in candidates:
                    browser=None
                    try:
                        browser=pw.chromium.launch(headless=True,executable_path=executable,args=['--no-first-run','--disable-extensions'])
                        page=browser.new_page(viewport={'width':1440,'height':1000})
                        errors=[]; failed=[]
                        page.on('pageerror',lambda e: errors.append(str(e)))
                        page.on('requestfailed',lambda r: failed.append(f'{r.method} {r.url}: {r.failure}'))
                        page.goto(f'http://127.0.0.1:{PORT}/',wait_until='networkidle',timeout=30000)
                        page.locator('#pageTitle').wait_for(timeout=10000)
                        main_views = ('home','library','import','recognition','tools','settings')
                        actual = page.locator('.sidebar nav > .nav-item').evaluate_all(
                            "nodes => nodes.map(node => node.dataset.view)"
                        )
                        assert actual == list(main_views), f'Glavni meni nije jednostavan i logičan: {actual}'
                        assert page.locator('.sidebar nav > .sps-nav-section:visible').count() == 0
                        assert page.locator('.suno-tools-drawer').count() == 1
                        for view in main_views:
                            page.locator(f'.sidebar nav > [data-view="{view}"]').click(); page.wait_for_timeout(100)
                            assert 'active' in (page.locator(f'#view-{view}').get_attribute('class') or '')
                        for view in ('download','folders','audio','smart','versions','release','stats','logs'):
                            page.locator('.suno-tools-drawer').evaluate('node => node.open = true')
                            page.locator(f'[data-open-view="{view}"]').first.click(); page.wait_for_timeout(100)
                            assert 'active' in (page.locator(f'#view-{view}').get_attribute('class') or '')
                        assert page.locator('[data-view="production"]').count() == 0
                        assert page.locator('#view-production').count() == 0
                        for theme in ('aurora-flow','graphite-console','vinyl-loft','signal-grid','paper-studio'):
                            page.locator('#modernSkinQuickSelect').select_option(theme)
                            assert page.locator('body').get_attribute('data-sps-skin') == theme
                        assert not errors, f'JavaScript greške: {errors}'
                        local_failed=[x for x in failed if f'127.0.0.1:{PORT}' in x]
                        assert not local_failed, f'Lokalni zahtevi nisu uspeli: {local_failed}'
                        results.append({'browser':browser_name,'ok':True})
                    except Exception as exc:
                        results.append({'browser':browser_name,'ok':False,'error':str(exc)[:1000]})
                    finally:
                        if browser: browser.close()
            passed=[r for r in results if r['ok']]
            print(json.dumps({'ok':bool(passed),'results':results},ensure_ascii=False,indent=2))
            return 0 if passed else 4
        finally:
            try:
                req=urllib.request.Request(f'http://127.0.0.1:{PORT}/api/shutdown',data=b'{}',headers={'Content-Type':'application/json'},method='POST'); urllib.request.urlopen(req,timeout=2).read()
            except Exception: pass
            try: proc.wait(timeout=5)
            except Exception: proc.kill()

if __name__=='__main__': raise SystemExit(main())
