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

    # Whole-file equality is deliberately NOT what this checks: style.css
    # legitimately grows via additions (new full-redesign themes, an
    # accessibility section, component CSS for newer features), which any
    # exact-hash-of-the-whole-file check would flag as "changed" even
    # though the load-bearing shared CSS never moved. What matters -- the
    # truly generic base CSS (:root vars, layout primitives, components)
    # PLUS the shared body[data-theme] palette-swap mixer every theme
    # depends on -- is checked directly via its own byte-for-byte prefix.
    #
    # This prefix was intentionally shortened on the explicit instruction
    # to strip the app down to the default theme + exactly 5 new full
    # themes (vinyl-loft/signal-grid/broadcast-redline/street-mixtape/
    # label-command): the old boundary covered 20 now-deleted simple
    # palette themes that are no longer part of any protected baseline.
    original_prefix_len = 25001
    style_bytes = style.read_bytes()
    check(
        "original CSS unchanged (byte-for-byte prefix, not whole-file hash)",
        len(style_bytes) >= original_prefix_len
        and hashlib.sha256(style_bytes[:original_prefix_len]).hexdigest() == "cb8cae75cf4446de86e7aeb3a0196d3a398689376fe0126354e067cff2b92ef2",
        f"len={len(style_bytes)}, prefix_sha256={hashlib.sha256(style_bytes[:original_prefix_len]).hexdigest()}",
    )
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
