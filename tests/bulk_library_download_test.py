from __future__ import annotations

import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "app"
sys.path.insert(0, str(APP))

import server


class FakeDB:
    def __init__(self, total: int):
        self.total = total
        self.offsets: list[int] = []

    def count_songs_filtered(self, **kwargs):
        return self.total

    def list_songs(self, *, limit: int, offset: int, **kwargs):
        self.offsets.append(offset)
        end = min(self.total, offset + limit)
        return [{"id": f"song-{i}"} for i in range(offset, end)]


def main() -> None:
    # Regression: 200 is display pagination only; even a >20k library must be
    # selectable as ids in bounded DB pages.
    fake = FakeDB(25037)
    ids = server._list_song_ids_unbounded(fake, limit=200)
    assert len(ids) == 25037, len(ids)
    assert ids[0] == "song-0" and ids[-1] == "song-25036"
    assert fake.offsets[:3] == [0, 1000, 2000]
    assert fake.offsets[-1] == 25000

    with tempfile.TemporaryDirectory() as td:
        options = {
            "target_dir": td,
            "folder_per_song": True,
            "collection_subfolder_id": 7,
            "folders_by_month": True,
        }
        patched = server._build_per_song_download_options(
            "abcdef1234567890",
            options,
            song={"title": "Moja pesma", "created_at": "2026-08-18T12:00:00Z"},
            collections=[{"id": 7, "name": "Moja kolekcija"}],
        )
        target = Path(patched["target_dir"])
        assert target.name == "Moja pesma [abcdef12]", target
        assert target.parent.name == "2026-08", target
        assert target.parent.parent.name == "Moja kolekcija", target
        assert patched["collection_subfolder_id"] == 0
        assert patched["folders_by_month"] is False

    extension = (APP / "web" / "bulk_download_extension.js").read_text(encoding="utf-8")
    for marker in (
        "SVE SA SUNO NALOGA",
        "SVE IZ TRENUTNOG FILTERA",
        "optSongFolders",
        "folder_per_song",
        "/api/song-ids",
        "SVE DOSTUPNO",
    ):
        assert marker in extension, marker

    print("bulk_library_download_test: PASS — whole-library selection + per-song folders")


if __name__ == "__main__":
    main()
