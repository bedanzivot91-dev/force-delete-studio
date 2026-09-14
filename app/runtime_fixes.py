from __future__ import annotations

"""Production hot-path fixes for Suno Pesme Studio 3.3.2.

This module deliberately patches the mature ``server_core`` at startup instead
of duplicating the 300k+ core file. Every fix here targets a failure seen on a
large real library / Windows desktop while preserving correctness fallbacks.
"""

import os
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from typing import Any

import youtube_tools as _youtube_tools


_DISCONNECT_WINERRORS = {10053, 10054, 10058}
_DISCONNECT_ERRNOS = {32, 54, 104}


def _is_client_disconnect(exc: BaseException) -> bool:
    if isinstance(exc, (BrokenPipeError, ConnectionResetError, ConnectionAbortedError)):
        return True
    if not isinstance(exc, OSError):
        return False
    return (
        getattr(exc, "winerror", None) in _DISCONNECT_WINERRORS
        or getattr(exc, "errno", None) in _DISCONNECT_ERRNOS
    )


def _install_http_disconnect_guards(core: Any) -> None:
    handler = core.Handler
    if getattr(handler, "_runtime_disconnect_guard_v1", False):
        return
    original_json = handler._send_json
    original_bytes = handler._send_bytes
    original_file = handler._send_file

    def safe_json(self: Any, *args: Any, **kwargs: Any) -> Any:
        try:
            return original_json(self, *args, **kwargs)
        except BaseException as exc:
            if _is_client_disconnect(exc): return None
            raise
    def safe_bytes(self: Any, *args: Any, **kwargs: Any) -> Any:
        try:
            return original_bytes(self, *args, **kwargs)
        except BaseException as exc:
            if _is_client_disconnect(exc): return None
            raise
    def safe_file(self: Any, *args: Any, **kwargs: Any) -> Any:
        try:
            return original_file(self, *args, **kwargs)
        except BaseException as exc:
            if _is_client_disconnect(exc): return None
            raise
    handler._send_json = safe_json
    handler._send_bytes = safe_bytes
    handler._send_file = safe_file
    handler._runtime_disconnect_guard_v1 = True


class _SongLookup:
    __slots__ = ("songs", "normalized", "tokens", "token_map", "exact_map", "durations")
    def __init__(self, songs: list[dict[str, Any]]):
        self.songs = songs; self.normalized=[]; self.tokens=[]; self.token_map={}; self.exact_map={}; self.durations=[]
        for index, song in enumerate(songs):
            normalized = _youtube_tools.normalize_title(str(song.get("title") or ""))
            tokens = {token for token in normalized.split() if len(token) >= 3}
            self.normalized.append(normalized); self.tokens.append(tokens); self.durations.append(float(song.get("duration") or 0))
            if normalized: self.exact_map.setdefault(normalized, set()).add(index)
            for token in tokens: self.token_map.setdefault(token, set()).add(index)

_LOOKUP_LOCK=threading.RLock(); _LOOKUP_KEY=None; _LOOKUP_VALUE=None

def _song_lookup(songs):
    global _LOOKUP_KEY,_LOOKUP_VALUE
    first=str(songs[0].get("id") or "") if songs else ""; last=str(songs[-1].get("id") or "") if songs else ""
    key=(id(songs),len(songs),first,last)
    with _LOOKUP_LOCK:
        if _LOOKUP_KEY==key and _LOOKUP_VALUE is not None: return _LOOKUP_VALUE
        value=_SongLookup(songs); _LOOKUP_KEY=key; _LOOKUP_VALUE=value; return value


def _quick_song_pool(video, songs, limit=180):
    if not songs: return []
    lookup=_song_lookup(songs); video_title=_youtube_tools.normalize_title(str(video.get("title") or "")); video_tokens={t for t in video_title.split() if len(t)>=3}; video_duration=float(video.get("duration") or 0)
    indices=set()
    if video_title: indices.update(lookup.exact_map.get(video_title,()))
    for token in video_tokens: indices.update(lookup.token_map.get(token,()))
    if video_duration>1:
        nearest=sorted((i for i,d in enumerate(lookup.durations) if d>1), key=lambda i:abs(lookup.durations[i]-video_duration))[:max(40,min(100,limit//2))]; indices.update(nearest)
    if not indices: indices.update(range(min(len(songs),max(40,limit//2))))
    ranked=[]
    for index in indices:
        song_title=lookup.normalized[index]; song_tokens=lookup.tokens[index]; score=0.0
        if song_title and video_title:
            if song_title==video_title: score+=120
            elif song_title in video_title or video_title in song_title: score+=95
        if song_tokens and video_tokens:
            overlap=len(song_tokens & video_tokens)
            if overlap: score += 60.0*overlap/max(1,len(song_tokens)) + min(20.0,overlap*4.0)
        duration=lookup.durations[index]
        if duration>1 and video_duration>1: score += max(0.0,1.0-abs(duration-video_duration)/max(duration,video_duration))*35.0
        ranked.append((score,index))
    ranked.sort(key=lambda row:row[0],reverse=True)
    return [songs[i] for _,i in ranked[:max(20,min(int(limit),400))]]


def _install_fast_youtube_metadata(core):
    original_best=core._best_song_match; original_candidates=core._metadata_candidates_for_video
    def fast_best_song_match(video,songs,owned_ids):
        if len(songs)<=500: return original_best(video,songs,owned_ids)
        best_song=None; best_match=None
        for song in _quick_song_pool(video,songs,180):
            match=core.match_song_to_video(song,video,owned_ids)
            if best_match is None or float(match.get("score") or 0)>float(best_match.get("score") or 0): best_song,best_match=song,match
        if best_match is not None and float(best_match.get("score") or 0)>=82: return best_song,best_match
        return original_best(video,songs,owned_ids)
    def fast_metadata_candidates(video,songs,owned_ids,limit=12,deep=False):
        if len(songs)<=500 or (deep and len(songs)<=120): return original_candidates(video,songs,owned_ids,limit,deep)
        candidate_limit=max(4,int(limit)); pool=_quick_song_pool(video,songs,max(120,candidate_limit*10)); scored=[]; used=set()
        for song in pool:
            sid=str(song.get("id") or "")
            if not sid or sid in used: continue
            match=core.match_song_to_video(song,video,owned_ids); scored.append((float(match.get("score") or 0),song,match)); used.add(sid)
        for song in core.closest_duration_candidates(songs,float(video.get("duration") or 0),limit=max(8,candidate_limit)):
            sid=str(song.get("id") or "")
            if not sid or sid in used: continue
            match=core.match_song_to_video(song,video,owned_ids); scored.append((float(match.get("score") or 0),song,match)); used.add(sid)
        scored.sort(key=lambda x:x[0],reverse=True); return [(s,m) for _,s,m in scored[:candidate_limit]]
    core._best_song_match=fast_best_song_match; core._metadata_candidates_for_video=fast_metadata_candidates
    return {"_best_song_match":fast_best_song_match,"_metadata_candidates_for_video":fast_metadata_candidates}


def _install_incremental_fingerprint_seeding(core):
    original_signature=core._signature_for_source; indexed_lock=threading.RLock(); indexed_ids=None
    def signature_for_source(source_type,source_id,source,task=None,label="",force=False):
        nonlocal indexed_ids
        signature=original_signature(source_type,source_id,source,task,label,force)
        if source_type!="suno" or (task is not None and getattr(task,"type","")=="song_finder_index"): return signature
        chromaprint=list(signature.get("chromaprint") or []); song_id=str(source_id or "")
        if not song_id or not chromaprint: return signature
        try:
            index=core.get_fingerprint_index()
            if index is None: return signature
            with indexed_lock:
                if indexed_ids is None: indexed_ids=set(index.indexed_song_ids())
                if song_id in indexed_ids: return signature
                index.add_songs([(song_id,chromaprint,core.AUDIO_MATCH_VERSION)]); indexed_ids.add(song_id)
        except Exception as exc: core.runtime_log(f"Brzi audio indeks nije dopunjen za {song_id}: {exc}","warning")
        return signature
    core._signature_for_source=signature_for_source; return {"_signature_for_source":signature_for_source}


def _install_scalable_song_indexer(core: Any) -> dict[str, Any]:
    """Keep full indexing explicit and scalable, but never skip a correctness-required recognition repair."""
    def scalable_song_finder_index_task(task: Any, options: dict[str, Any]) -> None:
        finish_task=bool(options.get("finish_task",True))
        required_for_youtube=bool(options.get("required_for_youtube",False))
        required_for_recognition=bool(options.get("required_for_recognition",False))
        if not finish_task and not required_for_youtube and not required_for_recognition:
            status=core.song_finder_status()
            task.log(f"Kompletan audio indeks nije zatražen. Nedostaje {int(status.get('songs_not_indexed') or 0)} otisaka; kandidati će se indeksirati usput.","info")
            return
        force=bool(options.get("force")); all_songs=core.DB.export_rows(); task.total=len(all_songs)
        if not all_songs:
            if finish_task: task.finish("Biblioteka je prazna; nema pesama za indeksiranje.")
            else: task.log("Biblioteka je prazna; nema pesama za indeksiranje.","warning")
            return
        ok=failed=completed=unavailable=remote_count=reused_without_source=0; lock=threading.RLock(); fast_index=core.get_fingerprint_index(); pending_index=[]; indexed_rows=0
        def flush_index(force_flush=False):
            nonlocal pending_index,indexed_rows
            if fast_index is None: return
            with lock:
                if not pending_index or (len(pending_index)<100 and not force_flush): return
                batch,pending_index=pending_index,[]
            try: indexed_rows += fast_index.add_songs(batch)
            except Exception as exc: task.log(f"Brzi indeks nije upisan za {len(batch)} pesama: {exc}","warning")
        def index_one(song):
            nonlocal ok,failed,completed,unavailable,remote_count,reused_without_source
            if task.cancel_event.is_set(): return
            core.wait_if_paused(task); sid=str(song.get("id") or ""); title=str(song.get("title") or song.get("display_name") or sid)
            source,is_remote=core._song_finder_source_cheap(song); cached=None if force else core.DB.get_audio_fingerprint("suno",sid,core.AUDIO_MATCH_VERSION)
            try:
                usable_cached=False
                if cached and source is None:
                    try:
                        signature=core.unpack_signature(cached.get("payload") or b""); usable_cached=bool(signature.get("chromaprint"))
                    except Exception: usable_cached=False
                    if usable_cached:
                        with lock: reused_without_source += 1
                if not usable_cached:
                    if source is None: source,is_remote=core._song_finder_source(song)
                    if source is None:
                        with lock: unavailable += 1
                        return
                    signature=core._signature_for_source("suno",sid,source,task,title,force=force or bool(cached))
                chromaprint=list(signature.get("chromaprint") or [])
                if not chromaprint: raise RuntimeError("audio fingerprint nema Chromaprint podatke")
                with lock:
                    pending_index.append((sid,chromaprint,core.AUDIO_MATCH_VERSION)); ok += 1
                    if is_remote: remote_count += 1
            except Exception as exc:
                with lock: failed += 1
                task.log(f"{title}: {exc}","warning")
            finally:
                with lock:
                    completed += 1; task.set_progress(completed,len(all_songs),title)
                flush_index()
        cpu=max(2,int(os.cpu_count() or 4)); default_workers=min(6,max(3,cpu//2)); workers=max(1,min(int(options.get("parallelism") or default_workers),8))
        task.log(f"Indeksiranje {len(all_songs)} pesama: {workers} paralelna radnika.","info")
        with ThreadPoolExecutor(max_workers=workers,thread_name_prefix="song-finder-index") as pool:
            for _ in pool.map(index_one,all_songs):
                if task.cancel_event.is_set(): break
        flush_index(True)
        if fast_index is not None:
            try: fast_index.prune({str(song.get("id") or "") for song in all_songs}); fast_index.checkpoint()
            except Exception as exc: task.log(f"Brzi indeks nije očišćen: {exc}","warning")
        core.DB.set_setting("song_finder_last_index_at",core.now_iso())
        summary=f"Indeksiranje završeno: {ok} obrađeno, {failed} neuspešno, {unavailable} bez dostupnog audio izvora." + (f" {remote_count} obrađeno direktno sa Suno servera." if remote_count else "") + (f" {reused_without_source} postojećih otisaka ponovo iskorišćeno bez mreže." if reused_without_source else "") + (f" Brzi indeks: {indexed_rows} otisaka." if indexed_rows else "")
        if finish_task:
            if task.cancel_event.is_set(): task.finish_partial(summary+" Posao je zaustavljen; sledeći put nastavlja ono što nedostaje.")
            elif failed or unavailable: task.finish_partial(summary)
            else: task.finish(summary)
        else: task.log(summary,"warning" if failed or unavailable else "success")
    core.song_finder_index_task=scalable_song_finder_index_task
    return {"song_finder_index_task":scalable_song_finder_index_task}


def _install_youtube_audio_defaults(core):
    original=core.analyze_owned_youtube_audio
    def wrapped(task,options):
        patched=dict(options or {}); patched.setdefault("max_pages",100)
        if "max_videos_per_channel" not in patched: patched["max_videos_per_channel"]=5000 if str(patched.get("scan_mode") or "new")=="full" else 500
        return original(task,patched)
    core.analyze_owned_youtube_audio=wrapped; return {"analyze_owned_youtube_audio":wrapped}


def _install_lightweight_youtube_connect_pipeline(core):
    def start_automatic_youtube_pipeline(delay_seconds=1.5):
        def orchestrate():
            if delay_seconds>0: time.sleep(delay_seconds)
            with core.STATE_LOCK:
                active=core.ACTIVE_TASK
                if active is not None and getattr(active,"status","")=="running":
                    core.runtime_log("Automatska YouTube provera je preskočena jer je drugi posao već u toku; kanal je ipak povezan.","info"); return
            options={"max_pages":100,"include_private_unlisted":True,"threshold":68,"scan_mode":"new"}
            try: core.start_task("youtube_owned","Automatsko skeniranje povezanih YouTube kanala",lambda task:core.scan_owned_youtube_channels(task,options),persistent_payload=options)
            except Exception as exc: core.runtime_log(f"Automatska YouTube metadata provera nije pokrenuta: {exc}","warning")
        threading.Thread(target=orchestrate,daemon=True,name="youtube-auto-metadata").start()
    core.start_automatic_youtube_pipeline=start_automatic_youtube_pipeline; return {"start_automatic_youtube_pipeline":start_automatic_youtube_pipeline}


def apply(core: Any) -> dict[str, Any]:
    if getattr(core,"_runtime_fixes_343_installed",False): return {}
    exports={}; _install_http_disconnect_guards(core); exports.update(_install_fast_youtube_metadata(core)); exports.update(_install_incremental_fingerprint_seeding(core)); exports.update(_install_scalable_song_indexer(core)); exports.update(_install_youtube_audio_defaults(core)); exports.update(_install_lightweight_youtube_connect_pipeline(core)); core._runtime_fixes_343_installed=True
    core.runtime_log("Runtime fixes 343 aktivni: localhost disconnect guard, skalabilni YouTube matcher, kontrolisani audio indeks.","info"); exports["Handler"]=core.Handler; return exports
