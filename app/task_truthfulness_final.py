from __future__ import annotations

"""Final task-status guards.

Several mature jobs already log/count individual failures but historically
called ``finish()`` unconditionally. This layer preserves their work/messages
and upgrades only the final task state when the requested operation was
provably incomplete.
"""

import re
from pathlib import Path
from typing import Any, Callable

from restore_truthfulness_final import apply as _apply_restore_truthfulness


def _cancelled(task: Any) -> bool:
    event = getattr(task, "cancel_event", None)
    try:
        return bool(event and event.is_set())
    except Exception:
        return False


def _set_partial(task: Any, message: str) -> None:
    if _cancelled(task):
        return
    if hasattr(task, "finish_partial"):
        task.finish_partial(message)
    elif hasattr(task, "finish"):
        task.finish(message, status="partial")


def _set_error(task: Any, message: str) -> None:
    if _cancelled(task):
        return
    if hasattr(task, "fail"):
        task.fail(message)
    elif hasattr(task, "finish"):
        task.finish(message, status="error")


def _number(message: str, pattern: str) -> int:
    match = re.search(pattern, str(message or ""), flags=re.IGNORECASE)
    return int(match.group(1)) if match else 0


def _wrap_message_failures(core: Any, name: str, parser: Callable[[str], tuple[int, int | None]]) -> Any:
    original = getattr(core, name, None)
    if original is None:
        return None

    def wrapped(task: Any, *args: Any, **kwargs: Any):
        before_errors = len(getattr(task, "errors", []) or [])
        result = original(task, *args, **kwargs)
        if _cancelled(task):
            return result
        message = str(getattr(task, "message", "") or "")
        failures, total = parser(message)
        failures = max(failures, len(getattr(task, "errors", []) or []) - before_errors)
        if failures <= 0:
            return result
        if total is not None and total > 0 and failures >= total:
            _set_error(task, message)
        else:
            _set_partial(task, message)
        return result

    setattr(core, name, wrapped)
    return wrapped


def apply(core: Any) -> dict[str, Any]:
    if getattr(core, "_task_truthfulness_final_v1", False):
        return {"task_truthfulness_final_installed": True}
    exports: dict[str, Any] = {}
    exports.update(_apply_restore_truthfulness(core))

    def channels_parser(message: str) -> tuple[int, int | None]:
        return _number(message, r"Greške:\s*(\d+)"), _number(message, r"YouTube kanali provereni:\s*(\d+)") or None

    def youtube_audio_parser(message: str) -> tuple[int, int | None]:
        return _number(message, r"(\d+)\s+grešaka"), _number(message, r"Audio analiza završena:\s*(\d+)\s+YouTube") or None

    def fingerprint_parser(message: str) -> tuple[int, int | None]:
        missing = _number(message, r"bez audio izvora\s+(\d+)")
        failures = _number(message, r"greške\s+(\d+)")
        ready = _number(message, r"spreman:\s*(\d+)/")
        total = _number(message, r"spreman:\s*\d+/(\d+)")
        return missing + failures, total or (ready + missing + failures if ready + missing + failures else None)

    def save_folder_parser(message: str) -> tuple[int, int | None]:
        failures = _number(message, r"greške\s*(\d+)")
        m = re.search(r"Sačuvano u folder:\s*(\d+)\s*/\s*(\d+)", message, flags=re.IGNORECASE)
        return failures, int(m.group(2)) if m else None

    def batch_finder_parser(message: str) -> tuple[int, int | None]:
        m = re.search(
            r"Batch provera završena:\s*(\d+)\s+pronađeno,\s*(\d+)\s+nije pronađeno,\s*(\d+)\s+grešaka",
            message,
            flags=re.IGNORECASE,
        )
        if not m:
            return 0, None
        found, not_found, errors = (int(m.group(1)), int(m.group(2)), int(m.group(3)))
        return errors, found + not_found + errors

    for name, parser in (
        ("scan_owned_youtube_channels", channels_parser),
        ("analyze_owned_youtube_audio", youtube_audio_parser),
        ("build_suno_fingerprint_index", fingerprint_parser),
        ("save_songs_to_folder", save_folder_parser),
        ("song_finder_analyze_batch_task", batch_finder_parser),
    ):
        wrapped = _wrap_message_failures(core, name, parser)
        if wrapped is not None:
            exports[name] = wrapped

    if hasattr(core, "import_urls"):
        original = core.import_urls
        def import_urls_truthful(task: Any, urls: list[str]) -> None:
            before = len(getattr(task, "errors", []) or [])
            original(task, urls)
            if _cancelled(task):
                return
            failures = len(getattr(task, "errors", []) or []) - before
            requested = len([u for u in urls if str(u).strip()])
            if failures:
                (_set_error if requested and failures >= requested else _set_partial)(task, str(getattr(task, "message", "")))
        core.import_urls = import_urls_truthful
        exports["import_urls"] = import_urls_truthful

    if hasattr(core, "rescan_watched_folders"):
        original = core.rescan_watched_folders
        def rescan_truthful(task: Any) -> None:
            original(task)
            if _cancelled(task):
                return
            message = str(getattr(task, "message", "") or "")
            failures = _number(message, r"grešaka\s+(\d+)")
            total = _number(message, r"Zapamćeni folderi provereni:\s*(\d+)")
            if failures:
                (_set_error if total and failures >= total else _set_partial)(task, message)
        core.rescan_watched_folders = rescan_truthful
        exports["rescan_watched_folders"] = rescan_truthful

    if hasattr(core, "advanced_quality_task"):
        original = core.advanced_quality_task
        def quality_truthful(task: Any, options: dict[str, Any]) -> None:
            original(task, options)
            if _cancelled(task):
                return
            message = str(getattr(task, "message", "") or "")
            m = re.search(r"Analiza kvaliteta završena:\s*(\d+)\s*/\s*(\d+)", message, flags=re.IGNORECASE)
            if not m:
                return
            done, total = int(m.group(1)), int(m.group(2))
            if total > 0 and done < total:
                (_set_error if done == 0 else _set_partial)(task, message)
        core.advanced_quality_task = quality_truthful
        exports["advanced_quality_task"] = quality_truthful

    if hasattr(core, "stem_task"):
        original = core.stem_task
        def stem_truthful(task: Any, options: dict[str, Any]) -> None:
            ids = [str(x) for x in (options.get("ids") or []) if str(x)]
            if not ids and options.get("id"):
                ids = [str(options.get("id"))]
            eligible = 0
            for sid in ids:
                try:
                    song = core.DB.get_song(sid) or {}
                    raw = str(song.get("local_audio") or song.get("local_wav") or "").strip()
                    if raw and Path(raw).is_file():
                        eligible += 1
                except Exception:
                    pass
            original(task, options)
            if _cancelled(task):
                return
            message = str(getattr(task, "message", "") or "")
            if ids and eligible < len(ids):
                (_set_error if eligible == 0 else _set_partial)(task, message)
        core.stem_task = stem_truthful
        exports["stem_task"] = stem_truthful

    if hasattr(core, "transcription_task"):
        original = core.transcription_task
        def transcription_truthful(task: Any, options: dict[str, Any]) -> None:
            original(task, options)
            if _cancelled(task):
                return
            message = str(getattr(task, "message", "") or "")
            m = re.search(r"završena:\s*(\d+)\s*/\s*(\d+)", message, flags=re.IGNORECASE)
            if not m:
                return
            done, total = int(m.group(1)), int(m.group(2))
            if total > 0 and done < total:
                (_set_error if done == 0 else _set_partial)(task, message)
        core.transcription_task = transcription_truthful
        exports["transcription_task"] = transcription_truthful

    if hasattr(core, "relocate_files_task"):
        original = core.relocate_files_task
        def relocate_truthful(task: Any, options: dict[str, Any]) -> None:
            original(task, options)
            if _cancelled(task):
                return
            message = str(getattr(task, "message", "") or "")
            m = re.search(r"pronađeno\s+(\d+)\s+od\s+(\d+)\s+nestalih", message, flags=re.IGNORECASE)
            if m:
                fixed, missing = int(m.group(1)), int(m.group(2))
                if missing and fixed < missing:
                    (_set_error if fixed == 0 else _set_partial)(task, message)
        core.relocate_files_task = relocate_truthful
        exports["relocate_files_task"] = relocate_truthful

    core._task_truthfulness_final_v1 = True
    exports["task_truthfulness_final_installed"] = True
    return exports
