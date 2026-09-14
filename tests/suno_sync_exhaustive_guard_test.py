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
        # Small explicit limits are an internal checkpoint/resume contract and
        # must remain exact. This is how regression_v300 verifies one-page
        # resume behavior without asking the UI to truncate a real account.
        server.sync_library(DummyTask(), {"max_pages": 3, "include_main": True, "include_workspaces": True})
        assert captured[-1]["max_pages"] == 3, captured[-1]

        # The normal UI historically sends 100 for "full sync". That value is
        # now interpreted as exhaustive so accounts larger than 100 pages are
        # not silently truncated.
        server.sync_library(DummyTask(), {"max_pages": 100, "include_main": True, "include_workspaces": True})
        assert captured[-1]["max_pages"] >= 100000, captured[-1]

        # "Check new" is correctness-first: it must scan through old known
        # pages to reach a missing historical song and it must not use resume.
        server.check_new_songs(DummyTask(), {
            "max_pages": 10,
            "include_main": True,
            "include_workspaces": True,
            "refresh_details": True,
            "reset_checkpoints": True,
        })
        options = captured[-1]
        assert options["max_pages"] >= 100000, options
        assert options["resume"] is False, options
        assert options["liked"] is False, options
        assert options["include_main"] is True, options
        assert options["include_workspaces"] is True, options
        assert options["refresh_details"] is True, options
        assert "reset_checkpoints" not in options, options

    print("suno_sync_exhaustive_guard_test: PASS — UI full sync/check-new are exhaustive while explicit internal page limits preserve resume semantics")


if __name__ == "__main__":
    main()
