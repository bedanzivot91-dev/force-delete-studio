from __future__ import annotations

"""Final correctness guards for the local/YouTube song-recognition index.

Loaded after the mature runtime layers. It fixes cross-layer cases that unit
-testing the individual layers could miss:
* repair only missing/corrupt fingerprints, not thousands of valid songs;
* refresh changed local MP3/WAV fingerprints before the fast shortlist;
* use cheap stat metadata for unchanged local files so every search does not
  SHA-256 the entire local music library.
"""

import os
import threading
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from typing import Any


def _usable_signature(core: Any, cached: Any) -> dict[str, Any] | None:
    if not cached or not cached.get("payload"):
        return None
    try:
        signature = core.unpack_signature(cached.get("payload") or b"")
    except Exception:
        return None
    if not isinstance(signature, dict) or not signature.get("chromaprint"):
        return None
    return signature


def _local_identity_changed(core: Any, song: dict[str, Any], path: Path, cached: Any) -> bool:
    """Cheaply reject unchanged files before the expensive SHA-256 fallback.

    audio_fingerprints stores the exact size and mtime used when the signature
    was created. Those two stat fields are enough to know that an unchanged
    file does not need to be hashed again. If either differs (or this is a
    legacy fingerprint without stat metadata), the normal signature path will
    calculate the definitive SHA only for that changed/suspicious file.
    """
    if not cached:
        return True
    expected_identity = str(cached.get("source_identity") or "")
    if not expected_identity:
        return True
    try:
        stat = path.stat()
        old_size = int(cached.get("source_size") or 0)
        old_mtime = float(cached.get("source_mtime") or 0.0)
        if old_size > 0 and old_mtime > 0:
            return stat.st_size != old_size or abs(stat.st_mtime - old_mtime) > 1e-6
        # Legacy row: one definitive SHA is necessary. Once regenerated the
        # stored size+mtime make all subsequent searches O(1) for this file.
        current = core.source_identity(path)
        return str(current.get("identity") or "") != expected_identity
    except Exception:
        return True


def _install_pre_shortlist_local_refresh(core: Any) -> dict[str, Any]:
    if getattr(core, "_pre_shortlist_local_refresh_v1", False):
        return {"pre_shortlist_local_refresh_installed": True}
    original = core._song_finder_shortlist

    def refreshed_shortlist(upload_signature: dict[str, Any], songs: list[dict[str, Any]]):
        index = core.get_fingerprint_index()
        indexed = set(index.indexed_song_ids()) if index is not None else set()
        changed: list[tuple[str, list[int], str]] = []
        for song in songs:
            song_id = str(song.get("id") or "").strip()
            if not song_id:
                continue
            try:
                source, is_remote = core._song_finder_source_cheap(song)
            except Exception:
                continue
            if not source or is_remote:
                continue
            path = Path(str(source)).expanduser()
            if not path.is_file():
                continue
            cached = core.DB.get_audio_fingerprint("suno", song_id, core.AUDIO_MATCH_VERSION)
            usable = _usable_signature(core, cached)
            needs_refresh = usable is None or _local_identity_changed(core, song, path, cached)
            if not needs_refresh and song_id in indexed:
                continue
            try:
                signature = core._signature_for_source(
                    "suno", song_id, path, None,
                    str(song.get("title") or song.get("display_name") or song_id),
                    force=(usable is None and bool(cached)),
                )
                chromaprint = list(signature.get("chromaprint") or [])
                if chromaprint and index is not None:
                    changed.append((song_id, chromaprint, core.AUDIO_MATCH_VERSION))
            except Exception as exc:
                core.runtime_log(f"Pronalazac: lokalni fast-indeks nije osvezen za {song_id}: {exc}", "warning")
        if changed and index is not None:
            try:
                index.add_songs(changed)
                index.checkpoint()
            except Exception as exc:
                core.runtime_log(f"Pronalazac: upis osvezenog fast-indeksa nije uspeo: {exc}", "warning")
        return original(upload_signature, songs)

    core._song_finder_shortlist = refreshed_shortlist
    core._pre_shortlist_local_refresh_v1 = True
    return {"_song_finder_shortlist": refreshed_shortlist, "pre_shortlist_local_refresh_installed": True}


def _install_selective_required_indexer(core: Any) -> dict[str, Any]:
    if getattr(core, "_selective_required_index_v1", False):
        return {"selective_required_index_installed": True}
    previous = core.song_finder_index_task

    def selective_index(task: Any, options: dict[str, Any]) -> None:
        opts = dict(options or {})
        required = bool(opts.get("required_for_recognition")) or bool(opts.get("required_for_youtube"))
        if getattr(task, "type", "") in {"youtube_audio_owned", "youtube_audio_url", "youtube_audio_one"}:
            required = True
            opts["required_for_youtube"] = True
        if not required or bool(opts.get("force")):
            return previous(task, opts)

        all_songs = core.DB.export_rows()
        pending: list[dict[str, Any]] = []
        for song in all_songs:
            sid = str(song.get("id") or "").strip()
            if not sid:
                continue
            cached = core.DB.get_audio_fingerprint("suno", sid, core.AUDIO_MATCH_VERSION)
            if _usable_signature(core, cached) is None:
                pending.append(song)
        task.total = len(pending)
        if not pending:
            if hasattr(task, "log"):
                task.log("Audio indeks je već kompletan; nema otisaka za popravku.", "success")
            return

        fast_index = core.get_fingerprint_index()
        write_lock = threading.RLock()
        pending_fast: list[tuple[str, list[int], str]] = []
        counters = {"ok": 0, "failed": 0, "unavailable": 0, "done": 0}

        def flush(force: bool = False) -> None:
            if fast_index is None:
                return
            with write_lock:
                if not pending_fast or (len(pending_fast) < 50 and not force):
                    return
                batch = list(pending_fast)
                pending_fast.clear()
            try:
                fast_index.add_songs(batch)
            except Exception as exc:
                task.log(f"Brzi indeks nije upisan za {len(batch)} popravljenih pesama: {exc}", "warning")

        def one(song: dict[str, Any]) -> None:
            sid = str(song.get("id") or "")
            title = str(song.get("title") or song.get("display_name") or sid)
            try:
                source, is_remote = core._song_finder_source_cheap(song)
                if is_remote and not sid.startswith(("local-", "recognized-")):
                    try:
                        detail = core.get_client().get_clip(sid)
                        if isinstance(detail, dict):
                            core.DB.upsert_song(detail, source_group=str(song.get("source_group") or "Suno"))
                            song = core.DB.get_song(sid) or song
                            source, is_remote = core._song_finder_source_cheap(song)
                    except Exception as refresh_exc:
                        task.log(f"{title}: Suno audio adresa nije osvežena ({refresh_exc}); pokušavam postojeći izvor.", "warning")
                if source is None:
                    source, is_remote = core._song_finder_source(song)
                if source is None:
                    with write_lock:
                        counters["unavailable"] += 1
                    return
                signature = core._signature_for_source("suno", sid, source, task, title, force=True)
                chromaprint = list(signature.get("chromaprint") or [])
                if not chromaprint:
                    raise RuntimeError("novi fingerprint nema Chromaprint podatke")
                with write_lock:
                    pending_fast.append((sid, chromaprint, core.AUDIO_MATCH_VERSION))
                    counters["ok"] += 1
            except Exception as exc:
                with write_lock:
                    counters["failed"] += 1
                task.log(f"{title}: {exc}", "warning")
            finally:
                with write_lock:
                    counters["done"] += 1
                    task.set_progress(counters["done"], len(pending), title)
                flush()

        workers = max(1, min(int(opts.get("parallelism") or max(2, min(4, int(os.cpu_count() or 4) // 2))), 6))
        task.log(f"Selektivno popravljam {len(pending)} nedostajućih/oštećenih otisaka; validne pesme ne diram.", "info")
        with ThreadPoolExecutor(max_workers=workers, thread_name_prefix="recognition-repair") as pool:
            list(pool.map(one, pending))
        flush(True)
        if fast_index is not None:
            try:
                fast_index.prune({str(s.get("id") or "") for s in all_songs})
                fast_index.checkpoint()
            except Exception as exc:
                task.log(f"Brzi indeks nije završno očišćen: {exc}", "warning")
        core.DB.set_setting("song_finder_last_index_at", core.now_iso())
        summary = (
            f"Selektivna popravka indeksa: {counters['ok']}/{len(pending)} popravljeno, "
            f"{counters['failed']} grešaka, {counters['unavailable']} bez dostupnog audio izvora."
        )
        if bool(opts.get("finish_task", True)):
            if counters["failed"] or counters["unavailable"]:
                task.finish_partial(summary)
            else:
                task.finish(summary)
        else:
            task.log(summary, "warning" if counters["failed"] or counters["unavailable"] else "success")

    core.song_finder_index_task = selective_index
    core._selective_required_index_v1 = True
    return {"song_finder_index_task": selective_index, "selective_required_index_installed": True}


def apply(core: Any) -> dict[str, Any]:
    exports: dict[str, Any] = {}
    exports.update(_install_pre_shortlist_local_refresh(core))
    exports.update(_install_selective_required_indexer(core))
    return exports
