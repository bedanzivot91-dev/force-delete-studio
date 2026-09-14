from __future__ import annotations

"""Runtime correctness fix for local Shorts/song recognition.

The local finder historically trusted only cached ``suno`` fingerprints.  If a
local MP3/WAV existed but its v4 fingerprint had never been built (or the file
had been replaced/remastered after the cache was created), the song was simply
skipped by ``_song_finder_candidates`` and could never be found.

For local files we can fix this without any network access: before the mature
candidate matcher runs, validate/regenerate the fingerprint through the core's
identity-aware ``_signature_for_source`` path.  Remote-only songs retain the
existing cached-index behaviour, so analysing a local Short never unexpectedly
downloads thousands of Suno tracks.
"""

from pathlib import Path
from typing import Any


def apply(core: Any) -> dict[str, Any]:
    original_candidates = core._song_finder_candidates

    if getattr(core, "_song_finder_fresh_local_fp_v1", False):
        return {"_song_finder_candidates": core._song_finder_candidates}

    def candidates_with_fresh_local_fingerprints(
        upload_signatures: dict[str, Any] | list[dict[str, Any]],
        songs: list[dict[str, Any]],
    ) -> tuple[list[dict[str, Any]], int]:
        refreshed = 0
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
                # _signature_for_source validates source_identity for local
                # files. Missing/stale fingerprints are regenerated; valid
                # fingerprints are reused without extra FFmpeg work.
                core._signature_for_source(
                    "suno",
                    song_id,
                    path,
                    None,
                    str(song.get("title") or song.get("display_name") or song_id),
                    False,
                )
                refreshed += 1
            except Exception as exc:
                core.runtime_log(
                    f"Pronalazac: lokalni otisak nije osvezen za {song_id}: {exc}",
                    "warning",
                )

        result, checked = original_candidates(upload_signatures, songs)
        return result, checked

    core._song_finder_candidates = candidates_with_fresh_local_fingerprints
    core._song_finder_fresh_local_fp_v1 = True
    return {"_song_finder_candidates": candidates_with_fresh_local_fingerprints}
