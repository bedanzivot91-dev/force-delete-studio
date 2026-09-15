from __future__ import annotations

import sys
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "app"))

import selection_fixes
import unbounded_operations


class FakeCore:
    YouTubeAPIError = RuntimeError


class FakeDB:
    def __init__(self, count: int):
        self.rows = [{"id": f"song-{i}"} for i in range(count)]

    def count_songs_filtered(self, **kwargs):
        return len(self.rows)

    def list_songs(self, *, limit=1000, offset=0, **kwargs):
        return self.rows[offset:offset + limit]


class FakeSelectionCore:
    def __init__(self, count: int):
        self.DB = FakeDB(count)
        self.messages = []
        self._selection_fixes_v1_installed = False

    def runtime_log(self, message, level="info"):
        self.messages.append((level, message))


def test_unlimited_youtube_pages() -> None:
    calls = {"n": 0}

    def fake_request(url, access_token=""):
        calls["n"] += 1
        page = calls["n"]
        first = (page - 1) * 50
        items = [
            {"contentDetails": {"videoId": f"video-{first+i}"}, "snippet": {}}
            for i in range(50)
        ]
        data = {"items": items}
        if page < 120:
            data["nextPageToken"] = f"page-{page+1}"
        return data

    def fake_hydrate(api_key, ids, access_token=""):
        return {video_id: {"video_id": video_id, "title": video_id, "duration": 180.0} for video_id in ids}

    channel = {"channel_id": "UC-test", "uploads_playlist": "UU-test"}
    with patch.object(unbounded_operations.yt, "_request_json", side_effect=fake_request), \
         patch.object(unbounded_operations.yt, "_hydrate_videos", side_effect=fake_hydrate):
        rows = list(unbounded_operations._iter_channel_videos(FakeCore, channel, access_token="token", max_videos=0))

    assert len(rows) == 6000, len(rows)
    assert calls["n"] == 120, calls


def test_user_limit_is_exact_not_clamped() -> None:
    for requested in (1, 10, 15, 100, 5000, 5123):
        calls = {"n": 0}

        def fake_request(url, access_token=""):
            calls["n"] += 1
            page = calls["n"]
            first = (page - 1) * 50
            return {
                "items": [{"contentDetails": {"videoId": f"v-{first+i}"}, "snippet": {}} for i in range(50)],
                "nextPageToken": f"p-{page+1}",
            }

        def fake_hydrate(api_key, ids, access_token=""):
            return {video_id: {"video_id": video_id, "duration": 120.0} for video_id in ids}

        with patch.object(unbounded_operations.yt, "_request_json", side_effect=fake_request), \
             patch.object(unbounded_operations.yt, "_hydrate_videos", side_effect=fake_hydrate):
            rows = list(unbounded_operations._iter_channel_videos(FakeCore, {"channel_id":"UC-x","uploads_playlist":"UU-x"}, access_token="token", max_videos=requested))
        assert len(rows) == requested, (requested, len(rows))


def test_exact_or_all_song_selection() -> None:
    core = FakeSelectionCore(7301)
    selection_fixes.apply(core)
    for requested in (1, 10, 15, 100, 5000, 7001):
        assert len(core.DB.list_song_ids(limit=requested)) == requested
    assert len(core.DB.list_song_ids(limit=0)) == 7301


def test_no_legacy_five_thousand_cap_in_active_unbounded_modules() -> None:
    backend = (ROOT / "app" / "unbounded_operations.py").read_text(encoding="utf-8")
    ui = (ROOT / "app" / "web" / "unbounded_youtube_ui_extension.js").read_text(encoding="utf-8")
    selection = (ROOT / "app" / "web" / "arbitrary_selection_extension.js").read_text(encoding="utf-8")
    assert "min(int(options.get(\"max_videos_per_channel\")" not in backend
    assert "Math.min(5000" not in ui
    assert "max=\"5000\"" not in ui
    assert "0 = SVI" in ui
    assert "0 = SVE" in selection
    assert "SHORT_CLIP_MIN_MATCH_SECONDS" in backend
    for value in ("10", "15", "100", "5000"):
        assert value in selection or value in ui


def main() -> None:
    test_unlimited_youtube_pages()
    test_user_limit_is_exact_not_clamped()
    test_exact_or_all_song_selection()
    test_no_legacy_five_thousand_cap_in_active_unbounded_modules()
    print("unbounded operations: OK")


if __name__ == "__main__":
    main()
