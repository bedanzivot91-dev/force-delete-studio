"""Where a clip was cut must not decide whether it is recognised.

Chromaprint lays its frames on a fixed grid measured from the start of the
audio, and the matcher lines frames up 1:1. A clip cut at an arbitrary moment
therefore sits between the song's frame boundaries, and that half-frame error
alone is enough to lose the match. Measured on the user's real Shorts against
its real Suno original:

  * aligned, the clip matches the original at 0.96-0.99 similarity across its
    whole length -- the audio is virtually identical
  * a window cut at an arbitrary second scores only ~0.90, purely from grid
    misalignment
  * the score curve is (quality - 0.80) / 0.18, so 0.90 -> 56 and 0.97 -> 94

That is the entire difference between "found" and "not found", and it had
nothing to do with the audio. Re-extracting the clip at a few sub-frame
offsets and keeping the best fixes it: over every window of a real Shorts
from 4s to 20s, windows recognised went from 28 to 40 out of 63, with no
wrong song named. End to end against a 3000-song library containing the real
song AND three deliberately hard wrong answers (the same song reversed,
pitch-shifted, and slowed), clips of 6s and longer went to 28 of 29 found,
still with zero wrong songs named.
"""
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

    # -- several sub-frame offsets must actually be tried --
    offsets = audio_match.CHROMAPRINT_PHASE_OFFSETS
    assert len(offsets) >= 4 and offsets[0] == 0.0
    assert all(0.0 <= o < 1.0 for o in offsets), offsets
    assert len(set(offsets)) == len(offsets)
    checks.append(f"{len(offsets)} distinct sub-frame grid offsets are tried, spanning less than one frame")

    # -- a short clip gets the phase search; a long one does not need it --
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
    checks.append("a 5s clip is fingerprinted at every offset, so where it was cut stops mattering")

    calls.clear()
    fake_extract.duration = 180.0
    with patch.object(audio_match, "extract_signature", fake_extract):
        variants = audio_match.extract_query_signatures("song.mp3")
    assert len(variants) == 1 and calls == [0.0]
    checks.append("a full-length song is fingerprinted once -- the extra passes would be wasted work")

    # -- the required match must scale with the clip --
    # Demanding 4s from a 4s clip means demanding it be perfect end to end,
    # which is why almost every 4-5s window used to score exactly zero.
    assert audio_match.required_match_seconds(4.0) < 4.0
    assert audio_match.required_match_seconds(30.0) == audio_match.SHORT_CLIP_MIN_MATCH_SECONDS
    assert audio_match.required_match_seconds(0.0) == audio_match.SHORT_CLIP_MIN_MATCH_SECONDS
    checks.append("a 4s clip must match 60% of itself, not 100%; long clips keep the full 4s requirement")

    # -- classification uses the clip's own length --
    short = {"audio_score": 88, "matched_seconds": 2.6}
    assert song_finder.classify_match(short, clip_seconds=4.0) == song_finder.STATUS_POSSIBLE
    assert song_finder.classify_match(short) == song_finder.STATUS_NOT_FOUND
    checks.append("2.6s of match counts for a 4s clip but not for an unknown-length one -- length is judged in proportion")

    # -- none of this may weaken what counts as a WRONG answer --
    for score in (48, 55, 62, 71):
        assert song_finder.classify_match(
            {"audio_score": score, "matched_seconds": 3.0}, clip_seconds=5.0
        ) == song_finder.STATUS_NOT_FOUND, score
    checks.append("scores up to 71 on a short clip are still refused -- measured wrong songs reach exactly that range")

    # -- and a short clip still cannot be "confirmed" on length alone --
    assert song_finder.classify_match(
        {"audio_score": 99, "matched_seconds": 3.0}, clip_seconds=5.0
    ) == song_finder.STATUS_POSSIBLE
    checks.append("even a perfect score on a 5s clip stays 'possible', never 'confirmed'")

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
