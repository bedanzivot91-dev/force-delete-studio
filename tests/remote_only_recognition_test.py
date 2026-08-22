"""The real user bug: Suno connects, the whole library indexes, a Shorts clip
containing one of their own songs is dropped in -- and the finder always says
"not found".

Root cause reproduced here: song_finder_analyze() filtered the library with
_existing_audio_path(), i.e. "only songs with a local audio file on disk".
A library indexed straight from Suno URLs (the normal case -- nothing is
downloaded permanently) has valid fingerprints but NO local files, so that
filter produced an empty list and there was literally nothing to compare the
clip against. "not found" was the only reachable answer.
"""
from __future__ import annotations
import json, sys, tempfile
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
import server as server_module
from database import LibraryDB
from audio_match import pack_signature


def signature(seed: int, frames: int = 240):
    """Deterministic fake signature. Same seed -> byte-identical features;
    different seed -> genuinely uncorrelated features, so the matcher can
    actually tell them apart."""
    import random
    rnd = random.Random(seed * 104729 + 17)
    feats = [[rnd.gauss(0.0, 1.0) for _ in range(8)] for _ in range(frames)]
    return {"algorithm": server_module.AUDIO_MATCH_VERSION, "duration": frames * 0.5,
            "interval": 0.5, "features": feats, "frame_count": frames,
            "chromaprint": [], "chromaprint_count": 0}


def main():
    checks = []

    with tempfile.TemporaryDirectory(prefix="sps-remote-reco-") as raw:
        db = LibraryDB(Path(raw) / "test.db")

        # A remote-only library exactly like the user's: real songs, real
        # fingerprints from indexing, but zero local audio files.
        for i in range(5):
            sid = f"song{i}"
            db.upsert_song({"id": sid, "title": f"Pesma {i}",
                            "audio_url": f"https://cdn.suno.com/{sid}.mp3", "duration": 120})
            db.save_audio_fingerprint("suno", sid, server_module.AUDIO_MATCH_VERSION,
                                      120.0, 0.5, pack_signature(signature(i)), f"ident{i}", 0.0, 0)

        rows = db.export_rows()
        assert len(rows) == 5
        assert all(not (r.get("local_audio") or r.get("local_wav")) for r in rows)
        checks.append("library is remote-only: 5 songs, 5 fingerprints, 0 local audio files")

        # --- the bug, stated as the code used to state it ---
        with patch.object(server_module, "DB", db):
            old_filter = [s for s in db.export_rows() if server_module._existing_audio_path(s)]
        assert old_filter == [], "precondition: the old local-file filter empties a remote-only library"
        checks.append("OLD behaviour reproduced: _existing_audio_path() filter yields 0 songs to compare -> 'not found' was unavoidable")

        # --- the fix: analyze must compare against fingerprinted songs ---
        clip_sig = signature(2)          # clip really is song2
        seen_song_ids = []

        def fake_candidates(upload_signatures, songs):
            seen_song_ids.extend(str(s.get("id") or "") for s in songs)
            return [], 0

        with patch.object(server_module, "DB", db), \
             patch.object(server_module, "has_chromaprint", return_value=True), \
             patch.object(server_module, "sha256_file", return_value="deadbeef"), \
             patch.object(server_module, "extract_query_signatures", return_value=[clip_sig]), \
             patch.object(server_module, "_song_finder_candidates", side_effect=fake_candidates), \
             patch.object(server_module.song_finder, "is_supported_file", return_value=True):
            probe = Path(raw) / "shorts.mp4"
            probe.write_bytes(b"\x00" * 32)
            server_module.song_finder_analyze(probe)

        assert len(seen_song_ids) >= 5, f"analyze compared against only {len(seen_song_ids)} songs"
        assert set(seen_song_ids[:5]) == {f"song{i}" for i in range(5)}
        checks.append("FIXED: song_finder_analyze now compares the clip against all 5 remote-only songs, not 0")

        # --- and the real matcher genuinely picks the right one ---
        with patch.object(server_module, "DB", db):
            cands, checked = server_module._song_finder_candidates(clip_sig, db.export_rows())
        assert checked == 5, f"expected all 5 fingerprints to be compared, got {checked}"
        checks.append("all 5 stored fingerprints are actually loaded and compared (checked==5)")
        assert cands, "the clip's own song must be found among remote-only songs"
        best = max(cands, key=lambda c: float(c.get("audio_score") or 0))
        assert best["song_id"] == "song2", f"expected song2, got {best['song_id']}"
        checks.append("the correct song (song2) is identified from a remote-only library")

        # --- chromaprint availability must be reported, not silently ignored ---
        with patch.object(server_module, "DB", db), \
             patch.object(server_module, "has_chromaprint", return_value=False):
            status = server_module.song_finder_status()
        assert status["chromaprint"] is False
        checks.append("song_finder_status reports chromaprint availability so a degraded FFmpeg build is visible, not silent")

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
