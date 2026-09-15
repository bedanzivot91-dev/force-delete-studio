from __future__ import annotations

"""Correctness fixes for operations that previously swallowed a real partial failure.

These patches deliberately change result truthfulness, not normal best-effort
cleanup/fallback behavior. A task may finish green only when the requested work
actually completed without hidden item failures.
"""

import json
import os
import shutil
import tempfile
import uuid
import zipfile
from pathlib import Path
from typing import Any

import advanced_features as _advanced


_PATCHED = False
_ORIGINAL_DELETE_SONG = None
_ORIGINAL_DELETE_DERIVED = None
_ORIGINAL_PROCESS_AUDIO_BATCH = None
_ORIGINAL_LOCAL_IMPORT_IMPL = None
_ORIGINAL_PANAKO_INDEX_TASK = None


def restore_cloud_backup_strict(
    db: Any,
    package: Path,
    *,
    restore_files: bool = True,
    restore_root: Path | None = None,
    preserve_original_paths: bool = False,
) -> dict[str, Any]:
    package = package.expanduser().resolve()
    if not package.is_file():
        raise RuntimeError("Cloud backup ZIP ne postoji.")
    controlled_root = (restore_root or (package.parent / "Vraceni_cloud_backup" / package.stem)).expanduser().resolve()

    with tempfile.TemporaryDirectory(prefix="suno_cloud_restore_") as tmp_raw:
        tmp = Path(tmp_raw)
        with zipfile.ZipFile(package, "r") as archive:
            names = set(archive.namelist())
            if "manifest.json" not in names:
                raise RuntimeError("Backup nema manifest.json.")
            manifest = json.loads(archive.read("manifest.json").decode("utf-8"))
            db_name = str(
                manifest.get("database")
                or ("data/suno_biblioteka.db.dpapi" if manifest.get("encrypted") else "data/suno_biblioteka.db")
            )
            if db_name not in names:
                raise RuntimeError("Backup ne sadrži bazu.")
            raw = archive.read(db_name)
            if manifest.get("encrypted"):
                if os.name != "nt":
                    raise RuntimeError(
                        "Ovaj backup je zaštićen Windows DPAPI sistemom i može da se vrati samo na Windows nalogu koji ga je napravio."
                    )
                raw = _advanced._dpapi(raw, False)

            db_file = tmp / "suno_biblioteka.db"
            db_file.write_bytes(raw)
            db.restore_from(db_file)

            restored = 0
            skipped: list[dict[str, str]] = []
            warnings: list[dict[str, str]] = []
            controlled_root.mkdir(parents=True, exist_ok=True)
            for item in (manifest.get("files") or []) if restore_files else []:
                arc = str(item.get("arcname") or "")
                target_raw = str(item.get("original_path") or "")
                song_id = str(item.get("song_id") or "")
                field = str(item.get("field") or "")
                if not arc or arc not in names:
                    skipped.append({"arcname": arc, "target": target_raw, "error": "Fajl ne postoji u ZIP-u"})
                    continue

                safe_name = _advanced.sanitize_filename(Path(target_raw or arc).name, 140)
                if preserve_original_paths and target_raw:
                    target = Path(target_raw).expanduser().resolve()
                else:
                    safe_field = _advanced.sanitize_filename(field.replace(":", "-"), 50) or "fajl"
                    target = controlled_root / (_advanced.sanitize_filename(song_id, 80) or "bez-id") / safe_field / safe_name

                temp = target.with_suffix(target.suffix + ".restore-part")
                previous: Path | None = None
                placed_new = False
                try:
                    target.parent.mkdir(parents=True, exist_ok=True)
                    with archive.open(arc) as src, temp.open("wb") as out:
                        shutil.copyfileobj(src, out)
                    expected = str(item.get("sha256") or "")
                    if expected and _advanced.sha256_file(temp) != expected:
                        raise RuntimeError("SHA-256 se ne poklapa")

                    if target.exists():
                        previous = target.with_name(target.name + ".restore-previous-" + uuid.uuid4().hex)
                        os.replace(target, previous)
                    os.replace(temp, target)
                    placed_new = True

                    if field.startswith("derived:"):
                        db.update_derived_file_path(int(field.split(":", 1)[1]), str(target.resolve()))
                    elif song_id and field:
                        db.update_song_files(song_id, **{field: str(target.resolve())})

                    restored += 1
                    if previous is not None and previous.exists():
                        try:
                            previous.unlink()
                        except OSError as cleanup_exc:
                            warnings.append({
                                "arcname": arc,
                                "target": str(previous),
                                "warning": f"Stara rollback kopija nije obrisana: {cleanup_exc}",
                            })
                except Exception as exc:
                    temp.unlink(missing_ok=True)
                    rollback_errors: list[str] = []
                    if placed_new:
                        try:
                            if target.exists() and target.is_file():
                                target.unlink()
                        except OSError as cleanup_exc:
                            rollback_errors.append(f"nova kopija nije obrisana: {cleanup_exc}")
                    if previous is not None and previous.exists():
                        try:
                            os.replace(previous, target)
                        except OSError as restore_exc:
                            rollback_errors.append(f"prethodni fajl nije vraćen: {restore_exc}")
                    error_text = str(exc)
                    if rollback_errors:
                        error_text += "; rollback: " + " | ".join(rollback_errors)
                    skipped.append({"arcname": arc, "target": str(target), "error": error_text})

    return {
        "path": str(package),
        "database_restored": True,
        "files_restored": restored,
        "restore_root": str(controlled_root),
        "skipped": skipped,
        "warnings": warnings,
    }


def _song_file_paths(song: dict[str, Any]) -> list[Path]:
    paths: list[Path] = []
    for key in ("local_audio", "local_wav", "local_video", "local_cover", "local_lyrics", "local_lrc", "local_srt"):
        raw = str(song.get(key) or "").strip()
        if raw:
            paths.append(Path(raw))
    for item in song.get("derived_files") or []:
        raw = str(item.get("path") or "").strip()
        if raw:
            paths.append(Path(raw))
    seen: set[str] = set()
    unique: list[Path] = []
    for path in paths:
        key = str(path.expanduser().absolute()).casefold()
        if key not in seen:
            seen.add(key)
            unique.append(path)
    return unique


def apply(core: Any) -> dict[str, Any]:
    global _PATCHED, _ORIGINAL_DELETE_SONG, _ORIGINAL_DELETE_DERIVED
    global _ORIGINAL_PROCESS_AUDIO_BATCH, _ORIGINAL_LOCAL_IMPORT_IMPL, _ORIGINAL_PANAKO_INDEX_TASK
    if _PATCHED:
        return {"restore_cloud_backup": restore_cloud_backup_strict, "truthfulness_fixes_installed": True}

    _advanced.restore_cloud_backup = restore_cloud_backup_strict
    core.restore_cloud_backup = restore_cloud_backup_strict

    db_cls = type(core.DB)
    _ORIGINAL_DELETE_SONG = db_cls.delete_song
    _ORIGINAL_DELETE_DERIVED = db_cls.delete_derived_file

    def delete_song_truthful(self: Any, song_id: str, delete_files: bool = False) -> dict[str, Any] | None:
        if not delete_files:
            return _ORIGINAL_DELETE_SONG(self, song_id, delete_files=False)
        song = self.get_song(song_id)
        if not song:
            return None
        paths = _song_file_paths(song)
        deleted = _ORIGINAL_DELETE_SONG(self, song_id, delete_files=False)
        errors: list[str] = []
        for path in paths:
            try:
                if path.exists() and path.is_file():
                    path.unlink()
            except Exception as exc:
                errors.append(f"{path}: {exc}")
        if errors:
            raise RuntimeError(
                "Pesma je uklonjena iz biblioteke, ali neki lokalni fajlovi nisu mogli da se obrišu: "
                + " | ".join(errors[:8])
            )
        return deleted

    def delete_derived_truthful(self: Any, file_id: int, delete_from_disk: bool = False) -> None:
        record = self.get_derived_file(file_id)
        _ORIGINAL_DELETE_DERIVED(self, file_id, delete_from_disk=False)
        if not delete_from_disk or not record:
            return
        raw = str(record.get("path") or "").strip()
        if not raw:
            return
        path = Path(raw)
        try:
            if path.exists() and path.is_file():
                path.unlink()
        except Exception as exc:
            raise RuntimeError(
                f"Zapis izvedenog fajla je uklonjen iz baze, ali fajl nije mogao da se obriše sa diska: {path}: {exc}"
            ) from exc

    db_cls.delete_song = delete_song_truthful
    db_cls.delete_derived_file = delete_derived_truthful

    if hasattr(core, "process_audio_batch_task"):
        _ORIGINAL_PROCESS_AUDIO_BATCH = core.process_audio_batch_task
        def process_audio_batch_truthful(task: Any, song_ids: list[str], options: dict[str, Any]) -> None:
            before_errors = len(getattr(task, "errors", []) or [])
            unique_count = len(dict.fromkeys(str(x) for x in song_ids if str(x)))
            _ORIGINAL_PROCESS_AUDIO_BATCH(task, song_ids, options)
            cancel_event = getattr(task, "cancel_event", None)
            if cancel_event is not None and hasattr(cancel_event, "is_set") and cancel_event.is_set():
                return
            new_errors = max(0, len(getattr(task, "errors", []) or []) - before_errors)
            if new_errors <= 0:
                return
            message = str(getattr(task, "message", "") or f"Masovna audio obrada: {new_errors} grešaka.")
            if unique_count and new_errors >= unique_count and hasattr(task, "fail"):
                task.fail(message)
            elif hasattr(task, "finish_partial"):
                task.finish_partial(message)
        core.process_audio_batch_task = process_audio_batch_truthful

    if hasattr(core, "_import_local_folder_impl"):
        _ORIGINAL_LOCAL_IMPORT_IMPL = core._import_local_folder_impl
        def import_local_folder_truthful(task: Any, folder: str, remember: bool = True) -> dict[str, Any]:
            before = int(core.DB.count_songs())
            result = dict(_ORIGINAL_LOCAL_IMPORT_IMPL(task, folder, remember=remember) or {})
            after = int(core.DB.count_songs())
            actual_added = max(0, after - before)
            files = max(0, int(result.get("files") or 0))
            skipped = max(0, int(result.get("skipped") or 0))
            processed = max(0, files - skipped)
            result["added"] = min(processed, actual_added)
            result["updated"] = max(0, processed - int(result["added"]))
            if remember and hasattr(core.DB, "update_watched_folder_scan"):
                core.DB.update_watched_folder_scan(str(Path(folder).expanduser().resolve()), files, int(result["added"]))
            return result
        core._import_local_folder_impl = import_local_folder_truthful

    if hasattr(core, "v3_panako_index_task"):
        _ORIGINAL_PANAKO_INDEX_TASK = core.v3_panako_index_task
        def panako_index_truthful(task: Any, options: dict[str, Any]) -> None:
            ids = list(dict.fromkeys(str(x) for x in options.get("ids") or [] if str(x)))
            if not ids:
                ids = [str(row.get("id") or "") for row in core.DB.export_rows() if str(row.get("id") or "")]
            missing: list[str] = []
            for song_id in ids:
                song = core.DB.get_song(song_id) or {}
                available = False
                for key in ("local_audio", "local_wav"):
                    raw = str(song.get(key) or "").strip()
                    if raw and Path(raw).expanduser().is_file():
                        available = True
                        break
                if not available:
                    missing.append(song_id)
            _ORIGINAL_PANAKO_INDEX_TASK(task, options)
            if missing:
                message = str(getattr(task, "message", "") or "Panako indeksiranje je završeno.")
                message += f" Preskočeno {len(missing)}/{len(ids)} pesama jer nemaju lokalni MP3/WAV."
                if len(missing) >= len(ids) and hasattr(task, "fail"):
                    task.fail(message)
                elif hasattr(task, "finish_partial"):
                    task.finish_partial(message)
        core.v3_panako_index_task = panako_index_truthful

    _PATCHED = True
    return {
        "restore_cloud_backup": restore_cloud_backup_strict,
        "process_audio_batch_task": getattr(core, "process_audio_batch_task", None),
        "_import_local_folder_impl": getattr(core, "_import_local_folder_impl", None),
        "v3_panako_index_task": getattr(core, "v3_panako_index_task", None),
        "truthfulness_fixes_installed": True,
    }
