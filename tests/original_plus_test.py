from __future__ import annotations

import hashlib
import json
import tempfile
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "app"))
from database import LibraryDB


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    checks: list[str] = []

    def check(name: str, condition: bool, detail: object = "") -> None:
        if not condition:
            raise AssertionError(f"{name}: {detail}")
        checks.append(name)

    style = ROOT / "app" / "web" / "style.css"
    index = (ROOT / "app" / "web" / "index.html").read_text(encoding="utf-8")
    js = (ROOT / "app" / "web" / "app.js").read_text(encoding="utf-8")
    server = (ROOT / "app" / "server.py").read_text(encoding="utf-8")

    check("original CSS unchanged", sha256(style) == "83285f159f486ce4bdbb21341b1e1d874d4bab85c138b30af63183061eb13351", sha256(style))
    for token in ("app-shell", "sidebar", "nav-item", "view-library", "view-import", "view-tools", "view-production"):
        check(f"original UI retained: {token}", token in index)
    check("dedicated song finder view", 'id="view-recognition"' in index and "Pronalazač pesme" in index)
    check("no visible Shazam name", "shazam" not in (index + js).lower())
    check("remembered folders UI", "watchedFoldersList" in index and "rescanWatchedFolders" in js)
    check("recognition history UI", "musicRecognitionHistory" in index and "loadMusicRecognitionHistory" in js)
    check("automatic YouTube pipeline", server.count("start_automatic_youtube_pipeline") >= 2)
    check("published folder logic", "copy_song_to_published_folder" in server and "PUBLISHED_DIR" in server)
    check("persistent library folder", "LOCAL_LIBRARY_DIR" in server and "_copy_imported_bundle_to_library" in server)

    with tempfile.TemporaryDirectory(prefix="sps331-db-") as raw:
        db_path = Path(raw) / "data" / "library.db"
        db = LibraryDB(db_path)
        first = db.remember_watched_folder(str(Path(raw) / "music"), recursive=True)
        check("remember folder created", first.get("enabled") in (1, True), first)
        db.update_watched_folder_scan(first["path"], file_count=9, added_count=7)
        rec = db.add_recognition({
            "original_filename": "clip.mp4",
            "input_path": str(Path(raw) / "Pronalazac_pesme" / "Ulazni_isecci" / "clip.mp4"),
            "prepared_audio_path": str(Path(raw) / "Pronalazac_pesme" / "Pripremljeni_audio" / "clip.wav"),
            "status": "done",
            "result": {
                "found": True,
                "provider": "AudD",
                "title": "Test pesma",
                "artist": "Test izvođač",
                "album": "Test album",
                "release_date": "2026-01-01",
                "song_link": "https://example.test/song",
                "timecode": "00:12",
                "raw": {"result": {"title": "Test pesma"}},
            },
        })
        check("recognition saved", int(rec.get("id") or 0) > 0, rec)

        reopened = LibraryDB(db_path)
        folders = reopened.list_watched_folders()
        history = reopened.list_recognitions()
        check("folders persist after restart", len(folders) == 1 and int(folders[0].get("last_added_count") or 0) == 7, folders)
        check("recognition persists after restart", len(history) == 1 and history[0].get("title") == "Test pesma", history)
        reopened.set_recognition_library_song(int(rec["id"]), "recognized:test")
        check("recognition library link persists", reopened.get_recognition(int(rec["id"])).get("library_song_id") == "recognized:test")

    print(json.dumps({"ok": True, "passed": len(checks), "checks": checks}, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
