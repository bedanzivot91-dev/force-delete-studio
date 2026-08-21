from __future__ import annotations

"""Correctness fixes for operations that previously swallowed a real partial failure.

These patches deliberately change only result truthfulness.  Best-effort cleanup,
network fallback and disconnect handling remain untouched.
"""

import json
import os
import shutil
import tempfile
import zipfile
from pathlib import Path
from typing import Any

import advanced_features as _advanced


_PATCHED = False
_ORIGINAL_DELETE_SONG = None
_ORIGINAL_DELETE_DERIVED = None


def restore_cloud_backup_strict(
    db: Any,
    package: Path,
    *,
    restore_files: bool = True,
    restore_root: Path | None = None,
    preserve_original_paths: bool = False,
) -> dict[str, Any]:
    """Restore a cloud backup without counting an unlinked derived file as success."""
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
                try:
                    target.parent.mkdir(parents=True, exist_ok=True)
                    with archive.open(arc) as src, temp.open("wb") as out:
                        shutil.copyfileobj(src, out)
                    expected = str(item.get("sha256") or "")
                    if expected and _advanced.sha256_file(temp) != expected:
                        raise RuntimeError("SHA-256 se ne poklapa")
                    os.replace(temp, target)

                    # This call MUST be part of the successful transaction result.
                    # The old implementation swallowed this exception and then
                    # incremented files_restored, leaving a restored file whose DB
                    # path still pointed to the old location.
                    if field.startswith("derived:"):
                        db.update_derived_file_path(int(field.split(":", 1)[1]), str(target.resolve()))
                    elif song_id and field:
                        db.update_song_files(song_id, **{field: str(target.resolve())})
                    restored += 1
                except Exception as exc:
                    temp.unlink(missing_ok=True)
                    # If the file was already moved into place but the DB link
                    # failed, remove that new copy so result + database + disk do
                    # not disagree about whether the item was restored.
                    try:
                        if target.exists() and target.is_file():
                            target.unlink()
                    except OSError as cleanup_exc:
                        skipped.append(
                            {
                                "arcname": arc,
                                "target": str(target),
                                "error": f"{exc}; rollback brisanje nije uspelo: {cleanup_exc}",
                            }
                        )
                        continue
                    skipped.append({"arcname": arc, "target": str(target), "error": str(exc)})

    return {
        "path": str(package),
        "database_restored": True,
        "files_restored": restored,
        "restore_root": str(controlled_root),
        "skipped": skipped,
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
    # Preserve order but do not attempt to delete the same physical file twice.
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
    if _PATCHED:
        return {
            "restore_cloud_backup": restore_cloud_backup_strict,
            "truthfulness_fixes_installed": True,
        }

    # Replace both the core's imported alias and the source module attribute so
    # later imports receive the same corrected behavior.
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
    _PATCHED = True
    return {
        "restore_cloud_backup": restore_cloud_backup_strict,
        "truthfulness_fixes_installed": True,
    }
