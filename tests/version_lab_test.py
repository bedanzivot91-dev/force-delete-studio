from __future__ import annotations
import json, sys, tempfile
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
from database import LibraryDB
import server as server_module


def main():
    checks = []

    with tempfile.TemporaryDirectory(prefix="sps-version-lab-") as raw:
        db = LibraryDB(Path(raw) / "test.db")
        for sid, title in (("a", "Verzija A"), ("b", "Verzija B"), ("c", "Verzija C")):
            db.upsert_song({"id": sid, "title": title, "duration": 180})

        try:
            db.create_version_group("x", ["a"])
            raise AssertionError("expected ValueError for a single-song group")
        except ValueError:
            pass
        checks.append("create_version_group refuses a group with fewer than 2 songs")

        group = db.create_version_group("Nedostaješ mi — verzije", ["a", "b", "c"])
        assert group["id"] and len(group["members"]) == 3
        checks.append("create_version_group creates a group with all 3 members")

        db.set_version_master(group["id"], "b", True)
        refreshed = db.get_version_group(group["id"])
        masters = [m for m in refreshed["members"] if m["is_master"]]
        assert len(masters) == 1 and masters[0]["song_id"] == "b"
        checks.append("set_version_master marks exactly one member as master")

        db.set_version_master(group["id"], "c", True)
        refreshed = db.get_version_group(group["id"])
        masters = [m for m in refreshed["members"] if m["is_master"]]
        assert len(masters) == 1 and masters[0]["song_id"] == "c"
        checks.append("marking a new master automatically un-marks the previous one within the same group")

        assert db.remove_from_version_group(group["id"], "a") is True
        refreshed = db.get_version_group(group["id"])
        assert len(refreshed["members"]) == 2
        checks.append("remove_from_version_group removes a member without deleting the song itself")
        assert db.get_song("a") is not None
        checks.append("the removed song still exists in the library, only the group membership was removed")

        assert db.master_group_names_for_song("c") == [refreshed["name"]]
        assert db.master_group_names_for_song("a") == []
        checks.append("master_group_names_for_song correctly reports which groups a song is the master of")

        # -- deletion protection: server.py's /api/song/delete handler must
        # refuse to delete a master song unless force_master_delete is set. --
        with patch.object(server_module, "DB", db):
            master_groups = server_module.DB.master_group_names_for_song("c")
            assert master_groups, "song c should still be reported as master"
        checks.append("the master-song check server.py relies on before deleting is queryable and accurate")

        assert db.delete_version_group(group["id"]) is True
        assert db.list_version_groups() == []
        checks.append("delete_version_group removes the group; songs are untouched")
        assert db.get_song("c") is not None
        checks.append("songs that were in a now-deleted group still exist in the library")

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
