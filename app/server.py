from __future__ import annotations

"""Compatibility wrapper for Suno Pesme Studio.

The full application remains in server_core.py.  This thin layer adds the
large-library download fixes without rewriting the mature server in-place:
- /api/song-ids can enumerate the complete filtered library in bounded pages;
- optional one-folder-per-song output for bulk downloads;
- the browser receives bulk_download_extension.js appended to app.js so the
  existing UI keeps all of its original code and gains whole-library controls.
"""

import sys
import types
from pathlib import Path
from typing import Any

# The shipped embeddable Python uses isolated-path mode.  Add this script's
# directory before importing server_core, matching the production-safe path
# handling of the original server entrypoint.
APP_DIR = Path(__file__).resolve().parent
if str(APP_DIR) not in sys.path:
    sys.path.insert(0, str(APP_DIR))

import server_core as _core

# The release workflow intentionally reads a literal APP_VERSION from this
# production entrypoint. Keep the literal for publishing compatibility, but
# fail immediately if it ever drifts from the mature core implementation.
APP_VERSION = "3.3.2"
if APP_VERSION != str(getattr(_core, "APP_VERSION", "")):
    raise RuntimeError(
        f"APP_VERSION mismatch: wrapper={APP_VERSION!r}, core={getattr(_core, 'APP_VERSION', None)!r}"
    )

# Preserve the old module API.  Existing tests/plugins import many names from
# "server", including a few private helpers, so mirror everything except
# Python's own dunder attributes before applying the small overrides below.
_CORE_EXPORT_NAMES = {
    name for name in vars(_core)
    if not name.startswith("__")
}
for _name, _value in vars(_core).items():
    if _name in _CORE_EXPORT_NAMES:
        globals()[_name] = _value


class _CoreMirroringModule(types.ModuleType):
    """Keep monkeypatches on ``server`` compatible with the old single module.

    Functions re-exported from server_core retain server_core as their globals
    dictionary.  Existing tests and plugins legitimately patch attributes such
    as ``server.DB``, ``server.check_update`` and ``server.download_update``.
    Without mirroring those assignments into server_core, the re-exported
    functions would silently continue using the unpatched originals.  A module
    subclass lets normal ``setattr`` / unittest.mock.patch semantics keep both
    module views synchronized while leaving direct wrapper-only helpers alone.
    """

    def __setattr__(self, name: str, value: Any) -> None:
        super().__setattr__(name, value)
        if name in _CORE_EXPORT_NAMES:
            setattr(_core, name, value)


# Python supports changing a live module to a ModuleType subclass.  Install the
# bridge after the initial export so later monkeypatches behave exactly as they
# did when server.py was a single file.
sys.modules[__name__].__class__ = _CoreMirroringModule


def _list_song_ids_unbounded(
    self,
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
    """Return every matching song id without tying selection to UI page size.

    The old implementation made one list_songs() call and capped it at 20,000.
    This walks the exact same filter in small pages, so 200 remains only a UI
    rendering choice and libraries can grow without another artificial ceiling.
    """
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

    result: list[str] = []
    seen: set[str] = set()
    offset = 0
    page_size = 1000

    # If count_songs_filtered is unavailable for an old database schema, keep
    # paging until list_songs returns a short/empty page.
    while total == 0 or offset < total:
        take = page_size if total == 0 else min(page_size, max(1, total - offset))
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

        added = 0
        for row in rows:
            song_id = str(row.get("id") or "")
            if song_id and song_id not in seen:
                seen.add(song_id)
                result.append(song_id)
                added += 1

        offset += len(rows)
        if len(rows) < take or added == 0:
            break

    return result


# Patch the actual LibraryDB class used by the running server, not just this
# one instance, so tests/new instances see identical behavior.
type(_core.DB).list_song_ids = _list_song_ids_unbounded
globals()["_list_song_ids_unbounded"] = _list_song_ids_unbounded


def _build_per_song_download_options(
    song_id: str,
    options: dict[str, Any],
    *,
    song: dict[str, Any] | None = None,
    collections: list[dict[str, Any]] | None = None,
) -> dict[str, Any]:
    """Translate folder_per_song into the target_dir understood by old code."""
    patched = dict(options)
    if not bool(patched.get("folder_per_song")):
        return patched

    song = song if song is not None else (_core.DB.get_song(song_id) or {})
    collections = collections if collections is not None else _core.DB.list_collections()

    target = Path(str(patched.get("target_dir") or _core.get_download_dir())).expanduser()

    collection_id = patched.get("collection_subfolder_id")
    if collection_id:
        for collection in collections:
            if int(collection.get("id") or 0) == int(collection_id):
                name = _core.sanitize_filename(str(collection.get("name") or ""))
                if name:
                    target = target / name
                break

    created_at = str(song.get("created_at") or "")
    if bool(patched.get("folders_by_month")) and len(created_at) >= 7:
        target = target / created_at[:7]

    title = _core.sanitize_filename(str(song.get("title") or song_id or "pesma"))
    # The short ID avoids collisions when two Suno generations have the same
    # visible title while keeping the folder human-readable.
    folder_name = f"{title or 'pesma'} [{song_id[:8]}]"
    target = target / folder_name

    patched["target_dir"] = str(target)
    patched["collection_subfolder_id"] = 0
    patched["folders_by_month"] = False
    return patched


_ORIGINAL_DOWNLOAD_ONE = _core._download_one


def _download_one(
    task: Any,
    client: Any,
    song_id: str,
    options: dict[str, Any],
    index: int,
    total: int,
) -> None:
    patched = _build_per_song_download_options(song_id, options)
    return _ORIGINAL_DOWNLOAD_ONE(task, client, song_id, patched, index, total)


_core._download_one = _download_one
globals()["_download_one"] = _download_one
globals()["_build_per_song_download_options"] = _build_per_song_download_options


_ORIGINAL_SEND_FILE = _core.Handler._send_file
_BULK_EXTENSION = _core.WEB_DIR / "bulk_download_extension.js"


def _send_file(
    self: Any,
    path: Path,
    download_name: str | None = None,
    no_cache: bool = False,
) -> None:
    """Serve app.js + the extension as ONE script lexical scope.

    app.js intentionally stays byte-for-byte unchanged on disk, preserving the
    existing UI audits.  Concatenating at response time lets the extension use
    the established state/api helpers without duplicating the application.
    """
    try:
        is_app_js = path.resolve() == (_core.WEB_DIR / "app.js").resolve()
    except OSError:
        is_app_js = False

    if is_app_js and _BULK_EXTENSION.is_file():
        payload = path.read_bytes() + b"\n\n/* whole-library download extension */\n" + _BULK_EXTENSION.read_bytes()
        self._send_bytes(payload, "application/javascript; charset=utf-8", download_name)
        return
    return _ORIGINAL_SEND_FILE(self, path, download_name=download_name, no_cache=no_cache)


_core.Handler._send_file = _send_file
globals()["Handler"] = _core.Handler

# Apply the Windows/local-server and large-library YouTube fixes only after the
# wrapper's own response/download overrides are in place, so the disconnect
# guard wraps the final production handlers rather than an obsolete version.
from runtime_fixes import apply as _apply_runtime_fixes
_RUNTIME_FIX_EXPORTS = _apply_runtime_fixes(_core)
globals().update(_RUNTIME_FIX_EXPORTS)


def main() -> None:
    return _core.main()


if __name__ == "__main__":
    main()
