from __future__ import annotations
import json, sys, tempfile, time
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
import server as server_module
from database import LibraryDB
from audio_match import pack_signature


def fake_signature(duration=30.0):
    return {"duration": duration, "interval": 0.5, "features": [[1.0]]}


def main():
    checks = []

    # -- 1. Pagination reads ALL pages Suno reports via has_more -- it must
    # not stop at the OLD default/ceiling (100/1000 pages). A fake client
    # that reports has_more=True until page 150 proves the sync doesn't
    # stop early; nothing here asserts or assumes any specific total. --
    with tempfile.TemporaryDirectory(prefix="sps-scale-pages-") as raw:
        db = LibraryDB(Path(raw) / "test.db")
        call_count = {"n": 0}
        TOTAL_FAKE_PAGES = 150  # deliberately > the old 100-page default

        class FakeClient:
            def list_all_projects(self, max_pages=100):
                return []
            def list_library_cursor(self, cursor, liked=False):
                call_count["n"] += 1
                n = call_count["n"]
                items = [{"id": f"p{n}", "title": f"Pesma {n}", "audio_url": f"https://cdn.suno.com/p{n}.mp3"}]
                has_more = n < TOTAL_FAKE_PAGES
                return items, (f"cursor{n}" if has_more else None), has_more, "cursor"

        task = server_module.TaskState("sync", "sync")
        with patch.object(server_module, "DB", db), patch.object(server_module, "get_client", return_value=FakeClient()):
            server_module.sync_library(task, {"include_workspaces": False, "resume": True})
        assert call_count["n"] == TOTAL_FAKE_PAGES, call_count
        checks.append(f"sync_library reads all {TOTAL_FAKE_PAGES} pages Suno reports, past the old 100-page default")
        assert db.count_songs() == TOTAL_FAKE_PAGES
        checks.append("every page's song is actually stored -- no silent truncation from an artificial page cap")

    # -- 2/3. Incremental indexing: N already-fingerprinted remote songs are
    # never re-extracted; only genuinely new songs trigger extract_signature. --
    with tempfile.TemporaryDirectory(prefix="sps-scale-incr-") as raw:
        db = LibraryDB(Path(raw) / "test.db")
        N = 12
        for i in range(N):
            song_id = f"old{i}"
            db.upsert_song({"id": song_id, "title": f"Stara {i}", "audio_url": f"https://cdn.suno.com/{song_id}.mp3"})
            db.save_audio_fingerprint("suno", song_id, server_module.AUDIO_MATCH_VERSION, 30.0, 0.5, pack_signature(fake_signature()), "whatever-remote-identity", 0.0, 0)

        extract_calls = []
        def fake_extract(source, progress=None, cancel_check=None, tempo=1.0):
            extract_calls.append(str(source))
            return fake_signature()

        with patch.object(server_module, "DB", db), patch.object(server_module, "extract_signature", side_effect=fake_extract):
            task1 = server_module.TaskState("index", "index")
            server_module.song_finder_index_task(task1, {})
        assert extract_calls == [], f"re-indexing unchanged already-fingerprinted songs must not extract again, got {extract_calls}"
        checks.append(f"{N} already-indexed songs stay untouched on a second index run (0 extract_signature calls)")

        for i in range(3):
            song_id = f"new{i}"
            db.upsert_song({"id": song_id, "title": f"Nova {i}", "audio_url": f"https://cdn.suno.com/{song_id}.mp3"})
        extract_calls.clear()
        with patch.object(server_module, "DB", db), patch.object(server_module, "extract_signature", side_effect=fake_extract):
            task2 = server_module.TaskState("index", "index")
            server_module.song_finder_index_task(task2, {})
        assert len(extract_calls) == 3, extract_calls
        checks.append("adding 3 new songs to a 12-song indexed library triggers extract_signature for exactly those 3, not all 15")

    # -- 4. A changed LOCAL audio file (content/identity actually different)
    # is re-extracted; an untouched local file is not. --
    with tempfile.TemporaryDirectory(prefix="sps-scale-changed-") as raw:
        db = LibraryDB(Path(raw) / "test.db")
        unchanged_file = Path(raw) / "unchanged.wav"
        unchanged_file.write_bytes(b"RIFF" + b"\x00" * 100)
        changed_file = Path(raw) / "changed.wav"
        changed_file.write_bytes(b"RIFF" + b"\x00" * 100)
        db.upsert_song({"id": "unchanged-song", "title": "Ista"})
        db.update_song_files("unchanged-song", local_wav=str(unchanged_file))
        db.upsert_song({"id": "changed-song", "title": "Promenjena"})
        db.update_song_files("changed-song", local_wav=str(changed_file))

        extract_calls = []
        def fake_extract(source, progress=None, cancel_check=None, tempo=1.0):
            extract_calls.append(str(source))
            return fake_signature()

        with patch.object(server_module, "DB", db), patch.object(server_module, "extract_signature", side_effect=fake_extract):
            task3 = server_module.TaskState("index", "index")
            server_module.song_finder_index_task(task3, {})
        assert len(extract_calls) == 2
        checks.append("both local songs are extracted on first index")

        time.sleep(1.1)  # ensure a strictly different mtime
        changed_file.write_bytes(b"RIFF" + b"\x11" * 250)  # real content+size change
        extract_calls.clear()
        with patch.object(server_module, "DB", db), patch.object(server_module, "extract_signature", side_effect=fake_extract):
            task4 = server_module.TaskState("index", "index")
            server_module.song_finder_index_task(task4, {})
        assert extract_calls == [str(changed_file)], extract_calls
        checks.append("only the local file whose content actually changed is re-extracted; the untouched one is skipped")

    # -- 5. Duplicate titles, different Suno IDs: both persist and are
    # indexed as two distinct songs, never collapsed into one. --
    with tempfile.TemporaryDirectory(prefix="sps-scale-dup-title-") as raw:
        db = LibraryDB(Path(raw) / "test.db")
        db.upsert_song({"id": "dupA", "title": "Ista Pesma", "audio_url": "https://cdn.suno.com/dupA.mp3"})
        db.upsert_song({"id": "dupB", "title": "Ista Pesma", "audio_url": "https://cdn.suno.com/dupB.mp3"})
        assert db.count_songs() == 2
        rows = db.export_rows()
        assert {r["id"] for r in rows} == {"dupA", "dupB"}
        assert all(r["title"] == "Ista Pesma" for r in rows)
        checks.append("two songs sharing an identical title but different Suno IDs both persist as separate records")
        with patch.object(server_module, "DB", db):
            status = server_module.song_finder_status()
        assert status["songs_total"] == 2 and status["songs_with_audio"] == 2
        checks.append("both same-titled songs are counted as indexable sources independently")

    # -- 6. Interrupted sync resumes from checkpoint without duplicating
    # already-stored songs (upsert is idempotent on Suno ID). --
    with tempfile.TemporaryDirectory(prefix="sps-scale-resume-") as raw:
        db = LibraryDB(Path(raw) / "test.db")
        call_count2 = {"n": 0}
        CANCEL_AFTER = 4
        TOTAL = 9

        class InterruptingClient:
            def __init__(self, task):
                self._task = task
            def list_all_projects(self, max_pages=100):
                return []
            def list_library_cursor(self, cursor, liked=False):
                call_count2["n"] += 1
                n = call_count2["n"]
                if n > CANCEL_AFTER:
                    self._task.cancel_event.set()
                items = [{"id": f"r{n}", "title": f"Resume {n}", "audio_url": f"https://cdn.suno.com/r{n}.mp3"}]
                has_more = n < TOTAL
                return items, (f"rcursor{n}" if has_more else None), has_more, "cursor"

        task5 = server_module.TaskState("sync", "sync")
        client1 = InterruptingClient(task5)
        with patch.object(server_module, "DB", db), patch.object(server_module, "get_client", return_value=client1):
            server_module.sync_library(task5, {"include_workspaces": False, "resume": True})
        stored_after_first_run = db.count_songs()
        assert 0 < stored_after_first_run < TOTAL, stored_after_first_run
        checks.append(f"an interrupted sync stops early (stored {stored_after_first_run}/{TOTAL}) instead of silently finishing")

        task6 = server_module.TaskState("sync", "sync")

        class ResumingClient:
            def list_all_projects(self, max_pages=100):
                return []
            def list_library_cursor(self, cursor, liked=False):
                n = int(cursor.replace("rcursor", "")) + 1 if cursor else 1
                items = [{"id": f"r{n}", "title": f"Resume {n}", "audio_url": f"https://cdn.suno.com/r{n}.mp3"}]
                has_more = n < TOTAL
                return items, (f"rcursor{n}" if has_more else None), has_more, "cursor"

        with patch.object(server_module, "DB", db), patch.object(server_module, "get_client", return_value=ResumingClient()):
            server_module.sync_library(task6, {"include_workspaces": False, "resume": True})
        assert db.count_songs() == TOTAL, db.count_songs()
        checks.append("resuming after the interruption completes the sync to the full real total, with no duplicate rows (upsert by Suno ID)")

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
