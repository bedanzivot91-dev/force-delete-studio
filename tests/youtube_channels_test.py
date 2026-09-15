from __future__ import annotations
import json, sys, tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
from database import LibraryDB


def main():
    checks = []
    with tempfile.TemporaryDirectory(prefix='sps-yt-channels-') as raw:
        db = LibraryDB(Path(raw) / 'library.db')

        # -- channel avatar (thumbnail_url) round-trips through upsert/list --
        saved = db.upsert_youtube_channel({
            "channel_id": "UC_test_channel", "title": "Moj Kanal", "handle": "@mojkanal",
            "thumbnail_url": "https://example.test/avatar.jpg", "subscriber_count": 120, "video_count": 3,
        }, is_owned=True)
        assert saved["thumbnail_url"] == "https://example.test/avatar.jpg"
        checks.append('upsert_youtube_channel: thumbnail_url stored on insert')

        listed = db.list_youtube_channels()
        assert len(listed) == 1 and listed[0]["thumbnail_url"] == "https://example.test/avatar.jpg"
        checks.append('list_youtube_channels: thumbnail_url present')

        # -- re-upsert with a blank thumbnail must NOT wipe a previously known one --
        db.upsert_youtube_channel({"channel_id": "UC_test_channel", "title": "Moj Kanal", "handle": "@mojkanal"}, is_owned=True)
        kept = db.get_youtube_channel("UC_test_channel")
        assert kept["thumbnail_url"] == "https://example.test/avatar.jpg"
        checks.append('upsert_youtube_channel: blank thumbnail_url on re-scan does not erase the stored one')

        # -- channel_shorts_report: empty channel --
        empty_report = db.channel_shorts_report("UC_test_channel")
        assert empty_report == {"videos_total": 0, "shorts_total": 0, "shorts_checked": 0, "shorts_not_checked": 0, "unknown_duration": 0}
        checks.append('channel_shorts_report: all-zero for a channel with no videos yet')

        # -- add a mix of videos: 2 Shorts (one checked, one not), 1 long video, 1 unknown-duration (RSS fallback) --
        db.upsert_youtube_video({"video_id": "short1", "channel_id": "UC_test_channel", "title": "Shorts 1", "duration": 45.0}, is_owned_channel=True)
        db.upsert_youtube_video({"video_id": "short2", "channel_id": "UC_test_channel", "title": "Shorts 2", "duration": 30.0}, is_owned_channel=True)
        db.upsert_youtube_video({"video_id": "long1", "channel_id": "UC_test_channel", "title": "Ceo spot", "duration": 210.0}, is_owned_channel=True)
        db.upsert_youtube_video({"video_id": "rssvid", "channel_id": "UC_test_channel", "title": "RSS bez trajanja", "duration": 0.0}, is_owned_channel=True)
        db.update_youtube_video_audio_cache("short1", "/tmp/short1.wav", "deadbeef")

        report = db.channel_shorts_report("UC_test_channel")
        assert report["videos_total"] == 4
        assert report["shorts_total"] == 2
        assert report["shorts_checked"] == 1
        assert report["shorts_not_checked"] == 1
        assert report["unknown_duration"] == 1
        checks.append('channel_shorts_report: correctly separates Shorts (<=70s) from long videos and unknown-duration RSS entries, and tracks which Shorts have a real audio result')

        # -- a video on a DIFFERENT channel must not leak into this report --
        db.upsert_youtube_channel({"channel_id": "UC_other", "title": "Drugi Kanal"}, is_owned=True)
        db.upsert_youtube_video({"video_id": "other_short", "channel_id": "UC_other", "title": "Tuđi Shorts", "duration": 20.0}, is_owned_channel=True)
        isolated = db.channel_shorts_report("UC_test_channel")
        assert isolated["videos_total"] == 4 and isolated["shorts_total"] == 2
        checks.append('channel_shorts_report: scoped strictly to the requested channel_id')

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
