from __future__ import annotations

import ast
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "app"
sys.path.insert(0, str(APP))

from database import LibraryDB


def function_source(tree: ast.AST, source: str, name: str) -> str:
    for node in ast.walk(tree):
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)) and node.name == name:
            segment = ast.get_source_segment(source, node)
            if segment:
                return segment
    raise AssertionError(f"function {name} not found")


def main() -> None:
    wrapper_source = (APP / "server.py").read_text(encoding="utf-8")
    wrapper_tree = ast.parse(wrapper_source)
    complete_reader = function_source(wrapper_tree, wrapper_source, "_list_library_cursor_complete")

    assert '"disliked": "False"' in complete_reader
    assert '"trashed": "True" if trashed else "False"' in complete_reader
    assert "fromStudioProject" not in complete_reader, (
        "main Suno v3 feed must not exclude Studio/Project songs; otherwise songs visible on suno.com can be absent from SQLite"
    )
    assert 'self.last_feed_mode = "v3-all"' in complete_reader
    assert "_core.SunoClient.list_library_cursor = _list_library_cursor_complete" in wrapper_source

    with tempfile.TemporaryDirectory(prefix="suno-complete-library-") as tmp:
        db = LibraryDB(Path(tmp) / "library.db")
        normal = {
            "id": "11111111-1111-1111-1111-111111111111",
            "title": "Glavna pesma",
            "audio_url": "https://cdn1.suno.ai/11111111-1111-1111-1111-111111111111.mp3",
            "metadata": {"duration": 180},
        }
        studio = {
            "id": "22222222-2222-2222-2222-222222222222",
            "title": "Studio Project pesma",
            "audio_url": "https://cdn1.suno.ai/22222222-2222-2222-2222-222222222222.mp3",
            "metadata": {"duration": 220, "fromStudioProject": True},
        }
        assert db.upsert_song(normal, source_group="glavna Suno biblioteka")
        assert db.upsert_song(studio, source_group="glavna Suno biblioteka")
        # The same clip can also be returned by a Workspace pass. It must update,
        # not duplicate, because the local database is keyed by the Suno clip ID.
        assert db.upsert_song(studio, source_group="Workspace Test")

        ids = set(db.all_song_ids())
        assert normal["id"] in ids
        assert studio["id"] in ids
        assert db.count_songs() == 2, f"expected 2 unique Suno IDs, got {db.count_songs()}"

    print("suno_complete_library_sync_test: PASS — main v3 feed includes Studio songs and SQLite de-duplicates by Suno ID")


if __name__ == "__main__":
    main()
