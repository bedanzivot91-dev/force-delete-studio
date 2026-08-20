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

    # -- Regression case: an arbitrarily-sized remote-only library (0 local
    # files, valid remote Suno audio_url on every song) -- status must NOT
    # be 0/0, whatever the actual song count happens to be. Uses a small N
    # only for test speed; the assertions below never assume any specific
    # library size. --
    with tempfile.TemporaryDirectory(prefix="sps-yt-status-") as raw:
        db = LibraryDB(Path(raw) / "test.db")
        for i in range(50):
            db.upsert_song({"id": f"song{i}", "title": f"Pesma {i}", "audio_url": f"https://cdn.suno.com/{i}.mp3", "duration": 120})
        with patch.object(server_module, "DB", db):
            status = server_module.song_finder_status()
        assert status["songs_total"] == 50, status
        assert status["songs_with_audio"] == 50, status
        assert status["songs_remote_only"] == 50, status
        checks.append("50 remote-only songs (0 local files) all count as songs_with_audio -- the exact regression shape")
        assert not (status["songs_with_audio"] == 0 and status["songs_total"] > 0), "must never regress to 0/0 with real remote audio available"
        checks.append("song_finder_status never reports 0 available sources when audio_url exists on every song")

        # -- a song with a cached fingerprint but no current source (e.g. the
        # local file was deleted and there's no audio_url) must still count
        # as indexed, not silently disappear from the indexed count. --
        db.upsert_song({"id": "orphan", "title": "Orphan", "audio_url": "", "local_wav": "", "duration": 90})
        db.save_audio_fingerprint("suno", "orphan", server_module.AUDIO_MATCH_VERSION, 90.0, 0.5, b"fake-signature-bytes", "old-identity", 0.0, 0)
        with patch.object(server_module, "DB", db):
            status2 = server_module.song_finder_status()
        assert status2["songs_indexed_without_current_source"] >= 1, status2
        assert status2["songs_indexed"] >= 1, status2
        checks.append("a song with a cached fingerprint but no resolvable current source still counts as indexed")

    # -- song_finder_status must never trigger a Suno API call per song
    # (that would hammer the API and block the UI for a 3000+ song library). --
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
        assert api_calls["n"] == 0, f"song_finder_status must not call the Suno API, got {api_calls['n']} calls"
        checks.append("song_finder_status stays network-free even for songs with neither a local file nor audio_url")

    # -- copy_song_to_published_folder: remote-only song downloads the full
    # original instead of silently finishing with zero copied audio. --
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
        checks.append("a remote-only song (no local file) downloads the full original via the refreshed Suno URL")
        folder = Path(result["folder"])
        assert folder.name == "yt123" and folder.parent.name.startswith("Puna Pesma [")
        assert folder.parent.parent.name == "complete" and folder.parent.parent.parent.name == "Moj Kanal"
        checks.append("folder structure matches <kanal>/<status>/<naslov> [<id>]/<video-id>/")
        assert (folder / "YouTube.url").exists()
        shortcut = (folder / "YouTube.url").read_text(encoding="utf-8")
        assert "[InternetShortcut]" in shortcut and video["video_url"] in shortcut
        checks.append("YouTube.url is a valid Windows internet shortcut pointing at the real video")
        manifest = json.loads((folder / "match.json").read_text(encoding="utf-8"))
        assert manifest["suno_song_id"] == "abcd1234" and manifest["youtube_video_id"] == "yt123"
        checks.append("match.json contains the Suno ID and YouTube ID")

        # -- re-running (rescan) must update the manifest in place, not
        # duplicate the folder. --
        with patch.object(server_module, "DB", db3), \
             patch.object(server_module, "get_client", return_value=FakeClient2()), \
             patch.object(server_module, "get_youtube_processed_dir", return_value=target_root), \
             patch.object(server_module, "probe_audio", return_value={"duration": 91.2}):
            result2 = server_module.copy_song_to_published_folder(db3.get_song("abcd1234"), video, status="complete")
        assert result2["folder"] == result["folder"]
        assert len(list(target_root.rglob("match.json"))) == 1
        checks.append("re-running on the same song+video updates the existing manifest instead of duplicating the folder")

    # -- a title-only (unconfirmed) result must never reach the copy path;
    # this is enforced by the caller only invoking it for confirmed
    # statuses, verified here at the status-gate boundary. --
    assert "possible" not in server_module.YOUTUBE_PROCESSED_STATUSES
    assert "title_only" not in server_module.YOUTUBE_PROCESSED_STATUSES
    assert set(server_module.YOUTUBE_PROCESSED_STATUSES) == {"complete", "almost_complete", "partial", "short_clip"}
    checks.append("only audio-confirmed statuses (complete/almost_complete/partial/short_clip) are eligible for auto-copy")

    # -- the publication matrix must expose source_url so the UI can offer
    # an "Otvori Suno original" button. --
    with tempfile.TemporaryDirectory(prefix="sps-yt-matrix-") as raw4:
        db4 = LibraryDB(Path(raw4) / "test.db")
        db4.upsert_song({"id": "s1", "title": "Pesma", "source_url": "https://suno.com/song/s1"})
        matrix = db4.youtube_publication_matrix()
        assert matrix["rows"][0]["song"].get("source_url") == "https://suno.com/song/s1"
        checks.append("youtube_publication_matrix rows include source_url for the Suno-original button")

    # -- NEW 3.3.2.343 regression: the internal YouTube preflight must NOT
    # build the complete 3000-song fingerprint index. It only requests an
    # index with finish_task=False; that path now returns immediately and lets
    # per-video candidate fingerprints be built on demand. --
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
        checks.append("YouTube audio preflight no longer launches a full-library fingerprint pass before checking videos")

    # -- Explicit indexing remains complete, but a cached fingerprint whose
    # temporary Suno URL disappeared is reused without a network refresh. --
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
        checks.append("explicit re-index reuses a valid cached fingerprint without rediscovering an expired Suno URL")

    # -- Large-library metadata matching must avoid videos x 3000 full fuzzy
    # comparisons when an exact/contained title is already a strong match. --
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
    checks.append(f"strong match in a 3000-song library is found with {calls['n']} expensive comparisons instead of 3000")

    # -- Closing/refreshing the WebView while /api/status is writing must be
    # treated as a client disconnect, not as a server crash (WinError 10053). --
    class AbortedWriter:
        def write(self, payload):
            raise ConnectionAbortedError(10053, "client closed")
    handler = object.__new__(server_module.Handler)
    handler.wfile = AbortedWriter()
    handler.send_response = lambda *args, **kwargs: None
    handler.send_header = lambda *args, **kwargs: None
    handler.end_headers = lambda *args, **kwargs: None
    server_module.Handler._send_json(handler, {"ok": True, "test": "disconnect"})
    checks.append("WinError/ConnectionAbortedError while writing localhost JSON is swallowed instead of killing the request thread")

    # -- OAuth automatic follow-up may scan metadata, but it must never start
    # the old heavy youtube_audio_owned pipeline by itself. --
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
    checks.append("connecting YouTube starts only the lightweight metadata scan, never an automatic full audio/index job")

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
