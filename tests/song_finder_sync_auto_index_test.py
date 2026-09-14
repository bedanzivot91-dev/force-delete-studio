from __future__ import annotations

import sys
import threading
import types
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "app"))

from song_finder_runtime_fix import _install_sync_auto_index


class Task:
    def __init__(self) -> None:
        self.cancel_event = threading.Event()
        self.status = "running"
        self.message = ""
        self.logs: list[tuple[str, str]] = []

    def log(self, message: str, level: str = "info") -> None:
        self.logs.append((level, str(message)))

    def finish(self, message: str, status: str = "done") -> None:
        self.status = status
        self.message = str(message)

    def finish_partial(self, message: str) -> None:
        self.status = "partial"
        self.message = str(message)


def test_sync_waits_for_missing_fingerprints() -> None:
    state = {"missing": 3, "index_calls": 0, "sync_finish_seen": False}

    def original_sync(task, _options):
        task.finish("Suno sync završen")
        state["sync_finish_seen"] = True

    def status():
        return {
            "songs_not_indexed": state["missing"],
            "songs_without_any_source": 0,
        }

    def index_task(_task, options):
        assert options.get("finish_task") is False
        state["index_calls"] += 1
        state["missing"] = 0

    core = types.SimpleNamespace(
        sync_library=original_sync,
        check_new_songs=original_sync,
        song_finder_status=status,
        song_finder_index_task=index_task,
    )
    _install_sync_auto_index(core)

    task = Task()
    core.sync_library(task, {})

    assert state["sync_finish_seen"] is True
    assert state["index_calls"] == 1
    assert task.status == "done", task.status
    assert "Audio indeks je kompletan" in task.message, task.message


def test_sync_is_partial_when_index_stays_incomplete() -> None:
    state = {"missing": 2, "index_calls": 0}

    def original_sync(task, _options):
        task.finish("Suno sync završen")

    def status():
        return {
            "songs_not_indexed": state["missing"],
            "songs_without_any_source": 1,
        }

    def index_task(_task, _options):
        state["index_calls"] += 1
        # Deliberately leave fingerprints missing.

    core = types.SimpleNamespace(
        sync_library=original_sync,
        check_new_songs=original_sync,
        song_finder_status=status,
        song_finder_index_task=index_task,
    )
    _install_sync_auto_index(core)

    task = Task()
    core.sync_library(task, {})

    assert state["index_calls"] == 1
    assert task.status == "partial", task.status
    assert "NIJE kompletan" in task.message, task.message
    assert "2 pesama" in task.message, task.message


def test_suno_row_without_cached_audio_url_still_triggers_index_refresh() -> None:
    state = {"indexed": False, "index_calls": 0}

    class FakeDB:
        def export_rows(self):
            # Genuine Suno-like ID, deliberately with no local file/audio_url.
            return [{"id": "9fcb6ad1-1111-4444-9999-0123456789ab", "title": "Stara Suno pesma", "audio_url": ""}]

        def get_audio_fingerprint(self, source_type, song_id, version):
            assert source_type == "suno"
            return {"payload": b"ok"} if state["indexed"] else None

    def original_sync(task, _options):
        task.finish("Suno sync završen")

    def status():
        # This reproduces the legacy blind spot: the core status used to say
        # zero because there was no cached source URL.
        return {"songs_not_indexed": 0, "songs_without_any_source": 1}

    def index_task(_task, options):
        assert options.get("finish_task") is False
        state["index_calls"] += 1
        state["indexed"] = True

    core = types.SimpleNamespace(
        DB=FakeDB(),
        AUDIO_MATCH_VERSION="v4",
        _song_finder_source_cheap=lambda _song: (None, False),
        sync_library=original_sync,
        check_new_songs=original_sync,
        song_finder_status=status,
        song_finder_index_task=index_task,
    )
    _install_sync_auto_index(core)

    task = Task()
    core.sync_library(task, {})

    assert state["index_calls"] == 1, "Suno row without cached audio_url must still be offered to the indexer"
    assert task.status == "done", task.status
    assert "Audio indeks je kompletan" in task.message, task.message


def main() -> None:
    test_sync_waits_for_missing_fingerprints()
    test_sync_is_partial_when_index_stays_incomplete()
    test_suno_row_without_cached_audio_url_still_triggers_index_refresh()
    print("song_finder_sync_auto_index_test: PASS — Suno sync cannot report complete while recognition fingerprints are missing, including rows without cached audio_url")


if __name__ == "__main__":
    main()
