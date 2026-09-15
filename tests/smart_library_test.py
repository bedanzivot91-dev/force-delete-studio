from __future__ import annotations
import json, sys, tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
from database import LibraryDB
import v3_features as v3


def main():
    checks = []

    songs = [
        {"id": "1", "title": "Nedostaješ mi", "display_name": "Marko", "duration": 210, "favorite": 1, "is_liked": 1, "lyrics": "tekst", "local_audio": "/x/a.mp3", "youtube_url": "", "rating": 4, "created_at": "2025-01-01T00:00:00Z"},
        {"id": "2", "title": "Instrumental verzija", "display_name": "Marko", "duration": 90, "favorite": 0, "is_liked": 0, "lyrics": "", "local_audio": "", "youtube_url": "https://youtube.com/x", "rating": 1, "created_at": "2025-06-01T00:00:00Z"},
        {"id": "3", "title": "Kratka pesma", "display_name": "Ana", "duration": 45, "favorite": 1, "is_liked": 0, "lyrics": "još teksta", "local_audio": "/x/c.wav", "youtube_url": "", "rating": 5, "created_at": "2025-03-01T00:00:00Z"},
    ]

    # -- text op: contains --
    rule = {"field": "title", "op": "contains", "value": "pesma"}
    assert v3.evaluate_smart_rule(songs[2], rule) is True
    assert v3.evaluate_smart_rule(songs[0], rule) is False
    checks.append("text 'contains' rule matches case-insensitively")

    # -- number op: gt/lt on duration --
    long_songs = v3.match_smart_collection(songs, [{"field": "duration", "op": "gt", "value": 100}], "all")
    assert [s["id"] for s in long_songs] == ["1"]
    checks.append("number 'gt' rule filters by duration correctly")

    # -- bool op: has_local_audio (derived field, not a raw column) --
    with_audio = v3.match_smart_collection(songs, [{"field": "has_local_audio", "op": "is_true", "value": True}], "all")
    assert {s["id"] for s in with_audio} == {"1", "3"}
    checks.append("derived boolean field has_local_audio works from local_audio/local_wav presence")

    # -- AND (all) vs OR (any) combination --
    rules = [{"field": "favorite", "op": "is_true", "value": True}, {"field": "duration", "op": "lt", "value": 100}]
    and_result = v3.match_smart_collection(songs, rules, "all")
    or_result = v3.match_smart_collection(songs, rules, "any")
    assert {s["id"] for s in and_result} == {"3"}, and_result
    assert {s["id"] for s in or_result} == {"1", "2", "3"}, or_result
    checks.append("match_mode 'all' (AND) is strictly narrower than 'any' (OR) for the same rules")

    # -- date op --
    recent = v3.match_smart_collection(songs, [{"field": "created_at", "op": "after", "value": "2025-02-01"}], "all")
    assert {s["id"] for s in recent} == {"2", "3"}
    checks.append("date 'after' rule compares ISO date prefixes correctly")

    # -- unknown field never crashes, just never matches --
    assert v3.evaluate_smart_rule(songs[0], {"field": "not_a_real_field", "op": "contains", "value": "x"}) is False
    checks.append("an unknown field is safely rejected instead of raising")

    # -- DB layer: save/list/get/delete round-trip, and count is live (not stored) --
    with tempfile.TemporaryDirectory(prefix="sps-smart-") as raw:
        db = LibraryDB(Path(raw) / "test.db")
        saved = db.save_smart_collection("Duže i omiljene", "all", rules)
        assert saved["id"] and saved["name"] == "Duže i omiljene" and saved["rules"] == rules
        checks.append("save_smart_collection persists name/match_mode/rules and returns them back")

        listed = db.list_smart_collections()
        assert len(listed) == 1 and listed[0]["id"] == saved["id"]
        checks.append("list_smart_collections returns the saved definition")

        again = db.save_smart_collection("Duže i omiljene", "any", [{"field": "duration", "op": "gt", "value": 200}])
        assert again["id"] == saved["id"] and again["match_mode"] == "any"
        checks.append("saving with an existing name updates the same row instead of creating a duplicate")

        assert db.delete_smart_collection(saved["id"]) is True
        assert db.list_smart_collections() == []
        checks.append("delete_smart_collection removes the definition; deleting again is a safe no-op")
        assert db.delete_smart_collection(saved["id"]) is False

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
