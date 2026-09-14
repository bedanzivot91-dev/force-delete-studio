from __future__ import annotations

import tempfile
import types
from pathlib import Path
from unittest import mock

import sys
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "app"))

import atomic_delete_fixes as fixes


class FakeDB:
    def __init__(self, path: Path):
        self.path = path
        self.present = True
        self.db_delete_called = False

    def get_song(self, song_id: str):
        if song_id == "s1" and self.present:
            return {"id": "s1", "local_audio": str(self.path), "derived_files": []}
        return None

    def delete_song(self, song_id: str, delete_files: bool = False):
        self.db_delete_called = True
        row = self.get_song(song_id)
        if row:
            self.present = False
        return row

    def get_derived_file(self, file_id: int):
        return None

    def delete_derived_file(self, file_id: int, delete_from_disk: bool = False):
        return None


def main() -> None:
    with tempfile.TemporaryDirectory(prefix="atomic-delete-") as raw:
        path = Path(raw) / "song.mp3"
        path.write_bytes(b"audio")
        db = FakeDB(path)
        core = types.SimpleNamespace(DB=db)
        old = fixes._PATCHED
        fixes._PATCHED = False
        try:
            fixes.apply(core)
            real_replace = __import__("os").replace

            def fail_stage(src, dst):
                if Path(src) == path:
                    raise PermissionError("locked")
                return real_replace(src, dst)

            with mock.patch("atomic_delete_fixes.os.replace", side_effect=fail_stage):
                try:
                    db.delete_song("s1", delete_files=True)
                except RuntimeError as exc:
                    assert "NIJE uklonjena" in str(exc), exc
                else:
                    raise AssertionError("locked file must abort deletion")

            assert db.present is True, "database row must remain when file staging fails"
            assert db.db_delete_called is False, "DB deletion must not run before filesystem staging succeeds"
            assert path.exists() and path.read_bytes() == b"audio"
        finally:
            fixes._PATCHED = old

    print("atomic_delete_fixes_test: PASS — locked files cannot delete the library row first")


if __name__ == "__main__":
    main()
