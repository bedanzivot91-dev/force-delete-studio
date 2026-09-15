from __future__ import annotations
import json, sys, tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
import v3_features as v3


def main():
    checks = []

    with tempfile.TemporaryDirectory(prefix="sps-release-") as raw:
        tmp = Path(raw)
        audio = tmp / "song.mp3"; audio.write_bytes(b"ID3fake-audio-bytes")
        cover = tmp / "cover.jpg"; cover.write_bytes(b"fake-jpg-bytes")
        lrc = tmp / "song.lrc"; lrc.write_text("[00:00.00]tekst")
        stem_dir = tmp / "stems"; stem_dir.mkdir()
        vocals = stem_dir / "vocals.wav"; vocals.write_bytes(b"fake-wav-bytes")

        song = {
            "id": "s1", "title": "Test Pesma", "local_audio": str(audio), "local_cover": str(cover),
            "local_lrc": str(lrc), "local_srt": "", "local_lyrics": "",
            "derived_files": [{"kind": "stem", "label": "Vokal", "path": str(vocals)}],
            "youtube_url": "",
        }

        readiness = v3.release_readiness(song)
        assert readiness["has_audio"] is True and readiness["has_cover"] is True
        assert readiness["has_lyrics"] is False, "local_lyrics is empty but 'lyrics' text field is also absent, so has_lyrics must be False"
        assert readiness["has_subtitles"] is True and readiness["has_stems"] is True
        assert readiness["youtube_published"] is False
        checks.append("release_readiness reports has_audio/has_cover/has_subtitles/has_stems accurately from real file presence")

        target = tmp / "export"
        result = v3.build_release_package(song, target)
        folder = Path(result["path"])
        assert folder.is_dir() and folder.name.startswith("Test")
        checks.append("build_release_package creates a per-song subfolder named after the sanitized title")
        assert (folder / "song.mp3").exists() and (folder / "cover.jpg").exists() and (folder / "song.lrc").exists()
        checks.append("audio, cover and lrc are copied into the release folder")
        assert (folder / "Stemovi" / "vocals.wav").exists()
        checks.append("stem derived files are copied into a Stemovi/ subfolder")
        assert audio.exists() and cover.exists() and vocals.exists()
        checks.append("original source files are untouched (copies, not moves)")

        manifest = json.loads((folder / "release-info.json").read_text(encoding="utf-8"))
        assert "audio" in manifest["included"] and "cover" in manifest["included"]
        assert "srt" in manifest["missing"] and "lyrics_txt" in manifest["missing"]
        checks.append("release-info.json manifest honestly lists both included AND missing items")

        # -- a song with nothing at all must not crash, just report everything missing --
        empty_song = {"id": "s2", "title": "Prazna pesma"}
        empty_result = v3.build_release_package(empty_song, target)
        assert len(empty_result["included"]) == 0 and "audio" in empty_result["missing"]
        checks.append("a song with no local files at all produces an honest empty-but-valid release folder, not a crash")

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
