from __future__ import annotations

import sys
import threading
import types
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "app"))

from song_finder_runtime_fix import _install_sync_auto_index


class Task:
    def __init__(self) -> None:
        self.cancel_event = threading.Event()
        self.status = "running"
        self.message = ""
        self.logs: list[tuple[str, str]] = []
    def log(self, message: str, level: str = "info") -> None: self.logs.append((level, str(message)))
    def finish(self, message: str, status: str = "done") -> None: self.status = status; self.message = str(message)
    def finish_partial(self, message: str) -> None: self.status = "partial"; self.message = str(message)


def assert_required_index(options: dict) -> None:
    assert options.get("finish_task") is False, options
    assert options.get("required_for_recognition") is True, options


def test_sync_waits_for_missing_fingerprints() -> None:
    state={"missing":3,"index_calls":0,"sync_finish_seen":False}
    def original_sync(task,_options): task.finish("Suno sync završen"); state["sync_finish_seen"]=True
    def status(): return {"songs_not_indexed":state["missing"],"songs_without_any_source":0}
    def index_task(_task,options): assert_required_index(options); state["index_calls"]+=1; state["missing"]=0
    core=types.SimpleNamespace(sync_library=original_sync,check_new_songs=original_sync,song_finder_status=status,song_finder_index_task=index_task)
    _install_sync_auto_index(core); task=Task(); core.sync_library(task,{})
    assert state["sync_finish_seen"] and state["index_calls"]==1
    assert task.status=="done",task.status
    assert "Audio indeks je kompletan" in task.message,task.message


def test_sync_is_partial_when_index_stays_incomplete() -> None:
    state={"missing":2,"index_calls":0}
    def original_sync(task,_options): task.finish("Suno sync završen")
    def status(): return {"songs_not_indexed":state["missing"],"songs_without_any_source":1}
    def index_task(_task,options): assert_required_index(options); state["index_calls"]+=1
    core=types.SimpleNamespace(sync_library=original_sync,check_new_songs=original_sync,song_finder_status=status,song_finder_index_task=index_task)
    _install_sync_auto_index(core); task=Task(); core.sync_library(task,{})
    assert state["index_calls"]==1
    assert task.status=="partial",task.status
    assert "NIJE kompletan" in task.message and "2 pesama" in task.message,task.message


def test_suno_row_without_cached_audio_url_still_triggers_index_refresh() -> None:
    state={"indexed":False,"index_calls":0}
    class FakeDB:
        def export_rows(self): return [{"id":"9fcb6ad1-1111-4444-9999-0123456789ab","title":"Stara Suno pesma","audio_url":""}]
        def get_audio_fingerprint(self,source_type,song_id,version): assert source_type=="suno"; return {"payload":b"ok"} if state["indexed"] else None
    def original_sync(task,_options): task.finish("Suno sync završen")
    def status(): return {"songs_not_indexed":0,"songs_without_any_source":1}
    def index_task(_task,options): assert_required_index(options); state["index_calls"]+=1; state["indexed"]=True
    core=types.SimpleNamespace(DB=FakeDB(),AUDIO_MATCH_VERSION="v4",unpack_signature=lambda payload:{"chromaprint":[1,2,3]} if payload==b"ok" else {},_song_finder_source_cheap=lambda _song:(None,False),sync_library=original_sync,check_new_songs=original_sync,song_finder_status=status,song_finder_index_task=index_task)
    _install_sync_auto_index(core); task=Task(); core.sync_library(task,{})
    assert state["index_calls"]==1 and task.status=="done"


def test_corrupt_cached_fingerprint_is_not_counted_as_complete() -> None:
    state={"repaired":False,"index_calls":0}
    class FakeDB:
        def export_rows(self): return [{"id":"8acb6ad1-2222-4444-9999-0123456789ab","title":"Suno pesma","audio_url":"https://cdn.suno.test/song.mp3"}]
        def get_audio_fingerprint(self,_source_type,_song_id,_version): return {"payload":b"good" if state["repaired"] else b"corrupt"}
    def original_sync(task,_options): task.finish("Suno sync završen")
    def index_task(_task,options): assert_required_index(options); state["index_calls"]+=1; state["repaired"]=True
    core=types.SimpleNamespace(DB=FakeDB(),AUDIO_MATCH_VERSION="v4",unpack_signature=lambda payload:({"chromaprint":[9,8,7]} if payload==b"good" else (_ for _ in ()).throw(ValueError("bad payload"))),_song_finder_source_cheap=lambda _song:("https://cdn.suno.test/song.mp3",True),sync_library=original_sync,check_new_songs=original_sync,song_finder_status=lambda:{"songs_not_indexed":0,"songs_without_any_source":0},song_finder_index_task=index_task)
    _install_sync_auto_index(core); task=Task(); core.sync_library(task,{})
    assert state["index_calls"]==1 and task.status=="done"


def test_core_sync_summary_with_errors_cannot_finish_green() -> None:
    def original_sync(task,_options):
        task.finish("Sinhronizacija završena: pročitano 50, uspešno obrađeno 49, novih 3, greške 1; u bazi je ukupno 3000 pesama. Nezavršeni izvori: 1 — pogledaj Dnevnik.")
    core=types.SimpleNamespace(sync_library=original_sync,check_new_songs=original_sync,song_finder_status=lambda:{"songs_not_indexed":0,"songs_without_any_source":0},song_finder_index_task=lambda *_a,**_k:None)
    _install_sync_auto_index(core); task=Task(); core.sync_library(task,{})
    assert task.status=="partial",task.status
    assert "završena uz greške" in task.message,task.message


def main() -> None:
    test_sync_waits_for_missing_fingerprints()
    test_sync_is_partial_when_index_stays_incomplete()
    test_suno_row_without_cached_audio_url_still_triggers_index_refresh()
    test_corrupt_cached_fingerprint_is_not_counted_as_complete()
    test_core_sync_summary_with_errors_cannot_finish_green()
    print("song_finder_sync_auto_index_test: PASS — sync forces real indexing and never hides Suno/fingerprint failures")


if __name__ == "__main__": main()
