"""Automatic updates must work with zero configuration.

Before this, update checking existed but was inert: update_manifest_url
defaulted to empty, so check_update() returned "not configured" and the user
had to find and paste a URL, then click Check and Download by hand. A fix
therefore never reached anyone who did not already know it existed.
"""
from __future__ import annotations
import json, sys, tempfile
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
import server as server_module
from database import LibraryDB


def main():
    checks = []

    with tempfile.TemporaryDirectory(prefix="sps-autoupd-") as raw:
        db = LibraryDB(Path(raw) / "test.db")

        with patch.object(server_module, "DB", db):
            url = server_module.get_update_manifest_url()
        assert url == server_module.DEFAULT_UPDATE_MANIFEST_URL
        assert url.startswith("https://")
        checks.append("with nothing configured, the built-in HTTPS manifest URL is used (updates work out of the box)")

        with patch.object(server_module, "DB", db):
            db.set_setting("update_manifest_url", "https://example.com/my.json")
            assert server_module.get_update_manifest_url() == "https://example.com/my.json"
            db.set_setting("update_manifest_url", "")
            assert server_module.get_update_manifest_url() == server_module.DEFAULT_UPDATE_MANIFEST_URL
        checks.append("a user-supplied manifest URL overrides the built-in one, and clearing it falls back safely")

        with patch.object(server_module, "DB", db):
            assert server_module.auto_update_enabled() is True
            assert server_module.auto_update_download_enabled() is False
        checks.append("checking is ON by default; a ~200 MB download is NOT started behind the user's back")

        # -- an available update is recorded for the UI, and NOT auto-downloaded --
        available = {"configured": True, "available": True, "current": "3.3.2",
                     "latest": "3.3.2.99", "download_url": "https://x/y.zip",
                     "sha256": "0" * 64, "notes": "test", "message": "Nova verzija je dostupna."}
        downloaded = {"n": 0}
        with patch.object(server_module, "DB", db), \
             patch.object(server_module, "check_update", return_value=available), \
             patch.object(server_module, "download_update", side_effect=lambda *a, **k: downloaded.__setitem__("n", downloaded["n"] + 1) or {"path": "x.zip"}):
            state = server_module.run_update_check()
        assert state["available"] is True and state["latest"] == "3.3.2.99" and state["checked_at"]
        checks.append("an available update is recorded (version + timestamp) so the UI can show it")
        assert downloaded["n"] == 0
        checks.append("with auto-download off, finding an update does NOT start a download")

        # -- opting in does download --
        with patch.object(server_module, "DB", db), \
             patch.object(server_module, "check_update", return_value=available), \
             patch.object(server_module, "download_update", side_effect=lambda *a, **k: downloaded.__setitem__("n", downloaded["n"] + 1) or {"path": "x.zip"}):
            db.set_setting("auto_update_download", "1")
            server_module.run_update_check()
        assert downloaded["n"] == 1
        checks.append("opting in to auto-download really downloads the new version")

        # -- a broken/unreachable manifest must never crash the program --
        with patch.object(server_module, "DB", db), \
             patch.object(server_module, "check_update", side_effect=RuntimeError("mreza nije dostupna")):
            try:
                server_module.run_update_check()
                raised = False
            except RuntimeError:
                raised = True
        assert raised, "run_update_check propagates so the caller can record it"
        checks.append("a failing check raises to its caller instead of silently reporting success")

    # -- the update loop swallows failures so the app keeps running offline --
    import inspect
    src = inspect.getsource(server_module.update_check_loop)
    assert "except Exception" in src and "UPDATE_STOP.wait" in src
    checks.append("the periodic loop catches errors and waits, so no internet never stops the program")

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
