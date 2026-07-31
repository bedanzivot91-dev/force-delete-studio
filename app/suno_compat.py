from __future__ import annotations
from dataclasses import dataclass
from typing import Any, Callable

@dataclass(slots=True)
class NormalizedPage:
    items: list[dict[str, Any]]
    next_cursor: str | None
    has_more: bool
    mode: str
    warnings: list[str]

CURSOR_KEYS = ("next_cursor", "nextCursor", "cursor", "continuation", "next")
MORE_KEYS = ("has_more", "hasMore", "more", "has_next", "hasNext")
CONTAINER_KEYS = ("data", "result", "feed", "library", "payload", "page")

def _nested_dicts(payload: Any, depth: int = 0):
    if depth > 5 or not isinstance(payload, dict):
        return
    yield payload
    for key in CONTAINER_KEYS:
        value = payload.get(key)
        if isinstance(value, dict):
            yield from _nested_dicts(value, depth + 1)

def normalize_page(payload: Any, extract_items: Callable[[Any], list[dict[str, Any]]], *, mode: str = "unknown") -> NormalizedPage:
    items = extract_items(payload)
    cursor: str | None = None
    more: bool | None = None
    warnings: list[str] = []
    for node in _nested_dicts(payload):
        if cursor is None:
            for key in CURSOR_KEYS:
                value = node.get(key)
                if value not in (None, "", False):
                    cursor = str(value)
                    break
        if more is None:
            for key in MORE_KEYS:
                if key in node:
                    more = bool(node.get(key))
                    break
        pagination = node.get("pagination")
        if isinstance(pagination, dict):
            if cursor is None:
                for key in CURSOR_KEYS:
                    value = pagination.get(key)
                    if value not in (None, "", False):
                        cursor = str(value); break
            if more is None:
                for key in MORE_KEYS:
                    if key in pagination:
                        more = bool(pagination.get(key)); break
    if more is None:
        more = bool(cursor)
    if more and not cursor:
        warnings.append("Suno odgovor kaže da postoji sledeća strana, ali nema cursor.")
    if not items and cursor:
        warnings.append("Suno odgovor ima cursor, ali nema prepoznate pesme.")
    return NormalizedPage(items=items, next_cursor=cursor, has_more=bool(more), mode=mode, warnings=warnings)

def validate_fixture(payload: Any, extract_items: Callable[[Any], list[dict[str, Any]]]) -> dict[str, Any]:
    page = normalize_page(payload, extract_items, mode="fixture")
    return {"count": len(page.items), "ids": [str(x.get("id") or "") for x in page.items], "next_cursor": page.next_cursor, "has_more": page.has_more, "warnings": page.warnings}
