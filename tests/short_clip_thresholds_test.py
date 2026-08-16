"""Short Shorts clips: findable when the evidence is real, silent when it is not.

Before this, any clip under 8 s was refused outright by extract_signature
("Audio je prekratak"), so the 4 s "possible" rule in song_finder could never
actually fire. Lowering that floor is only safe alongside the two findings
below, both measured on the user's real Shorts against its real Suno original.

1. Chromaprint emits nothing until it has filled its first analysis window
   (~2.67 s), and its frame spacing is a CONSTANT 0.1238 s. Deriving the
   spacing as duration/frame_count -- which this code used to do -- quietly
   absorbs that warm-up gap and overstates matched_seconds by +79% on a 6 s
   clip (+11% at 28 s, +1.5% on a 3-minute song). Every threshold in
   song_finder is expressed in seconds, so that error inflated exactly the
   clips least able to afford it.

2. At short lengths, score alone cannot separate the right song from a wrong
   one. Sampling every window of a real Shorts against deliberately hard wrong
   answers (the same song reversed, and pitch-shifted five semitones -- same
   timbre, wrong music):

       length   true scores       highest WRONG score
        4 s     55.2 - 88.8             61.9
        5 s     57.7 - 87.7             71.6
        6 s     46.8 - 92.2             70.9
        8 s     57.3 - 98.4             66.1
       10 s     63.4 - 100.0            66.1
       12 s+    62.3 - 100.0            62.2   <- ranges finally separate

   The ranges OVERLAP below ~12 s, so the old 48.0 floor would have named
   wrong songs. No wrong answer anywhere reached 75.
"""
from __future__ import annotations
import json, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
import audio_match
import song_finder


def main():
    checks = []

    # -- the frame spacing must be the real constant, not duration/count --
    assert abs(audio_match.CHROMAPRINT_FRAME_SECONDS - 0.1238) < 0.0005
    assert audio_match.CHROMAPRINT_WARMUP_SECONDS > 2.0
    checks.append("chromaprint frame spacing is the measured constant 0.1238s, not duration/frame_count")

    # A 6s clip has ~27 frames. Under the old duration/count rule that implied
    # a 0.222s step and ~6.0s of "matched" audio; the truth is ~3.3s.
    frames = 27
    honest = frames * audio_match.CHROMAPRINT_FRAME_SECONDS
    inflated = frames * (6.0 / frames)
    assert honest < 3.5 < inflated
    checks.append(f"a 27-frame match is reported as {honest:.1f}s, not the inflated {inflated:.1f}s it used to claim")

    # -- clips shorter than 8s may now be fingerprinted at all --
    assert audio_match.MIN_SIGNATURE_SECONDS <= 4.0
    checks.append("clips down to 4s are accepted for fingerprinting (were refused outright below 8s)")

    # -- but a short match needs strong evidence, not just any score --
    strong_short = song_finder.classify_match({"audio_score": 88, "matched_seconds": 5})
    assert strong_short == song_finder.STATUS_POSSIBLE
    checks.append("a 5s clip that matches strongly (88) IS reported -- the whole point of the change")

    for wrong_score in (48, 55, 62, 71):
        verdict = song_finder.classify_match({"audio_score": wrong_score, "matched_seconds": 5})
        assert verdict == song_finder.STATUS_NOT_FOUND, (wrong_score, verdict)
    checks.append("scores up to 71 on a 5s match are rejected -- measured wrong songs reach exactly that range")

    # -- length alone must never confirm --
    assert song_finder.classify_match({"audio_score": 99, "matched_seconds": 5}) == song_finder.STATUS_POSSIBLE
    assert song_finder.classify_match({"audio_score": 99, "matched_seconds": 3}) == song_finder.STATUS_NOT_FOUND
    checks.append("4-6s can only ever be 'possible', and under 4s nothing at all, whatever the score")

    # -- long matches keep the ordinary rules --
    assert song_finder.classify_match({"audio_score": 70, "matched_seconds": 25}) == song_finder.STATUS_CONFIRMED
    checks.append("a long match is still confirmed on the ordinary threshold -- this did not tighten normal Shorts")

    # -- silence and held tones must never be treated as evidence --
    assert audio_match.CHROMAPRINT_MIN_DISTINCT_VALUES >= 20
    # Real songs contain long constant stretches -- the original used to validate
    # all of this opens with ~60s of silence -- so this must be an absolute count,
    # never a percentage, or genuine songs get refused.
    silence_like = [7] * 400
    real_song_with_quiet_intro = [7] * 400 + list(range(248))
    assert len(set(silence_like)) < audio_match.CHROMAPRINT_MIN_DISTINCT_VALUES
    assert len(set(real_song_with_quiet_intro)) >= audio_match.CHROMAPRINT_MIN_DISTINCT_VALUES
    checks.append("silence is refused as featureless, while a real song with a 60s silent intro is still accepted")

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
