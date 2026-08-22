from __future__ import annotations
import json, sys, tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
from database import LibraryDB
import advanced_features as af
from suno_client import format_vtt


def main():
    checks = []

    cues = [{"start_s": 0.0, "end_s": 2.5, "text": "Prva linija"}, {"start_s": 2.5, "end_s": 5.0, "text": "Druga linija"}]
    vtt = format_vtt(cues)
    assert vtt.startswith("WEBVTT\n\n"), vtt
    assert "00:00:00.000 --> 00:00:02.500" in vtt and "Prva linija" in vtt
    checks.append("format_vtt produces a well-formed WEBVTT file with dot-separated milliseconds")

    with tempfile.TemporaryDirectory(prefix="sps-subtitle-") as raw:
        tmp = Path(raw)
        db = LibraryDB(tmp / "test.db")
        song = {"id": "song1", "title": "Test pesma"}
        db.upsert_song(song)
        out_dir = tmp / "out"
        paths = af.save_subtitle_files(song, cues, db, out_dir, source="manual")
        assert Path(paths["vtt"]).exists() and Path(paths["lrc"]).exists() and Path(paths["srt"]).exists()
        checks.append("save_subtitle_files now writes .lrc, .srt AND .vtt")

        assert af.has_manual_subtitle_cues(db, "song1") is True
        checks.append("has_manual_subtitle_cues() is true right after a manual editor save")

        # -- an automatic transcription re-align must NOT silently overwrite
        # the manually-saved cues (this is the actual bug the segment
        # editor needed fixed: source was hardcoded to "manual" for every
        # caller, so there was no way to tell an auto re-run "don't touch
        # this song, the user already fixed it by hand"). --
        auto_cues = [{"start_s": 0.0, "end_s": 1.0, "text": "Pogrešna auto linija"}]
        if af.has_manual_subtitle_cues(db, "song1"):
            saved_auto = False
        else:
            af.save_subtitle_files(song, auto_cues, db, out_dir, source="auto")
            saved_auto = True
        assert saved_auto is False
        checks.append("caller-side guard correctly skips overwriting when manual cues already exist")

        still = db.list_subtitle_cues("song1")
        assert len(still) == 2 and still[0]["text"] == "Prva linija"
        checks.append("manual cues in the DB are untouched after the guarded auto-save was skipped")

        # -- an explicit auto save (simulating overwrite_manual_cues=True) still works --
        af.save_subtitle_files(song, auto_cues, db, out_dir, source="auto")
        after = db.list_subtitle_cues("song1")
        assert len(after) == 1 and after[0]["source"] == "auto"
        checks.append("an explicit forced auto-save still replaces cues and tags them source=auto")

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
