from __future__ import annotations
import json, random, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
import audio_match


def main():
    checks = []

    # -- _resample_int_sequence: pure nearest-neighbor resampling --
    seq = list(range(100))
    assert audio_match._resample_int_sequence(seq, 100) == seq
    checks.append('_resample_int_sequence: no-op when new_len == len(seq)')
    assert audio_match._resample_int_sequence([], 50) == []
    checks.append('_resample_int_sequence: empty input -> empty output')
    assert audio_match._resample_int_sequence(seq, 0) == []
    checks.append('_resample_int_sequence: new_len<=0 -> empty output')
    shrunk = audio_match._resample_int_sequence(seq, 50)
    assert len(shrunk) == 50 and shrunk[0] == 0 and shrunk[-1] in (98, 99)
    checks.append('_resample_int_sequence: shrinks to the requested length, preserving start/end content')
    grown = audio_match._resample_int_sequence(seq, 200)
    assert len(grown) == 200 and grown[0] == 0
    checks.append('_resample_int_sequence: can also expand a sequence')

    rng = random.Random(42)
    FRAME_INTERVAL = 0.128

    def fake_fingerprint(n: int) -> list[int]:
        return [rng.getrandbits(32) for _ in range(n)]

    # A ~38s synthetic "song" fingerprint, and a clean 100-frame (~12.8s)
    # slice of it in the middle to stand in for a Shorts-style clip.
    src = fake_fingerprint(300)
    slice_start = 100
    pristine_clip = src[slice_start:slice_start + 100]

    def signature(seq: list[int]) -> dict:
        return {"chromaprint": seq, "duration": len(seq) * FRAME_INTERVAL}

    # -- pristine (no speed change) clip: must match strongly at tempo_hint=1.0 --
    pristine_candidates = audio_match._chromaprint_candidates(signature(src), signature(pristine_clip))
    assert pristine_candidates, "expected at least one candidate for a pristine matching clip"
    best_pristine = max(pristine_candidates, key=lambda c: c["rank"])
    assert best_pristine["rank"] >= audio_match._TEMPO_HYPOTHESIS_GOOD_ENOUGH_RANK
    assert best_pristine["tempo_hint"] == 1.0
    checks.append('_chromaprint_candidates: pristine clip matches strongly at tempo_hint=1.0 without needing the tempo search')

    # -- sped-up clip (content compressed into fewer frames, same bits) must still match --
    # atempo=1.03 means the clip plays 3% faster: the same audio content is
    # squeezed into ~1/1.03 as many frames. Simulate that by resampling the
    # pristine slice down, keeping the underlying fingerprint bits (nearest-
    # neighbor of the SAME content, not new random content).
    sped_up_clip = audio_match._resample_int_sequence(pristine_clip, round(len(pristine_clip) / 1.03))
    sped_up_candidates = audio_match._chromaprint_candidates(signature(src), signature(sped_up_clip))
    assert sped_up_candidates, "expected the tempo search to recover a match for a 3% sped-up clip"
    best_sped_up = max(sped_up_candidates, key=lambda c: c["rank"])
    assert best_sped_up["rank"] >= audio_match._TEMPO_HYPOTHESIS_GOOD_ENOUGH_RANK, f"best rank too low: {best_sped_up['rank']}"
    assert best_sped_up["tempo_hint"] != 1.0, "expected a non-1.0 tempo hypothesis to win for a sped-up clip"
    checks.append('_chromaprint_candidates: a 3% sped-up clip (content compressed into fewer frames) is still recovered via the tempo hypothesis search')

    # -- slowed-down clip (content expanded into more frames) must also still match --
    slowed_down_clip = audio_match._resample_int_sequence(pristine_clip, round(len(pristine_clip) / 0.97))
    slowed_candidates = audio_match._chromaprint_candidates(signature(src), signature(slowed_down_clip))
    assert slowed_candidates, "expected the tempo search to recover a match for a 3% slowed-down clip"
    best_slowed = max(slowed_candidates, key=lambda c: c["rank"])
    assert best_slowed["rank"] >= audio_match._TEMPO_HYPOTHESIS_GOOD_ENOUGH_RANK, f"best rank too low: {best_slowed['rank']}"
    checks.append('_chromaprint_candidates: a 3% slowed-down clip is also recovered via the tempo hypothesis search')

    # -- unrelated random content must NOT produce a false positive, even with 5 tempo hypotheses tried --
    unrelated_clip = fake_fingerprint(100)
    unrelated_candidates = audio_match._chromaprint_candidates(signature(src), signature(unrelated_clip))
    if unrelated_candidates:
        best_unrelated = max(unrelated_candidates, key=lambda c: c["rank"])
        assert best_unrelated["rank"] < audio_match._TEMPO_HYPOTHESIS_GOOD_ENOUGH_RANK, (
            f"tempo search produced a false-positive-strength match ({best_unrelated['rank']}) for unrelated random content"
        )
    checks.append('_chromaprint_candidates: unrelated random content never scores as a strong match, even after trying all tempo hypotheses')

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
