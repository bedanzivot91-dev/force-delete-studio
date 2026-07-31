from __future__ import annotations
import json, os, sys, tempfile, time, zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'app'))

from database import LibraryDB
from advanced_features import create_incremental_backup, create_cloud_backup, restore_cloud_backup, relocate_missing_files, save_subtitle_files


def song(sid: str, title: str, audio: str='') -> dict:
    return {'id':sid,'title':title,'metadata':{},'audio_url':'','local_audio':audio}


def main() -> int:
    checks=[]
    def ok(name, condition, detail=''):
        if not condition: raise AssertionError(f'{name}: {detail}')
        checks.append(name)

    with tempfile.TemporaryDirectory(prefix='sps182-reg-') as raw:
        base=Path(raw); data=base/'data'; downloads=base/'downloads'; exports=base/'exports'
        os.environ.update({'SUNO_STUDIO_DATA_DIR':str(data),'SUNO_STUDIO_DOWNLOAD_DIR':str(downloads),'SUNO_STUDIO_EXPORT_DIR':str(exports),'SUNO_AUTO_OPEN':'0','SUNO_STUDIO_PORT':'8891'})
        db=LibraryDB(data/'suno_biblioteka.db')
        audio=base/'same.mp3'; audio.write_bytes(b'ID3'+b'x'*128)
        db.upsert_song(song('s1','Test'))
        db.update_song_files('s1', local_audio=str(audio))

        # Same-second backups must never collide.
        b1=create_incremental_backup(db,base/'inc',include_files=False)
        b2=create_incremental_backup(db,base/'inc',include_files=False)
        ok('incremental unique',b1['path']!=b2['path'] and Path(b1['path']).is_dir() and Path(b2['path']).is_dir())
        ok('incremental no-file count',b1['files']==0 and b1['candidates']==1,str(b1))
        c1=create_cloud_backup(db,base/'cloud',include_files=False)
        c2=create_cloud_backup(db,base/'cloud',include_files=False)
        ok('cloud unique',c1['path']!=c2['path'] and Path(c1['path']).is_file() and Path(c2['path']).is_file())

        # Remote-only subtitles must go to a controlled fallback, not parent of cwd.
        db.upsert_song(song('remote','Remote Song'))
        remote=db.get_song('remote') or {}
        sub=save_subtitle_files(remote,[{'start_s':0,'end_s':1,'text':'Test'}],db,fallback_dir=downloads/'Tekstovi')
        ok('subtitle fallback',Path(sub['lrc']).parent==(downloads/'Tekstovi').resolve(),sub['lrc'])

        # Ambiguous same-name relocation must not guess.
        missing=base/'old'/'dup.mp3'
        db.upsert_song(song('amb','Ambiguous'))
        db.update_song_files('amb', local_audio=str(missing))
        for d in (base/'r1',base/'r2'):
            d.mkdir(); (d/'dup.mp3').write_bytes(b'different-'+d.name.encode())
        rel=relocate_missing_files(db,[base/'r1',base/'r2'])
        ok('relocation no guessing',any(x['song_id']=='amb' for x in rel['unresolved']),str(rel))

        # Cloud restore defaults to controlled root and rewrites database paths.
        cloud_with=create_cloud_backup(db,base/'cloud-files',include_files=True)
        audio.unlink()
        restore_root=base/'safe-restore'
        restored=restore_cloud_backup(db,Path(cloud_with['path']),restore_files=True,restore_root=restore_root)
        restored_song=db.get_song('s1') or {}
        restored_audio=Path(str(restored_song.get('local_audio') or ''))
        ok('cloud controlled restore',restored_audio.exists() and restore_root.resolve() in restored_audio.parents,str(restored))

        # Import server after environment is isolated.
        import audio_match
        ok('audio cache env',str(audio_match.CACHE_DIR).startswith(str(data.resolve())),str(audio_match.CACHE_DIR))
        import server

        # New persistent job starts with attempts=1 and started_at populated.
        t=server.start_task('test_job','Test job',lambda task: task.finish('ok'),persistent_payload={'x':1})
        for _ in range(100):
            row=server.DB.get_job(int(t.job_id or 0))
            if row and row.get('status')!='running': break
            time.sleep(.02)
        row=server.DB.get_job(int(t.job_id or 0)) or {}
        ok('job metadata',int(row.get('attempts') or 0)==1 and bool(row.get('started_at')),str(row))

        # Synchronization cancellation inside a page must resume the same page.
        server.DB.clear_sync_checkpoints()
        class Fake:
            def list_all_projects(self,max_pages=100): return []
            def get_clip(self,sid): return {'id':sid,'title':sid,'metadata':{}}
            def list_library_cursor(self,cursor,liked=False):
                if cursor=='c2': return ([{'id':'s4','title':'Four','metadata':{}}],None,False,'fake')
                return ([{'id':'s1','title':'One','metadata':{}},{'id':'s2','title':'Two','metadata':{}},{'id':'s3','title':'Three','metadata':{}}],'c2',True,'fake')
        fake=Fake(); server.get_client=lambda: fake
        original=server.DB.upsert_song
        first_task=server.TaskState('sync','sync')
        calls={'n':0}
        def cancelling(item,source_group=''):
            out=original(item,source_group=source_group); calls['n']+=1
            if calls['n']==1: first_task.cancel_event.set()
            return out
        server.DB.upsert_song=cancelling
        server.sync_library(first_task,{'max_pages':1,'include_main':True,'include_workspaces':False,'resume':True})
        server.DB.upsert_song=original
        cp=server.DB.get_sync_checkpoint('main:liked=0') or {}
        ok('cancel checkpoint same page',int(cp.get('page_no') or 0)==0 and not cp.get('cursor') and int(cp.get('completed') or 0)==0,str(cp))
        second=server.TaskState('sync','sync')
        server.sync_library(second,{'max_pages':1,'include_main':True,'include_workspaces':False,'resume':True})
        cp2=server.DB.get_sync_checkpoint('main:liked=0') or {}
        ok('max pages first run',int(cp2.get('page_no') or 0)==1 and cp2.get('cursor')=='c2' and int(cp2.get('completed') or 0)==0,str(cp2))
        third=server.TaskState('sync','sync')
        server.sync_library(third,{'max_pages':1,'include_main':True,'include_workspaces':False,'resume':True})
        cp3=server.DB.get_sync_checkpoint('main:liked=0') or {}
        ok('resume later page',int(cp3.get('page_no') or 0)==2 and int(cp3.get('completed') or 0)==1,str(cp3))
        ok('all sync records',server.DB.get_song('s4') is not None)

        # A checkpoint at page 100 must still process one page when max_pages=1.
        server.DB.clear_sync_checkpoints()
        server.DB.save_sync_checkpoint('main:liked=0','main','c100',100,5000,'fake',completed=False)
        class Page100(Fake):
            def list_library_cursor(self,cursor,liked=False):
                assert cursor=='c100'; return ([{'id':'s101','title':'101','metadata':{}}],None,False,'fake')
        server.get_client=lambda: Page100()
        fourth=server.TaskState('sync','sync')
        server.sync_library(fourth,{'max_pages':1,'include_main':True,'include_workspaces':False,'resume':True})
        ok('max pages is per run',server.DB.get_song('s101') is not None)

        # A repeated cursor must be marked complete instead of looping every future run.
        server.DB.clear_sync_checkpoints()
        class RepeatCursor(Fake):
            def list_library_cursor(self,cursor,liked=False):
                if cursor=='same': return ([{'id':'r2','title':'R2','metadata':{}}],'same',True,'fake')
                return ([{'id':'r1','title':'R1','metadata':{}}],'same',True,'fake')
        server.get_client=lambda: RepeatCursor()
        repeat_task=server.TaskState('sync','sync')
        server.sync_library(repeat_task,{'max_pages':5,'include_main':True,'include_workspaces':False,'resume':True})
        repeat_cp=server.DB.get_sync_checkpoint('main:liked=0') or {}
        ok('repeated cursor completes',int(repeat_cp.get('completed') or 0)==1,str(repeat_cp))

        # Quick new-song check must not erase full-sync checkpoints.
        server.DB.save_sync_checkpoint('keep-me','Keep','cursor-x',7,350,'fake',completed=False)
        class NoNew(Fake):
            def list_library_cursor(self,cursor,liked=False): return ([],None,False,'fake')
        server.get_client=lambda: NoNew()
        new_task=server.TaskState('check','check')
        server.check_new_songs(new_task,{'max_pages':1,'include_main':True,'include_workspaces':False,'reset_checkpoints':True})
        ok('check-new preserves sync checkpoints',server.DB.get_sync_checkpoint('keep-me') is not None)

        # A page with only already-seen IDs but an advancing cursor must not hide later new songs.
        server.DB.clear_sync_checkpoints()
        class OverlapPages(Fake):
            def list_library_cursor(self,cursor,liked=False):
                if cursor == 'p2': return ([{'id':'ov1','title':'Overlap 1','metadata':{}},{'id':'ov2','title':'Overlap 2','metadata':{}}], 'p3', True, 'fake')
                if cursor == 'p3': return ([{'id':'ov3','title':'New after overlap','metadata':{}}], None, False, 'fake')
                return ([{'id':'ov1','title':'Overlap 1','metadata':{}},{'id':'ov2','title':'Overlap 2','metadata':{}}], 'p2', True, 'fake')
        server.get_client=lambda: OverlapPages()
        overlap_task=server.TaskState('sync','sync')
        server.sync_library(overlap_task,{'max_pages':5,'include_main':True,'include_workspaces':False,'resume':True})
        ok('overlap page does not truncate',server.DB.get_song('ov3') is not None)

        # ID-only queries must return all filtered songs without full payload transfer.
        ids=server.DB.list_song_ids(filter_name='not_downloaded',limit=20000)
        ok('filtered id list', 'ov3' in ids and 's1' not in ids, str(ids[-10:]))

        # Local update packages are accepted only with a valid 64-char SHA and intact ZIP.
        from advanced_features import download_update, sha256_file
        update_zip=base/'update.zip'
        with zipfile.ZipFile(update_zip,'w') as z: z.writestr('README.txt','test')
        manifest=base/'manifest.json'
        manifest.write_text(json.dumps({'version':'9.9.9','download_url':update_zip.as_uri(),'sha256':sha256_file(update_zip)}),encoding='utf-8')
        upd=download_update(base,manifest.as_uri(),'2.0.0')
        ok('verified atomic update',Path(upd['path']).is_file() and upd.get('verified') is True,str(upd))
        manifest.write_text(json.dumps({'version':'9.9.9','download_url':update_zip.as_uri(),'sha256':'bad'}),encoding='utf-8')
        try:
            download_update(base,manifest.as_uri(),'2.0.0'); rejected=False
        except RuntimeError:
            rejected=True
        ok('invalid update sha rejected',rejected)

        # Private/unlisted YouTube mode only emits an explicit supported browser argument.
        ok('youtube cookie args',audio_match._cookie_args('chrome')==['--cookies-from-browser','chrome'] and audio_match._cookie_args('unknown')==[])
        try:
            server._validated_public_remote_url('https://127.0.0.1/test.mp3'); blocked=False
        except PermissionError:
            blocked=True
        ok('remote proxy blocks loopback',blocked)


        # Minimal legacy database must migrate without losing its song.
        import sqlite3
        legacy=base/'legacy.db'
        conn=sqlite3.connect(legacy)
        conn.execute('CREATE TABLE songs(id TEXT PRIMARY KEY,title TEXT)')
        conn.execute('CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT)')
        conn.execute('INSERT INTO songs(id,title) VALUES(?,?)',('legacy-1','Stara pesma'))
        conn.commit(); conn.close()
        migrated=LibraryDB(legacy)
        migrated_song=migrated.get_song('legacy-1') or {}
        with sqlite3.connect(legacy) as conn:
            columns={row[1] for row in conn.execute('PRAGMA table_info(songs)')}
            integrity=conn.execute('PRAGMA integrity_check').fetchone()[0]
        ok('legacy database migration',migrated_song.get('title')=='Stara pesma' and 'created_at' in columns and integrity=='ok',str((migrated_song,columns,integrity)))

        # ID3v2.4 footer must be skipped, not copied into the audio body.
        from id3 import embed_mp3_metadata
        audio_payload=b'FAKEAUDIOPAYLOAD'*20
        tag_payload=b'0123456789'
        synchsafe=bytes([0,0,0,len(tag_payload)])
        tagged=base/'footer.mp3'
        tagged.write_bytes(b'ID3'+bytes([4,0,0x10])+synchsafe+tag_payload+b'3DI'+bytes([4,0,0x10])+synchsafe+audio_payload)
        plain=base/'plain.mp3'; plain.write_bytes(audio_payload)
        embed_mp3_metadata(tagged,title='Test')
        ok('id3 footer preserved audio',tagged.read_bytes().endswith(audio_payload) and server._media_content_sha256(tagged)==server._media_content_sha256(plain))

    print(json.dumps({'ok':True,'passed':len(checks),'checks':checks},ensure_ascii=False,indent=2))
    return 0

if __name__=='__main__': raise SystemExit(main())
