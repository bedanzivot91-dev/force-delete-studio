from __future__ import annotations
import json, math, sys, tempfile, threading, wave, urllib.error
from unittest import mock
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'app'))
from database import LibraryDB
from security_lock import AppSecurity
from suno_client import SunoClient
import v3_features
from v3_features import (align_original_lyrics, create_proof_package, create_youtube_package,
    cleanup_storage, duplicate_detection, library_integrity_scan, organize_library, render_short_clip, suggest_short_clips, youtube_resumable_upload)


def make_wav(path:Path,seconds=8.0,hz=440):
    sr=44100
    with wave.open(str(path),'wb') as w:
        w.setnchannels(1);w.setsampwidth(2);w.setframerate(sr)
        data=bytearray()
        for i in range(int(sr*seconds)):
            amp=8000 if i < sr*2 else 15000
            v=int(amp*math.sin(2*math.pi*hz*i/sr));data+=v.to_bytes(2,'little',signed=True)
        w.writeframes(data)

class ResumeHandler(BaseHTTPRequestHandler):
    payload=b'ID3'+b'R'*200000
    calls=0
    def do_GET(self):
        cls=type(self);cls.calls+=1
        r=self.headers.get('Range','')
        if r:
            start=int(r.split('=')[1].split('-')[0]); body=cls.payload[start:]
            self.send_response(206);self.send_header('Content-Range',f'bytes {start}-{len(cls.payload)-1}/{len(cls.payload)}')
            self.send_header('Content-Length',str(len(body)));self.send_header('Content-Type','audio/mpeg');self.end_headers();self.wfile.write(body);return
        self.send_response(200);self.send_header('Content-Length',str(len(cls.payload)));self.send_header('Content-Type','audio/mpeg');self.end_headers()
        self.wfile.write(cls.payload[:70000]);self.wfile.flush();self.connection.shutdown(1)
    def log_message(self,*a): pass


class FakeResponse:
    def __init__(self,status,headers=None,body=b''):
        self.status=status;self.headers=headers or {};self._body=body
    def read(self):return self._body
    def __enter__(self):return self
    def __exit__(self,*args):return False

class FakeOAuth:
    def get_access_token(self,profile_id=''):return 'token'
    def refresh_access_token(self,profile_id=''):return 'token2'


def main():
    checks=[]
    with tempfile.TemporaryDirectory(prefix='sps-v3-') as raw:
        base=Path(raw); db=LibraryDB(base/'library.db'); wav=base/'pesma.wav'; make_wav(wav)
        song={'id':'song-1','title':'Moja pesma','display_name':'Autor','duration':8.0,'created_at':'2026-07-29T00:00:00Z','lyrics':'Prvi stih\nDrugi stih'}
        db.upsert_song(song);db.update_song_files('song-1',local_wav=str(wav))
        # probe_audio_files=False: this test verifies library_integrity_scan's
        # DB/filesystem bookkeeping (checked/missing counts), not FFmpeg
        # itself -- FFmpeg here would come from tools/ffmpeg/bin/ffmpeg.exe
        # (see app/audio_tools.py:ffmpeg_path), which is only staged into
        # the package by the installer/CI's later "Stage real components"
        # step, not present yet when this test suite runs. Real production
        # use (and CI's own component-staging checks) exercises the actual
        # ffprobe path separately.
        integrity=library_integrity_scan(db,probe_audio_files=False);assert integrity['checked_files']>=1 and not integrity['missing'];checks.append('library integrity')
        plan=organize_library(db,base/'organized',dry_run=True);assert plan['count']>=1 and plan['dry_run'];checks.append('organizer dry run')
        dup=duplicate_detection(db);assert 'exact_groups' in dup;checks.append('duplicate report')
        aligned=align_original_lyrics('Prvi stih\nDrugi stih',{'words':[{'start':.2,'end':.6,'word':'Prvi'},{'start':.6,'end':1.0,'word':'stih'},{'start':2.0,'end':2.4,'word':'Drugi'},{'start':2.4,'end':2.8,'word':'stih'}]},8)
        assert len(aligned)==2 and aligned[0]['start']<aligned[1]['start'];checks.append('original lyrics alignment')
        suggestions=suggest_short_clips(song,wav,[5]);assert suggestions['suggestions'];checks.append('short suggestions')
        clip=render_short_clip(wav,base/'short.mp3',0,5,normalize=False);assert Path(clip['path']).is_file();checks.append('short render')
        full=db.get_song('song-1');proof=create_proof_package(full,base/'proof');assert Path(proof['path']).is_file();checks.append('proof package')
        package=create_youtube_package(full,base/'yt');assert Path(package['path'],'youtube-metadata.json').is_file();checks.append('youtube package')
        security=AppSecurity(db);security.set_pin('4826',timeout_minutes=15);security.lock_now();assert security.is_locked();security.unlock('4826');assert not security.is_locked();security.disable('4826');assert not security.enabled();checks.append('pin lock')
        httpd=ThreadingHTTPServer(('127.0.0.1',0),ResumeHandler);threading.Thread(target=httpd.serve_forever,daemon=True).start()
        target=base/'resume.mp3';SunoClient('x').download_file(f'http://127.0.0.1:{httpd.server_address[1]}/audio',target,auth=False)
        httpd.shutdown();assert target.read_bytes()==ResumeHandler.payload and ResumeHandler.calls>=2;checks.append('resumable download')

        video=base/'video.mp4';video.write_bytes(b'V'*(9*1024*1024))
        calls={'n':0}
        def fake_urlopen(request,timeout=0):
            calls['n']+=1
            if calls['n']==1:
                return FakeResponse(200,{'Location':'https://upload.youtube.com/resumable/session-test'})
            if calls['n']==2:
                return FakeResponse(308,{'Range':f'bytes=0-{8*1024*1024-1}'})
            if calls['n']==3:
                raise urllib.error.URLError('simulated disconnect')
            if calls['n']==4:
                return FakeResponse(308,{'Range':f'bytes=0-{8*1024*1024-1}'})
            return FakeResponse(200,{},json.dumps({'id':'video123','status':{},'snippet':{}}).encode())
        progress=[]
        with mock.patch.object(v3_features.urllib.request,'urlopen',side_effect=fake_urlopen):
            uploaded=youtube_resumable_upload(FakeOAuth(),video,{'title':'Test','privacy_status':'private'},progress=lambda n,d,t:progress.append((d,t)))
        assert uploaded['video_id']=='video123' and uploaded['resumable'] and calls['n']>=5 and progress[-1][0]==video.stat().st_size
        checks.append('resumable youtube upload')
        old_part=base/'downloads'/'old.mp3.part';old_part.parent.mkdir();old_part.write_bytes(b'x'*100)
        import os,time;os.utime(old_part,(time.time()-40*86400,time.time()-40*86400))
        cleaned=cleanup_storage(base,base/'data',base/'downloads',cache_days=30,keep_rollbacks=2)
        assert not old_part.exists() and cleaned['removed_count']>=1;checks.append('safe storage cleanup')
    print(json.dumps({'ok':True,'passed':len(checks),'checks':checks},ensure_ascii=False,indent=2))
if __name__=='__main__':main()
