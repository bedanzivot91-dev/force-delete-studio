from __future__ import annotations
import json, sys, tempfile, urllib.parse
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
import youtube_tools
from database import LibraryDB


def _playlist_page(video_ids, next_token=""):
    return {
        "items": [
            {"snippet": {"resourceId": {"videoId": vid}}, "contentDetails": {"videoId": vid}}
            for vid in video_ids
        ],
        "nextPageToken": next_token,
    }


def _hydrate_response(video_ids):
    return {
        "items": [
            {
                "id": vid,
                "snippet": {"channelId": "UCabcdefghijklmnopqrstuv", "channelTitle": "Test", "title": f"Video {vid}", "publishedAt": "2026-01-01T00:00:00Z", "thumbnails": {}},
                "contentDetails": {"duration": "PT30S"},
                "statistics": {"viewCount": "0", "likeCount": "0", "commentCount": "0"},
                "status": {"privacyStatus": "public"},
            }
            for vid in video_ids
        ]
    }


def main():
    checks = []

    calls = []

    def fake_request_json(url, timeout=30, access_token=""):
        calls.append(url)
        if "playlistItems" in url:
            if len(calls) == 1:
                return _playlist_page(["newvideo001", "newvideo002"], next_token="page2")
            if len(calls) == 2:
                # entirely already-known ids -- must trigger early stop, page3 must never be requested
                return _playlist_page(["oldvideo001", "oldvideo002"], next_token="page3")
            raise AssertionError(f"playlistItems paginated past the point where every id was already known: {url}")
        if "videos" in url:
            ids = urllib.parse.parse_qs(urllib.parse.urlparse(url).query)["id"][0].split(",")
            return _hydrate_response(ids)
        raise AssertionError(f"unexpected API call: {url}")

    channel = {"channel_id": "UCabcdefghijklmnopqrstuv", "uploads_playlist": "UU_test"}
    known_ids = {"oldvideo001", "oldvideo002"}
    with mock.patch.object(youtube_tools, "_request_json", side_effect=fake_request_json):
        videos = youtube_tools.list_channel_videos(channel, api_key="fake-key", max_pages=20, known_ids=known_ids)

    got_ids = {v["video_id"] for v in videos}
    assert got_ids == {"newvideo001", "newvideo002", "oldvideo001", "oldvideo002"}, got_ids
    checks.append('list_channel_videos: collects the new page plus the first fully-known page')
    playlist_calls = [c for c in calls if "playlistItems" in c]
    assert len(playlist_calls) == 2, f"expected exactly 2 playlistItems pages (stopped after the fully-known one), got {len(playlist_calls)}"
    checks.append('list_channel_videos: stops paginating as soon as an entire page is already known, never re-walking full channel history')

    # -- without known_ids (scan_mode="full"), pagination must NOT early-stop --
    calls.clear()

    def fake_request_json_full(url, timeout=30, access_token=""):
        calls.append(url)
        if "playlistItems" in url:
            if len(calls) == 1:
                return _playlist_page(["newvideo001", "newvideo002"], next_token="page2")
            if len(calls) == 2:
                return _playlist_page(["oldvideo001", "oldvideo002"], next_token="")  # no more pages
            raise AssertionError("unexpected extra page")
        if "videos" in url:
            ids = urllib.parse.parse_qs(urllib.parse.urlparse(url).query)["id"][0].split(",")
            return _hydrate_response(ids)
        raise AssertionError(f"unexpected API call: {url}")

    with mock.patch.object(youtube_tools, "_request_json", side_effect=fake_request_json_full):
        videos_full = youtube_tools.list_channel_videos(channel, api_key="fake-key", max_pages=20, known_ids=None)
    assert {v["video_id"] for v in videos_full} == {"newvideo001", "newvideo002", "oldvideo001", "oldvideo002"}
    checks.append('list_channel_videos: known_ids=None (full rescan mode) walks every page normally')

    # -- DB.list_youtube_video_ids: scoped per channel --
    with tempfile.TemporaryDirectory(prefix='sps-yt-scan-') as raw:
        db = LibraryDB(Path(raw) / 'library.db')
        db.upsert_youtube_channel({"channel_id": "UC_a", "title": "A"}, is_owned=True)
        db.upsert_youtube_channel({"channel_id": "UC_b", "title": "B"}, is_owned=True)
        db.upsert_youtube_video({"video_id": "v1", "channel_id": "UC_a", "title": "V1"}, is_owned_channel=True)
        db.upsert_youtube_video({"video_id": "v2", "channel_id": "UC_a", "title": "V2"}, is_owned_channel=True)
        db.upsert_youtube_video({"video_id": "v3", "channel_id": "UC_b", "title": "V3"}, is_owned_channel=True)
        assert db.list_youtube_video_ids("UC_a") == {"v1", "v2"}
        assert db.list_youtube_video_ids("UC_b") == {"v3"}
        assert db.list_youtube_video_ids("UC_nonexistent") == set()
        checks.append('DB.list_youtube_video_ids: scoped per channel_id, empty set for unknown channel')

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
