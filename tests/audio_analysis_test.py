from __future__ import annotations
import json, math, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
import advanced_features as af


def main():
    checks = []

    # -- estimate_bpm: synthesize a clean periodic RMS envelope at a known
    # tempo (120 BPM = 0.5s per beat = 10 frames at 50ms/frame) and check
    # the autocorrelation actually recovers that period. --
    frames_per_beat = round(0.5 / af._BEAT_FRAME_SECONDS)
    values = []
    for i in range(400):
        phase = i % frames_per_beat
        # sharp decaying "pluck" per beat, silence between -- a clear onset pattern
        values.append(-6.0 if phase == 0 else -6.0 - 3.0 * phase)
    result = af.estimate_bpm(values)
    assert result is not None, "estimate_bpm returned None for a clean periodic signal"
    checks.append("estimate_bpm returns a result for a clean periodic RMS envelope")
    assert abs(result["bpm"] - 120.0) <= 6.0, result
    checks.append(f"estimate_bpm recovers ~120 BPM from a synthetic 120 BPM envelope (got {result['bpm']})")
    assert 0.0 <= result["confidence"] <= 100.0
    checks.append("estimate_bpm confidence is bounded 0-100")

    # -- too short / flat envelope must not crash, just return None --
    assert af.estimate_bpm([-20.0] * 10) is None
    checks.append("estimate_bpm returns None (not a crash) for too-short input")
    assert af.estimate_bpm([-20.0] * 200) is None
    checks.append("estimate_bpm returns None (not a crash) for a flat/silent envelope")

    # -- _goertzel_power: a synthetic sine at a known frequency should score
    # far higher at that frequency than at an unrelated one. --
    sr = 2000
    freq = 220.0  # A3
    n = sr * 2
    frame = [int(3000 * math.sin(2 * math.pi * freq * i / sr)) for i in range(n)]
    from array import array as _array
    frame = _array("h", frame)
    on_freq = af._goertzel_power(frame, freq, sr)
    off_freq = af._goertzel_power(frame, freq * 1.6, sr)
    assert on_freq > off_freq * 5, (on_freq, off_freq)
    checks.append("_goertzel_power scores a pure tone's own frequency far above an unrelated one")

    # -- Krumhansl-Schmuckler scoring picks the tonic whose chroma weight is
    # concentrated on the notes of that key's own scale (C major triad here). --
    chroma = [0.0] * 12
    for pc, weight in ((0, 0.5), (4, 0.3), (7, 0.2)):  # C, E, G -- a C major triad
        chroma[pc] = weight
    best_tonic, best_mode, best_score = 0, "major", float("-inf")
    for tonic in range(12):
        for mode, profile in (("major", af._KS_MAJOR_PROFILE), ("minor", af._KS_MINOR_PROFILE)):
            score = sum(chroma[pc] * profile[(pc - tonic) % 12] for pc in range(12))
            if score > best_score:
                best_score, best_tonic, best_mode = score, tonic, mode
    assert af._NOTE_NAMES[best_tonic] == "C" and best_mode == "major", (af._NOTE_NAMES[best_tonic], best_mode)
    checks.append("Krumhansl-Schmuckler correlation picks C major for a C-E-G chroma weighting")

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
