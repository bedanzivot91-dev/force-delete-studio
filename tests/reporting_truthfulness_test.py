from __future__ import annotations

import sys
import tempfile
import types
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "app"))

import truthfulness_fixes as fixes


class Task:
    def __init__(self) -> None:
        self.status = "running"
        self.message = ""
    def finish(self, message: str, status: str = "done") -> None:
        self.status = status; self.message = str(message)
    def finish_partial(self, message: str) -> None:
        self.status = "partial"; self.message = str(message)
    def fail(self, message: str) -> None:
        self.status = "error"; self.message = str(message)


class DB:
    def __init__(self, local_file: Path) -> None:
        self.total = 10
        self.local_file = local_file
        self.watch = None
    def count_songs(self) -> int: return self.total
    def delete_song(self, *_a, **_k): return None
    def delete_derived_file(self, *_a, **_k): return None
    def get_derived_file(self, *_a, **_k): return None
    def get_song(self, song_id: str):
        if song_id == "a": return {"id": "a", "local_audio": str(self.local_file), "local_wav": ""}
        if song_id == "b": return {"id": "b", "local_audio": "", "local_wav": ""}
        return None
    def export_rows(self): return [{"id": "a"}, {"id": "b"}]
    def update_watched_folder_scan(self, folder: str, files: int, added: int) -> None:
        self.watch = (folder, files, added)


def main() -> None:
    with tempfile.TemporaryDirectory(prefix="truthful-report-") as raw:
        root = Path(raw)
        audio = root / "a.mp3"; audio.write_bytes(b"audio")
        db = DB(audio)

        def legacy_import(_task, _folder, remember=True):
            db.total += 1  # exactly one genuinely new DB row
            return {"files": 3, "added": 3, "updated": 0, "skipped": 0}

        def legacy_panako(task, _options):
            task.finish("Panako je indeksirao 1 fajl.")

        core = types.SimpleNamespace(
            DB=db,
            restore_cloud_backup=lambda *_a, **_k: None,
            _import_local_folder_impl=legacy_import,
            v3_panako_index_task=legacy_panako,
        )

        old_patched = fixes._PATCHED
        old_song = fixes._ORIGINAL_DELETE_SONG
        old_derived = fixes._ORIGINAL_DELETE_DERIVED
        old_batch = fixes._ORIGINAL_PROCESS_AUDIO_BATCH
        old_import = fixes._ORIGINAL_LOCAL_IMPORT_IMPL
        old_panako = fixes._ORIGINAL_PANAKO_INDEX_TASK
        orig_delete_song = DB.delete_song
        orig_delete_derived = DB.delete_derived_file
        fixes._PATCHED = False
        try:
            fixes.apply(core)
            result = core._import_local_folder_impl(Task(), str(root), remember=True)
            assert result["added"] == 1, result
            assert result["updated"] == 2, result
            assert db.watch is not None and db.watch[2] == 1, db.watch

            task = Task()
            core.v3_panako_index_task(task, {"ids": ["a", "b"]})
            assert task.status == "partial", task.status
            assert "Preskočeno 1/2" in task.message, task.message
        finally:
            DB.delete_song = orig_delete_song
            DB.delete_derived_file = orig_delete_derived
            fixes._PATCHED = old_patched
            fixes._ORIGINAL_DELETE_SONG = old_song
            fixes._ORIGINAL_DELETE_DERIVED = old_derived
            fixes._ORIGINAL_PROCESS_AUDIO_BATCH = old_batch
            fixes._ORIGINAL_LOCAL_IMPORT_IMPL = old_import
            fixes._ORIGINAL_PANAKO_INDEX_TASK = old_panako

    print("reporting_truthfulness_test: PASS — local import counts real inserts and Panako exposes skipped songs")


if __name__ == "__main__":
    main()
