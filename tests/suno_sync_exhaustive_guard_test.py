from __future__ import annotations

import sys
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "app"))

import server


class DummyTask:
    pass


def main() -> None:
    captured: list[dict] = []

    def fake_sync(task, options):
        captured.append(dict(options))

    with patch.object(server, "_ORIGINAL_SYNC_LIBRARY", side_effect=fake_sync):
        server.sync_library(DummyTask(), {"max_pages": 3, "include_main": True, "include_workspaces": True})
        assert captured[-1]["max_pages"] >= 100000, captured[-1]

        server.check_new_songs(DummyTask(), {
            "max_pages": 10,
            "include_main": True,
            "include_workspaces": True,
            "refresh_details": True,
        })
        options = captured[-1]
        assert options["max_pages"] >= 100000, options
        assert options["resume"] is False, options
        assert options["liked"] is False, options
        assert options["include_main"] is True, options
        assert options["include_workspaces"] is True, options
        assert options["refresh_details"] is True, options

    print("suno_sync_exhaustive_guard_test: PASS — UI page caps and quick-check early stop can no longer truncate Suno account coverage")


if __name__ == "__main__":
    main()
