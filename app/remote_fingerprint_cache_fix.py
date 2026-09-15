from __future__ import annotations

"""Final remote-fingerprint cache guard.

A Suno CDN URL is a transport location, not a stable identity for the audio.
If a current v4 cached fingerprint already contains Chromaprint data, normal
re-indexing must reuse it instead of downloading/extracting the same remote
song again merely because the URL/identity differs. Explicit ``force=True``
still rebuilds the fingerprint. Local files are intentionally unaffected so
stat/content identity checks remain strict for changed MP3/WAV files.
"""

from typing import Any


def _usable_cached_signature(core: Any, source_id: str) -> dict[str, Any] | None:
    try:
        cached = core.DB.get_audio_fingerprint("suno", str(source_id or ""), core.AUDIO_MATCH_VERSION)
        if not cached or not cached.get("payload"):
            return None
        signature = core.unpack_signature(cached.get("payload") or b"")
        if isinstance(signature, dict) and signature.get("chromaprint"):
            return signature
    except Exception:
        return None
    return None


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
        is_remote = isinstance(source, str) and source.startswith(("http://", "https://"))
        explicit_force = bool(getattr(task, "_sps_explicit_force", False)) if task is not None else False
        if source_type == "suno" and is_remote and not explicit_force:
            cached = _usable_cached_signature(core, str(source_id or ""))
            if cached is not None:
                return cached
        return previous_signature(source_type, source_id, source, task, label, force)

    core.song_finder_index_task = indexed
    core._signature_for_source = signature_for_source
    core._remote_fingerprint_cache_fix_v1 = True
    return {
        "song_finder_index_task": indexed,
        "_signature_for_source": signature_for_source,
        "remote_fingerprint_cache_fix_installed": True,
    }
