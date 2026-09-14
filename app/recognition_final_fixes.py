from __future__ import annotations

"""Final correctness guards for the local/YouTube song-recognition index.

Loaded after the mature runtime layers. It fixes cross-layer cases that unit
-testing the individual layers could miss:
* repair only missing/corrupt fingerprints, not thousands of valid songs;
* refresh changed local MP3/WAV fingerprints before the fast shortlist;
* use cheap stat metadata for unchanged local files so every search does not
  SHA-256 the entire local music library;
* reuse a stat-valid cached local signature during final comparison;
* report only decodable Chromaprint payloads as indexed.
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
        current = core.source_identity(path)
        return str(current.get("identity") or "") != expected_identity
    except Exception:
        return True


def _install_stat_cached_local_signature(core: Any) -> dict[str, Any]:
    """Return an unchanged local song's valid cached signature in O(1).

    The mature signature resolver computes a full SHA-256 source identity on
    every local call. Exact matching after the fast shortlist calls that path
    again, which can reread many large MP3/WAV files even though their size and
    mtime exactly match the fingerprint row. Preserve force=True semantics and
    remote URLs; only unchanged local files take this fast path.
    """
    if getattr(core, "_stat_cached_local_signature_v1", False):
        return {"stat_cached_local_signature_installed": True}
    original = core._signature_for_source

    def signature_for_source(
        source_type: str,
        source_id: str,
        source: Any,
        task: Any = None,
        label: str = "",
        force: bool = False,
    ) -> dict[str, Any]:
        if source_type == "suno" and not force and not (
            isinstance(source, str) and source.startswith(("http://", "https://"))
        ):
            try:
                path = Path(str(source)).expanduser()
                if path.is_file():
                    cached = core.DB.get_audio_fingerprint("suno", str(source_id or ""), core.AUDIO_MATCH_VERSION)
                    usable = _usable_signature(core, cached)
                    if usable is not None:
                        stat = path.stat()
                        old_size = int(cached.get("source_size") or 0)
                        old_mtime = float(cached.get("source_mtime") or 0.0)
                        if (
                            old_size > 0
                            and old_mtime > 0
                            and stat.st_size == old_size
                            and abs(stat.st_mtime - old_mtime) <= 1e-6
                        ):
                            return usable
            except Exception:
                # Any uncertainty falls back to the mature identity-checked
                # extractor; correctness is never traded for this optimization.
                pass
        return original(source_type, source_id, source, task, label, force)

    core._signature_for_source = signature_for_source
    core._stat_cached_local_signature_v1 = True
    return {"_signature_for_source": signature_for_source, "stat_cached_local_signature_installed": True}


def _install_truthful_status(core: Any) -> dict[str, Any]:
    if getattr(core, "_truthful_recognition_status_v1", False):
        return {"truthful_recognition_status_installed": True}
    original = core.song_finder_status

    def truthful_status() -> dict[str, Any]:
        base = dict(original() or {})
        try:
            songs = core.DB.export_rows()
        except Exception:
            return base
        usable_ids: set[str] = set()
        source_ids: set[str] = set()
        no_source_but_indexed = 0
        remote_only = 0
        for song in songs:
            sid = str(song.get("id") or "").strip()
            if not sid:
                continue
            try:
                source, is_remote = core._song_finder_source_cheap(song)
            except Exception:
                source, is_remote = None, False
            if source:
                source_ids.add(sid)
                if is_remote:
                    remote_only += 1
            try:
                cached = core.DB.get_audio_fingerprint("suno", sid, core.AUDIO_MATCH_VERSION)
            except Exception:
                cached = None
            if _usable_signature(core, cached) is not None:
                usable_ids.add(sid)
                if not source:
                    no_source_but_indexed += 1
        base.update({
            "songs_total": len(songs),
            "songs_with_audio": len(source_ids),
            "songs_remote_only": remote_only,
            "songs_indexed": len(usable_ids),
            "songs_indexed_without_current_source": no_source_but_indexed,
            "songs_not_indexed": sum(1 for sid in source_ids if sid not in usable_ids),
            "songs_without_any_source": sum(
                1
                for song in songs
                if str(song.get("id") or "").strip() not in source_ids
                and str(song.get("id") or "").strip() not in usable_ids
            ),
        })
        return base

    core.song_finder_status = truthful_status
    core._truthful_recognition_status_v1 = True
    return {"song_finder_status": truthful_status, "truthful_recognition_status_installed": True}


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
                        task.log(
                            f"{title}: Suno audio adresa nije osvežena ({refresh_exc}); pokušavam postojeći izvor.",
                            "warning",
                        )
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

        workers = max(
            1,
            min(int(opts.get("parallelism") or max(2, min(4, int(os.cpu_count() or 4) // 2))), 6),
        )
        task.log(
            f"Selektivno popravljam {len(pending)} nedostajućih/oštećenih otisaka; validne pesme ne diram.",
            "info",
        )
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
    exports.update(_install_stat_cached_local_signature(core))
    exports.update(_install_truthful_status(core))
    exports.update(_install_pre_shortlist_local_refresh(core))
    exports.update(_install_selective_required_indexer(core))
    return exports
