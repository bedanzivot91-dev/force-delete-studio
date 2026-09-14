from __future__ import annotations

import hashlib
import tempfile
import threading
from pathlib import Path
from types import SimpleNamespace
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "app"))
from recognition_final_fixes import apply


class Task:
    def __init__(self, task_type="sync"):
        self.type = task_type; self.cancel_event = threading.Event(); self.logs=[]
        self.status="running"; self.message=""; self.total=0; self.done=0
    def log(self, message, level="info"): self.logs.append((level,str(message)))
    def set_progress(self, done, total, current=""): self.done=done; self.total=total
    def finish(self, message, status="done"): self.status=status; self.message=str(message)
    def finish_partial(self, message): self.status="partial"; self.message=str(message)


class FakeIndex:
    def __init__(self, ids=None): self.ids=set(ids or []); self.added=[]
    def indexed_song_ids(self): return set(self.ids)
    def add_songs(self, rows):
        rows=list(rows); self.added.extend(rows); self.ids.update(r[0] for r in rows); return len(rows)
    def checkpoint(self): return None
    def prune(self, keep): self.ids.intersection_update(set(keep))


def pack(fp, identity="", size=0, mtime=0.0):
    return {"payload": (",".join(map(str, fp))).encode(), "source_identity": identity, "source_size": size, "source_mtime": mtime}


def unpack(payload):
    text=bytes(payload).decode()
    if text=="corrupt": raise ValueError("corrupt")
    return {"chromaprint":[int(x) for x in text.split(",") if x]}


def base_status():
    return {"ok":True,"songs_total":0,"songs_with_audio":0,"songs_remote_only":0,"songs_indexed":0,"songs_not_indexed":0,"songs_without_any_source":0}


def test_required_repair_is_selective() -> None:
    rows=[{"id":f"song-{i}","title":f"Song {i}","audio_url":f"https://cdn/{i}.mp3"} for i in range(3000)]
    fingerprints={row["id"]:pack([1,2,3]) for row in rows}
    fingerprints["song-1777"]={"payload":b"corrupt","source_identity":"old"}
    calls=[]; index=FakeIndex({row["id"] for row in rows})
    class DB:
        def export_rows(self): return list(rows)
        def get_audio_fingerprint(self,_type,sid,_version): return fingerprints.get(sid)
        def get_song(self,sid): return next((r for r in rows if r["id"]==sid),None)
        def upsert_song(self,item,source_group=""): return True
        def set_setting(self,key,value): return None
    def signature(_type,sid,source,task=None,label="",force=False):
        calls.append(sid); fingerprints[sid]=pack([9,8,7]); return {"chromaprint":[9,8,7]}
    core=SimpleNamespace(
        DB=DB(), AUDIO_MATCH_VERSION="v4", unpack_signature=unpack, song_finder_status=base_status,
        song_finder_index_task=lambda task,options: (_ for _ in ()).throw(AssertionError("legacy full indexer must not run")),
        _song_finder_shortlist=lambda sig,songs:(songs,False), _song_finder_source_cheap=lambda song:(song["audio_url"],True),
        _song_finder_source=lambda song:(song["audio_url"],True), _signature_for_source=signature, get_fingerprint_index=lambda:index,
        get_client=lambda:SimpleNamespace(get_clip=lambda sid:next(r for r in rows if r["id"]==sid)), now_iso=lambda:"now",
        runtime_log=lambda *a,**k:None, source_identity=lambda path:{"identity":"x"},
    )
    apply(core); task=Task("sync")
    core.song_finder_index_task(task,{"finish_task":False,"required_for_recognition":True})
    assert calls==["song-1777"],f"repair touched {len(calls)} songs: {calls[:10]}"
    assert task.total==1,task.total
    assert any("1/1" in msg for _level,msg in task.logs),task.logs


def test_changed_local_file_refreshes_fast_index_before_shortlist() -> None:
    with tempfile.TemporaryDirectory(prefix="stale-fast-index-") as raw:
        path=Path(raw)/"ZIVOT JE TO Remastered.mp3"; path.write_bytes(b"new-remastered-audio")
        cached={"payload":b"1,2,3","source_identity":"old-identity","source_size":1,"source_mtime":1.0}
        index=FakeIndex({"song-1","song-2"}); seen=[]
        class DB:
            def export_rows(self): return [{"id":"song-1","title":"ZIVOT JE TO Remastered","local_audio":str(path)}]
            def get_audio_fingerprint(self,_type,sid,_version): return cached if sid=="song-1" else pack([4,5,6])
        def signature(_type,sid,source,task=None,label="",force=False):
            assert sid=="song-1"; cached["payload"]=b"9,9,9"; return {"chromaprint":[9,9,9]}
        def original_shortlist(upload,songs):
            seen.extend(index.added)
            assert any(row[0]=="song-1" and list(row[1])==[9,9,9] for row in index.added),index.added
            return songs,True
        core=SimpleNamespace(
            DB=DB(), AUDIO_MATCH_VERSION="v4", unpack_signature=unpack, song_finder_status=base_status,
            _song_finder_shortlist=original_shortlist, song_finder_index_task=lambda task,options:None,
            _song_finder_source_cheap=lambda song:(path,False) if song["id"]=="song-1" else (None,False),
            _signature_for_source=signature, get_fingerprint_index=lambda:index,
            source_identity=lambda p:{"identity":hashlib.sha256(Path(p).read_bytes()).hexdigest()}, runtime_log=lambda *a,**k:None,
        )
        apply(core); songs=[{"id":"song-1","title":"ZIVOT JE TO Remastered"},{"id":"song-2","title":"Wrong"}]
        shortlist,used=core._song_finder_shortlist({"chromaprint":[9,9,9]},songs)
        assert used is True and shortlist[0]["id"]=="song-1"
        assert seen,"fast index was not refreshed before original shortlist"


def test_unchanged_local_file_does_not_hash_whole_audio() -> None:
    with tempfile.TemporaryDirectory(prefix="unchanged-fast-index-") as raw:
        path=Path(raw)/"song.mp3"; path.write_bytes(b"unchanged-audio")
        stat=path.stat(); index=FakeIndex({"song-1"}); identity_calls=[]; signature_calls=[]
        cached=pack([1,2,3],identity="local:already-hashed",size=stat.st_size,mtime=stat.st_mtime)
        class DB:
            def export_rows(self): return [{"id":"song-1","local_audio":str(path)}]
            def get_audio_fingerprint(self,_type,sid,_version): return cached
        core=SimpleNamespace(
            DB=DB(), AUDIO_MATCH_VERSION="v4", unpack_signature=unpack, song_finder_status=base_status,
            _song_finder_shortlist=lambda upload,songs:(songs,True), song_finder_index_task=lambda task,options:None,
            _song_finder_source_cheap=lambda song:(path,False), _signature_for_source=lambda *a,**k:signature_calls.append(a) or {"chromaprint":[1,2,3]},
            get_fingerprint_index=lambda:index, source_identity=lambda p:identity_calls.append(p) or {"identity":"should-not-run"}, runtime_log=lambda *a,**k:None,
        )
        apply(core); core._song_finder_shortlist({"chromaprint":[1,2,3]},[{"id":"song-1"}])
        assert identity_calls==[],"unchanged local song was fully SHA-hashed"
        assert signature_calls==[],"unchanged indexed local song was unnecessarily re-fingerprinted"


def test_status_rejects_corrupt_fingerprint_row() -> None:
    rows=[{"id":"good","audio_url":"https://cdn/good.mp3"},{"id":"bad","audio_url":"https://cdn/bad.mp3"}]
    fps={"good":pack([1,2,3]),"bad":{"payload":b"corrupt"}}
    class DB:
        def export_rows(self): return rows
        def get_audio_fingerprint(self,_type,sid,_version): return fps.get(sid)
    core=SimpleNamespace(
        DB=DB(), AUDIO_MATCH_VERSION="v4", unpack_signature=unpack, song_finder_status=lambda:{"ok":True,"songs_indexed":2},
        _song_finder_shortlist=lambda sig,songs:(songs,False), song_finder_index_task=lambda task,options:None,
        _song_finder_source_cheap=lambda song:(song.get("audio_url"),True), _signature_for_source=lambda *a,**k:{"chromaprint":[1]},
        get_fingerprint_index=lambda:FakeIndex(), source_identity=lambda p:{"identity":"x"}, runtime_log=lambda *a,**k:None,
    )
    apply(core); status=core.song_finder_status()
    assert status["songs_indexed"]==1,status
    assert status["songs_not_indexed"]==1,status
    assert status["songs_with_audio"]==2,status


def main() -> None:
    test_required_repair_is_selective()
    test_changed_local_file_refreshes_fast_index_before_shortlist()
    test_unchanged_local_file_does_not_hash_whole_audio()
    test_status_rejects_corrupt_fingerprint_row()
    print("recognition_final_fixes_test: PASS — selective repair, pre-shortlist refresh, O(1) unchanged checks and truthful status")


if __name__=="__main__": main()
