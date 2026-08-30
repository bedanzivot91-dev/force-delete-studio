from __future__ import annotations
import json, random, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
import audio_match
import song_finder


def make_song_chromaprint(n_frames, seed):
    rnd = random.Random(seed)
    return [rnd.getrandbits(32) for _ in range(n_frames)]


def make_features(n_frames, seed):
    # Realistic per-song envelope features. They must genuinely differ between
    # songs: identical (e.g. all-zero) features would make the envelope
    # fallback report a perfect match for two unrelated songs.
    rnd = random.Random(seed * 31 + 7)
    return [[rnd.uniform(-3.0, 3.0) for _ in range(24)] for _ in range(n_frames)]


def sig(chroma, duration, features):
    return {"chromaprint": chroma, "duration": duration, "interval": 0.5, "features": features}


def main():
    checks = []

    STEP = 0.128
    FEAT_STEP = 0.5
    song_frames = int(180 / STEP)          # a real ~3 minute Suno song
    song_chroma = make_song_chromaprint(song_frames, seed=1234)
    song_feats = make_features(int(180 / FEAT_STEP), seed=1234)
    song = sig(song_chroma, 180.0, song_feats)

    def clip_at(offset_s, length_s, chroma_src, feat_src):
        c0 = int(offset_s / STEP)
        f0 = int(offset_s / FEAT_STEP)
        return sig(
            list(chroma_src[c0:c0 + int(length_s / STEP)]),
            float(length_s),
            [list(r) for r in feat_src[f0:f0 + int(length_s / FEAT_STEP)]],
        )

    # -- A Shorts clip that genuinely contains ~8 seconds of the song, taken
    # from 60s in. This is exactly the user's real case: their own song is
    # inside the Short, but only for a few seconds. --
    clip = clip_at(60.0, 8.0, song_chroma, song_feats)

    # -- OLD behaviour (12s floor, the shipped default): this exact clip is
    # rejected outright -- which is why the program said "not found". --
    old = audio_match.compare_signatures(song, clip, min_match_seconds=audio_match.FULL_SONG_MIN_MATCH_SECONDS)
    assert float(old.get("matched_seconds") or 0) == 0.0, old
    assert old.get("completeness_status") == "different_audio", old
    checks.append("REPRODUCED the bug: an 8-second clip of the user's own song is rejected under the 12s full-song floor")

    # -- NEW behaviour (short-clip mode): the same clip is now found. --
    new = audio_match.compare_signatures(song, clip, min_match_seconds=audio_match.SHORT_CLIP_MIN_MATCH_SECONDS)
    assert float(new.get("matched_seconds") or 0) >= 4.0, new
    assert float(new.get("audio_score") or 0) > 90, new
    checks.append(f"FIXED: the same 8s clip now matches with audio_score={new['audio_score']} over {new['matched_seconds']:.1f}s")

    status = song_finder.classify_match(new)
    assert status == song_finder.STATUS_CONFIRMED, (status, new)
    checks.append("an 8s clean match of the real song is classified CONFIRMED by song_finder")

    # -- The clip's position inside the original song is reported correctly. --
    assert 55.0 <= float(new.get("source_start") or 0) <= 65.0, new
    checks.append(f"the match is located at the right place in the original (~60s, got {new['source_start']:.1f}s)")

    # -- A 5-second clip may be found but must NEVER be auto-confirmed on
    # length alone -- it can only reach "possible". --
    tiny = clip_at(60.0, 5.0, song_chroma, song_feats)
    tiny_result = audio_match.compare_signatures(song, tiny, min_match_seconds=audio_match.SHORT_CLIP_MIN_MATCH_SECONDS)
    tiny_status = song_finder.classify_match(tiny_result)
    assert tiny_status != song_finder.STATUS_CONFIRMED, (tiny_status, tiny_result)
    checks.append(f"a 5s match is never auto-confirmed (got '{tiny_status}', as required)")

    # -- A DIFFERENT song must still be rejected in short-clip mode: loosening
    # the length floor must not turn the finder into a false-positive machine. --
    other = sig(make_song_chromaprint(song_frames, seed=9999), 180.0, make_features(int(180 / FEAT_STEP), seed=9999))
    wrong = audio_match.compare_signatures(other, clip, min_match_seconds=audio_match.SHORT_CLIP_MIN_MATCH_SECONDS)
    wrong_status = song_finder.classify_match(wrong)
    assert wrong_status == song_finder.STATUS_NOT_FOUND, (wrong_status, wrong)
    checks.append("an unrelated song is still correctly REJECTED in short-clip mode (no false positive)")

    # -- The full-song YouTube path must be unchanged: a long match still
    # behaves exactly as before under the default 12s floor. --
    long_clip = clip_at(60.0, 45.0, song_chroma, song_feats)
    long_result = audio_match.compare_signatures(song, long_clip)
    assert float(long_result.get("matched_seconds") or 0) >= 12.0, long_result
    assert float(long_result.get("audio_score") or 0) > 90, long_result
    checks.append("the unchanged default (12s) still matches a long 45s excerpt -- YouTube completeness path intact")

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
