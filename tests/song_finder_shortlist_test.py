"""The fast index must make the search cheaper WITHOUT making it miss songs.

That trade is the whole risk of this optimisation: a prefilter that is even
slightly wrong turns "found in 306 seconds" into "not found in 5 seconds",
which is worse than the slow version. So the rule the server implements, and
the rule these tests pin down, is that a song is only ever skipped when the
index has actually seen it and ruled it out. An index that is empty, partial,
stale or broken can cost time -- never a match.
"""
from __future__ import annotations
import json, sys, tempfile
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
import server as server_module
from fingerprint_index import FingerprintIndex


def _songs(n):
    return [{"id": f"s{i}", "title": f"Pesma {i}", "display_name": ""} for i in range(n)]


def _ids(rows):
    return [str(r["id"]) for r in rows]


def main():
    checks = []
    songs = _songs(50)
    signature = {"chromaprint": [1, 2, 3, 4, 5]}

    with tempfile.TemporaryDirectory(prefix="sps-shortlist-") as raw:
        index = FingerprintIndex(Path(raw) / "fp.db")

        # -- no index yet: every song is still compared --
        with patch.object(server_module, "get_fingerprint_index", return_value=index):
            shortlist, used = server_module._song_finder_shortlist(signature, songs)
        assert used is False and len(shortlist) == len(songs)
        checks.append("with an empty index the finder compares every song, exactly as it did before")

        # -- fully indexed library: only the index's picks are compared --
        for song in songs:
            index.add_song(song["id"], [7, 7, 7], "v4")
        picks = [{"song_id": "s31", "votes": 300, "offset_frames": 40},
                 {"song_id": "s4", "votes": 12, "offset_frames": 8}]
        with patch.object(server_module, "get_fingerprint_index", return_value=index), \
             patch.object(FingerprintIndex, "candidates", return_value=picks):
            shortlist, used = server_module._song_finder_shortlist(signature, songs)
        assert used is True
        assert _ids(shortlist) == ["s31", "s4"], _ids(shortlist)
        checks.append("with a full index only the index's candidates get the expensive comparison (2 instead of 50)")
        checks.append("candidates are compared strongest-first, in the index's own vote order")

        # -- THE important one: a partially built index must not hide songs --
        index.remove_songs([f"s{i}" for i in range(40, 50)])
        with patch.object(server_module, "get_fingerprint_index", return_value=index), \
             patch.object(FingerprintIndex, "candidates", return_value=picks):
            shortlist, used = server_module._song_finder_shortlist(signature, songs)
        got = _ids(shortlist)
        assert got[:2] == ["s31", "s4"]
        assert set(got) == {"s31", "s4"} | {f"s{i}" for i in range(40, 50)}, got
        checks.append("songs the index has not reached yet are STILL compared, so a half-built index cannot cause a miss")

        # -- a broken index degrades to the old full scan, it does not fail --
        with patch.object(server_module, "get_fingerprint_index", return_value=index), \
             patch.object(FingerprintIndex, "candidates", side_effect=RuntimeError("indeks je oštećen")), \
             patch.object(server_module, "runtime_log", lambda *a, **k: None):
            shortlist, used = server_module._song_finder_shortlist(signature, songs)
        assert used is False and len(shortlist) == len(songs)
        checks.append("a corrupt index falls back to comparing everything instead of returning nothing")

        # -- no index at all (could not be opened) is also survivable --
        with patch.object(server_module, "get_fingerprint_index", return_value=None):
            shortlist, used = server_module._song_finder_shortlist(signature, songs)
        assert used is False and len(shortlist) == len(songs)
        checks.append("if the index file cannot be opened at all, the search still runs")

        # -- a clip with no chromaprint (FFmpeg without the muxer) --
        with patch.object(server_module, "get_fingerprint_index", return_value=index):
            shortlist, used = server_module._song_finder_shortlist({"chromaprint": []}, songs)
        assert used is False and len(shortlist) == len(songs)
        checks.append("a clip with no chromaprint data skips the index rather than shortlisting to nothing")

        # -- the index never invents a song the library no longer has --
        with patch.object(server_module, "get_fingerprint_index", return_value=index), \
             patch.object(FingerprintIndex, "candidates",
                          return_value=[{"song_id": "obrisana", "votes": 99, "offset_frames": 0}] + picks):
            shortlist, _ = server_module._song_finder_shortlist(signature, songs)
        assert "obrisana" not in _ids(shortlist)
        checks.append("a stale index entry for a deleted song cannot put that song back into results")

        index.close()

    # -- the shortlist has to be big enough to be safe --
    assert server_module.SONG_FINDER_SHORTLIST >= 10
    checks.append(f"the shortlist keeps {server_module.SONG_FINDER_SHORTLIST} candidates of headroom, far above the measured 8.6x worst-case margin")

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
