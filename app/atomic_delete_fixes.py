from __future__ import annotations

"""Filesystem/database deletion safety.

A local file is first atomically renamed beside itself. Only after every file
has been staged does the database row get removed. If staging or the database
delete fails, staged files are restored to their original names. This prevents
a locked file from making a song disappear from the library before the program
has proved it can safely perform the requested physical deletion.
"""

import os
import uuid
from pathlib import Path
from typing import Any

_PATCHED = False


def _stage(paths: list[Path]) -> list[tuple[Path, Path]]:
    staged: list[tuple[Path, Path]] = []
    try:
        for original in paths:
            if not original.exists() or not original.is_file():
                continue
            pending = original.with_name(original.name + ".sps-delete-pending-" + uuid.uuid4().hex)
            os.replace(original, pending)
            staged.append((original, pending))
        return staged
    except Exception:
        _rollback(staged)
        raise


def _rollback(staged: list[tuple[Path, Path]]) -> list[str]:
    errors: list[str] = []
    for original, pending in reversed(staged):
        if not pending.exists():
            continue
        try:
            os.replace(pending, original)
        except Exception as exc:
            errors.append(f"{pending} -> {original}: {exc}")
    return errors


def _finish(staged: list[tuple[Path, Path]]) -> list[str]:
    errors: list[str] = []
    for _original, pending in staged:
        if not pending.exists():
            continue
        try:
            pending.unlink()
        except Exception as exc:
            errors.append(f"{pending}: {exc}")
    return errors


def _song_paths(song: dict[str, Any]) -> list[Path]:
    values: list[str] = []
    for key in ("local_audio", "local_wav", "local_video", "local_cover", "local_lyrics", "local_lrc", "local_srt"):
        raw = str(song.get(key) or "").strip()
        if raw:
            values.append(raw)
    for item in song.get("derived_files") or []:
        raw = str(item.get("path") or "").strip()
        if raw:
            values.append(raw)
    result: list[Path] = []
    seen: set[str] = set()
    for value in values:
        path = Path(value)
        key = str(path.expanduser().absolute()).casefold()
        if key not in seen:
            seen.add(key)
            result.append(path)
    return result


def apply(core: Any) -> dict[str, Any]:
    global _PATCHED
    if _PATCHED:
        return {"atomic_delete_fixes_installed": True}

    db_cls = type(core.DB)
    previous_delete_song = db_cls.delete_song
    previous_delete_derived = db_cls.delete_derived_file

    def delete_song_atomic(self: Any, song_id: str, delete_files: bool = False):
        if not delete_files:
            return previous_delete_song(self, song_id, delete_files=False)
        song = self.get_song(song_id)
        if not song:
            return None
        staged: list[tuple[Path, Path]] = []
        try:
            staged = _stage(_song_paths(song))
        except Exception as exc:
            raise RuntimeError(
                "Pesma NIJE uklonjena iz biblioteke jer lokalni fajlovi nisu mogli bezbedno da se pripreme za brisanje: "
                + str(exc)
            ) from exc
        try:
            deleted = previous_delete_song(self, song_id, delete_files=False)
        except Exception as exc:
            rollback = _rollback(staged)
            suffix = ("; rollback: " + " | ".join(rollback)) if rollback else ""
            raise RuntimeError(f"Brisanje zapisa iz baze nije uspelo; lokalni fajlovi su vraćeni: {exc}{suffix}") from exc
        cleanup = _finish(staged)
        if cleanup:
            raise RuntimeError(
                "Pesma je uklonjena iz biblioteke, ali privremeno preimenovani fajlovi nisu mogli konačno da se obrišu; podaci nisu izgubljeni: "
                + " | ".join(cleanup[:8])
            )
        return deleted

    def delete_derived_atomic(self: Any, file_id: int, delete_from_disk: bool = False) -> None:
        if not delete_from_disk:
            return previous_delete_derived(self, file_id, delete_from_disk=False)
        record = self.get_derived_file(file_id)
        if not record:
            return
        raw = str(record.get("path") or "").strip()
        paths = [Path(raw)] if raw else []
        try:
            staged = _stage(paths)
        except Exception as exc:
            raise RuntimeError(
                f"Izvedeni fajl NIJE uklonjen iz baze jer fajl nije mogao bezbedno da se pripremi za brisanje: {exc}"
            ) from exc
        try:
            previous_delete_derived(self, file_id, delete_from_disk=False)
        except Exception as exc:
            rollback = _rollback(staged)
            suffix = ("; rollback: " + " | ".join(rollback)) if rollback else ""
            raise RuntimeError(f"Brisanje izvedenog zapisa iz baze nije uspelo; fajl je vraćen: {exc}{suffix}") from exc
        cleanup = _finish(staged)
        if cleanup:
            raise RuntimeError(
                "Zapis izvedenog fajla je uklonjen iz baze, ali privremeni fajl nije mogao konačno da se obriše; podaci nisu izgubljeni: "
                + " | ".join(cleanup)
            )

    db_cls.delete_song = delete_song_atomic
    db_cls.delete_derived_file = delete_derived_atomic
    _PATCHED = True
    return {"atomic_delete_fixes_installed": True}
