from __future__ import annotations

import tempfile
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
    def legacy(task, options): task.finish("YouTube kanali provereni: 3. Novi videi: 2. Greške: 1.")
    core = SimpleNamespace(scan_owned_youtube_channels=legacy); apply(core)
    task = Task(); core.scan_owned_youtube_channels(task, {})
    assert task.status == "partial", task.status


def test_all_youtube_channel_failures_are_error():
    def legacy(task, options): task.finish("YouTube kanali provereni: 2. Novi videi: 0. Greške: 2.")
    core = SimpleNamespace(scan_owned_youtube_channels=legacy); apply(core)
    task = Task(); core.scan_owned_youtube_channels(task, {})
    assert task.status == "error", task.status


def test_watched_folder_error_without_new_song_is_partial():
    def legacy(task): task.finish("Zapamćeni folderi provereni: 3. Fajlova 10, novih pesama 0, osveženih 9, duplikata 0, grešaka 1.")
    core = SimpleNamespace(rescan_watched_folders=legacy); apply(core)
    task = Task(); core.rescan_watched_folders(task)
    assert task.status == "partial", task.status


def test_unresolved_relocation_cannot_finish_green():
    def legacy(task, options): task.finish("Pretraga završena: pronađeno 2 od 5 nestalih fajlova.")
    core = SimpleNamespace(relocate_files_task=legacy); apply(core)
    task = Task(); core.relocate_files_task(task, {})
    assert task.status == "partial", task.status


def test_complete_relocation_stays_done():
    def legacy(task, options): task.finish("Pretraga završena: pronađeno 5 od 5 nestalih fajlova.")
    core = SimpleNamespace(relocate_files_task=legacy); apply(core)
    task = Task(); core.relocate_files_task(task, {})
    assert task.status == "done", task.status


def test_quality_summary_partial_and_error():
    def partial_legacy(task, options): task.finish("Analiza kvaliteta završena: 2/3. Prosečni LUFS -14.0.")
    core = SimpleNamespace(advanced_quality_task=partial_legacy); apply(core)
    task = Task(); core.advanced_quality_task(task, {})
    assert task.status == "partial", task.status

    def error_legacy(task, options): task.finish("Analiza kvaliteta završena: 0/3. Nema rezultata.")
    core = SimpleNamespace(advanced_quality_task=error_legacy); apply(core)
    task = Task(); core.advanced_quality_task(task, {})
    assert task.status == "error", task.status


def test_stem_uses_input_eligibility_not_output_file_count():
    with tempfile.TemporaryDirectory(prefix="stem-truth-") as raw:
        good = Path(raw) / "song.mp3"; good.write_bytes(b"audio")
        songs = {
            "a": {"id":"a", "local_audio": str(good)},
            "b": {"id":"b", "local_audio": str(Path(raw) / "missing.mp3")},
        }
        class DB:
            def get_song(self, sid): return songs.get(sid)
        def legacy(task, options):
            # Four stems from ONE processed song must not be mistaken for four successful songs.
            task.finish("Stem obrada je završena: 4 izlaznih fajlova za 2 pesama.")
        core = SimpleNamespace(DB=DB(), stem_task=legacy); apply(core)
        task = Task(); core.stem_task(task, {"ids":["a","b"]})
        assert task.status == "partial", task.status


def test_batch_finder_all_errors_is_error():
    def legacy(task, options): task.finish("Batch provera završena: 0 pronađeno, 0 nije pronađeno, 3 grešaka.")
    core = SimpleNamespace(song_finder_analyze_batch_task=legacy); apply(core)
    task = Task(); core.song_finder_analyze_batch_task(task, {})
    assert task.status == "error", task.status


def test_batch_finder_some_errors_is_partial():
    def legacy(task, options): task.finish("Batch provera završena: 1 pronađeno, 1 nije pronađeno, 1 grešaka.")
    core = SimpleNamespace(song_finder_analyze_batch_task=legacy); apply(core)
    task = Task(); core.song_finder_analyze_batch_task(task, {})
    assert task.status == "partial", task.status


def main():
    test_youtube_channel_errors_cannot_finish_green()
    test_all_youtube_channel_failures_are_error()
    test_watched_folder_error_without_new_song_is_partial()
    test_unresolved_relocation_cannot_finish_green()
    test_complete_relocation_stays_done()
    test_quality_summary_partial_and_error()
    test_stem_uses_input_eligibility_not_output_file_count()
    test_batch_finder_all_errors_is_error()
    test_batch_finder_some_errors_is_partial()
    print("task_truthfulness_final_test: PASS — partial/all-failed jobs cannot be reported as clean success")


if __name__ == "__main__":
    main()
