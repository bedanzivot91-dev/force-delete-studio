from __future__ import annotations

"""Truthful restore-result guards.

A restore can successfully replace the SQLite database and still fail to copy
or relink one or more media files. Such a result must never be exposed through
the HTTP API as a clean success. The underlying restore keeps its rollback and
best-effort behaviour; this layer only validates its returned result.
"""

from typing import Any


def _restore_failures(result: Any) -> tuple[int, list[str]]:
    if not isinstance(result, dict):
        return 0, []
    count = 0
    details: list[str] = []

    for key in ("skipped_files", "failed_files", "errors_count", "files_failed"):
        try:
            value = int(result.get(key) or 0)
        except (TypeError, ValueError):
            value = 0
        if value > 0:
            count += value
            details.append(f"{key}={value}")

    for key in ("skipped", "errors", "failed"):
        value = result.get(key)
        if isinstance(value, list) and value:
            count += len(value)
            for item in value[:4]:
                if isinstance(item, dict):
                    text = str(item.get("error") or item.get("message") or item.get("target") or item)
                else:
                    text = str(item)
                if text:
                    details.append(text)

    # Some restore helpers return warnings for non-fatal cleanup only; those do
    # not invalidate the restored data. Only explicit skipped/failed/errors are
    # treated as incomplete restore here.
    return count, details


def _guard_restore(name: str, original: Any):
    def guarded(*args: Any, **kwargs: Any):
        result = original(*args, **kwargs)
        failures, details = _restore_failures(result)
        if failures:
            suffix = " | ".join(details[:6])
            raise RuntimeError(
                f"Backup baza je obrađena, ali vraćanje NIJE potpuno: {failures} stavki/fajlova nije vraćeno."
                + (f" Detalji: {suffix}" if suffix else "")
            )
        return result
    guarded.__name__ = getattr(original, "__name__", name)
    guarded.__doc__ = getattr(original, "__doc__", None)
    return guarded


def apply(core: Any) -> dict[str, Any]:
    if getattr(core, "_restore_truthfulness_final_v1", False):
        return {"restore_truthfulness_final_installed": True}
    exports: dict[str, Any] = {}
    for name in ("restore_backup_file", "restore_cloud_backup"):
        original = getattr(core, name, None)
        if original is None:
            continue
        guarded = _guard_restore(name, original)
        setattr(core, name, guarded)
        exports[name] = guarded
    core._restore_truthfulness_final_v1 = True
    exports["restore_truthfulness_final_installed"] = True
    return exports
