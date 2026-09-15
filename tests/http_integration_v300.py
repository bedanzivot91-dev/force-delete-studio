from __future__ import annotations
import json, math, os, subprocess, sys, tempfile, time, urllib.error, urllib.parse, urllib.request, wave, zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
PORT=8892
BASE=f'http://127.0.0.1:{PORT}'

def req(path, body=None, method=None):
    data=None; headers={}
    if body is not None:
        data=json.dumps(body).encode(); headers['Content-Type']='application/json'; method=method or 'POST'
    with urllib.request.urlopen(urllib.request.Request(BASE+path,data=data,headers=headers,method=method or 'GET'),timeout=120) as r:
        raw=r.read(); return json.loads(raw.decode()) if 'json' in (r.headers.get('Content-Type') or '') else raw

def wait_task(timeout=120):
    end=time.time()+timeout
    while time.time()<end:
        t=req('/api/task').get('task')
        if t and t.get('status') in ('done','error','cancelled'):
            if t.get('status')=='error': raise AssertionError(t)
            return t
        time.sleep(.1)
    raise TimeoutError('task timeout')

def make_wav(path: Path, seconds=2.0, hz=440):
    sr=44100
    with wave.open(str(path),'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(sr)
        frames=bytearray()
        for i in range(int(sr*seconds)):
            v=int(14000*math.sin(2*math.pi*hz*i/sr)); frames += int(v).to_bytes(2,'little',signed=True)
        w.writeframes(frames)

def main():
    checks=[]
    def check(name, cond, detail=''):
        if not cond: raise AssertionError(f'{name}: {detail}')
        checks.append(name)
    with tempfile.TemporaryDirectory(prefix='sps182-http-') as raw:
        base=Path(raw); data=base/'data'; dl=base/'downloads'; ex=base/'exports'; src=base/'source'; src.mkdir()
        wav=src/'Test pesma.wav'; make_wav(wav); (src/'Test pesma.txt').write_text('Prvi stih\nDrugi stih',encoding='utf-8')
        env=os.environ.copy(); env.update({'SUNO_STUDIO_USER_DIR':str(base/'user'),'SUNO_STUDIO_DATA_DIR':str(data),'SUNO_STUDIO_DOWNLOAD_DIR':str(dl),'SUNO_STUDIO_EXPORT_DIR':str(ex),'SUNO_STUDIO_PORT':str(PORT),'SUNO_AUTO_OPEN':'0','PYTHONUTF8':'1'})
        proc=subprocess.Popen([sys.executable,str(ROOT/'app/server.py')],cwd=str(ROOT),env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
        try:
            for _ in range(100):
                try:
                    h=req('/api/health')
                    if h.get('version')=='3.3.2': break
                except Exception: time.sleep(.1)
            else: raise RuntimeError('server start failed')
            check('health',h.get('ok') and h.get('version')=='3.3.2',str(h))
            # Security headers and host validation.
            with urllib.request.urlopen(BASE+'/',timeout=10) as resp:
                check('security headers',resp.headers.get('X-Content-Type-Options')=='nosniff' and resp.headers.get('Content-Security-Policy'),dict(resp.headers))
            try:
                urllib.request.urlopen(urllib.request.Request(BASE+'/api/health',headers={'Host':'evil.example'}),timeout=10)
                hostile=False
            except urllib.error.HTTPError as e:
                hostile=e.code==403
            check('host header protection',hostile)
            try:
                badreq=urllib.request.Request(BASE+'/api/settings',data=b'{bad json',headers={'Content-Type':'application/json'},method='POST')
                urllib.request.urlopen(badreq,timeout=10)
                malformed=False
            except urllib.error.HTTPError as e:
                malformed=e.code==400
            check('malformed json 400',malformed)
            req('/api/import/folder',{'folder':str(src)}); wait_task()
            songs=req('/api/songs?limit=20')
            check('local import',songs['count']==1 and songs['total_library']==1,str(songs))
            sid=songs['songs'][0]['id']
            detail=req('/api/song?id='+urllib.parse.quote(sid))['song']
            check('lyrics sidecar','Prvi stih' in detail.get('lyrics',''))
            check('local wav',Path(detail['local_wav']).exists(),str(detail))
            search=req('/api/songs?search=Test&limit=20'); check('search',search['count']==1)
            ids=req('/api/song-ids?filter=all&limit=20000'); check('song ids endpoint',ids['count']==1 and ids['ids']==[sid],str(ids))
            yset=req('/api/youtube/settings',{'cookies_browser':'chrome','copyright_owner_name':'Tester'}); check('youtube cookie setting',yset.get('cookies_browser')=='chrome',str(yset))
            st=req('/api/status'); check('youtube cookie status',st.get('youtube_cookies_browser')=='chrome',str(st))
            stats=req('/api/stats')['stats']; check('stats',int(stats.get('total') or stats.get('total_songs') or 0)>=1,str(stats))
            info=req('/api/audio/info?id='+urllib.parse.quote(sid)); check('audio info',float(info.get('info',{}).get('duration') or 0)>1)
            media_path=urllib.parse.quote(detail['local_wav'],safe='')
            r=urllib.request.Request(BASE+'/media?path='+media_path,headers={'Range':'bytes=0-99'})
            with urllib.request.urlopen(r,timeout=10) as resp: payload=resp.read(); status=resp.status
            check('range 206',status==206 and len(payload)==100,(status,len(payload)))
            try:
                r=urllib.request.Request(BASE+'/media?path='+media_path,headers={'Range':'bytes=99999999-100000000'})
                urllib.request.urlopen(r,timeout=10)
                bad=False
            except urllib.error.HTTPError as e: bad=e.code==416
            check('range 416',bad)
            wf=req('/api/audio/waveform?id='+urllib.parse.quote(sid)+'&width=250'); check('waveform',len(wf.get('peaks') or wf.get('waveform',{}).get('peaks') or [])>0,str(wf)[:300])
            req('/api/song/update',{'id':sid,'fields':{'title':'Promenjena pesma','notes':'test'}})
            hist=req('/api/song/history?id='+urllib.parse.quote(sid)); check('history',len(hist['history'])>=1)
            req('/api/song/history/undo',{'id':sid}); check('undo',req('/api/song?id='+urllib.parse.quote(sid))['song']['title']=='Test pesma')
            cues=req('/api/subtitles?id='+urllib.parse.quote(sid))['cues']; check('subtitle draft',len(cues)>=2)
            saved=req('/api/subtitles/save',{'id':sid,'cues':cues}); check('subtitle save',Path(saved['paths']['lrc']).exists() and str(Path(saved['paths']['lrc']).resolve()).startswith(str((base/'user'/'Biblioteka_pesama').resolve())),str(saved))
            req('/api/audio/process',{'id':sid,'options':{'format':'wav','start':0.2,'end':1.2,'processing_mode':'precise','label':'Test trim'}}); t=wait_task(); check('audio process',t['status']=='done',str(t))
            detail=req('/api/song?id='+urllib.parse.quote(sid))['song']; check('derived file',len(detail.get('derived_files') or [])>=1)
            for fmt in ('json','csv','zip'):
                out=req('/api/export',{'ids':[sid],'format':fmt}); check('export '+fmt,Path(out['path']).is_file(),str(out))
            m3u=req('/api/export/m3u',{'ids':[sid]}); check('m3u',Path(m3u['path']).is_file())
            lyrics=req('/api/export/lyrics',{'ids':[sid]}); check('lyrics zip',zipfile.is_zipfile(lyrics['path']))
            b=req('/api/backup',{}); check('backup',zipfile.is_zipfile(b['path']))
            bf=req('/api/backup/full',{}); check('full backup',zipfile.is_zipfile(bf['path']))
            # A structurally valid ZIP with altered audio must be rejected by manifest SHA before DB replacement.
            corrupt=base/'corrupt-full.zip'
            with zipfile.ZipFile(bf['path'],'r') as zin, zipfile.ZipFile(corrupt,'w',compression=zipfile.ZIP_DEFLATED) as zout:
                manifest=json.loads(zin.read('backup_manifest.json').decode('utf-8'))
                corrupt_name=next(item['archive_path'] for item in manifest.get('files',[]) if item.get('kind')=='song_field')
                for name in zin.namelist():
                    payload=zin.read(name)
                    if name==corrupt_name: payload=payload+b'corrupt'
                    zout.writestr(name,payload)
            try:
                req('/api/restore',{'path':str(corrupt)})
                corrupt_rejected=False
            except urllib.error.HTTPError as e:
                corrupt_rejected=e.code in (409,500)
            check('corrupt full backup rejected',corrupt_rejected)
            Path(detail['local_wav']).unlink()
            restored_full=req('/api/restore',{'path':bf['path']})
            detail=req('/api/song?id='+urllib.parse.quote(sid))['song']
            check('full backup restore files',restored_full.get('restored_files',0)>=1 and Path(detail['local_wav']).exists(),str(restored_full))
            inc_dir=base/'inc'; req('/api/backup/incremental',{'folder':str(inc_dir),'include_files':True}); wait_task(); adv=req('/api/advanced/status'); check('incremental api',len(adv.get('incremental_backups') or [])>=1)
            cloud_dir=base/'cloud'; c1=req('/api/cloud-backup',{'folder':str(cloud_dir),'include_files':True}); c2=req('/api/cloud-backup',{'folder':str(cloud_dir),'include_files':True}); check('cloud collision',c1['path']!=c2['path'])
            cr=req('/api/cloud-backup/restore',{'path':c1['path'],'restore_files':True}); check('cloud restore',cr['files_restored']>=1 and Path(cr['restore_root']).exists(),str(cr))
            health=req('/api/library/health')['health']; check('library health',isinstance(health,dict),str(health))
            selftest=req('/api/self-test',{'include_audio':True})['report']; check('self test',int(selftest.get('failed') or 0)==0,str(selftest))
            diag=req('/api/diagnostics/export',{'include_audio':False}); check('diagnostics',zipfile.is_zipfile(diag['path']))
            try:
                req('/media?path='+urllib.parse.quote('/etc/passwd',safe=''))
                traversal=False
            except urllib.error.HTTPError as e: traversal=e.code in (403,404)
            check('media security',traversal)
            v3=req('/api/v3/status'); check('v3 status',v3.get('ok') and v3.get('version')=='3.3.2',str(v3)[:400])
            v3dup=req('/api/v3/duplicates'); check('v3 duplicates','report' in v3dup,str(v3dup)[:300])
            ytp=req('/api/v3/youtube/package',{'id':sid,'channel_profile':'Nedostaješ PUNOO PESME'}); check('v3 youtube package',Path(ytp['path']).is_dir(),str(ytp))
            proof=req('/api/v3/proof',{'id':sid}); check('v3 proof package',zipfile.is_zipfile(proof['path']),str(proof))
            oldpart=dl/'staro.mp3.part';oldpart.parent.mkdir(parents=True,exist_ok=True);oldpart.write_bytes(b'x')
            os.utime(oldpart,(time.time()-40*86400,time.time()-40*86400))
            cleanup=req('/api/v3/storage/cleanup',{'confirm':True,'cache_days':30,'keep_rollbacks':5});check('v3 safe cleanup',cleanup['report']['removed_count']>=1 and not oldpart.exists(),str(cleanup))
            sec=req('/api/v3/security/set-pin',{'new_pin':'4826','timeout_minutes':15}); check('security pin set',sec['security']['enabled'],str(sec))
            req('/api/v3/security/lock',{})
            try:
                req('/api/songs?limit=1'); locked=False
            except urllib.error.HTTPError as e:
                locked=e.code==423
            check('security api lock',locked)
            unlocked=req('/api/v3/security/unlock',{'pin':'4826'}); check('security unlock',not unlocked['security']['locked'],str(unlocked))
            disabled=req('/api/v3/security/disable',{'pin':'4826'}); check('security disable',not disabled['security']['enabled'],str(disabled))
            print(json.dumps({'ok':True,'passed':len(checks),'checks':checks},ensure_ascii=False,indent=2))
        finally:
            try: req('/api/shutdown',{})
            except Exception: pass
            try: proc.wait(timeout=8)
            except Exception: proc.kill()
            if proc.returncode not in (0,None):
                print(proc.stdout.read() if proc.stdout else '',file=sys.stderr)
    return 0
if __name__=='__main__': raise SystemExit(main())
