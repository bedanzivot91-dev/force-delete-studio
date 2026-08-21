from __future__ import annotations

"""Exact-count and unlimited song selection.

A positive ``limit`` is the user's requested count. ``0`` means every matching
song.  There is no application-side maximum; rows are fetched from SQLite in
bounded internal pages only so the UI is not tied to its 50/100/200 page size.
"""

from typing import Any


def apply(core: Any) -> dict[str, Any]:
    if getattr(core, "_selection_fixes_v1_installed", False):
        return {}

    def list_song_ids(
        self: Any,
        search: str = "",
        filter_name: str = "all",
        collection_id: int | None = None,
        source_group: str = "",
        date_from: str = "",
        date_to: str = "",
        min_duration: float | None = None,
        max_duration: float | None = None,
        limit: int = 0,
    ) -> list[str]:
        try:
            requested = max(0, int(limit or 0))
        except (TypeError, ValueError):
            requested = 0

        filter_kwargs = {
            "search": search,
            "filter_name": filter_name,
            "collection_id": collection_id,
            "source_group": source_group,
            "date_from": date_from,
            "date_to": date_to,
            "min_duration": min_duration,
            "max_duration": max_duration,
        }
        try:
            total = max(0, int(self.count_songs_filtered(**filter_kwargs)))
        except Exception:
            total = 0

        target = min(total, requested) if requested and total else requested
        result: list[str] = []
        seen: set[str] = set()
        offset = 0
        page_size = 1000

        while True:
            if target and len(result) >= target:
                break
            if total and offset >= total:
                break
            remaining = (target - len(result)) if target else page_size
            take = min(page_size, max(1, remaining))
            if total:
                take = min(take, max(1, total - offset))
            rows = self.list_songs(
                search=search,
                filter_name=filter_name,
                sort="newest",
                limit=take,
                offset=offset,
                collection_id=collection_id,
                source_group=source_group,
                date_from=date_from,
                date_to=date_to,
                min_duration=min_duration,
                max_duration=max_duration,
            )
            if not rows:
                break
            for row in rows:
                song_id = str(row.get("id") or "")
                if song_id and song_id not in seen:
                    seen.add(song_id)
                    result.append(song_id)
                    if target and len(result) >= target:
                        break
            offset += len(rows)
            if len(rows) < take:
                break

        return result[:target] if target else result

    # Patch the database method used by /api/song-ids.  Do NOT export this
    # function as _list_song_ids_unbounded: server.py already owns a separate
    # whole-library helper with that name whose ``limit`` argument is only the
    # old UI page size.  Reusing the same export name caused workspace_backend
    # to overwrite that helper and truncate whole-library selection to 200.
    type(core.DB).list_song_ids = list_song_ids
    core._selection_fixes_v1_installed = True
    core.runtime_log("Izbor pesama: pozitivan broj = tačan broj, 0 = sve; nema internog maksimuma.", "info")
    return {"_list_song_ids_exact_or_all": list_song_ids}
