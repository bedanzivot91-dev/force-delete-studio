"""Where a clip was cut must not decide whether it is recognised."""
from __future__ import annotations
import json, sys
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
import audio_match
import song_finder


def main():
    checks = []
    offsets = audio_match.CHROMAPRINT_PHASE_OFFSETS
    assert len(offsets) >= 4 and offsets[0] == 0.0
    assert all(0.0 <= o < 1.0 for o in offsets), offsets
    assert len(set(offsets)) == len(offsets)
    checks.append(f"{len(offsets)} distinct sub-frame offsets")

    calls = []
    def fake_extract(source, progress=None, cancel_check=None, tempo=1.0, phase=0.0):
        calls.append(phase)
        return {"duration": fake_extract.duration, "chromaprint": [1, 2, 3], "interval": 0.5,
                "features": [[0.0]], "frame_count": 1}

    fake_extract.duration = 5.0
    with patch.object(audio_match, "extract_signature", fake_extract):
        variants = audio_match.extract_query_signatures("clip.mp4")
    assert len(variants) == len(offsets), (len(variants), len(offsets))
    assert calls == list(offsets), calls
    checks.append("5s clip gets all phase offsets")

    # Exact regression for the user's reproduced failure: the Short is
    # 26.399 s, just above the historical 25 s boundary. Importing song_finder
    # raises the runtime phase-search ceiling to 70 s; this exact duration must
    # therefore receive the same four recovery passes as other Shorts.
    calls.clear()
    fake_extract.duration = 26.399
    with patch.object(audio_match, "extract_signature", fake_extract):
        variants = audio_match.extract_query_signatures("user-short-26.399.mp4")
    assert audio_match.PHASE_SEARCH_BELOW_SECONDS >= 70.0, audio_match.PHASE_SEARCH_BELOW_SECONDS
    assert len(variants) == len(offsets), (len(variants), len(offsets))
    assert calls == list(offsets), calls
    checks.append("26.399s reproduced Short gets all phase offsets instead of falling outside the old 25s limit")

    calls.clear()
    fake_extract.duration = 180.0
    with patch.object(audio_match, "extract_signature", fake_extract):
        variants = audio_match.extract_query_signatures("song.mp3")
    assert len(variants) == 1 and calls == [0.0]
    checks.append("180s full song remains one-pass")

    assert audio_match.required_match_seconds(4.0) < 4.0
    assert audio_match.required_match_seconds(30.0) == audio_match.SHORT_CLIP_MIN_MATCH_SECONDS
    assert audio_match.required_match_seconds(0.0) == audio_match.SHORT_CLIP_MIN_MATCH_SECONDS
    checks.append("short-match floor scales with clip length")

    short = {"audio_score": 88, "matched_seconds": 2.6}
    assert song_finder.classify_match(short, clip_seconds=4.0) == song_finder.STATUS_POSSIBLE
    assert song_finder.classify_match(short) == song_finder.STATUS_NOT_FOUND
    checks.append("classification respects clip length")

    for score in (48, 55, 62, 71):
        assert song_finder.classify_match({"audio_score": score, "matched_seconds": 3.0}, clip_seconds=5.0) == song_finder.STATUS_NOT_FOUND, score
    checks.append("weak short matches remain rejected")

    assert song_finder.classify_match({"audio_score": 99, "matched_seconds": 3.0}, clip_seconds=5.0) == song_finder.STATUS_POSSIBLE
    checks.append("very short match cannot become confirmed on score alone")

    import inspect
    code = "\n".join(line for line in inspect.getsource(audio_match._extract_chromaprint).splitlines() if not line.strip().startswith("#"))
    assert "adelay" in code
    assert "lavfi" not in code and "anullsrc" not in code
    checks.append("padding uses adelay without lavfi dependency")

    extract_body = inspect.getsource(audio_match.extract_signature)
    assert "if pad and not chromaprint" in extract_body
    checks.append("padded extraction falls back to unpadded fingerprint")

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
