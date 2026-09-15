from __future__ import annotations

"""Final fingerprint-cache guard for scalable Suno indexing.

The scalable indexer intentionally asks the mature signature path to repair a
cached entry when necessary. Historically it expressed that internal repair
hint as ``force=True`` whenever any cache row existed, which accidentally made
normal re-indexing re-extract every already-indexed song.

This layer separates an explicit user ``force=True`` request from that internal
hint:
- valid remote Suno fingerprints are reused because a CDN URL is transport,
  not stable audio identity;
- valid local fingerprints are reused only while their saved source identity
  still matches the current file;
- changed local files are rebuilt;
- explicit force still rebuilds everything.
"""

from typing import Any


def _cached_entry(core: Any, source_id: str) -> tuple[dict[str, Any] | None, dict[str, Any] | None]:
    try:
        cached = core.DB.get_audio_fingerprint("suno", str(source_id or ""), core.AUDIO_MATCH_VERSION)
        if not cached or not cached.get("payload"):
            return cached, None
        signature = core.unpack_signature(cached.get("payload") or b"")
        if isinstance(signature, dict) and signature.get("chromaprint"):
            return cached, signature
        return cached, None
    except Exception:
        return None, None


def apply(core: Any) -> dict[str, Any]:
    if getattr(core, "_remote_fingerprint_cache_fix_v1", False):
        return {"remote_fingerprint_cache_fix_installed": True}

    previous_index = core.song_finder_index_task
    previous_signature = core._signature_for_source

    def indexed(task: Any, options: dict[str, Any] | None = None) -> Any:
        opts = dict(options or {})
        sentinel = object()
        old_value = getattr(task, "_sps_explicit_force", sentinel)
        try:
            task._sps_explicit_force = bool(opts.get("force"))
            return previous_index(task, opts)
        finally:
            if old_value is sentinel:
                try:
                    delattr(task, "_sps_explicit_force")
                except Exception:
                    pass
            else:
                task._sps_explicit_force = old_value

    def signature_for_source(
        source_type: str,
        source_id: str,
        source: Any,
        task: Any = None,
        label: str = "",
        force: bool = False,
    ) -> dict[str, Any]:
        marker = getattr(task, "_sps_explicit_force", None) if task is not None else None
        explicit_force = bool(marker) if marker is not None else bool(force)

        if source_type == "suno" and not explicit_force:
            cached, signature = _cached_entry(core, str(source_id or ""))
            if signature is not None:
                is_remote = isinstance(source, str) and source.startswith(("http://", "https://"))
                if is_remote:
                    return signature

                # server_core imports audio_match.source_identity directly as
                # ``source_identity``.  Compare exactly the same identity value
                # the mature cache writer persisted; this keeps unchanged local
                # files as cache hits while changed/replaced files fall through
                # and get rebuilt.
                try:
                    current = core.source_identity(source)
                except Exception:
                    current = None
                cached_identity = str((cached or {}).get("source_identity") or "")
                current_identity = str((current or {}).get("identity") or "") if isinstance(current, dict) else ""
                if cached_identity and current_identity and cached_identity == current_identity:
                    return signature

        return previous_signature(source_type, source_id, source, task, label, force)

    core.song_finder_index_task = indexed
    core._signature_for_source = signature_for_source
    core._remote_fingerprint_cache_fix_v1 = True
    return {
        "song_finder_index_task": indexed,
        "_signature_for_source": signature_for_source,
        "remote_fingerprint_cache_fix_installed": True,
    }
