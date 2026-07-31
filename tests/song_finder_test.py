from __future__ import annotations
import json, sys, tempfile
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
import song_finder
import v3_features
from database import LibraryDB


def main():
    checks = []

    # -- classify_match(): score/seconds thresholds for Shorts-length clips --
    confirmed = song_finder.classify_match({"audio_score": 80, "matched_seconds": 10})
    assert confirmed == song_finder.STATUS_CONFIRMED
    checks.append('classify: high score + long match -> confirmed')

    possible = song_finder.classify_match({"audio_score": 55, "matched_seconds": 5})
    assert possible == song_finder.STATUS_POSSIBLE
    checks.append('classify: mid score + short match -> possible')

    not_found = song_finder.classify_match({"audio_score": 10, "matched_seconds": 1})
    assert not_found == song_finder.STATUS_NOT_FOUND
    checks.append('classify: low score + tiny match -> not_found')

    # High score alone, with too little matched audio, must not be CONFIRMED --
    # a few seconds of coincidental overlap should not falsely confirm a song.
    borderline = song_finder.classify_match({"audio_score": 90, "matched_seconds": 1})
    assert borderline == song_finder.STATUS_NOT_FOUND
    checks.append('classify: high score but too few matched seconds -> not_found, not confirmed')

    # covered_seconds is accepted as a fallback key when matched_seconds is absent.
    fallback = song_finder.classify_match({"audio_score": 70, "covered_seconds": 7})
    assert fallback == song_finder.STATUS_CONFIRMED
    checks.append('classify: covered_seconds fallback key honored')

    # -- confidence_percent(): clamped 0-100 --
    assert song_finder.confidence_percent({"audio_score": 143.7}) == 100
    assert song_finder.confidence_percent({"audio_score": -5}) == 0
    assert song_finder.confidence_percent({"audio_score": 62.4}) == 62
    checks.append('confidence_percent clamps to 0-100')

    # -- rank_song_candidates(): confirmed before possible before not_found, then by score --
    candidates = [
        {"song_id": "a", "status": song_finder.STATUS_POSSIBLE, "audio_score": 60},
        {"song_id": "b", "status": song_finder.STATUS_CONFIRMED, "audio_score": 70},
        {"song_id": "c", "status": song_finder.STATUS_CONFIRMED, "audio_score": 95},
        {"song_id": "d", "status": song_finder.STATUS_NOT_FOUND, "audio_score": 99},
    ]
    ranked = song_finder.rank_song_candidates(candidates)
    assert [c["song_id"] for c in ranked] == ["c", "b", "a", "d"]
    checks.append('rank_song_candidates: status tier wins over raw score, then score descending')

    # -- is_supported_file(): Shorts/video/audio formats accepted, unrelated formats rejected --
    for ext in ('.mp3', '.wav', '.m4a', '.aac', '.flac', '.ogg', '.webm', '.mp4', '.mov', '.mkv'):
        assert song_finder.is_supported_file(Path(f'clip{ext}'))
    for ext in ('.txt', '.pdf', '.exe', '.json'):
        assert not song_finder.is_supported_file(Path(f'file{ext}'))
    checks.append('is_supported_file: accepts audio/video Shorts formats, rejects unrelated ones')

    # -- system_preflight(): Panako missing must not block the whole program --
    # This is the exact user-reported scenario: ffmpeg/yt_dlp/deno/python all
    # ready, panako not installed. Before the fix every tool's absence counted
    # as a hard "error" and readiness became "blocked"; now only REQUIRED
    # tools do that, and Panako is marked optional by v3_tool_status().
    tool_status_panako_missing = {
        "ffmpeg": {"ready": True, "required": True, "optional": False, "severity": "ok"},
        "yt_dlp": {"ready": True, "required": True, "optional": False, "severity": "ok"},
        "deno": {"ready": True, "required": True, "optional": False, "severity": "ok"},
        "python": {"ready": True, "required": True, "optional": False, "severity": "ok"},
        "panako": {"ready": False, "required": False, "optional": True, "severity": "warning"},
    }
    with tempfile.TemporaryDirectory(prefix='sps-preflight-') as raw:
        base = Path(raw)
        with mock.patch.object(v3_features, '_url_check', return_value={"ok": True, "status": 200, "ms": 1}):
            report = v3_features.system_preflight(base, base / 'data', base / 'downloads', tool_status_panako_missing)
    assert report['readiness'] == 'limited', f"expected limited, got {report['readiness']!r}"
    assert report['errors'] == 0, f"expected 0 errors with only an optional tool missing, got {report['errors']}"
    panako_check = next(c for c in report['checks'] if c['key'] == 'tool_panako')
    assert panako_check['level'] == 'warning' and not panako_check['ok']
    checks.append('system_preflight: missing optional Panako -> readiness=limited, errors=0, not blocked')

    # Sanity check the opposite: a missing REQUIRED tool must still block.
    tool_status_ffmpeg_missing = dict(tool_status_panako_missing)
    tool_status_ffmpeg_missing['ffmpeg'] = {"ready": False, "required": True, "optional": False, "severity": "error"}
    with tempfile.TemporaryDirectory(prefix='sps-preflight-') as raw:
        base = Path(raw)
        with mock.patch.object(v3_features, '_url_check', return_value={"ok": True, "status": 200, "ms": 1}):
            report2 = v3_features.system_preflight(base, base / 'data', base / 'downloads', tool_status_ffmpeg_missing)
    assert report2['readiness'] == 'blocked', f"expected blocked when a required tool is missing, got {report2['readiness']!r}"
    checks.append('system_preflight: missing REQUIRED tool still blocks (fix did not weaken real errors)')

    # -- DB reuse: song_finder must not need any schema beyond what already exists --
    with tempfile.TemporaryDirectory(prefix='sps-songfinder-') as raw:
        db = LibraryDB(Path(raw) / 'library.db')
        record = db.add_recognition({
            "original_filename": "shorts_clip.mp4", "input_path": str(Path(raw) / 'shorts_clip.mp4'),
            "prepared_audio_path": "", "library_song_id": "song-1", "status": "recognized",
            "result": {"found": True, "provider": "local_library", "title": "Moja pesma", "status": "confirmed"},
        })
        assert record.get('id')
        items = [r for r in db.list_recognitions(limit=50) if r.get('provider') == 'local_library']
        assert len(items) == 1 and items[0]['result']['status'] == 'confirmed'
        checks.append('recognized_tracks table reused as-is for local_library provider, no migration needed')

        # -- manual override timestamp: confirming a result records WHEN it was manually corrected --
        before = db.get_recognition(int(record["id"]))
        assert not before.get("manual_override_at")
        db.set_recognition_library_song(int(record["id"]), "song-2")
        after = db.get_recognition(int(record["id"]))
        assert after.get("library_song_id") == "song-2" and after.get("manual_override_at")
        checks.append('set_recognition_library_song: records manual_override_at, not just the new song id')

    # -- select_distinct_matches(): two of the user's OWN songs in one Shorts must both survive --
    two_songs = [
        {"song_id": "a", "status": song_finder.STATUS_CONFIRMED, "audio_score": 90, "clip_start": 0.0, "clip_end": 14.0},
        {"song_id": "b", "status": song_finder.STATUS_CONFIRMED, "audio_score": 85, "clip_start": 15.0, "clip_end": 29.0},
    ]
    distinct = song_finder.select_distinct_matches(song_finder.rank_song_candidates(two_songs))
    assert {c["song_id"] for c in distinct} == {"a", "b"}
    checks.append('select_distinct_matches: two songs at two different, non-overlapping clip timestamps both kept')

    # -- same segment matched by two different songs (e.g. a near-duplicate or a false extra
    # candidate) must NOT both be kept -- only the higher-ranked one for that time range survives --
    same_segment = [
        {"song_id": "best", "status": song_finder.STATUS_CONFIRMED, "audio_score": 90, "clip_start": 0.0, "clip_end": 20.0},
        {"song_id": "worse", "status": song_finder.STATUS_POSSIBLE, "audio_score": 55, "clip_start": 1.0, "clip_end": 19.0},
    ]
    distinct_dedup = song_finder.select_distinct_matches(song_finder.rank_song_candidates(same_segment))
    assert [c["song_id"] for c in distinct_dedup] == ["best"]
    checks.append('select_distinct_matches: overlapping candidates for the same segment collapse to the best one')

    # -- not_found candidates never count as a "distinct match" --
    with_not_found = [
        {"song_id": "real", "status": song_finder.STATUS_CONFIRMED, "audio_score": 90, "clip_start": 0.0, "clip_end": 10.0},
        {"song_id": "irrelevant", "status": song_finder.STATUS_NOT_FOUND, "audio_score": 5, "clip_start": 20.0, "clip_end": 25.0},
    ]
    distinct_filtered = song_finder.select_distinct_matches(song_finder.rank_song_candidates(with_not_found))
    assert [c["song_id"] for c in distinct_filtered] == ["real"]
    checks.append('select_distinct_matches: not_found candidates are excluded even if their time range is free')

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
