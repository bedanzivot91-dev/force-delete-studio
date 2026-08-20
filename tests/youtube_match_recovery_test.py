from __future__ import annotations

import sys
import threading
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "app"))

import youtube_match_recovery


class _Task:
    def __init__(self):
        self.logs = []

    def log(self, message, level="info"):
        self.logs.append((level, str(message)))


class _Conn:
    def __init__(self, db):
        self.db = db

    def __enter__(self):
        return self

    def __exit__(self, *_args):
        return False

    def execute(self, sql, params=()):
        self.db.sql.append((" ".join(str(sql).split()), tuple(params)))
        return self


class _DB:
    def __init__(self):
        self._lock = threading.RLock()
        self.sql = []

    def _connect(self):
        return _Conn(self)


class _ShortCore:
    SONG_FINDER_SHORTLIST = 20

    def __init__(self):
        self.DB = _DB()
        self.compare_minimums = []
        self.scan_options = []
        self._youtube_match_recovery_v1_installed = False
        self._song_finder_shortlist = lambda sig, songs: (songs[: self.SONG_FINDER_SHORTLIST], True)
        self._analyse_video_against_songs = self._original_analyse
        self.analyze_owned_youtube_audio = self._original_scan

    def required_match_seconds(self, duration):
        return max(1.0, min(4.0, float(duration) * 0.60))

    def compare_signatures(self, _source, target, min_match_seconds=12.0):
        self.compare_minimums.append(float(min_match_seconds))
        # Reproduce the owned-YouTube bug: excellent 8 s identity still comes
        # back as different_audio from the full-song completeness classifier.
        return {
            "audio_score": 90.0,
            "matched_seconds": 4.8,
            "covered_seconds": 4.8,
            "completeness_status": "different_audio",
            "confidence": "low",
            "reason": "full-song classifier",
        }

    def _original_analyse(self, _task, video, _songs, _owned, _options):
        return self.compare_signatures({}, {"duration": float(video.get("duration") or 0)})

    def _original_scan(self, _task, options):
        self.scan_options.append(dict(options))

    def runtime_log(self, *_args, **_kwargs):
        pass


class _FallbackCore(_ShortCore):
    def __init__(self):
        super().__init__()
        self.pass_limits = []

    def _original_analyse(self, _task, video, songs, _owned, _options):
        self.pass_limits.append(int(self.SONG_FINDER_SHORTLIST))
        if int(self.SONG_FINDER_SHORTLIST) >= len(songs):
            return {
                "song_id": "true-song",
                "audio_score": 94.0,
                "matched_seconds": 18.0,
                "completeness_status": "short_clip",
                "confidence": "high",
            }
        return {
            "song_id": "wrong-shortlist-song",
            "audio_score": 18.0,
            "matched_seconds": 2.0,
            "completeness_status": "different_audio",
            "confidence": "low",
        }


def test_owned_short_uses_short_minimum_and_is_promoted() -> None:
    core = _ShortCore()
    youtube_match_recovery.apply(core)
    result = core._analyse_video_against_songs(_Task(), {"video_id": "v1", "duration": 8.0}, [{"id": "s1"}], set(), {})
    assert core.compare_minimums, "compare_signatures was not called"
    assert core.compare_minimums[0] <= 4.0, core.compare_minimums
    assert result["completeness_status"] == "short_clip", result
    assert result["audio_score"] == 90.0


def test_new_mode_retries_old_uncertain_results() -> None:
    core = _ShortCore()
    youtube_match_recovery.apply(core)
    core.analyze_owned_youtube_audio(_Task(), {"scan_mode": "new", "max_videos_per_channel": 0})
    assert core.scan_options[-1]["scan_mode"] == "uncertain", core.scan_options


def test_stubborn_short_falls_back_to_full_library() -> None:
    core = _FallbackCore()
    youtube_match_recovery.apply(core)
    songs = [{"id": f"song-{i}"} for i in range(300)]
    result = core._analyse_video_against_songs(
        _Task(),
        {"video_id": "short-300", "title": "quote title", "duration": 30.0},
        songs,
        {"UC-owner"},
        {},
    )
    assert core.pass_limits[0] == 20, core.pass_limits
    assert any(limit >= 128 for limit in core.pass_limits[1:]), core.pass_limits
    assert core.pass_limits[-1] >= 300, core.pass_limits
    assert result["song_id"] == "true-song", result
    assert result["completeness_status"] == "short_clip", result
    assert any("DELETE FROM youtube_matches" in sql for sql, _ in core.DB.sql), core.DB.sql


def main() -> None:
    test_owned_short_uses_short_minimum_and_is_promoted()
    test_new_mode_retries_old_uncertain_results()
    test_stubborn_short_falls_back_to_full_library()
    print("youtube_match_recovery_test: PASS — short minimum + stale retry + exhaustive fallback")


if __name__ == "__main__":
    main()
