from __future__ import annotations
import json, os, subprocess, sys, tempfile, time, urllib.request
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
PORT=8893
BASE=f'http://127.0.0.1:{PORT}'

def request(path:str,body=None):
    raw=None;headers={};method='GET'
    if body is not None:
        raw=json.dumps(body).encode();headers['Content-Type']='application/json';method='POST'
    with urllib.request.urlopen(urllib.request.Request(BASE+path,data=raw,headers=headers,method=method),timeout=5) as response:
        return json.loads(response.read().decode())

def main()->int:
    with tempfile.TemporaryDirectory(prefix='sps-watchdog-') as raw:
        base=Path(raw);env=os.environ.copy();env.update({
            'SUNO_STUDIO_DATA_DIR':str(base/'data'),
            'SUNO_STUDIO_DOWNLOAD_DIR':str(base/'downloads'),
            'SUNO_STUDIO_EXPORT_DIR':str(base/'exports'),
            'SUNO_STUDIO_PORT':str(PORT),'SUNO_AUTO_OPEN':'0','PYTHONUTF8':'1',
        })
        proc=subprocess.Popen([sys.executable,str(ROOT/'app/watchdog.py')],cwd=str(ROOT),env=env)
        try:
            status=None
            for _ in range(150):
                try:
                    status=request('/api/health')
                    if status.get('watchdog'):break
                except Exception:time.sleep(.1)
            if not status or not status.get('watchdog'):
                raise AssertionError('Watchdog server se nije pokrenuo.')
            request('/api/shutdown',{})
            code=proc.wait(timeout=15)
            if code!=0:raise AssertionError(f'Watchdog izlazni kod: {code}')
            if (base/'data'/'watchdog.stop').exists():raise AssertionError('Stop marker nije očišćen.')
            log=(base/'data'/'watchdog.log').read_text(encoding='utf-8')
            if 'normalno ugašen' not in log and 'normalno gašenje' not in log:raise AssertionError(log)
        finally:
            if proc.poll() is None:proc.kill()
    print(json.dumps({'ok':True,'passed':1,'checks':['watchdog start and normal shutdown']},ensure_ascii=False,indent=2))
    return 0

if __name__=='__main__':raise SystemExit(main())
