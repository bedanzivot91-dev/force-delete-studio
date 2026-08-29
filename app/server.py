from __future__ import annotations

"""Compatibility wrapper for Suno Pesme Studio.

The full application remains in server_core.py.  This thin layer adds the
large-library download fixes without rewriting the mature server in-place:
- /api/song-ids can enumerate the complete filtered library in bounded pages;
- optional one-folder-per-song output for bulk downloads;
- the browser receives small extension modules appended to app.js so the
  existing UI keeps all of its original code while gaining whole-library
  controls. The standalone program never appends the historical video editor.
"""

import sys
import types
from pathlib import Path
from typing import Any

APP_DIR = Path(__file__).resolve().parent
if str(APP_DIR) not in sys.path:
    sys.path.insert(0, str(APP_DIR))

import server_core as _core

APP_VERSION = "3.3.2"
if APP_VERSION != str(getattr(_core, "APP_VERSION", "")):
    raise RuntimeError(
        f"APP_VERSION mismatch: wrapper={APP_VERSION!r}, core={getattr(_core, 'APP_VERSION', None)!r}"
    )

_CORE_EXPORT_NAMES = {
    name for name in vars(_core)
    if not name.startswith("__")
}
for _name, _value in vars(_core).items():
    if _name in _CORE_EXPORT_NAMES:
        globals()[_name] = _value


class _CoreMirroringModule(types.ModuleType):
    def __setattr__(self, name: str, value: Any) -> None:
        super().__setattr__(name, value)
        if name in _CORE_EXPORT_NAMES:
            setattr(_core, name, value)


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


type(_core.DB).list_song_ids = _list_song_ids_unbounded
globals()["_list_song_ids_unbounded"] = _list_song_ids_unbounded


def _build_per_song_download_options(
    song_id: str,
    options: dict[str, Any],
    *,
    song: dict[str, Any] | None = None,
    collections: list[dict[str, Any]] | None = None,
) -> dict[str, Any]:
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
    folder_name = f"{title or 'pesma'} [{song_id[:8]}]"
    target = target / folder_name
    patched["target_dir"] = str(target)
    patched["collection_subfolder_id"] = 0
    patched["folders_by_month"] = False
    return patched


# --- SUNO MP3 FIX ---------------------------------------------------------
# Suno has used both snake_case and camelCase audio fields in different web/API
# responses. The old download path only trusted audio_url. If the current
# response contains audioUrl/audioURL/streamAudioUrl, the UI can show a song but
# the downloader receives an empty URL and never creates the MP3.
def _find_suno_audio_url(value: Any, depth: int = 0) -> str:
    if depth > 8:
        return ""
    if isinstance(value, str):
        text = value.strip()
        lower = text.lower()
        if text.startswith(("http://", "https://")) and (".mp3" in lower or "audiopipe" in lower or "audio" in lower):
            return text
        return ""
    if isinstance(value, dict):
        for key in (
            "audio_url", "audioUrl", "audioURL", "audio_url_mp3", "mp3_url", "mp3Url",
            "stream_audio_url", "streamAudioUrl", "streamAudioURL", "download_url", "downloadUrl",
        ):
            found = _find_suno_audio_url(value.get(key), depth + 1)
            if found:
                return found
        for key in ("clip", "song", "track", "data", "result", "payload", "content", "metadata"):
            if key in value:
                found = _find_suno_audio_url(value.get(key), depth + 1)
                if found:
                    return found
        for item in value.values():
            if isinstance(item, (dict, list, str)):
                found = _find_suno_audio_url(item, depth + 1)
                if found:
                    return found
    elif isinstance(value, list):
        for item in value:
            found = _find_suno_audio_url(item, depth + 1)
            if found:
                return found
    return ""


_ORIGINAL_GET_CLIP = _core.SunoClient.get_clip


def _get_clip_with_audio_aliases(self: Any, song_id: str) -> dict[str, Any]:
    detail = _ORIGINAL_GET_CLIP(self, song_id)
    if isinstance(detail, dict):
        audio_url = _find_suno_audio_url(detail)
        if audio_url and not str(detail.get("audio_url") or "").strip():
            detail["audio_url"] = audio_url
    return detail


_core.SunoClient.get_clip = _get_clip_with_audio_aliases
globals()["SunoClient"] = _core.SunoClient


_ORIGINAL_DOWNLOAD_ONE = _core._download_one


def _download_one(
    task: Any,
    client: Any,
    song_id: str,
    options: dict[str, Any],
    index: int,
    total: int,
) -> None:
    # Resolve the clip once before the mature downloader runs. This writes a
    # canonical audio_url into the clip/DB even when Suno returned audioUrl or
    # another current alias. The existing downloader then performs its normal
    # validated atomic MP3 download and refresh/retry path.
    try:
        detail = _ORIGINAL_GET_CLIP(client, song_id)
        audio_url = _find_suno_audio_url(detail)
        if audio_url:
            normalized = dict(detail) if isinstance(detail, dict) else {}
            normalized["audio_url"] = audio_url
            _core.DB.upsert_song(normalized)
    except Exception as exc:
        if task is not None and hasattr(task, "log"):
            task.log(f"Osvežavanje Suno MP3 adrese za {song_id} nije uspelo; pokušavam standardni download: {exc}", "warning")
    patched = _build_per_song_download_options(song_id, options)
    return _ORIGINAL_DOWNLOAD_ONE(task, client, song_id, patched, index, total)


_core._download_one = _download_one
globals()["_download_one"] = _download_one
globals()["_build_per_song_download_options"] = _build_per_song_download_options


_ORIGINAL_SEND_FILE = _core.Handler._send_file
_SCRIPT_EXTENSIONS = (
    ("GridStack 13.2.0 MIT workspace engine", _core.WEB_DIR / "vendor" / "gridstack" / "gridstack-all.js"),
    ("whole-library download extension", _core.WEB_DIR / "bulk_download_extension.js"),
)


def _send_file(
    self: Any,
    path: Path,
    download_name: str | None = None,
    no_cache: bool = False,
) -> None:
    try:
        is_app_js = path.resolve() == (_core.WEB_DIR / "app.js").resolve()
    except OSError:
        is_app_js = False
    if is_app_js:
        payload = path.read_bytes()
        appended = False
        for label, extension in _SCRIPT_EXTENSIONS:
            if not extension.is_file():
                continue
            payload += f"\n\n/* {label} */\n".encode("utf-8") + extension.read_bytes()
            appended = True
        if appended:
            self._send_bytes(payload, "application/javascript; charset=utf-8", download_name)
            return
    return _ORIGINAL_SEND_FILE(self, path, download_name=download_name, no_cache=no_cache)


_core.Handler._send_file = _send_file
globals()["Handler"] = _core.Handler

from runtime_fixes import apply as _apply_runtime_fixes
_RUNTIME_FIX_EXPORTS = _apply_runtime_fixes(_core)
globals().update(_RUNTIME_FIX_EXPORTS)

from workspace_backend import apply as _apply_workspace_backend
_WORKSPACE_EXPORTS = _apply_workspace_backend(_core)
globals().update(_WORKSPACE_EXPORTS)

from truthfulness_fixes import apply as _apply_truthfulness_fixes
_TRUTHFULNESS_EXPORTS = _apply_truthfulness_fixes(_core)
globals().update(_TRUTHFULNESS_EXPORTS)


def main() -> None:
    return _core.main()


if __name__ == "__main__":
    main()
