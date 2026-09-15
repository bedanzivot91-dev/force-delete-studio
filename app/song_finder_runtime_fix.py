from __future__ import annotations

"""Runtime correctness fixes for Suno song recognition and related state reporting."""

import re
from pathlib import Path
from typing import Any

from atomic_delete_fixes import apply as _apply_atomic_delete_fixes


class _DeferredFinishTask:
    def __init__(self, task: Any):
        self._task = task
        self.final_message = ""
        self.final_status = "done"
    def __getattr__(self, name: str) -> Any: return getattr(self._task, name)
    def finish(self, message: str, status: str = "done") -> None: self.final_message=str(message); self.final_status=str(status or "done")
    def finish_partial(self, message: str) -> None: self.final_message=str(message); self.final_status="partial"


def _task_cancelled(task: Any) -> bool:
    event=getattr(task,"cancel_event",None)
    if event is None or not hasattr(event,"is_set"): return False
    try: return bool(event.is_set())
    except Exception: return False


def _finish_task(task: Any, message: str, status: str = "done") -> None:
    if status=="partial" and hasattr(task,"finish_partial"): task.finish_partial(message); return
    if status=="error" and hasattr(task,"fail"): task.fail(message); return
    if hasattr(task,"finish"):
        try: task.finish(message,status=status)
        except TypeError: task.finish(message)


def _sync_message_has_failures(message: str) -> bool:
    text=str(message or "")
    for pattern in (r"greške\s+(\d+)",r"Neuspešni izvori:\s*(\d+)",r"Nezavršeni izvori:\s*(\d+)"):
        match=re.search(pattern,text,flags=re.IGNORECASE)
        if match and int(match.group(1))>0: return True
    return False


def _fingerprint_is_usable(core: Any, cached: Any) -> bool:
    if not cached or not cached.get("payload"): return False
    try: signature=core.unpack_signature(cached.get("payload") or b"")
    except Exception: return False
    return isinstance(signature,dict) and bool(signature.get("chromaprint"))


def _pending_index_count(core: Any) -> int:
    pending=0
    try: rows=core.DB.export_rows()
    except Exception: return 0
    for song in rows:
        song_id=str(song.get("id") or "").strip()
        if not song_id: continue
        try: cached=core.DB.get_audio_fingerprint("suno",song_id,core.AUDIO_MATCH_VERSION)
        except Exception: cached=None
        if cached and _fingerprint_is_usable(core,cached): continue
        try: source,_is_remote=core._song_finder_source_cheap(song)
        except Exception: source=None
        if source or not song_id.startswith(("local-","recognized-")): pending+=1
    return pending


def _install_oauth_removal_guard(core: Any) -> dict[str, Any]:
    manager=getattr(core,"YOUTUBE_OAUTH",None)
    if manager is None: return {}
    cls=type(manager)
    if getattr(cls,"_sps_truthful_remove_config_v1",False): return {"oauth_removal_guard_installed":True}
    original=cls.remove_client_config
    def remove_client_config_truthful(self: Any) -> None:
        original(self)
        remaining=[]
        for raw in (getattr(self,"config_path",None),getattr(self,"tokens_path",None)):
            if raw is None: continue
            path=Path(raw)
            if path.exists(): remaining.append(str(path))
        if remaining:
            error_type=getattr(core,"YouTubeOAuthError",RuntimeError)
            raise error_type("Google OAuth podaci nisu potpuno uklonjeni sa diska: " + " | ".join(remaining))
    cls.remove_client_config=remove_client_config_truthful
    cls._sps_truthful_remove_config_v1=True
    return {"oauth_removal_guard_installed":True}


def _install_sync_auto_index(core: Any) -> dict[str, Any]:
    if getattr(core,"_sync_auto_index_v1",False): return {"sync_auto_index_installed":True}
    if not all(hasattr(core,name) for name in ("sync_library","check_new_songs","song_finder_status","song_finder_index_task")): return {}
    previous_sync=core.sync_library; previous_check_new=core.check_new_songs
    def run_with_index(previous: Any,task: Any,options: dict[str,Any]|None=None)->Any:
        proxy=_DeferredFinishTask(task); result=previous(proxy,dict(options or {}))
        if _task_cancelled(task): _finish_task(task,proxy.final_message or "Sinhronizacija je zaustavljena.","cancelled"); return result
        source_partial=proxy.final_status=="partial" or _sync_message_has_failures(proxy.final_message)
        status_before=core.song_finder_status(); missing_before=max(int(status_before.get("songs_not_indexed") or 0),_pending_index_count(core))
        if missing_before>0:
            if hasattr(task,"log"): task.log(f"Suno baza je osvežena, ali {missing_before} pesama još nema ispravan audio otisak. Automatski dopunjujem ili popravljam indeks pre završetka sinhronizacije.","warning")
            core.song_finder_index_task(task,{"force":False,"finish_task":False,"required_for_recognition":True})
        status_after=core.song_finder_status(); missing_after=max(int(status_after.get("songs_not_indexed") or 0),_pending_index_count(core)); no_source=int(status_after.get("songs_without_any_source") or 0); base=proxy.final_message or "Sinhronizacija Suno biblioteke je završena."
        if missing_after>0: _finish_task(task,base+f" Audio indeks NIJE kompletan: {missing_after} pesama još nema upotrebljiv fingerprint"+(f", a {no_source} trenutno nema ni lokalni ni sačuvani Suno audio izvor." if no_source else "."),"partial")
        elif source_partial: _finish_task(task,base+" Audio indeks je kompletan, ali Suno sinhronizacija je završena uz greške.","partial")
        else: _finish_task(task,base+" Audio indeks je kompletan.","done")
        return result
    def sync_library_with_index(task:Any,options:dict[str,Any]|None=None)->Any: return run_with_index(previous_sync,task,options)
    def check_new_with_index(task:Any,options:dict[str,Any]|None=None)->Any: return run_with_index(previous_check_new,task,options)
    core.sync_library=sync_library_with_index; core.check_new_songs=check_new_with_index; core._sync_auto_index_v1=True
    return {"sync_library":sync_library_with_index,"check_new_songs":check_new_with_index,"sync_auto_index_installed":True}


def apply(core: Any) -> dict[str, Any]:
    atomic_exports={}; sync_exports={}; oauth_exports=_install_oauth_removal_guard(core)
    if hasattr(core,"DB"):
        atomic_exports=_apply_atomic_delete_fixes(core); sync_exports=_install_sync_auto_index(core)
    original_candidates=core._song_finder_candidates
    if getattr(core,"_song_finder_fresh_local_fp_v1",False): return {"_song_finder_candidates":core._song_finder_candidates,**atomic_exports,**sync_exports,**oauth_exports}
    def candidates_with_fresh_local_fingerprints(upload_signatures,songs):
        for song in songs:
            song_id=str(song.get("id") or "").strip()
            if not song_id: continue
            try: source,is_remote=core._song_finder_source_cheap(song)
            except Exception: source,is_remote=None,False
            if not source or is_remote: continue
            try:
                path=Path(str(source)).expanduser()
                if not path.exists() or not path.is_file(): continue
                core._signature_for_source("suno",song_id,path,None,str(song.get("title") or song.get("display_name") or song_id),False)
            except Exception as exc: core.runtime_log(f"Pronalazac: lokalni otisak nije osvezen za {song_id}: {exc}","warning")
        return original_candidates(upload_signatures,songs)
    core._song_finder_candidates=candidates_with_fresh_local_fingerprints; core._song_finder_fresh_local_fp_v1=True
    return {"_song_finder_candidates":candidates_with_fresh_local_fingerprints,**atomic_exports,**sync_exports,**oauth_exports}
