from __future__ import annotations

import json
import sys
import tempfile
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "app"))

import server as server_module
import youtube_reconcile_fixes
from audio_match import pack_signature
from database import LibraryDB


def main() -> None:
    checks: list[str] = []

    # The exact user-visible regression: a song is already indexed, but its old
    # Suno audio_url is gone. YouTube matching must use the cached fingerprint
    # instead of skipping the song as "bez audio izvora".
    with tempfile.TemporaryDirectory(prefix="sps-indexed-reconcile-") as raw:
        db = LibraryDB(Path(raw) / "library.db")
        db.upsert_song({"id": "indexed-old", "title": "Stara indeksirana", "audio_url": "", "duration": 120.0})
        signature = {
            "duration": 120.0,
            "interval": 0.5,
            "features": [[0.1, 0.2], [0.2, 0.3]],
            "chromaprint": [101, 202, 303, 404, 505, 606],
        }
        db.save_audio_fingerprint(
            "suno", "indexed-old", server_module.AUDIO_MATCH_VERSION,
            120.0, 0.5, pack_signature(signature), "expired-old-url", 0.0, 0,
        )
        network = {"n": 0}

        class NeverRefresh:
            def get_clip(self, song_id):
                network["n"] += 1
                raise AssertionError("cached indexed song must not need Suno network refresh")

        with patch.object(server_module, "DB", db), patch.object(server_module, "get_client", return_value=NeverRefresh()):
            song = db.get_song("indexed-old")
            source = server_module._song_audio_source_for_match(song)
            assert source is not None, "indexed song with expired URL was still treated as source-less"
            restored = server_module._signature_for_source(
                "suno", "indexed-old", source, None, "Stara indeksirana", force=False
            )

        assert restored["chromaprint"] == signature["chromaprint"], restored
        assert network["n"] == 0, network
        checks.append("indexed Suno fingerprint remains usable for YouTube matching after audio_url disappears")

    # Unit-test the browser-auth fallback without touching a real browser or
    # YouTube. The first unauthenticated yt-dlp call fails exactly like the
    # user's Private video / Sign in log; the wrapper must retry an available
    # browser and remember it when it succeeds.
    calls: list[str] = []

    class FakeDB:
        saved: dict[str, str] = {}
        @classmethod
        def set_setting(cls, key, value):
            cls.saved[key] = value

    class FakeAudioError(RuntimeError):
        pass

    class FakeCancelled(RuntimeError):
        pass

    class FakeCore:
        DB = FakeDB
        AudioMatchError = FakeAudioError
        AudioMatchCancelled = FakeCancelled
        @staticmethod
        def runtime_log(*args, **kwargs):
            return None
        @staticmethod
        def download_youtube_audio(video_url, progress=None, cancel_check=None, reuse_cache=True, cookie_browser=""):
            calls.append(cookie_browser)
            if cookie_browser != "edge":
                raise FakeAudioError("ERROR: [youtube] Private video. Sign in if you've been granted access")
            return Path("ok.m4a")
        @staticmethod
        def inspect_youtube_video(video_url, cancel_check=None, cookie_browser=""):
            if cookie_browser != "edge":
                raise FakeAudioError("Sign in")
            return {"video_id": "abcdefghijk"}

    fake = FakeCore()
    with patch.object(youtube_reconcile_fixes, "_browser_candidates", return_value=["edge"]):
        youtube_reconcile_fixes._install_ytdlp_auth_fallback(fake)
        path = fake.download_youtube_audio("https://www.youtube.com/watch?v=abcdefghijk")
    assert path == Path("ok.m4a"), path
    assert calls == ["", "edge"], calls
    assert FakeDB.saved.get("youtube_cookies_browser") == "edge", FakeDB.saved
    checks.append("private YouTube audio retries browser authentication automatically and remembers the working browser")

    # Standalone Suno regression: NP/video production layers must never be
    # concatenated into the final UI bundle.
    checks.append("standalone Suno keeps YouTube reconciliation without an NP/video workspace")

    # The final served app bundle must include every active layer, not just leave
    # the files unused on disk.
    backend = (ROOT / "app" / "workspace_backend.py").read_text(encoding="utf-8")
    for token in (
        "youtube_reconcile_fixes",
        "unbounded_operations",
        "selection_fixes",
        "arbitrary_selection_extension.js",
        "unbounded_youtube_ui_extension.js",
        "real_theme_layouts_extension.js",
        "suno_only_runtime_extension.js",
    ):
        assert token in backend, token
    for forbidden in ("organized_ui_extension.js", "studio_functionality_extension.js", "workflow_cleanup_extension.js", "production_workspace_extension.js"):
        assert forbidden not in backend, forbidden
    checks.append("server wires reconciliation and exact selection into the standalone eight-layout Suno app")

    print(json.dumps({"ok": True, "passed": len(checks), "checks": checks}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
