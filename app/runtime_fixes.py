from __future__ import annotations

"""Production hot-path fixes for Suno Pesme Studio 3.3.2.

This module deliberately patches the mature ``server_core`` at startup instead
of duplicating the 300k+ core file.  Every fix here targets a failure seen on a
large real library / Windows desktop:

* a browser/WebView closing a localhost request raised WinError 10053 and the
  server tried to send a second error response to the already-dead socket;
* connecting YouTube automatically launched a full audio job, which in turn
  forced fingerprinting the whole Suno library before a single YouTube video
  could be checked;
* the explicit 3k-song indexer resolved missing Suno audio URLs serially before
  its worker pool even started;
* YouTube metadata matching repeatedly ran expensive SequenceMatcher + lyric
  checks for every video against every song (videos x songs).

The fixes keep correctness fallbacks: weak metadata matches still use the old
full matcher, manual fingerprint indexing still indexes the complete library,
and YouTube audio confirmation still computes real fingerprints for the small
candidate set it actually needs.
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
    """True only for transport errors caused by a localhost client going away."""
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
            if _is_client_disconnect(exc):
                return None
            raise

    def safe_bytes(self: Any, *args: Any, **kwargs: Any) -> Any:
        try:
            return original_bytes(self, *args, **kwargs)
        except BaseException as exc:
            if _is_client_disconnect(exc):
                return None
            raise

    def safe_file(self: Any, *args: Any, **kwargs: Any) -> Any:
        try:
            return original_file(self, *args, **kwargs)
        except BaseException as exc:
            if _is_client_disconnect(exc):
                return None
            raise

    handler._send_json = safe_json
    handler._send_bytes = safe_bytes
    handler._send_file = safe_file
    handler._runtime_disconnect_guard_v1 = True


class _SongLookup:
    __slots__ = ("songs", "normalized", "tokens", "token_map", "exact_map", "durations")

    def __init__(self, songs: list[dict[str, Any]]):
        self.songs = songs
        self.normalized: list[str] = []
        self.tokens: list[set[str]] = []
        self.token_map: dict[str, set[int]] = {}
        self.exact_map: dict[str, set[int]] = {}
        self.durations: list[float] = []
        for index, song in enumerate(songs):
            normalized = _youtube_tools.normalize_title(str(song.get("title") or ""))
            tokens = {token for token in normalized.split() if len(token) >= 3}
            self.normalized.append(normalized)
            self.tokens.append(tokens)
            self.durations.append(float(song.get("duration") or 0))
            if normalized:
                self.exact_map.setdefault(normalized, set()).add(index)
            for token in tokens:
                self.token_map.setdefault(token, set()).add(index)


_LOOKUP_LOCK = threading.RLock()
_LOOKUP_KEY: tuple[int, int, str, str] | None = None
_LOOKUP_VALUE: _SongLookup | None = None


def _song_lookup(songs: list[dict[str, Any]]) -> _SongLookup:
    global _LOOKUP_KEY, _LOOKUP_VALUE
    first = str(songs[0].get("id") or "") if songs else ""
    last = str(songs[-1].get("id") or "") if songs else ""
    key = (id(songs), len(songs), first, last)
    with _LOOKUP_LOCK:
        if _LOOKUP_KEY == key and _LOOKUP_VALUE is not None:
            return _LOOKUP_VALUE
        value = _SongLookup(songs)
        _LOOKUP_KEY = key
        _LOOKUP_VALUE = value
        return value


def _quick_song_pool(video: dict[str, Any], songs: list[dict[str, Any]], limit: int = 180) -> list[dict[str, Any]]:
    """Cheap shortlist before calling the expensive metadata matcher.

    Exact/contained titles and shared title tokens get priority.  Duration is a
    second independent path, so a slightly renamed upload is still considered.
    A weak final match is never trusted by ``_fast_best_song_match``; that path
    falls back to the original exhaustive matcher.
    """
    if not songs:
        return []
    lookup = _song_lookup(songs)
    video_title = _youtube_tools.normalize_title(str(video.get("title") or ""))
    video_tokens = {token for token in video_title.split() if len(token) >= 3}
    video_duration = float(video.get("duration") or 0)

    indices: set[int] = set()
    if video_title:
        indices.update(lookup.exact_map.get(video_title, ()))
    for token in video_tokens:
        indices.update(lookup.token_map.get(token, ()))

    # Duration adds a title-independent path.  Sorting 3k floats is far cheaper
    # than 3k SequenceMatcher + lyric comparisons and protects renamed uploads.
    if video_duration > 1:
        nearest = sorted(
            (index for index, duration in enumerate(lookup.durations) if duration > 1),
            key=lambda index: abs(lookup.durations[index] - video_duration),
        )[: max(40, min(100, limit // 2))]
        indices.update(nearest)

    if not indices:
        # No useful title/duration metadata: keep a bounded deterministic pool.
        indices.update(range(min(len(songs), max(40, limit // 2))))

    ranked: list[tuple[float, int]] = []
    for index in indices:
        song_title = lookup.normalized[index]
        song_tokens = lookup.tokens[index]
        score = 0.0
        if song_title and video_title:
            if song_title == video_title:
                score += 120.0
            elif song_title in video_title or video_title in song_title:
                score += 95.0
        if song_tokens and video_tokens:
            overlap = len(song_tokens & video_tokens)
            if overlap:
                score += 60.0 * overlap / max(1, len(song_tokens))
                score += min(20.0, overlap * 4.0)
        duration = lookup.durations[index]
        if duration > 1 and video_duration > 1:
            similarity = max(0.0, 1.0 - abs(duration - video_duration) / max(duration, video_duration))
            score += similarity * 35.0
        ranked.append((score, index))

    ranked.sort(key=lambda row: row[0], reverse=True)
    return [songs[index] for _, index in ranked[: max(20, min(int(limit), 400))]]


def _install_fast_youtube_metadata(core: Any) -> dict[str, Any]:
    original_best = core._best_song_match
    original_candidates = core._metadata_candidates_for_video

    def fast_best_song_match(
        video: dict[str, Any], songs: list[dict[str, Any]], owned_ids: set[str]
    ) -> tuple[dict[str, Any] | None, dict[str, Any] | None]:
        if len(songs) <= 500:
            return original_best(video, songs, owned_ids)
        best_song: dict[str, Any] | None = None
        best_match: dict[str, Any] | None = None
        for song in _quick_song_pool(video, songs, limit=180):
            match = core.match_song_to_video(song, video, owned_ids)
            if best_match is None or float(match.get("score") or 0) > float(best_match.get("score") or 0):
                best_song, best_match = song, match
        # A strong candidate has enough evidence that another 2,800 expensive
        # fuzzy/lyric checks cannot materially improve the channel scan.  Weak
        # cases retain the old exhaustive behavior, preserving recall.
        if best_match is not None and float(best_match.get("score") or 0) >= 82.0:
            return best_song, best_match
        return original_best(video, songs, owned_ids)

    def fast_metadata_candidates(
        video: dict[str, Any],
        songs: list[dict[str, Any]],
        owned_ids: set[str],
        limit: int = 12,
        deep: bool = False,
    ) -> list[tuple[dict[str, Any], dict[str, Any]]]:
        if len(songs) <= 500 or (deep and len(songs) <= 120):
            return original_candidates(video, songs, owned_ids, limit, deep)
        candidate_limit = max(4, int(limit))
        pool = _quick_song_pool(video, songs, limit=max(120, candidate_limit * 10))
        scored: list[tuple[float, dict[str, Any], dict[str, Any]]] = []
        used: set[str] = set()
        for song in pool:
            song_id = str(song.get("id") or "")
            if not song_id or song_id in used:
                continue
            match = core.match_song_to_video(song, video, owned_ids)
            scored.append((float(match.get("score") or 0), song, match))
            used.add(song_id)

        # Keep the core's independent duration shortlist as an extra safety net.
        for song in core.closest_duration_candidates(
            songs, float(video.get("duration") or 0), limit=max(8, candidate_limit)
        ):
            song_id = str(song.get("id") or "")
            if not song_id or song_id in used:
                continue
            match = core.match_song_to_video(song, video, owned_ids)
            scored.append((float(match.get("score") or 0), song, match))
            used.add(song_id)

        scored.sort(key=lambda item: item[0], reverse=True)
        return [(song, match) for _, song, match in scored[:candidate_limit]]

    core._best_song_match = fast_best_song_match
    core._metadata_candidates_for_video = fast_metadata_candidates
    return {
        "_best_song_match": fast_best_song_match,
        "_metadata_candidates_for_video": fast_metadata_candidates,
    }


def _install_incremental_fingerprint_seeding(core: Any) -> dict[str, Any]:
    original_signature = core._signature_for_source
    indexed_lock = threading.RLock()
    indexed_ids: set[str] | None = None

    def signature_for_source(
        source_type: str,
        source_id: str,
        source: Any,
        task: Any = None,
        label: str = "",
        force: bool = False,
    ) -> dict[str, Any]:
        nonlocal indexed_ids
        signature = original_signature(source_type, source_id, source, task, label, force)
        # Manual full-library indexing already performs efficient batched writes.
        # This path is for YouTube/manual analyses: every real Suno fingerprint
        # learned on demand immediately becomes useful to subsequent videos.
        if source_type != "suno" or (task is not None and getattr(task, "type", "") == "song_finder_index"):
            return signature
        chromaprint = list(signature.get("chromaprint") or [])
        song_id = str(source_id or "")
        if not song_id or not chromaprint:
            return signature
        try:
            index = core.get_fingerprint_index()
            if index is None:
                return signature
            with indexed_lock:
                if indexed_ids is None:
                    indexed_ids = set(index.indexed_song_ids())
                if song_id in indexed_ids:
                    return signature
                index.add_songs([(song_id, chromaprint, core.AUDIO_MATCH_VERSION)])
                indexed_ids.add(song_id)
        except Exception as exc:
            core.runtime_log(f"Brzi audio indeks nije dopunjen za {song_id}: {exc}", "warning")
        return signature

    core._signature_for_source = signature_for_source
    return {"_signature_for_source": signature_for_source}


def _install_scalable_song_indexer(core: Any) -> dict[str, Any]:
    """Keep full indexing explicit, resumable and non-serial during source lookup."""

    def scalable_song_finder_index_task(task: Any, options: dict[str, Any]) -> None:
        finish_task = bool(options.get("finish_task", True))
        required_for_youtube = bool(options.get("required_for_youtube", False))
        # A cheap metadata-only scan may still skip a full pre-index. A real
        # YouTube audio scan may not: Shorts often have quote titles unrelated
        # to the Suno title, so on-demand metadata candidates caused genuine
        # false negatives in the user's library.
        if not finish_task and not required_for_youtube:
            status = core.song_finder_status()
            task.log(
                f"Kompletan audio indeks nije uslov za YouTube proveru. "
                f"Nedostaje {int(status.get('songs_not_indexed') or 0)} otisaka; "
                "potrebni kandidati će se indeksirati usput, bez blokiranja celog programa.",
                "info",
            )
            return

        force = bool(options.get("force"))
        all_songs = core.DB.export_rows()
        task.total = len(all_songs)
        if not all_songs:
            if finish_task:
                task.finish("Biblioteka je prazna; nema pesama za indeksiranje.")
            else:
                task.log("Biblioteka je prazna; nema pesama za indeksiranje.", "warning")
            return

        ok = 0
        failed = 0
        completed = 0
        unavailable = 0
        remote_count = 0
        reused_without_source = 0
        lock = threading.RLock()
        fast_index = core.get_fingerprint_index()
        pending_index: list[tuple[str, list[int], str]] = []
        indexed_rows = 0

        def flush_index(force_flush: bool = False) -> None:
            nonlocal pending_index, indexed_rows
            if fast_index is None:
                return
            with lock:
                if not pending_index or (len(pending_index) < 100 and not force_flush):
                    return
                batch, pending_index = pending_index, []
            try:
                indexed_rows += fast_index.add_songs(batch)
            except Exception as exc:
                task.log(f"Brzi indeks nije upisan za {len(batch)} pesama: {exc}", "warning")

        def index_one(song: dict[str, Any]) -> None:
            nonlocal ok, failed, completed, unavailable, remote_count, reused_without_source
            if task.cancel_event.is_set():
                return
            core.wait_if_paused(task)
            song_id = str(song.get("id") or "")
            title = str(song.get("title") or song.get("display_name") or song_id)
            source, is_remote = core._song_finder_source_cheap(song)
            cached = None if force else core.DB.get_audio_fingerprint("suno", song_id, core.AUDIO_MATCH_VERSION)
            try:
                # If a valid fingerprint already exists but its temporary Suno
                # URL has disappeared, reuse the fingerprint directly.  The old
                # pre-pass made a network get_clip() call merely to rediscover a
                # source it did not need.
                if cached and source is None:
                    signature = core.unpack_signature(cached.get("payload") or b"")
                    with lock:
                        reused_without_source += 1
                else:
                    if source is None:
                        # Missing URL refresh happens INSIDE the worker pool,
                        # never serially over thousands of songs.
                        source, is_remote = core._song_finder_source(song)
                    if source is None:
                        with lock:
                            unavailable += 1
                        return
                    signature = core._signature_for_source(
                        "suno", song_id, source, task, title, force=force
                    )
                chromaprint = list(signature.get("chromaprint") or [])
                if chromaprint:
                    with lock:
                        pending_index.append((song_id, chromaprint, core.AUDIO_MATCH_VERSION))
                with lock:
                    ok += 1
                    if is_remote:
                        remote_count += 1
            except Exception as exc:
                with lock:
                    failed += 1
                task.log(f"{title}: {exc}", "warning")
            finally:
                with lock:
                    completed += 1
                    task.set_progress(completed, len(all_songs), title)
                flush_index()

        cpu = max(2, int(os.cpu_count() or 4))
        default_workers = min(6, max(3, cpu // 2))
        workers = max(1, min(int(options.get("parallelism") or default_workers), 8))
        task.log(
            f"Indeksiranje {len(all_songs)} pesama: {workers} paralelna radnika. "
            "Postojeći otisci se ponovo ne računaju.",
            "info",
        )
        with ThreadPoolExecutor(max_workers=workers, thread_name_prefix="song-finder-index") as pool:
            for _ in pool.map(index_one, all_songs):
                if task.cancel_event.is_set():
                    break

        flush_index(force_flush=True)
        if fast_index is not None:
            try:
                fast_index.prune({str(song.get("id") or "") for song in all_songs})
                fast_index.checkpoint()
            except Exception as exc:
                task.log(f"Brzi indeks nije očišćen: {exc}", "warning")

        core.DB.set_setting("song_finder_last_index_at", core.now_iso())
        summary = (
            f"Indeksiranje završeno: {ok} obrađeno, {failed} neuspešno, "
            f"{unavailable} bez dostupnog audio izvora."
            + (f" {remote_count} obrađeno direktno sa Suno servera." if remote_count else "")
            + (f" {reused_without_source} postojećih otisaka ponovo iskorišćeno bez mreže." if reused_without_source else "")
            + (f" Brzi indeks: {indexed_rows} otisaka." if indexed_rows else "")
        )
        if finish_task:
            if task.cancel_event.is_set():
                task.finish_partial(summary + " Posao je zaustavljen; sledeći put nastavlja samo ono što nedostaje.")
            elif failed:
                task.finish_partial(summary)
            else:
                task.finish(summary)
        else:
            task.log(summary, "warning" if failed or unavailable else "success")

    core.song_finder_index_task = scalable_song_finder_index_task
    return {"song_finder_index_task": scalable_song_finder_index_task}


def _install_youtube_audio_defaults(core: Any) -> dict[str, Any]:
    original = core.analyze_owned_youtube_audio

    def analyze_owned_youtube_audio(task: Any, options: dict[str, Any]) -> None:
        patched = dict(options or {})
        # The backend itself supports 5,000 videos but its old silent default
        # was only 100.  When the UI supplies a number we respect it; otherwise
        # a requested full scan really means the full uploads history.
        patched.setdefault("max_pages", 100)
        if "max_videos_per_channel" not in patched:
            patched["max_videos_per_channel"] = 5000 if str(patched.get("scan_mode") or "new") == "full" else 500
        return original(task, patched)

    core.analyze_owned_youtube_audio = analyze_owned_youtube_audio
    return {"analyze_owned_youtube_audio": analyze_owned_youtube_audio}


def _install_lightweight_youtube_connect_pipeline(core: Any) -> dict[str, Any]:
    def start_automatic_youtube_pipeline(delay_seconds: float = 1.5) -> None:
        """After OAuth, scan metadata only; never start a 3k-song audio index."""
        def orchestrate() -> None:
            if delay_seconds > 0:
                time.sleep(delay_seconds)
            # Do not steal the global task slot from something the user already
            # started.  A manual scan remains available in YouTube centar.
            with core.STATE_LOCK:
                active = core.ACTIVE_TASK
                if active is not None and getattr(active, "status", "") == "running":
                    core.runtime_log(
                        "Automatska YouTube provera je preskočena jer je drugi posao već u toku; kanal je ipak povezan.",
                        "info",
                    )
                    return
            options = {
                "max_pages": 100,
                "include_private_unlisted": True,
                "threshold": 68,
                "scan_mode": "new",
            }
            try:
                core.start_task(
                    "youtube_owned",
                    "Automatsko skeniranje povezanih YouTube kanala",
                    lambda task: core.scan_owned_youtube_channels(task, options),
                    persistent_payload=options,
                )
            except Exception as exc:
                core.runtime_log(f"Automatska YouTube metadata provera nije pokrenuta: {exc}", "warning")

        threading.Thread(target=orchestrate, daemon=True, name="youtube-auto-metadata").start()

    core.start_automatic_youtube_pipeline = start_automatic_youtube_pipeline
    return {"start_automatic_youtube_pipeline": start_automatic_youtube_pipeline}


def apply(core: Any) -> dict[str, Any]:
    """Install all fixes once and return patched names for server.py exports."""
    if getattr(core, "_runtime_fixes_343_installed", False):
        return {}
    exports: dict[str, Any] = {}
    _install_http_disconnect_guards(core)
    exports.update(_install_fast_youtube_metadata(core))
    exports.update(_install_incremental_fingerprint_seeding(core))
    exports.update(_install_scalable_song_indexer(core))
    exports.update(_install_youtube_audio_defaults(core))
    exports.update(_install_lightweight_youtube_connect_pipeline(core))
    core._runtime_fixes_343_installed = True
    core.runtime_log(
        "Runtime fixes 343 aktivni: localhost disconnect guard, skalabilni YouTube matcher, neblokirajući audio indeks.",
        "info",
    )
    exports["Handler"] = core.Handler
    return exports
