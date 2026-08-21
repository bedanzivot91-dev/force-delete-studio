from __future__ import annotations

import hashlib
import json
import sys
import tempfile
import types
import zipfile
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "app"
if str(APP) not in sys.path:
    sys.path.insert(0, str(APP))

import truthfulness_fixes as fixes


def make_restore_zip(root: Path, payload: bytes = b"derived-audio") -> Path:
    package = root / "cloud.zip"
    arc = "files/song-1/derived-7/restored.wav"
    manifest = {
        "format": "suno-pesme-cloud-v2",
        "created_at": "2026-08-21T00:00:00+00:00",
        "encrypted": False,
        "database": "data/suno_biblioteka.db",
        "files": [
            {
                "song_id": "song-1",
                "field": "derived:7",
                "arcname": arc,
                "original_path": "C:/old/restored.wav",
                "sha256": hashlib.sha256(payload).hexdigest(),
                "size": len(payload),
            }
        ],
    }
    with zipfile.ZipFile(package, "w", zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("data/suno_biblioteka.db", b"fake-db")
        archive.writestr(arc, payload)
        archive.writestr("manifest.json", json.dumps(manifest))
    return package


class RestoreFailDB:
    def __init__(self) -> None:
        self.restore_called = False
        self.update_called = False

    def restore_from(self, _path: Path) -> None:
        self.restore_called = True

    def update_derived_file_path(self, _file_id: int, _path: str) -> None:
        self.update_called = True
        raise RuntimeError("simulated DB link failure")

    def update_song_files(self, _song_id: str, **_fields) -> None:
        raise AssertionError("derived item must not call update_song_files")


class DeleteDB:
    def __init__(self, song_file: Path, derived_file: Path) -> None:
        self.song_file = song_file
        self.derived_file = derived_file
        self.song_present = True
        self.derived_present = True
        self.song_delete_flag = None
        self.derived_delete_flag = None

    def get_song(self, song_id: str):
        if song_id != "song-1" or not self.song_present:
            return None
        return {
            "id": "song-1",
            "local_audio": str(self.song_file),
            "derived_files": [],
        }

    def delete_song(self, song_id: str, delete_files: bool = False):
        self.song_delete_flag = delete_files
        song = self.get_song(song_id)
        if song:
            self.song_present = False
        return song

    def get_derived_file(self, file_id: int):
        if file_id != 7 or not self.derived_present:
            return None
        return {"id": 7, "path": str(self.derived_file)}

    def delete_derived_file(self, file_id: int, delete_from_disk: bool = False) -> None:
        self.derived_delete_flag = delete_from_disk
        if file_id == 7:
            self.derived_present = False


def test_restore_does_not_claim_success_when_db_link_fails() -> None:
    with tempfile.TemporaryDirectory(prefix="truthful-restore-") as tmp_raw:
        tmp = Path(tmp_raw)
        package = make_restore_zip(tmp)
        output = tmp / "restored"
        db = RestoreFailDB()
        result = fixes.restore_cloud_backup_strict(db, package, restore_root=output)
        assert db.restore_called
        assert db.update_called
        assert result["files_restored"] == 0, result
        assert len(result["skipped"]) == 1, result
        assert "simulated DB link failure" in result["skipped"][0]["error"], result
        assert not any(p.is_file() for p in output.rglob("*")), "failed derived restore must be rolled back from disk"


def test_delete_failures_are_reported_instead_of_silently_ignored() -> None:
    with tempfile.TemporaryDirectory(prefix="truthful-delete-") as tmp_raw:
        tmp = Path(tmp_raw)
        song_file = tmp / "song.mp3"
        derived_file = tmp / "clip.wav"
        song_file.write_bytes(b"song")
        derived_file.write_bytes(b"clip")
        db = DeleteDB(song_file, derived_file)
        core = types.SimpleNamespace(DB=db, restore_cloud_backup=lambda *_a, **_k: None)

        old_patched = fixes._PATCHED
        old_song = fixes._ORIGINAL_DELETE_SONG
        old_derived = fixes._ORIGINAL_DELETE_DERIVED
        fixes._PATCHED = False
        try:
            exports = fixes.apply(core)
            assert exports["truthfulness_fixes_installed"] is True

            with mock.patch.object(Path, "unlink", side_effect=PermissionError("locked song file")):
                try:
                    db.delete_song("song-1", delete_files=True)
                except RuntimeError as exc:
                    assert "nisu mogli da se obrišu" in str(exc)
                    assert "locked song file" in str(exc)
                else:
                    raise AssertionError("delete_song must report filesystem failure")
            assert db.song_delete_flag is False, "original DB delete must not silently perform its own swallowed file delete"
            assert db.song_present is False, "library row deletion is explicit even when physical cleanup reports failure"

            with mock.patch.object(Path, "unlink", side_effect=PermissionError("locked derived file")):
                try:
                    db.delete_derived_file(7, delete_from_disk=True)
                except RuntimeError as exc:
                    assert "nije mogao da se obriše" in str(exc)
                    assert "locked derived file" in str(exc)
                else:
                    raise AssertionError("delete_derived_file must report filesystem failure")
            assert db.derived_delete_flag is False
            assert db.derived_present is False
        finally:
            # Restore the fake class and module globals so this test cannot leak
            # a patched fake method into another test in the same interpreter.
            DeleteDB.delete_song = fixes._ORIGINAL_DELETE_SONG
            DeleteDB.delete_derived_file = fixes._ORIGINAL_DELETE_DERIVED
            fixes._PATCHED = old_patched
            fixes._ORIGINAL_DELETE_SONG = old_song
            fixes._ORIGINAL_DELETE_DERIVED = old_derived


def main() -> None:
    test_restore_does_not_claim_success_when_db_link_fails()
    test_delete_failures_are_reported_instead_of_silently_ignored()
    print("truthfulness_fixes_test: PASS — restore/delete partial failures cannot be reported as clean success")


if __name__ == "__main__":
    main()
