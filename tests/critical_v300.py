from __future__ import annotations

import json
import sys
import tempfile
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "app"))

import server as server_module
from server import TaskState
from suno_client import SunoAPIError, SunoClient
from youtube_tools import build_search_queries, match_song_to_video
from unittest.mock import patch


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/expired":
            self.send_response(403)
            self.end_headers()
            return
        if self.path == "/html":
            body = b"<html><body>temporary error</body></html>" * 80
            self.send_response(200)
            self.send_header("Content-Type", "text/html")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        body = b"ID3" + (b"\x00" * 4096)
        self.send_response(200)
        self.send_header("Content-Type", "audio/mpeg")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *args):
        pass


def main() -> None:
    checks = []

    task = TaskState("test", "test")
    task.finish_partial("deo")
    assert task.status == "partial"
    checks.append("partial task status")

    task2 = TaskState("test", "test")
    task2.fail("palo")
    assert task2.status == "error" and task2.percent == 0
    checks.append("failed task status")

    failed_download = TaskState("download", "download")
    with patch.object(server_module, "get_client", return_value=object()), \
         patch.object(server_module, "get_download_dir", return_value=Path("C:/Downloads")), \
         patch.object(server_module.DB, "get_song", side_effect=lambda sid: {"id": sid, "title": sid}), \
         patch.object(server_module, "_download_one", side_effect=RuntimeError("simulirana greška")):
        server_module.download_songs(failed_download, ["a", "b"], {"parallel_downloads": 2})
    assert failed_download.status == "error" and "0/2" in failed_download.message
    checks.append("zero successful downloads produce error status")

    partial_download = TaskState("download", "download")
    def simulated_download(_task, _client, song_id, *_args):
        if song_id == "b":
            raise RuntimeError("simulirana greška")
    with patch.object(server_module, "get_client", return_value=object()), \
         patch.object(server_module, "get_download_dir", return_value=Path("C:/Downloads")), \
         patch.object(server_module.DB, "get_song", side_effect=lambda sid: {"id": sid, "title": sid}), \
         patch.object(server_module, "_download_one", side_effect=simulated_download):
        server_module.download_songs(partial_download, ["a", "b"], {"parallel_downloads": 2})
    assert partial_download.status == "partial" and "1/2" in partial_download.message
    checks.append("partial downloads produce partial status")

    client = SunoClient("token")
    client.json_request = lambda *_args, **_kwargs: {"data": {"clip": {"id": "abc", "title": "Pesma", "audio_url": "https://cdn.example/a.mp3"}}}  # type: ignore[method-assign]
    clip = client.get_clip("abc")
    assert clip["id"] == "abc" and clip["audio_url"].endswith(".mp3")
    checks.append("nested Suno clip response")

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    base = f"http://127.0.0.1:{server.server_address[1]}"
    with tempfile.TemporaryDirectory() as tmp:
        target = Path(tmp) / "song.mp3"
        client.download_file(base + "/expired", target, refresh_url=lambda: base + "/audio")
        assert target.exists() and target.stat().st_size > 1024
        checks.append("expired URL refresh and MP3 validation")
        bad = Path(tmp) / "bad.mp3"
        try:
            client.download_file(base + "/html", bad)
        except SunoAPIError:
            pass
        else:
            raise AssertionError("HTML response was accepted as MP3")
        assert not bad.exists()
        checks.append("HTML rejected as audio")
    server.shutdown()

    song = {"title": "Ne pričam nikome o tebi", "display_name": "Nedostaješ PUNOO", "lyrics": "Ne pričam nikome o tebi\nali te čuvam u sebi", "duration": 180}
    queries = build_search_queries(song, 5)
    assert any("Nedostaješ" in q for q in queries) and len(queries) >= 3
    checks.append("multiple YouTube query variants")
    video = {"title": "NEDOSTAJEŠ PUNOO - Ne pricam nikome o tebi Official Audio", "description": "", "channel_title": "Nedostaješ PUNOO PESME", "duration": 181, "channel_id": "owned"}
    match = match_song_to_video(song, video, {"owned"})
    assert match["is_owned_channel"] is True and match["score"] >= 80
    checks.append("owned YouTube result can match")

    print(json.dumps({"ok": True, "passed": len(checks), "checks": checks}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
