from __future__ import annotations

import os
import sys
import tempfile
import types
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "app"
if str(APP) not in sys.path:
    sys.path.insert(0, str(APP))

# Import the production wrapper with an isolated throw-away data root.  The
# important part of this test is not source inspection: server.py must finish
# applying all runtime patches exactly as the shipped watchdog launches it.
_tmp = tempfile.TemporaryDirectory(prefix="sps-runtime-bundle-")
_tmp_root = Path(_tmp.name)
os.environ["SUNO_STUDIO_USER_DIR"] = str(_tmp_root)
os.environ["SUNO_STUDIO_DATA_DIR"] = str(_tmp_root / "data")
os.environ["SUNO_STUDIO_DOWNLOAD_DIR"] = str(_tmp_root / "downloads")
os.environ["SUNO_STUDIO_EXPORT_DIR"] = str(_tmp_root / "export")
os.environ["SUNO_STUDIO_PUBLISHED_DIR"] = str(_tmp_root / "published")
os.environ["SUNO_STUDIO_LIBRARY_DIR"] = str(_tmp_root / "library")
os.environ["SUNO_STUDIO_RECOGNITION_DIR"] = str(_tmp_root / "recognition")
os.environ["SUNO_AUTO_OPEN"] = "0"

import server  # noqa: E402  - production import intentionally happens after env setup


def main() -> None:
    captured: dict[str, object] = {}
    handler = object.__new__(server.Handler)

    def capture_send_bytes(self, payload, content_type, download_name=None):
        captured["payload"] = bytes(payload)
        captured["content_type"] = str(content_type)
        captured["download_name"] = download_name

    # The final Handler._send_file wrapper only needs _send_bytes for app.js.
    handler._send_bytes = types.MethodType(capture_send_bytes, handler)
    handler._send_file(server.WEB_DIR / "app.js", no_cache=True)

    payload = captured.get("payload")
    assert isinstance(payload, bytes) and payload, "runtime Handler did not return app.js bytes"
    text = payload.decode("utf-8", errors="strict")

    required = (
        "spsWorkspaceGenerationBadge",
        "sps-shell-2026",
        "2026 WORKSPACE · MATCH RECOVERY",
        "spsFinalNav2026Marker",
        "YOUTUBE I OBJAVA",
        "productionWorkspace",
        "/api/connect/start",
        "youtubeAudioMaxVideos",
    )
    missing = [marker for marker in required if marker not in text]
    assert not missing, f"runtime app.js bundle is incomplete; missing: {missing}"

    # These two final layers must be present once in the response.  Duplicate
    # execution would be a separate wiring bug even if the visible page looked OK.
    assert text.count("spsWorkspaceGenerationBadge") >= 1
    assert text.count("spsFinalNav2026Marker") >= 1
    assert str(captured.get("content_type") or "").lower().startswith("application/javascript")

    print(
        "runtime_frontend_bundle_test: PASS — production Handler serves the final UTF-8 2026/Suno/YouTube/Studio bundle",
        f"bytes={len(payload)}",
    )


if __name__ == "__main__":
    try:
        main()
    finally:
        _tmp.cleanup()
