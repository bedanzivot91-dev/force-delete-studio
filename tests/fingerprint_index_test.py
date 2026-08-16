"""The song finder must not get slower every time a song is added.

Measured before this index existed: one precise comparison costs ~99 ms, and
the finder ran one per song in the library. That is 306 s for a 3091-song
library and ~16 min at 10000 songs -- and the library only grows. The index
answers "which songs could possibly contain this clip" in ~0.13 s so the
precise matcher only runs on a handful of candidates.

These tests use synthetic fingerprints so they run anywhere, but the
parameters they exercise were chosen against a real measurement: on the
user's real Shorts against its real Suno original, only 22 of 204 frames were
bit-identical and 1.98 of 32 bits differed per frame on average. Exact-hash
lookups would miss ~89% of frames, which is why the index samples bits.
"""
from __future__ import annotations
import json, random, sys, tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
from fingerprint_index import (
    FingerprintIndex, BAND_COUNT, BAND_BITS, OFFSET_BUCKET, _keys_for_value,
)


def _song(rnd, frames=1400):
    """Chromaprint is temporally smooth -- consecutive frames differ by a few
    bits, not randomly. Uniform noise would make the index look far more
    selective than it really is."""
    value = rnd.getrandbits(32)
    out = [value]
    for _ in range(frames - 1):
        for _ in range(rnd.randint(1, 4)):
            value ^= 1 << rnd.randrange(32)
        out.append(value)
    return out


def _degrade(frames, rnd, bits=2):
    """Imitate what a lossy re-encode does to a fingerprint."""
    out = []
    for value in frames:
        for _ in range(rnd.randint(0, bits * 2)):
            value ^= 1 << rnd.randrange(32)
        out.append(value)
    return out


def main():
    checks = []
    rnd = random.Random(1234)

    with tempfile.TemporaryDirectory(prefix="sps-fpidx-") as raw:
        index = FingerprintIndex(Path(raw) / "fp.db")

        # -- a clip cut out of a song must find that song, not the 500 others --
        target = _song(rnd, 1400)
        index.add_song("TARGET", target, "v4")
        for i in range(500):
            index.add_song(f"decoy-{i}", _song(rnd), "v4")

        start = 600
        clip = _degrade(target[start:start + 210], rnd)
        found = index.candidates(clip, limit=10)
        assert found and found[0]["song_id"] == "TARGET", found[:3]
        checks.append("a re-encoded clip finds its own song first out of 501 songs")

        assert found[0]["votes"] >= 5 * max(1, found[1]["votes"] if len(found) > 1 else 1)
        checks.append("the correct song wins by a wide vote margin, not by a hair")

        assert abs(found[0]["offset_frames"] - start) <= OFFSET_BUCKET * 2
        checks.append("the index also reports WHERE in the song the clip came from")

        # -- a clip from no indexed song must not confidently name one --
        stranger = _song(random.Random(999))[300:520]
        other = index.candidates(stranger, limit=10)
        top = other[0]["votes"] if other else 0
        assert top < found[0]["votes"] / 5, (top, found[0]["votes"])
        checks.append("an unrelated clip scores far below a real match instead of picking a random song")

        # -- exact hashing would NOT have worked; that is the whole point --
        identical = sum(1 for a, b in zip(clip, target[start:start + 210]) if a == b)
        assert identical < len(clip) * 0.5
        checks.append(f"only {identical}/{len(clip)} clip frames survive re-encoding bit-identical, so bit-sampling is required")

        # -- searching must not slow down as the library grows --
        before = index.stats()["songs"]
        for i in range(500, 1500):
            index.add_song(f"decoy-{i}", _song(rnd), "v4")
        assert index.stats()["songs"] == before + 1000
        again = index.candidates(clip, limit=10)
        assert again[0]["song_id"] == "TARGET"
        checks.append("adding 1000 more songs does not dislodge the correct answer")

        # -- re-indexing a song replaces it instead of duplicating it --
        rows_before = index.stats()["hashes"]
        index.add_song("TARGET", target, "v4")
        assert index.stats()["hashes"] == rows_before, (rows_before, index.stats()["hashes"])
        checks.append("re-indexing a song replaces its rows instead of piling up duplicates")

        # -- deleted songs stop being findable --
        index.remove_song("TARGET")
        gone = index.candidates(clip, limit=5)
        assert all(c["song_id"] != "TARGET" for c in gone)
        checks.append("a song removed from the library stops appearing in results")

        # -- prune drops everything the library no longer has --
        removed = index.prune({f"decoy-{i}" for i in range(10)})
        assert removed == 1490 and index.stats()["songs"] == 10
        checks.append("prune() drops every song that is no longer in the library")

        index.close()

    # -- the hashing itself has to be deterministic across runs/processes --
    assert len(_keys_for_value(0xDEADBEEF)) == BAND_COUNT
    assert _keys_for_value(0xDEADBEEF) == _keys_for_value(0xDEADBEEF)
    assert len(set(_keys_for_value(0))) == BAND_COUNT, "each band must be tagged so bands cannot collide"
    assert all(k < (BAND_COUNT << BAND_BITS) for k in _keys_for_value(0xFFFFFFFF))
    checks.append("hash keys are deterministic and band-tagged, so an index stays readable after a restart")

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
