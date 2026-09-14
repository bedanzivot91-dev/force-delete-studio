from __future__ import annotations

"""Final task-status guards.

Several mature jobs already logged/count individual failures but still called
``finish()`` unconditionally.  This layer preserves their work and messages,
then upgrades the final state to partial/error when the message or error list
proves that the requested operation was incomplete.
"""

import re
from typing import Any, Callable


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

    def channels_parser(message: str) -> tuple[int, int | None]:
        failures = _number(message, r"Greške:\s*(\d+)")
        total = _number(message, r"YouTube kanali provereni:\s*(\d+)")
        return failures, total or None

    def youtube_audio_parser(message: str) -> tuple[int, int | None]:
        failures = _number(message, r"(\d+)\s+grešaka")
        total = _number(message, r"Audio analiza završena:\s*(\d+)\s+YouTube")
        return failures, total or None

    def fingerprint_parser(message: str) -> tuple[int, int | None]:
        missing = _number(message, r"bez audio izvora\s+(\d+)")
        failures = _number(message, r"greške\s+(\d+)")
        ready = _number(message, r"spreman:\s*(\d+)/")
        total = _number(message, r"spreman:\s*\d+/(\d+)")
        return missing + failures, total or (ready + missing + failures if ready + missing + failures else None)

    def generic_errors_parser(message: str) -> tuple[int, int | None]:
        failures = _number(message, r"greške\s*(\d+)")
        total = _number(message, r"(\d+)/(\d+)")
        # _number returns the first capture only; derive denominator separately.
        m = re.search(r"(\d+)\s*/\s*(\d+)", message)
        return failures, int(m.group(2)) if m else None

    for name, parser in (
        ("scan_owned_youtube_channels", channels_parser),
        ("analyze_owned_youtube_audio", youtube_audio_parser),
        ("build_suno_fingerprint_index", fingerprint_parser),
        ("save_songs_to_folder", generic_errors_parser),
        ("song_finder_analyze_batch_task", generic_errors_parser),
    ):
        wrapped = _wrap_message_failures(core, name, parser)
        if wrapped is not None:
            exports[name] = wrapped

    # URL import records malformed URLs directly in task.errors.
    if hasattr(core, "import_urls"):
        original = core.import_urls
        def import_urls_truthful(task: Any, urls: list[str]) -> None:
            before = len(getattr(task, "errors", []) or [])
            original(task, urls)
            if _cancelled(task):
                return
            failures = len(getattr(task, "errors", []) or []) - before
            if failures:
                (_set_error if failures >= len([u for u in urls if str(u).strip()]) else _set_partial)(task, str(getattr(task, "message", "")))
        core.import_urls = import_urls_truthful
        exports["import_urls"] = import_urls_truthful

    # Watched-folder rescan's legacy condition only marked partial when at
    # least one NEW song was added. A failed folder with zero new songs could
    # therefore finish green. The message already contains the real count.
    if hasattr(core, "rescan_watched_folders"):
        original = core.rescan_watched_folders
        def rescan_truthful(task: Any) -> None:
            original(task)
            if _cancelled(task):
                return
            message = str(getattr(task, "message", ""))
            failures = _number(message, r"grešaka\s+(\d+)")
            total = _number(message, r"Zapamćeni folderi provereni:\s*(\d+)")
            if failures:
                (_set_error if total and failures >= total else _set_partial)(task, message)
        core.rescan_watched_folders = rescan_truthful
        exports["rescan_watched_folders"] = rescan_truthful

    # Quality analysis already appends real processing exceptions to task.errors.
    if hasattr(core, "advanced_quality_task"):
        original = core.advanced_quality_task
        def quality_truthful(task: Any, options: dict[str, Any]) -> None:
            before = len(getattr(task, "errors", []) or [])
            original(task, options)
            if _cancelled(task): return
            failures = len(getattr(task, "errors", []) or []) - before
            if failures:
                total = int(getattr(task, "total", 0) or 0)
                (_set_error if total and failures >= total else _set_partial)(task, str(getattr(task, "message", "")))
        core.advanced_quality_task = quality_truthful
        exports["advanced_quality_task"] = quality_truthful

    # Stem/transcription can skip input without throwing. Their summary carries
    # the actual completed/output count, so incomplete requested batches are partial.
    for name, pattern in (
        ("stem_task", r"(\d+)\s+izlaznih fajlova za\s+(\d+)\s+pesama"),
        ("transcription_task", r"završena:\s*(\d+)\s*/\s*(\d+)"),
    ):
        original = getattr(core, name, None)
        if original is None:
            continue
        def make_wrapper(fn: Any, regex: str):
            def wrapper(task: Any, options: dict[str, Any]) -> None:
                fn(task, options)
                if _cancelled(task): return
                message = str(getattr(task, "message", ""))
                m = re.search(regex, message, flags=re.IGNORECASE)
                if not m: return
                done, total = int(m.group(1)), int(m.group(2))
                if total > 0 and done < total:
                    (_set_error if done == 0 else _set_partial)(task, message)
            return wrapper
        wrapped = make_wrapper(original, pattern)
        setattr(core, name, wrapped)
        exports[name] = wrapped

    if hasattr(core, "relocate_files_task"):
        original = core.relocate_files_task
        def relocate_truthful(task: Any, options: dict[str, Any]) -> None:
            original(task, options)
            if _cancelled(task): return
            message = str(getattr(task, "message", ""))
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
