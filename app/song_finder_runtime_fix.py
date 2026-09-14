from __future__ import annotations

"""Runtime correctness fixes for Suno song recognition.

The local finder historically trusted only cached ``suno`` fingerprints. If a
local MP3/WAV existed but its current fingerprint had never been built, the song
could be skipped. Remote-only Suno songs could also be present in SQLite after a
sync while still missing from the recognition index.

This module therefore enforces two invariants:
1. local files get an identity-checked fingerprint before matching;
2. a successful Suno sync is not reported as fully complete until all currently
   indexable songs have been incrementally fingerprinted as well.
"""

from pathlib import Path
from typing import Any

from atomic_delete_fixes import apply as _apply_atomic_delete_fixes


class _DeferredFinishTask:
    """Forward a task while delaying only its final success/partial state."""

    def __init__(self, task: Any):
        self._task = task
        self.final_message = ""
        self.final_status = "done"

    def __getattr__(self, name: str) -> Any:
        return getattr(self._task, name)

    def finish(self, message: str, status: str = "done") -> None:
        self.final_message = str(message)
        self.final_status = str(status or "done")

    def finish_partial(self, message: str) -> None:
        self.final_message = str(message)
        self.final_status = "partial"


def _install_sync_auto_index(core: Any) -> dict[str, Any]:
    if getattr(core, "_sync_auto_index_v1", False):
        return {"sync_auto_index_installed": True}
    if not all(hasattr(core, name) for name in ("sync_library", "check_new_songs", "song_finder_status", "song_finder_index_task")):
        return {}

    previous_sync = core.sync_library
    previous_check_new = core.check_new_songs

    def run_with_index(previous: Any, task: Any, options: dict[str, Any] | None = None) -> Any:
        proxy = _DeferredFinishTask(task)
        result = previous(proxy, dict(options or {}))

        # Cancellation remains cancellation; do not start a long remote index
        # after the user explicitly stopped the sync.
        if task.cancel_event.is_set():
            task.finish(proxy.final_message or "Sinhronizacija je zaustavljena.", status="cancelled")
            return result

        status_before = core.song_finder_status()
        missing_before = int(status_before.get("songs_not_indexed") or 0)
        if missing_before > 0:
            task.log(
                f"Suno baza je osvežena, ali {missing_before} pesama još nema audio otisak. "
                "Automatski dopunjujem indeks pre završetka sinhronizacije.",
                "warning",
            )
            core.song_finder_index_task(task, {"force": False, "finish_task": False})

        status_after = core.song_finder_status()
        missing_after = int(status_after.get("songs_not_indexed") or 0)
        no_source = int(status_after.get("songs_without_any_source") or 0)
        base_message = proxy.final_message or "Sinhronizacija Suno biblioteke je završena."
        if missing_after > 0:
            task.finish_partial(
                base_message
                + f" Audio indeks NIJE kompletan: {missing_after} pesama još nema upotrebljiv fingerprint"
                + (f", a {no_source} nema ni lokalni ni Suno audio izvor." if no_source else ".")
            )
        elif proxy.final_status == "partial":
            task.finish_partial(base_message + " Audio indeks je kompletan.")
        else:
            task.finish(base_message + " Audio indeks je kompletan.")
        return result

    def sync_library_with_index(task: Any, options: dict[str, Any] | None = None) -> Any:
        return run_with_index(previous_sync, task, options)

    def check_new_with_index(task: Any, options: dict[str, Any] | None = None) -> Any:
        return run_with_index(previous_check_new, task, options)

    core.sync_library = sync_library_with_index
    core.check_new_songs = check_new_with_index
    core._sync_auto_index_v1 = True
    return {
        "sync_library": sync_library_with_index,
        "check_new_songs": check_new_with_index,
        "sync_auto_index_installed": True,
    }


def apply(core: Any) -> dict[str, Any]:
    atomic_exports: dict[str, Any] = {}
    sync_exports: dict[str, Any] = {}
    if hasattr(core, "DB"):
        atomic_exports = _apply_atomic_delete_fixes(core)
        sync_exports = _install_sync_auto_index(core)

    original_candidates = core._song_finder_candidates

    if getattr(core, "_song_finder_fresh_local_fp_v1", False):
        return {
            "_song_finder_candidates": core._song_finder_candidates,
            **atomic_exports,
            **sync_exports,
        }

    def candidates_with_fresh_local_fingerprints(
        upload_signatures: dict[str, Any] | list[dict[str, Any]],
        songs: list[dict[str, Any]],
    ) -> tuple[list[dict[str, Any]], int]:
        for song in songs:
            song_id = str(song.get("id") or "").strip()
            if not song_id:
                continue
            try:
                source, is_remote = core._song_finder_source_cheap(song)
            except Exception:
                source, is_remote = None, False
            if not source or is_remote:
                continue
            try:
                path = Path(str(source)).expanduser()
                if not path.exists() or not path.is_file():
                    continue
                core._signature_for_source(
                    "suno",
                    song_id,
                    path,
                    None,
                    str(song.get("title") or song.get("display_name") or song_id),
                    False,
                )
            except Exception as exc:
                core.runtime_log(
                    f"Pronalazac: lokalni otisak nije osvezen za {song_id}: {exc}",
                    "warning",
                )

        return original_candidates(upload_signatures, songs)

    core._song_finder_candidates = candidates_with_fresh_local_fingerprints
    core._song_finder_fresh_local_fp_v1 = True
    return {
        "_song_finder_candidates": candidates_with_fresh_local_fingerprints,
        **atomic_exports,
        **sync_exports,
    }
