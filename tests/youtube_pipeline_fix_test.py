from __future__ import annotations
import json, sys, tempfile, threading
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
import server as server_module
from database import LibraryDB
from audio_match import pack_signature


def main():
    checks = []

    with tempfile.TemporaryDirectory(prefix="sps-yt-status-") as raw:
        db = LibraryDB(Path(raw) / "test.db")
        for i in range(50):
            db.upsert_song({"id": f"song{i}", "title": f"Pesma {i}", "audio_url": f"https://cdn.suno.com/{i}.mp3", "duration": 120})
        with patch.object(server_module, "DB", db):
            status = server_module.song_finder_status()
        assert status["songs_total"] == 50, status
        assert status["songs_with_audio"] == 50, status
        assert status["songs_remote_only"] == 50, status
        checks.append("50 remote-only songs (0 local files) all count as songs_with_audio")
        assert not (status["songs_with_audio"] == 0 and status["songs_total"] > 0)
        checks.append("song_finder_status never reports 0 available sources when audio_url exists")

        db.upsert_song({"id": "orphan", "title": "Orphan", "audio_url": "", "local_wav": "", "duration": 90})
        orphan_signature = {"duration": 90.0, "interval": 0.5, "features": [[1.0]], "chromaprint": [101, 202, 303, 404]}
        db.save_audio_fingerprint("suno", "orphan", server_module.AUDIO_MATCH_VERSION, 90.0, 0.5, pack_signature(orphan_signature), "old-identity", 0.0, 0)
        with patch.object(server_module, "DB", db):
            status2 = server_module.song_finder_status()
        assert status2["songs_indexed_without_current_source"] >= 1, status2
        assert status2["songs_indexed"] >= 1, status2
        checks.append("valid cached fingerprint without current source still counts as indexed")

    with tempfile.TemporaryDirectory(prefix="sps-yt-status2-") as raw2:
        db2 = LibraryDB(Path(raw2) / "test.db")
        db2.upsert_song({"id": "no-source", "title": "Bez izvora", "audio_url": "", "local_wav": "", "duration": 60})
        api_calls = {"n": 0}
        class FakeClient:
            def get_clip(self, song_id):
                api_calls["n"] += 1
                return {"id": song_id, "audio_url": "https://cdn.suno.com/refreshed.mp3"}
        with patch.object(server_module, "DB", db2), patch.object(server_module, "get_client", return_value=FakeClient()):
            server_module.song_finder_status()
        assert api_calls["n"] == 0, f"song_finder_status must not call Suno API, got {api_calls['n']}"
        checks.append("song_finder_status stays network-free")

    with tempfile.TemporaryDirectory(prefix="sps-yt-copy-") as raw3:
        db3 = LibraryDB(Path(raw3) / "test.db")
        song = {"id": "abcd1234", "title": "Puna Pesma", "audio_url": "https://cdn.suno.com/abcd1234.mp3", "source_url": "https://suno.com/song/abcd1234"}
        db3.upsert_song(song)
        target_root = Path(raw3) / "OBRAĐENO NA YOUTUBE"

        def fake_download_file(url, target, **kwargs):
            Path(target).write_bytes(b"ID3" + b"\x00" * 4096)

        class FakeClient2:
            def get_clip(self, song_id):
                return {"id": song_id, "audio_url": song["audio_url"]}
            def download_file(self, url, target, **kwargs):
                fake_download_file(url, target, **kwargs)

        video = {"video_id": "yt123", "video_url": "https://www.youtube.com/watch?v=yt123", "channel_title": "Moj Kanal", "title": "Video naslov"}
        with patch.object(server_module, "DB", db3), \
             patch.object(server_module, "get_client", return_value=FakeClient2()), \
             patch.object(server_module, "get_youtube_processed_dir", return_value=target_root), \
             patch.object(server_module, "probe_audio", return_value={"duration": 91.2}):
            result = server_module.copy_song_to_published_folder(db3.get_song("abcd1234"), video, status="complete")

        assert result["has_full_audio"] is True, result
        checks.append("remote-only song downloads full original via refreshed Suno URL")
        folder = Path(result["folder"])
        assert folder.name == "yt123" and folder.parent.name.startswith("Puna Pesma [")
        assert folder.parent.parent.name == "complete" and folder.parent.parent.parent.name == "Moj Kanal"
        checks.append("published folder structure is correct")
        assert (folder / "YouTube.url").exists()
        shortcut = (folder / "YouTube.url").read_text(encoding="utf-8")
        assert "[InternetShortcut]" in shortcut and video["video_url"] in shortcut
        checks.append("YouTube.url is a valid shortcut")
        manifest = json.loads((folder / "match.json").read_text(encoding="utf-8"))
        assert manifest["suno_song_id"] == "abcd1234" and manifest["youtube_video_id"] == "yt123"
        checks.append("match.json contains Suno and YouTube IDs")

        with patch.object(server_module, "DB", db3), \
             patch.object(server_module, "get_client", return_value=FakeClient2()), \
             patch.object(server_module, "get_youtube_processed_dir", return_value=target_root), \
             patch.object(server_module, "probe_audio", return_value={"duration": 91.2}):
            result2 = server_module.copy_song_to_published_folder(db3.get_song("abcd1234"), video, status="complete")
        assert result2["folder"] == result["folder"]
        assert len(list(target_root.rglob("match.json"))) == 1
        checks.append("rescan updates existing publication folder without duplication")

    assert "possible" not in server_module.YOUTUBE_PROCESSED_STATUSES
    assert "title_only" not in server_module.YOUTUBE_PROCESSED_STATUSES
    assert set(server_module.YOUTUBE_PROCESSED_STATUSES) == {"complete", "almost_complete", "partial", "short_clip"}
    checks.append("only audio-confirmed statuses are eligible for auto-copy")

    with tempfile.TemporaryDirectory(prefix="sps-yt-matrix-") as raw4:
        db4 = LibraryDB(Path(raw4) / "test.db")
        db4.upsert_song({"id": "s1", "title": "Pesma", "source_url": "https://suno.com/song/s1"})
        matrix = db4.youtube_publication_matrix()
        assert matrix["rows"][0]["song"].get("source_url") == "https://suno.com/song/s1"
        checks.append("publication matrix exposes source_url")

    with tempfile.TemporaryDirectory(prefix="sps-yt-no-full-preindex-") as raw5:
        db5 = LibraryDB(Path(raw5) / "test.db")
        for i in range(25):
            db5.upsert_song({"id": f"r{i}", "title": f"Remote {i}", "audio_url": f"https://cdn.suno.com/r{i}.mp3", "duration": 120})
        api_calls2 = {"n": 0}
        class NeverCallClient:
            def get_clip(self, song_id):
                api_calls2["n"] += 1
                raise AssertionError("YouTube preflight must not refresh every Suno song")
        task = server_module.TaskState("youtube_audio_owned", "yt")
        with patch.object(server_module, "DB", db5), patch.object(server_module, "get_client", return_value=NeverCallClient()):
            server_module.song_finder_index_task(task, {"force": False, "finish_task": False})
        assert api_calls2["n"] == 0, api_calls2
        assert any("nije uslov" in str(row.get("message") or "") for row in task.logs), task.logs
    checks.append("YouTube preflight stays lightweight and network-free")

    unbounded_source = (ROOT / "app" / "unbounded_operations.py").read_text(encoding="utf-8")
    preflight_source = (ROOT / "app" / "youtube_preflight_final_fix.py").read_text(encoding="utf-8")
    for token in (
        'missing_before = int(index_before.get("songs_not_indexed") or 0)',
        '"required_for_youtube": True',
        'Nijedna Suno pesma nema napravljen audio-otisak',
    ):
        assert token in unbounded_source, token
    assert 'explicit_required = bool(opts.get("required_for_youtube"))' in preflight_source
    assert 'and not bool(opts.get("finish_task", True))' in preflight_source
    checks.append("real owned-channel audio scan explicitly requests missing fingerprints")

    with tempfile.TemporaryDirectory(prefix="sps-yt-index-resume-") as raw6:
        db6 = LibraryDB(Path(raw6) / "test.db")
        db6.upsert_song({"id": "cached-only", "title": "Cached only", "audio_url": "", "duration": 90})
        signature = {"duration": 90.0, "interval": 0.5, "features": [[1.0]], "chromaprint": [11, 22, 33, 44]}
        db6.save_audio_fingerprint("suno", "cached-only", server_module.AUDIO_MATCH_VERSION, 90.0, 0.5, pack_signature(signature), "old-url", 0.0, 0)
        network = {"n": 0}
        class NoRefreshClient:
            def get_clip(self, song_id):
                network["n"] += 1
                raise AssertionError("cached fingerprint should not need get_clip")
        task2 = server_module.TaskState("song_finder_index", "index")
        with patch.object(server_module, "DB", db6), \
             patch.object(server_module, "get_client", return_value=NoRefreshClient()), \
             patch.object(server_module, "get_fingerprint_index", return_value=None):
            server_module.song_finder_index_task(task2, {"force": False, "parallelism": 2})
        assert network["n"] == 0, network
        assert task2.status in ("done", "partial"), task2.as_dict()
        checks.append("explicit re-index reuses valid cached fingerprint without network refresh")

    songs = [{"id": f"s{i}", "title": f"Pesma {i}", "duration": 180.0} for i in range(3000)]
    video = {"video_id": "abcdefghijk", "title": "Pesma 2222 - Official Video", "duration": 180.0, "channel_id": "UCtest"}
    calls = {"n": 0}
    def fake_match(song, video_row, owned):
        calls["n"] += 1
        score = 96.0 if song.get("id") == "s2222" else 10.0
        return {"score": score, "match_type": "owned_publication", "reason": "test"}
    with patch.object(server_module, "match_song_to_video", side_effect=fake_match):
        found_song, found_match = server_module._best_song_match(video, songs, {"UCtest"})
    assert found_song and found_song["id"] == "s2222", (found_song, found_match)
    assert calls["n"] < 500, f"strong match should not do 3000 expensive comparisons, got {calls['n']}"
    checks.append(f"strong match in 3000-song library needs only {calls['n']} expensive comparisons")

    class AbortedWriter:
        def write(self, payload):
            raise ConnectionAbortedError(10053, "client closed")
    handler = object.__new__(server_module.Handler)
    handler.wfile = AbortedWriter()
    handler.send_response = lambda *args, **kwargs: None
    handler.send_header = lambda *args, **kwargs: None
    handler.end_headers = lambda *args, **kwargs: None
    server_module.Handler._send_json(handler, {"ok": True, "test": "disconnect"})
    checks.append("localhost client disconnect does not crash request thread")

    started = []
    started_event = threading.Event()
    class DummyTask:
        status = "running"
    def fake_start_task(task_type, title, runner, **kwargs):
        started.append(task_type)
        started_event.set()
        return DummyTask()
    with patch.object(server_module, "ACTIVE_TASK", None), patch.object(server_module, "start_task", side_effect=fake_start_task):
        server_module.start_automatic_youtube_pipeline(delay_seconds=0)
        assert started_event.wait(2.0), "automatic metadata scan did not start"
    assert started == ["youtube_owned"], started
    checks.append("connecting YouTube starts only lightweight metadata scan")

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
