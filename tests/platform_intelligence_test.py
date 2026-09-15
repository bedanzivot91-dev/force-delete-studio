from __future__ import annotations

import json
import sys
import tempfile
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "app"))

import platform_intelligence as pi
import youtube_oauth


def main() -> None:
    analytics_payload = {
        "columnHeaders": [{"name": "day"}, {"name": "views"}, {"name": "averageViewPercentage"}],
        "rows": [["2026-08-18", 100, 50], ["2026-08-19", 200, 70]],
    }
    with patch.object(pi, "_google_json", return_value=analytics_payload):
        report = pi.youtube_analytics("token", "2026-08-18", "2026-08-19")
    assert report["totals"]["views"] == 300
    assert report["totals"]["averageViewPercentage"] == 60

    with patch.object(pi, "_analytics_report", side_effect=[
        {"available": True, "rows": [{"views": 10}], "source": "YouTube Analytics API"},
        RuntimeError("not allowed"), RuntimeError("not allowed"), RuntimeError("not allowed"),
        RuntimeError("not allowed"), RuntimeError("not allowed"), RuntimeError("not allowed"),
    ]):
        suite = pi.youtube_analytics_suite("token", "2026-08-01", "2026-08-02")
    assert suite["reports"]["daily"]["available"] is True
    assert suite["reports"]["traffic_sources"]["available"] is False
    assert suite["reports"]["traffic_sources"]["rows"] == []
    assert suite["estimated"] is False

    comments = [
        {"text": "Prelepo, bravo!", "likes": 10, "replies": 2},
        {"text": "Zašto je zvuk tako tiho?", "likes": 3, "replies": 1},
    ]
    analysis = pi.analyze_comments(comments)
    assert analysis["count"] == 2 and analysis["positive"] == 1 and analysis["negative"] == 1
    assert analysis["questions"] == 1
    assert analysis["estimated"] is True and "heuristička" in analysis["warning"]

    diff = pi.suno_snapshot_diff(
        [{"id": "a", "title": "Staro"}, {"id": "b", "title": "Nestalo"}],
        [{"id": "a", "title": "Novo"}, {"id": "c", "title": "Dodato"}],
    )
    assert [x["id"] for x in diff["new"]] == ["c"]
    assert [x["id"] for x in diff["missing"]] == ["b"]
    assert diff["changed"][0]["fields"] == ["title"]
    assert diff["changed"][0]["before"]["title"] == "Staro"

    assert pi.quota_estimate(2, 3, 1)["estimated_total"] == 14
    assert pi.review_priority({"confidence": 60, "matched_seconds": 20})["priority"] == "high"
    assert pi.review_priority({"confidence": 95, "matched_seconds": 20})["needs_review"] is False

    probe = type("Run", (), {"stdout": json.dumps({"streams": [{"width": 1080, "height": 1920}], "format": {"duration": "20"}}), "stderr": ""})()
    scan = type("Run", (), {"stdout": "", "stderr": "black_start:1.0 black_end:2.0\nfreeze_start: 4.5\nblur mean: 2.0"})()
    with tempfile.TemporaryDirectory() as tmp:
        video = Path(tmp) / "short.mp4"
        video.write_bytes(b"test")
        with patch.object(pi.subprocess, "run", side_effect=[probe, scan]):
            visual = pi.visual_video_analysis(video, "ffmpeg", "ffprobe")
    assert visual["orientation"] == "vertical"
    assert len(visual["black_segments"]) == 1 and len(visual["freeze_segments"]) == 1
    assert not any("Format nije" in x for x in visual["issues"])

    assert "yt-analytics.readonly" in youtube_oauth.AUTH_SCOPE
    assert "https://www.googleapis.com/auth/yt-analytics.readonly" in youtube_oauth.REQUIRED_YOUTUBE_SCOPES

    queue = pi.build_review_queue(
        [{"id": 1, "found": 0, "original_filename": "short.mp4", "result": {}}],
        [{"id": 2, "song_title": "Pesma", "completeness_status": "short_clip", "audio_score": 75}],
    )
    assert len(queue) == 2 and {x["kind"] for x in queue} == {"local_clip", "youtube_match"}
    truth = pi.build_song_report({"id": "s1", "title": "Pesma"})
    assert truth["analytics"]["available"] is False
    assert "ne zamenjuje" in truth["truth_policy"]

    server = (ROOT / "app" / "server_core.py").read_text(encoding="utf-8")
    html = (ROOT / "app" / "web" / "index.html").read_text(encoding="utf-8")
    javascript = (ROOT / "app" / "web" / "app.js").read_text(encoding="utf-8")
    for route in ("/api/youtube/analytics-suite/run", "/api/platform-intelligence/review-queue", "/api/platform-intelligence/song-report", "/api/youtube/comments/export"):
        assert route in server and route in javascript
    for button in ("runYoutubeAnalyticsBtn", "loadIntelligenceReviewQueueBtn", "loadIntelligenceSongReportBtn", "exportYoutubeCommentsBtn", "loadSunoSongHistoryBtn"):
        assert f'id="{button}"' in html and button in javascript
    print("platform intelligence tests: OK")


if __name__ == "__main__":
    main()
