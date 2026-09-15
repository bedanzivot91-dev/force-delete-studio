from __future__ import annotations
import json, sys
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
import server as server_module


def main():
    checks = []

    songs = {
        "a": {"id": "a", "title": "Pesma A", "local_audio": str(ROOT / "tests" / "fake_a.mp3")},
        "b": {"id": "b", "title": "Pesma A (2)", "local_audio": str(ROOT / "tests" / "fake_b.mp3")},
        "c": {"id": "c", "title": "Pesma C", "local_audio": ""},  # no local audio -> must be skipped, not crash
        "d": {"id": "d", "title": "Pesma D", "local_audio": str(ROOT / "tests" / "fake_d.mp3")},
    }
    probable = [
        {"a": {"id": "a", "title": "Pesma A", "duration": 180}, "b": {"id": "b", "title": "Pesma A (2)", "duration": 181}, "title_similarity": 92.0, "duration_delta": 1.0, "reason": "Sličan naslov i skoro isto trajanje; potrebna audio provera."},
        {"a": {"id": "a", "title": "Pesma A", "duration": 180}, "b": {"id": "c", "title": "Pesma C", "duration": 179}, "title_similarity": 80.0, "duration_delta": 1.0, "reason": "test"},
        {"a": {"id": "d", "title": "Pesma D", "duration": 200}, "b": {"id": "b", "title": "Pesma A (2)", "duration": 181}, "title_similarity": 76.0, "duration_delta": 19.0, "reason": "test"},
    ]

    def fake_existing_audio_path(song):
        raw = str(song.get("local_audio") or "")
        return Path(raw) if raw else None

    def fake_signature_for_source(source_type, source_id, source, task=None, label="", force=False):
        return {"features": [[1.0]], "duration": 180.0, "interval": 0.5}

    def fake_compare_signatures(sig_a, sig_b):
        # pair a/b -> high score (confirmed), pair a/c and d/b -> low score (not confirmed)
        return {"audio_score": 90.0, "matched_seconds": 20.0}

    with patch.object(server_module, "DB") as mock_db, \
         patch.object(server_module, "_existing_audio_path", side_effect=fake_existing_audio_path), \
         patch.object(server_module, "_signature_for_source", side_effect=fake_signature_for_source), \
         patch.object(server_module, "compare_signatures", side_effect=fake_compare_signatures):
        mock_db.get_song.side_effect = lambda sid: songs.get(sid)
        result = server_module._duplicate_audio_confirm(probable, limit=40)

    assert result["skipped_no_local_audio"] == 1, result
    checks.append("a pair missing a local file on either side is skipped, not crashed on")
    assert result["checked"] == 2, result
    checks.append("pairs where both songs have local audio are actually fingerprint-compared")
    assert result["confirmed_count"] == 2 and result["confirmed"][0]["audio_status"] == "confirmed"
    checks.append("a high Chromaprint score is classified as confirmed via song_finder.classify_match")

    # -- limit is respected even when more checkable pairs exist --
    many_probable = [probable[0]] * 5
    with patch.object(server_module, "DB") as mock_db, \
         patch.object(server_module, "_existing_audio_path", side_effect=fake_existing_audio_path), \
         patch.object(server_module, "_signature_for_source", side_effect=fake_signature_for_source), \
         patch.object(server_module, "compare_signatures", side_effect=fake_compare_signatures):
        mock_db.get_song.side_effect = lambda sid: songs.get(sid)
        capped = server_module._duplicate_audio_confirm(many_probable, limit=2)
    assert capped["checked"] == 2 and capped["remaining"] == 3, capped
    checks.append("the per-run cap is respected so a large library never blocks the HTTP response")

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
