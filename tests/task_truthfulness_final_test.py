from __future__ import annotations

import threading
from types import SimpleNamespace
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "app"))
from task_truthfulness_final import apply


class Task:
    def __init__(self):
        self.status = "running"
        self.message = ""
        self.errors = []
        self.cancel_event = threading.Event()
        self.total = 0
    def finish(self, message, status="done"):
        self.message = str(message); self.status = status
    def finish_partial(self, message):
        self.message = str(message); self.status = "partial"
    def fail(self, message):
        self.message = str(message); self.status = "error"


def test_youtube_channel_errors_cannot_finish_green():
    def legacy(task, options):
        task.finish("YouTube kanali provereni: 3. Novi videi: 2. Greške: 1.")
    core = SimpleNamespace(scan_owned_youtube_channels=legacy)
    apply(core)
    task = Task(); core.scan_owned_youtube_channels(task, {})
    assert task.status == "partial", task.status


def test_all_youtube_channel_failures_are_error():
    def legacy(task, options):
        task.finish("YouTube kanali provereni: 2. Novi videi: 0. Greške: 2.")
    core = SimpleNamespace(scan_owned_youtube_channels=legacy)
    apply(core)
    task = Task(); core.scan_owned_youtube_channels(task, {})
    assert task.status == "error", task.status


def test_watched_folder_error_without_new_song_is_partial():
    def legacy(task):
        task.finish("Zapamćeni folderi provereni: 3. Fajlova 10, novih pesama 0, osveženih 9, duplikata 0, grešaka 1.")
    core = SimpleNamespace(rescan_watched_folders=legacy)
    apply(core)
    task = Task(); core.rescan_watched_folders(task)
    assert task.status == "partial", task.status


def test_unresolved_relocation_cannot_finish_green():
    def legacy(task, options):
        task.finish("Pretraga završena: pronađeno 2 od 5 nestalih fajlova.")
    core = SimpleNamespace(relocate_files_task=legacy)
    apply(core)
    task = Task(); core.relocate_files_task(task, {})
    assert task.status == "partial", task.status


def test_complete_relocation_stays_done():
    def legacy(task, options):
        task.finish("Pretraga završena: pronađeno 5 od 5 nestalih fajlova.")
    core = SimpleNamespace(relocate_files_task=legacy)
    apply(core)
    task = Task(); core.relocate_files_task(task, {})
    assert task.status == "done", task.status


def main():
    test_youtube_channel_errors_cannot_finish_green()
    test_all_youtube_channel_failures_are_error()
    test_watched_folder_error_without_new_song_is_partial()
    test_unresolved_relocation_cannot_finish_green()
    test_complete_relocation_stays_done()
    print("task_truthfulness_final_test: PASS — item failures cannot be reported as clean success")


if __name__ == "__main__":
    main()
