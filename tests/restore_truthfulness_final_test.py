from __future__ import annotations

import sys
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "app"))

from restore_truthfulness_final import apply


def test_clean_restore_passes() -> None:
    core = SimpleNamespace(restore_backup_file=lambda *a, **k: {"database_restored": True, "files_restored": 7, "skipped_files": 0})
    apply(core)
    result = core.restore_backup_file("backup.zip")
    assert result["files_restored"] == 7


def test_skipped_file_count_rejects_clean_success() -> None:
    core = SimpleNamespace(restore_backup_file=lambda *a, **k: {"database_restored": True, "files_restored": 6, "skipped_files": 1})
    apply(core)
    try:
        core.restore_backup_file("backup.zip")
    except RuntimeError as exc:
        text = str(exc)
        assert "NIJE potpuno" in text and "skipped_files=1" in text, text
    else:
        raise AssertionError("restore with skipped_files must not return clean success")


def test_skipped_detail_list_rejects_clean_success() -> None:
    core = SimpleNamespace(restore_cloud_backup=lambda *a, **k: {
        "database_restored": True,
        "files_restored": 1,
        "skipped": [{"target": "song.mp3", "error": "disk full"}],
    })
    apply(core)
    try:
        core.restore_cloud_backup("cloud.zip")
    except RuntimeError as exc:
        text = str(exc)
        assert "NIJE potpuno" in text and "disk full" in text, text
    else:
        raise AssertionError("cloud restore with skipped file must not return clean success")


def main() -> None:
    test_clean_restore_passes()
    test_skipped_file_count_rejects_clean_success()
    test_skipped_detail_list_rejects_clean_success()
    print("restore_truthfulness_final_test: PASS — incomplete restore cannot be returned as clean success")


if __name__ == "__main__":
    main()
