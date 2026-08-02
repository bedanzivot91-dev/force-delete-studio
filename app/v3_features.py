from __future__ import annotations

import ctypes
import hashlib
import http.client
import json
import math
import os
import platform
import re
import shutil
import socket
import sqlite3
import subprocess
import tempfile
import time
import unicodedata
import urllib.error
import urllib.parse
import urllib.request
import uuid
import zipfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Iterable

from audio_tools import ensure_ffmpeg, ffmpeg_path, ffprobe_path, probe_audio
from suno_client import sanitize_filename


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def sha256_file(path: str | Path, chunk_size: int = 1024 * 1024) -> str:
    p = Path(path)
    h = hashlib.sha256()
    with p.open("rb") as fh:
        while True:
            chunk = fh.read(chunk_size)
            if not chunk:
                break
            h.update(chunk)
    return h.hexdigest()


def atomic_write(path: str | Path, data: bytes) -> Path:
    target = Path(path).expanduser().resolve()
    target.parent.mkdir(parents=True, exist_ok=True)
    temp = target.with_name(target.name + f".{uuid.uuid4().hex[:8]}.tmp")
    with temp.open("wb") as fh:
        fh.write(data)
        fh.flush()
        os.fsync(fh.fileno())
    os.replace(temp, target)
    return target


def atomic_write_json(path: str | Path, payload: Any) -> Path:
    return atomic_write(path, json.dumps(payload, ensure_ascii=False, indent=2, default=str).encode("utf-8"))


def _ram_bytes() -> int:
    if os.name == "nt":
        class MEMORYSTATUSEX(ctypes.Structure):
            _fields_ = [
                ("dwLength", ctypes.c_ulong), ("dwMemoryLoad", ctypes.c_ulong),
                ("ullTotalPhys", ctypes.c_ulonglong), ("ullAvailPhys", ctypes.c_ulonglong),
                ("ullTotalPageFile", ctypes.c_ulonglong), ("ullAvailPageFile", ctypes.c_ulonglong),
                ("ullTotalVirtual", ctypes.c_ulonglong), ("ullAvailVirtual", ctypes.c_ulonglong),
                ("ullAvailExtendedVirtual", ctypes.c_ulonglong),
            ]
        state = MEMORYSTATUSEX(); state.dwLength = ctypes.sizeof(MEMORYSTATUSEX)
        if ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(state)):
            return int(state.ullTotalPhys)
    try:
        return int(os.sysconf("SC_PAGE_SIZE") * os.sysconf("SC_PHYS_PAGES"))
    except Exception:
        return 0


def _url_check(url: str, timeout: int = 5) -> dict[str, Any]:
    request = urllib.request.Request(url, method="HEAD", headers={"User-Agent": "SunoPesmeStudio/3.0"})
    started = time.monotonic()
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return {"ok": 200 <= int(response.status) < 500, "status": int(response.status), "ms": round((time.monotonic() - started) * 1000)}
    except urllib.error.HTTPError as exc:
        return {"ok": exc.code < 500, "status": exc.code, "ms": round((time.monotonic() - started) * 1000), "message": str(exc)}
    except Exception as exc:
        return {"ok": False, "status": 0, "ms": round((time.monotonic() - started) * 1000), "message": str(exc)}


def system_preflight(root: Path, data_dir: Path, download_dir: Path, tool_status: dict[str, Any]) -> dict[str, Any]:
    root = root.resolve(); data_dir = data_dir.resolve(); download_dir = download_dir.resolve()
    checks: list[dict[str, Any]] = []
    def add(key: str, ok: bool, label: str, details: Any = "", level: str | None = None) -> None:
        checks.append({"key": key, "ok": bool(ok), "label": label, "details": details, "level": level or ("ok" if ok else "error")})
    is_64 = platform.machine().lower() in {"amd64", "x86_64", "arm64", "aarch64"} or (8 * ctypes.sizeof(ctypes.c_void_p) == 64)
    add("os", os.name == "nt", "Windows 10/11", platform.platform(), "ok" if os.name == "nt" else "warning")
    add("arch", is_64, "64-bitni sistem", platform.machine())
    ram = _ram_bytes()
    add("ram", ram >= 8 * 1024**3, "Najmanje 8 GB RAM-a", {"bytes": ram, "gb": round(ram / 1024**3, 1)}, "ok" if ram >= 8 * 1024**3 else "warning")
    for key, path in (("root_write", root), ("data_write", data_dir), ("download_write", download_dir)):
        try:
            path.mkdir(parents=True, exist_ok=True)
            probe = path / f".write-test-{uuid.uuid4().hex}.tmp"
            probe.write_bytes(b"ok"); probe.unlink()
            add(key, True, f"Upis u {path.name or path.drive}", str(path))
        except Exception as exc:
            add(key, False, f"Upis u {path.name or path.drive}", str(exc))
    usage = shutil.disk_usage(download_dir)
    add("disk", usage.free >= 10 * 1024**3, "Najmanje 10 GB slobodnog prostora", {"free_bytes": usage.free, "free_gb": round(usage.free / 1024**3, 1)}, "ok" if usage.free >= 10 * 1024**3 else "warning")
    add("long_paths", len(str(root)) < 180, "Bezbedna dužina putanje", {"length": len(str(root)), "path": str(root)}, "ok" if len(str(root)) < 180 else "warning")
    for name, status in tool_status.items():
        ready = bool(status.get("ready"))
        # required/optional/severity come from v3_tool_status() itself now
        # (single source of truth) -- criticality was previously decided
        # purely from `ready`, so a missing OPTIONAL tool like Panako
        # counted as a hard "error" and readiness became "blocked" even
        # though every core feature (FFmpeg, yt-dlp, Deno, Python) was
        # fine. Panako has its own separate install/status flow and must
        # never prevent the program from starting.
        level = status.get("severity") or ("ok" if ready else "error")
        optional = bool(status.get("optional"))
        label = f"Alat {name}" + (" (opcioni dodatak)" if optional and not ready else "")
        add(f"tool_{name}", ready, label, status, level)
    internet = {
        "suno": _url_check("https://suno.com", 5),
        "youtube": _url_check("https://www.youtube.com", 5),
        "google_api": _url_check("https://www.googleapis.com", 5),
        "github": _url_check("https://github.com", 5),
    }
    add("internet", any(v.get("ok") for v in internet.values()), "Internet i TLS veze", internet)
    errors = sum(1 for x in checks if x["level"] == "error" and not x["ok"])
    warnings = sum(1 for x in checks if x["level"] == "warning" and not x["ok"])
    readiness = "ready" if errors == 0 and warnings == 0 else ("limited" if errors == 0 else "blocked")
    return {"ok": errors == 0, "readiness": readiness, "checks": checks, "errors": errors, "warnings": warnings, "created_at": now_iso()}


def _song_paths(song: dict[str, Any]) -> Iterable[tuple[str, Path]]:
    for field in ("local_audio", "local_wav", "local_video", "local_cover", "local_lyrics", "local_lrc", "local_srt"):
        raw = str(song.get(field) or "").strip()
        if raw:
            yield field, Path(raw).expanduser()
    for derived in song.get("derived_files") or []:
        raw = str(derived.get("path") or "").strip()
        if raw:
            yield f"derived:{derived.get('id')}", Path(raw).expanduser()


def library_integrity_scan(db: Any, *, repair_hashes: bool = True, probe_audio_files: bool = True, progress: Callable[[str, int, int], None] | None = None) -> dict[str, Any]:
    songs = db.export_rows()
    candidates: list[tuple[str, str, Path, dict[str, Any]]] = []
    for row in songs:
        full = db.get_song(str(row.get("id") or "")) or row
        for field, path in _song_paths(full):
            candidates.append((str(full.get("id") or ""), field, path, full))
    missing: list[dict[str, Any]] = []
    corrupt: list[dict[str, Any]] = []
    checked: list[dict[str, Any]] = []
    exact_hashes: dict[str, list[dict[str, Any]]] = {}
    for idx, (song_id, field, path, song) in enumerate(candidates, 1):
        if progress: progress(path.name, idx, len(candidates))
        if not path.exists() or not path.is_file():
            missing.append({"song_id": song_id, "title": song.get("title"), "field": field, "path": str(path)})
            continue
        try:
            size = path.stat().st_size
            digest = sha256_file(path)
            item: dict[str, Any] = {"song_id": song_id, "title": song.get("title"), "field": field, "path": str(path.resolve()), "size": size, "sha256": digest}
            if field in {"local_audio", "local_wav"} or field.startswith("derived:"):
                if probe_audio_files and path.suffix.lower() in {".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".opus"}:
                    info = probe_audio(path)
                    item["audio"] = info
                    if float(info.get("duration") or 0) <= 0:
                        corrupt.append({**item, "reason": "FFprobe nije pronašao trajanje zvuka."})
                exact_hashes.setdefault(digest, []).append(item)
                if repair_hashes and field in {"local_audio", "local_wav"}:
                    db.update_song_files(song_id, file_sha256=digest, content_sha256=digest)
            checked.append(item)
        except Exception as exc:
            corrupt.append({"song_id": song_id, "title": song.get("title"), "field": field, "path": str(path), "reason": str(exc)})
    duplicates = [items for items in exact_hashes.values() if len(items) > 1]
    return {"ok": not missing and not corrupt, "songs": len(songs), "checked_files": len(checked), "missing": missing, "corrupt": corrupt, "exact_duplicate_groups": duplicates, "created_at": now_iso()}


def _norm_title(value: str) -> str:
    value = value.casefold()
    value = value.translate(str.maketrans("čćžšđ", "cczsd"))
    value = re.sub(r"\b(official|audio|video|lyrics?|lyric|suno|remix|version|verzija|nedostajes|punoo)\b", " ", value)
    return re.sub(r"[^a-z0-9]+", " ", value).strip()


def duplicate_detection(db: Any) -> dict[str, Any]:
    rows = db.export_rows()
    exact: dict[str, list[dict[str, Any]]] = {}
    for row in rows:
        h = str(row.get("content_sha256") or row.get("file_sha256") or "")
        if h: exact.setdefault(h, []).append(row)
    exact_groups = [[{"id": r.get("id"), "title": r.get("title"), "duration": r.get("duration"), "path": r.get("local_audio") or r.get("local_wav")} for r in group] for group in exact.values() if len(group) > 1]
    probable: list[dict[str, Any]] = []
    indexed = []
    for row in rows:
        title = _norm_title(str(row.get("title") or "")); duration = float(row.get("duration") or 0)
        if title and duration > 0: indexed.append((row, title, duration))
    for i, (a, at, ad) in enumerate(indexed):
        aw = set(at.split())
        for b, bt, bd in indexed[i + 1:]:
            if abs(ad - bd) > max(3.0, min(ad, bd) * 0.025): continue
            bw = set(bt.split()); overlap = len(aw & bw) / max(1, len(aw | bw))
            if overlap >= 0.75 and str(a.get("content_sha256") or "") != str(b.get("content_sha256") or ""):
                probable.append({"a": {"id": a.get("id"), "title": a.get("title"), "duration": ad}, "b": {"id": b.get("id"), "title": b.get("title"), "duration": bd}, "title_similarity": round(overlap * 100, 1), "duration_delta": round(abs(ad - bd), 3), "reason": "Sličan naslov i skoro isto trajanje; potrebna audio provera."})
    return {"exact_groups": exact_groups, "probable": probable[:1000], "exact_count": len(exact_groups), "probable_count": len(probable), "created_at": now_iso()}


# -- Smart Library: rule-based AND/OR filters saved as a named definition
# and re-evaluated live against the current library every time it's opened
# (a "live view", never a copy -- nothing is duplicated or moved). --

SMART_LIBRARY_FIELDS = {
    "title": "text", "display_name": "text", "tags": "text", "custom_tags": "text",
    "prompt": "text", "lyrics": "text", "notes": "text", "model_version": "text",
    "duration": "number", "rating": "number",
    "favorite": "bool", "is_liked": "bool", "is_public": "bool",
    "has_local_audio": "bool", "has_lyrics": "bool", "has_youtube": "bool",
    "created_at": "date",
}

_TEXT_OPS = {"contains", "not_contains", "equals", "starts_with"}
_NUMBER_OPS = {"gt", "gte", "lt", "lte", "eq"}
_BOOL_OPS = {"is_true", "is_false"}
_DATE_OPS = {"before", "after"}


def _smart_field_value(song: dict[str, Any], field: str) -> Any:
    if field == "has_local_audio":
        return bool(str(song.get("local_audio") or song.get("local_wav") or "").strip())
    if field == "has_lyrics":
        return bool(str(song.get("lyrics") or "").strip())
    if field == "has_youtube":
        return bool(str(song.get("youtube_url") or "").strip())
    return song.get(field)


def evaluate_smart_rule(song: dict[str, Any], rule: dict[str, Any]) -> bool:
    field = str(rule.get("field") or "")
    kind = SMART_LIBRARY_FIELDS.get(field)
    if kind is None:
        return False
    op = str(rule.get("op") or "")
    value = _smart_field_value(song, field)
    target = rule.get("value")
    if kind == "text":
        haystack = str(value or "").casefold()
        needle = str(target or "").casefold()
        if op == "contains":
            return needle in haystack
        if op == "not_contains":
            return needle not in haystack
        if op == "equals":
            return haystack == needle
        if op == "starts_with":
            return haystack.startswith(needle)
        return False
    if kind == "number":
        try:
            actual = float(value or 0)
            target_num = float(target)
        except (TypeError, ValueError):
            return False
        if op == "gt":
            return actual > target_num
        if op == "gte":
            return actual >= target_num
        if op == "lt":
            return actual < target_num
        if op == "lte":
            return actual <= target_num
        if op == "eq":
            return actual == target_num
        return False
    if kind == "bool":
        actual_bool = bool(value)
        if op == "is_true":
            return actual_bool
        if op == "is_false":
            return not actual_bool
        return False
    if kind == "date":
        actual_text = str(value or "")[:10]
        target_text = str(target or "")[:10]
        if not actual_text or not target_text:
            return False
        if op == "before":
            return actual_text < target_text
        if op == "after":
            return actual_text > target_text
        return False
    return False


def evaluate_smart_rules(song: dict[str, Any], rules: list[dict[str, Any]], match_mode: str = "all") -> bool:
    if not rules:
        return False
    results = [evaluate_smart_rule(song, rule) for rule in rules]
    return all(results) if match_mode != "any" else any(results)


def match_smart_collection(songs: Iterable[dict[str, Any]], rules: list[dict[str, Any]], match_mode: str = "all") -> list[dict[str, Any]]:
    return [song for song in songs if evaluate_smart_rules(song, rules, match_mode)]


def _template_values(song: dict[str, Any]) -> dict[str, str]:
    created = str(song.get("created_at") or song.get("first_seen_at") or "")
    try: dt = datetime.fromisoformat(created.replace("Z", "+00:00"))
    except Exception: dt = datetime.now()
    published = bool(str(song.get("youtube_url") or "") or str(song.get("youtube_published_at") or ""))
    return {
        "id": sanitize_filename(str(song.get("id") or ""), 80), "naslov": sanitize_filename(str(song.get("title") or song.get("id") or "Bez naslova"), 120),
        "autor": sanitize_filename(str(song.get("display_name") or "Suno"), 80), "godina": str(dt.year), "mesec": f"{dt.month:02d}",
        "dan": f"{dt.day:02d}", "model": sanitize_filename(str(song.get("model_version") or "nepoznat-model"), 60),
        "zanr": sanitize_filename(str(song.get("genre") or song.get("tags") or "bez-zanra").split(",")[0], 60),
        "status": "Objavljene" if published else "Neobjavljene", "kolekcija": sanitize_filename(str(song.get("source_group") or "Biblioteka"), 80),
    }


def organize_library(db: Any, target_root: Path, *, template: str = "{status}/{godina}/{naslov}", mode: str = "move", dry_run: bool = True, ids: list[str] | None = None, progress: Callable[[str, int, int], None] | None = None) -> dict[str, Any]:
    target_root = target_root.expanduser().resolve(); target_root.mkdir(parents=True, exist_ok=True)
    rows = db.export_rows(ids)
    plan: list[dict[str, Any]] = []
    file_fields = ("local_audio", "local_wav", "local_video", "local_cover", "local_lyrics", "local_lrc", "local_srt")
    total = sum(1 for song in rows for field in file_fields if str(song.get(field) or ""))
    done = 0
    for song in rows:
        values = _template_values(song)
        try: relative = template.format_map(values)
        except KeyError as exc: raise RuntimeError(f"Nepoznata promenljiva u šablonu: {exc}") from exc
        base = target_root / Path(relative)
        for field in file_fields:
            raw = str(song.get(field) or "").strip()
            if not raw: continue
            source = Path(raw).expanduser()
            done += 1
            if progress: progress(source.name, done, total)
            if not source.exists() or not source.is_file():
                plan.append({"song_id": song.get("id"), "field": field, "source": str(source), "status": "missing"}); continue
            target = base / source.name
            n = 2
            while target.exists() and target.resolve() != source.resolve():
                target = base / f"{source.stem} ({n}){source.suffix}"; n += 1
            item = {"song_id": song.get("id"), "title": song.get("title"), "field": field, "source": str(source.resolve()), "target": str(target.resolve()), "mode": mode, "status": "planned" if dry_run else "done"}
            if not dry_run and source.resolve() != target.resolve():
                target.parent.mkdir(parents=True, exist_ok=True)
                if mode == "copy": shutil.copy2(source, target)
                else: shutil.move(str(source), str(target))
                db.update_song_files(str(song.get("id") or ""), **{field: str(target.resolve())})
            plan.append(item)
    return {"ok": True, "dry_run": dry_run, "mode": mode, "target_root": str(target_root), "template": template, "items": plan, "count": len(plan), "created_at": now_iso()}


def _audio_energy_points(audio_path: Path, temp_dir: Path) -> list[tuple[float, float]]:
    ensure_ffmpeg()
    metadata = temp_dir / "energy.txt"
    filt = f"aresample=8000,asetnsamples=n=8000:p=0,astats=metadata=1:reset=1,ametadata=print:file='{str(metadata).replace(':', '\\:').replace("'", "\\'")}'"
    cmd = [str(ffmpeg_path()), "-hide_banner", "-nostats", "-y", "-i", str(audio_path), "-vn", "-af", filt, "-f", "null", "-"]
    subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=900, creationflags=(subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0))
    if not metadata.exists(): return []
    t = 0.0; points: list[tuple[float, float]] = []
    for line in metadata.read_text(encoding="utf-8", errors="replace").splitlines():
        if line.startswith("frame:"):
            m = re.search(r"pts_time:([0-9.]+)", line)
            if m: t = float(m.group(1))
        elif "lavfi.astats.Overall.RMS_level=" in line:
            raw = line.rsplit("=", 1)[-1].strip()
            try: value = float(raw)
            except ValueError: value = -100.0
            points.append((t, value if math.isfinite(value) else -100.0))
    return points


def suggest_short_clips(song: dict[str, Any], audio_path: Path, durations: list[int] | None = None) -> dict[str, Any]:
    audio_path = audio_path.expanduser().resolve()
    if not audio_path.exists(): raise RuntimeError("Audio fajl ne postoji.")
    info = probe_audio(audio_path); total = float(info.get("duration") or song.get("duration") or 0)
    if total <= 1: raise RuntimeError("Trajanje pesme nije moglo da se pročita.")
    durations = [int(x) for x in (durations or [15, 30, 45, 60]) if 5 <= int(x) <= min(90, int(total))]
    with tempfile.TemporaryDirectory(prefix="suno-energy-") as tmp:
        points = _audio_energy_points(audio_path, Path(tmp))
    if not points: points = [(float(i), -20.0) for i in range(max(1, int(total)))]
    suggestions: list[dict[str, Any]] = []
    lyrics = [x.strip() for x in str(song.get("lyrics") or "").splitlines() if x.strip() and not x.strip().startswith("[")]
    repeated = {line.casefold() for line in lyrics if sum(1 for x in lyrics if x.casefold() == line.casefold()) >= 2}
    for duration in durations:
        best: list[tuple[float, float]] = []
        step = max(1, duration // 5)
        for start in range(0, max(1, int(total - duration) + 1), step):
            vals = [v for t, v in points if start <= t < start + duration]
            if not vals: continue
            average = sum(vals) / len(vals); peak = max(vals)
            edge_penalty = 2.0 if start < 5 or start + duration > total - 3 else 0.0
            score = average * 0.65 + peak * 0.35 - edge_penalty
            best.append((score, float(start)))
        best.sort(reverse=True)
        used: list[float] = []
        for score, start in best:
            if any(abs(start - x) < duration * 0.6 for x in used): continue
            used.append(start)
            suggestions.append({"duration": duration, "start": round(start, 3), "end": round(min(total, start + duration), 3), "score": round(max(0.0, min(100.0, 100 + score * 2.3)), 1), "reason": "Energetski jak deo zvuka" + ("; tekst sadrži ponovljene refrene" if repeated else "")})
            if len(used) >= 3: break
    suggestions.sort(key=lambda x: (x["duration"], -x["score"]))
    return {"song_id": song.get("id"), "title": song.get("title"), "audio": str(audio_path), "duration": total, "suggestions": suggestions, "created_at": now_iso()}


TEASER_MIN_DURATION = 30.0
TEASER_MAX_DURATION = 50.0


def _normalize_lyrics_text(text: str) -> str:
    decomposed = unicodedata.normalize("NFKD", str(text or ""))
    stripped = "".join(ch for ch in decomposed if not unicodedata.combining(ch))
    return re.sub(r"\s+", " ", stripped.casefold()).strip()


def find_lyrics_timestamp(cues: list[dict[str, Any]], query: str) -> dict[str, Any] | None:
    """Find the earliest saved LRC/SRT cue whose text contains the given
    lyrics snippet (diacritics/case-insensitive substring match)."""
    needle = _normalize_lyrics_text(query)
    if not needle:
        return None
    best: dict[str, Any] | None = None
    for cue in cues:
        haystack = _normalize_lyrics_text(str(cue.get("text") or ""))
        if needle not in haystack:
            continue
        start = float(cue.get("start_s", cue.get("start", 0)) or 0)
        end = float(cue.get("end_s", cue.get("end", start)) or start)
        if best is None or start < best["start"]:
            best = {"start": start, "end": max(start, end), "text": str(cue.get("text") or ""), "source": str(cue.get("source") or "")}
    return best


def teaser_clip_from_text(cues: list[dict[str, Any]], query: str, total_duration: float, duration: float = 30.0, pre_roll: float = 2.0) -> dict[str, Any] | None:
    """Locate `query` in the saved lyrics timing and return a clip window of
    `duration` seconds that starts a little before the matched line."""
    hit = find_lyrics_timestamp(cues, query)
    if not hit:
        return None
    total_duration = max(0.0, float(total_duration))
    duration = max(5.0, min(duration, total_duration)) if total_duration > 0 else max(5.0, duration)
    start = max(0.0, hit["start"] - pre_roll)
    end = min(total_duration, start + duration) if total_duration > 0 else start + duration
    start = max(0.0, end - duration)
    return {
        "start": round(start, 3), "end": round(end, 3), "duration": round(end - start, 3),
        "matched_text": hit["text"], "matched_at": round(hit["start"], 3),
        "approximate": hit.get("source") == "generated-draft",
    }


def suggest_teaser_clips(song: dict[str, Any], audio_path: Path, count: int = 3, durations: list[int] | None = None) -> list[dict[str, Any]]:
    """Pick the single best (loudest) non-overlapping window per requested
    duration, all sized within the 30-50s Shorts-teaser range."""
    durations = durations or [30, 40, 50]
    suggestions = suggest_short_clips(song, audio_path, durations)["suggestions"]
    best_per_duration: dict[int, dict[str, Any]] = {}
    for item in suggestions:
        d = int(item.get("duration") or 0)
        if d not in best_per_duration or float(item["score"]) > float(best_per_duration[d]["score"]):
            best_per_duration[d] = item
    return [best_per_duration[d] for d in durations if d in best_per_duration][:count]


def render_short_clip(audio_path: Path, output_path: Path, start: float, duration: float, *, normalize: bool = True) -> dict[str, Any]:
    ensure_ffmpeg(); audio_path = audio_path.resolve(); output_path = output_path.resolve(); output_path.parent.mkdir(parents=True, exist_ok=True)
    temp = output_path.with_suffix(output_path.suffix + ".part")
    af = ["afade=t=in:st=0:d=0.25", f"afade=t=out:st={max(0.0, duration - 0.5):.3f}:d=0.5"]
    if normalize: af.insert(0, "loudnorm=I=-14:TP=-1.0:LRA=11")
    cmd = [str(ffmpeg_path()), "-hide_banner", "-y", "-ss", f"{start:.3f}", "-i", str(audio_path), "-t", f"{duration:.3f}", "-vn", "-af", ",".join(af), "-c:a", "libmp3lame", "-b:a", "320k", "-f", "mp3", str(temp)]
    proc = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace", timeout=1800, creationflags=(subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0))
    if proc.returncode != 0 or not temp.exists(): raise RuntimeError("FFmpeg nije napravio kratki isečak: " + (proc.stdout or "")[-1200:])
    os.replace(temp, output_path)
    return {"path": str(output_path), "start": start, "duration": duration, "sha256": sha256_file(output_path), "audio": probe_audio(output_path)}


def _ass_time(seconds: float) -> str:
    seconds = max(0.0, float(seconds)); h = int(seconds // 3600); m = int((seconds % 3600) // 60); s = seconds % 60
    return f"{h}:{m:02d}:{s:05.2f}"


def _ass_escape(text: str) -> str:
    return str(text).replace("\\", r"\\").replace("{", r"\{").replace("}", r"\}").replace("\n", r"\N")


def build_ass(cues: list[dict[str, Any]], target: Path, *, width: int, height: int, font: str = "Arial", font_size: int = 54, primary: str = "&H00FFFFFF", outline: str = "&H00000000", alignment: int = 2, margin_v: int = 110) -> Path:
    header = f"""[Script Info]\nScriptType: v4.00+\nPlayResX: {width}\nPlayResY: {height}\nWrapStyle: 2\nScaledBorderAndShadow: yes\n\n[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\nStyle: Default,{font},{font_size},{primary},&H0000FFFF,{outline},&H64000000,-1,0,0,0,100,100,0,0,1,3,1,{alignment},70,70,{margin_v},1\n\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n"""
    lines = []
    for cue in cues:
        start = float(cue.get("start_s", cue.get("start", 0)) or 0); end = float(cue.get("end_s", cue.get("end", start + 3)) or (start + 3)); text = str(cue.get("text") or "").strip()
        if text: lines.append(f"Dialogue: 0,{_ass_time(start)},{_ass_time(max(start + .1, end))},Default,,0,0,0,,{_ass_escape(text)}")
    target.write_text(header + "\n".join(lines) + "\n", encoding="utf-8-sig")
    return target


def render_lyric_video(song: dict[str, Any], audio_path: Path, cues: list[dict[str, Any]], output_path: Path, *, background_path: Path | None = None, aspect: str = "16:9", font: str = "Arial", font_size: int = 54, text_color: str = "&H00FFFFFF", outline_color: str = "&H00000000", waveform: bool = False) -> dict[str, Any]:
    ensure_ffmpeg(); audio_path = audio_path.expanduser().resolve(); output_path = output_path.expanduser().resolve(); output_path.parent.mkdir(parents=True, exist_ok=True)
    width, height = (1080, 1920) if aspect == "9:16" else (1920, 1080)
    duration = float(probe_audio(audio_path).get("duration") or song.get("duration") or 0)
    if duration <= 0: raise RuntimeError("Trajanje zvuka nije poznato.")
    with tempfile.TemporaryDirectory(prefix="suno-lyric-") as tmp_name:
        tmp = Path(tmp_name); ass = build_ass(cues, tmp / "lyrics.ass", width=width, height=height, font=font, font_size=font_size, primary=text_color, outline=outline_color, alignment=2, margin_v=int(height * .08))
        bg = background_path.expanduser().resolve() if background_path else None
        cmd = [str(ffmpeg_path()), "-hide_banner", "-y"]
        if bg and bg.exists() and bg.suffix.lower() in {".mp4", ".mov", ".mkv", ".webm", ".avi"}:
            cmd += ["-stream_loop", "-1", "-i", str(bg), "-i", str(audio_path)]
            video_input = "[0:v]"; audio_index = "1:a"
        elif bg and bg.exists():
            cmd += ["-loop", "1", "-framerate", "30", "-i", str(bg), "-i", str(audio_path)]
            video_input = "[0:v]"; audio_index = "1:a"
        else:
            cmd += ["-f", "lavfi", "-i", f"color=c=0x111827:s={width}x{height}:r=30:d={duration:.3f}", "-i", str(audio_path)]
            video_input = "[0:v]"; audio_index = "1:a"
        ass_escaped = str(ass).replace("\\", "/").replace(":", "\\:").replace("'", "\\'")
        filters = f"{video_input}scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height},format=yuv420p,subtitles='{ass_escaped}'[v]"
        cmd += ["-filter_complex", filters, "-map", "[v]", "-map", audio_index, "-t", f"{duration:.3f}", "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-c:a", "aac", "-b:a", "320k", "-movflags", "+faststart", str(output_path)]
        proc = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace", timeout=7200, creationflags=(subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0))
        if proc.returncode != 0 or not output_path.exists() or output_path.stat().st_size < 100000:
            raise RuntimeError("Lyric video nije napravljen: " + (proc.stdout or "")[-1800:])
    return {"path": str(output_path), "sha256": sha256_file(output_path), "size": output_path.stat().st_size, "aspect": aspect, "duration": duration}


def youtube_metadata(song: dict[str, Any], channel: str = "Nedostaješ PUNOO PESME") -> dict[str, Any]:
    title = str(song.get("title") or "Nova pesma").strip()
    lyrics = str(song.get("lyrics") or "").strip()
    tags = [x.strip() for x in re.split(r"[,;\n]", str(song.get("tags") or song.get("genre") or "")) if x.strip()]
    prefix = "NEDOSTAJEŠ PUNOO - "
    yt_title = title if title.upper().startswith(prefix) else prefix + title.upper()
    yt_title = yt_title[:100]
    description = f"{title} — emotivna autorska pesma kanala {channel}.\n\n{lyrics[:2200]}\n\nPretplati se, ostavi komentar i podeli pesmu sa osobom kojoj nedostaješ.\n\n#NedostaješPUNOO #TužnaLjubav #LjubavnaPesma #BalkanMuzika"
    defaults = ["nedostaješ punoo", "tužna ljubavna pesma", "ljubavna balada", "balkan muzika", "suno pesma", "emotivna pesma"]
    final_tags = []
    for tag in defaults + tags:
        tag = tag[:50]
        if tag.casefold() not in {x.casefold() for x in final_tags}: final_tags.append(tag)
    return {"title": yt_title, "description": description[:5000], "tags": _youtube_trim_tags(final_tags), "category_id": "10", "privacy_status": "private", "made_for_kids": False, "channel_profile": channel}


def create_youtube_package(song: dict[str, Any], target_dir: Path, *, channel: str = "Nedostaješ PUNOO PESME", video_path: str = "", thumbnail_path: str = "", extra_metadata: dict[str, Any] | None = None) -> dict[str, Any]:
    target_dir = target_dir.expanduser().resolve() / f"YouTube-{sanitize_filename(str(song.get('title') or song.get('id')), 100)}"
    target_dir.mkdir(parents=True, exist_ok=True)
    meta = youtube_metadata(song, channel)
    if extra_metadata:
        for key in ("privacy_status", "playlist_id", "publish_at", "made_for_kids"):
            if key in extra_metadata and extra_metadata[key] not in (None, ""):
                meta[key] = extra_metadata[key]
    meta.update({"song_id": song.get("id"), "video_path": video_path, "thumbnail_path": thumbnail_path, "created_at": now_iso()})
    atomic_write_json(target_dir / "youtube-metadata.json", meta)
    atomic_write(target_dir / "NASLOV.txt", meta["title"].encode("utf-8"))
    atomic_write(target_dir / "OPIS.txt", meta["description"].encode("utf-8"))
    atomic_write(target_dir / "TAGOVI.txt", ", ".join(meta["tags"]).encode("utf-8"))
    atomic_write(target_dir / "ZAKACENI_KOMENTAR.txt", f"Koji stih vas je najviše pogodio? 💔\n\nHvala što slušate kanal {channel}.".encode("utf-8"))
    return {"path": str(target_dir), "metadata": meta}


def _read_json_http_response(response: http.client.HTTPResponse) -> dict[str, Any]:
    raw = response.read().decode("utf-8", errors="replace")
    if response.status >= 400:
        try: message = json.loads(raw).get("error", {}).get("message") or raw
        except Exception: message = raw
        raise RuntimeError(f"YouTube HTTP {response.status}: {str(message)[:1200]}")
    try: return json.loads(raw) if raw else {}
    except Exception as exc: raise RuntimeError("YouTube je vratio neispravan JSON odgovor.") from exc


def _youtube_response_payload(raw: bytes, status: int) -> dict[str, Any]:
    text = raw.decode("utf-8", errors="replace")
    try:
        payload = json.loads(text) if text else {}
    except Exception:
        payload = {}
    if status >= 400 and status != 308:
        message = payload.get("error", {}).get("message") if isinstance(payload.get("error"), dict) else ""
        raise RuntimeError(f"YouTube HTTP {status}: {str(message or text or 'nepoznata greška')[:1200]}")
    return payload if isinstance(payload, dict) else {}


def _youtube_upload_request(request: urllib.request.Request, timeout: int = 180) -> tuple[int, Any, bytes]:
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return int(response.status), response.headers, response.read()
    except urllib.error.HTTPError as exc:
        raw = exc.read()
        if exc.code == 308:
            return 308, exc.headers, raw
        _youtube_response_payload(raw, int(exc.code))
        raise RuntimeError(f"YouTube HTTP {exc.code}") from exc
    except urllib.error.URLError as exc:
        raise ConnectionError(f"YouTube upload veza je prekinuta: {exc.reason}") from exc


def _youtube_received_offset(headers: Any) -> int:
    value = str(headers.get("Range") or headers.get("range") or "") if headers else ""
    match = re.search(r"(?:bytes=)?\s*\d+-(\d+)", value)
    return int(match.group(1)) + 1 if match else 0


def _youtube_query_upload_offset(location: str, total: int, token: str) -> tuple[int, dict[str, Any] | None]:
    request = urllib.request.Request(
        location,
        data=b"",
        method="PUT",
        headers={
            "Authorization": f"Bearer {token}",
            "Content-Length": "0",
            "Content-Range": f"bytes */{total}",
            "User-Agent": "SunoPesmeStudio/3.0",
        },
    )
    status, headers, raw = _youtube_upload_request(request, timeout=60)
    if status in {200, 201}:
        return total, _youtube_response_payload(raw, status)
    if status == 308:
        return _youtube_received_offset(headers), None
    raise RuntimeError(f"YouTube nije vratio stanje resumable uploada: HTTP {status}")


def _youtube_trim_tags(values: Iterable[Any], max_total: int = 450) -> list[str]:
    result: list[str] = []
    used = 0
    for value in values:
        tag = str(value or "").strip()[:50]
        if not tag or tag.casefold() in {x.casefold() for x in result}:
            continue
        extra = len(tag) + (1 if result else 0)
        if used + extra > max_total:
            break
        result.append(tag); used += extra
    return result


def _youtube_set_thumbnail(token: str, video_id: str, thumbnail_path: Path) -> dict[str, Any]:
    thumbnail_path = thumbnail_path.expanduser().resolve()
    if not thumbnail_path.is_file():
        raise RuntimeError("Thumbnail fajl ne postoji.")
    if thumbnail_path.stat().st_size > 2 * 1024 * 1024:
        raise RuntimeError("Thumbnail je veći od 2 MB.")
    suffix = thumbnail_path.suffix.lower()
    content_type = "image/png" if suffix == ".png" else "image/jpeg"
    if suffix not in {".jpg", ".jpeg", ".png"}:
        raise RuntimeError("Thumbnail mora biti JPG ili PNG.")
    url = "https://www.googleapis.com/upload/youtube/v3/thumbnails/set?" + urllib.parse.urlencode({"videoId": video_id, "uploadType": "media"})
    request = urllib.request.Request(url, data=thumbnail_path.read_bytes(), method="POST", headers={"Authorization": f"Bearer {token}", "Content-Type": content_type, "User-Agent": "SunoPesmeStudio/3.0"})
    status, _, raw = _youtube_upload_request(request, timeout=120)
    return _youtube_response_payload(raw, status)


def _youtube_add_to_playlist(token: str, video_id: str, playlist_id: str) -> dict[str, Any]:
    payload = json.dumps({"snippet": {"playlistId": playlist_id, "resourceId": {"kind": "youtube#video", "videoId": video_id}}}).encode("utf-8")
    url = "https://www.googleapis.com/youtube/v3/playlistItems?part=snippet"
    request = urllib.request.Request(url, data=payload, method="POST", headers={"Authorization": f"Bearer {token}", "Content-Type": "application/json; charset=UTF-8", "User-Agent": "SunoPesmeStudio/3.0"})
    status, _, raw = _youtube_upload_request(request, timeout=120)
    return _youtube_response_payload(raw, status)


def youtube_resumable_upload(oauth_manager: Any, video_path: Path, metadata: dict[str, Any], *, profile_id: str = "", progress: Callable[[str, int, int], None] | None = None) -> dict[str, Any]:
    """Pravi YouTube chunked resumable upload sa nastavkom posle prekida veze."""
    video_path = video_path.expanduser().resolve()
    if not video_path.exists() or not video_path.is_file():
        raise RuntimeError("Video fajl ne postoji.")
    size = video_path.stat().st_size
    if size <= 0:
        raise RuntimeError("Video fajl je prazan.")
    token = oauth_manager.get_access_token(profile_id)
    status_payload: dict[str, Any] = {
        "privacyStatus": str(metadata.get("privacy_status") or "private"),
        "selfDeclaredMadeForKids": bool(metadata.get("made_for_kids", False)),
    }
    publish_at = str(metadata.get("publish_at") or "").strip()
    if publish_at:
        status_payload["privacyStatus"] = "private"
        status_payload["publishAt"] = publish_at
    body = {
        "snippet": {
            "title": str(metadata.get("title") or video_path.stem)[:100],
            "description": str(metadata.get("description") or "")[:5000],
            "tags": _youtube_trim_tags(metadata.get("tags") or []),
            "categoryId": str(metadata.get("category_id") or "10"),
        },
        "status": status_payload,
    }
    payload = json.dumps(body, ensure_ascii=False).encode("utf-8")
    init_req = urllib.request.Request(
        "https://www.googleapis.com/upload/youtube/v3/videos?part=snippet,status&uploadType=resumable",
        data=payload,
        method="POST",
        headers={
            "Authorization": f"Bearer {token}",
            "Content-Type": "application/json; charset=UTF-8",
            "X-Upload-Content-Length": str(size),
            "X-Upload-Content-Type": "video/*",
            "User-Agent": "SunoPesmeStudio/3.0",
        },
    )
    status, headers, raw = _youtube_upload_request(init_req, timeout=60)
    if status not in {200, 201}:
        _youtube_response_payload(raw, status)
        raise RuntimeError(f"YouTube nije prihvatio početak uploada: HTTP {status}")
    location = str(headers.get("Location") or "")
    parsed = urllib.parse.urlparse(location)
    host = (parsed.hostname or "").lower()
    if not location or parsed.scheme != "https" or not (host == "googleapis.com" or host.endswith(".googleapis.com") or host == "youtube.com" or host.endswith(".youtube.com")):
        raise RuntimeError("YouTube nije vratio bezbednu resumable upload adresu.")

    chunk_size = 8 * 1024 * 1024  # višekratnik od 256 KB
    offset = 0
    final: dict[str, Any] | None = None
    with video_path.open("rb") as fh:
        while offset < size:
            fh.seek(offset)
            chunk = fh.read(min(chunk_size, size - offset))
            if not chunk:
                raise RuntimeError("Neočekivan kraj video fajla tokom uploada.")
            chunk_end = offset + len(chunk) - 1
            uploaded = False
            last_error: Exception | None = None
            for attempt in range(1, 6):
                request = urllib.request.Request(
                    location,
                    data=chunk,
                    method="PUT",
                    headers={
                        "Authorization": f"Bearer {token}",
                        "Content-Type": "video/*",
                        "Content-Length": str(len(chunk)),
                        "Content-Range": f"bytes {offset}-{chunk_end}/{size}",
                        "User-Agent": "SunoPesmeStudio/3.0",
                    },
                )
                try:
                    upload_status, upload_headers, upload_raw = _youtube_upload_request(request, timeout=600)
                    if upload_status in {200, 201}:
                        final = _youtube_response_payload(upload_raw, upload_status)
                        offset = size
                        uploaded = True
                        if progress: progress(video_path.name, size, size)
                        break
                    if upload_status == 308:
                        remote_offset = _youtube_received_offset(upload_headers)
                        offset = max(offset + len(chunk), remote_offset)
                        uploaded = True
                        if progress: progress(video_path.name, min(offset, size), size)
                        break
                    raise RuntimeError(f"Neočekivan YouTube upload status: {upload_status}")
                except Exception as exc:
                    last_error = exc
                    if "HTTP 401" in str(exc) and hasattr(oauth_manager, "refresh_access_token"):
                        token = oauth_manager.refresh_access_token(profile_id)
                    try:
                        remote_offset, completed = _youtube_query_upload_offset(location, size, token)
                        if completed:
                            final = completed; offset = size; uploaded = True
                            if progress: progress(video_path.name, size, size)
                            break
                        if remote_offset > offset:
                            offset = remote_offset; uploaded = True
                            if progress: progress(video_path.name, min(offset, size), size)
                            break
                    except Exception:
                        pass
                    if attempt < 5:
                        time.sleep(min(16, 2 ** (attempt - 1)))
            if not uploaded:
                raise RuntimeError(f"YouTube upload nije nastavljen posle 5 pokušaja: {last_error}")

    if not final:
        _, final = _youtube_query_upload_offset(location, size, token)
    final = final or {}
    video_id = str(final.get("id") or "")
    if not video_id:
        raise RuntimeError("YouTube upload je završen bez video ID-a.")
    extras: dict[str, Any] = {}
    thumbnail_raw = str(metadata.get("thumbnail_path") or "").strip()
    if thumbnail_raw:
        extras["thumbnail"] = _youtube_set_thumbnail(token, video_id, Path(thumbnail_raw))
    playlist_id = str(metadata.get("playlist_id") or "").strip()
    if playlist_id:
        extras["playlist"] = _youtube_add_to_playlist(token, video_id, playlist_id)
    return {
        "video_id": video_id,
        "url": f"https://www.youtube.com/watch?v={video_id}",
        "status": final.get("status") or {},
        "snippet": final.get("snippet") or {},
        "extras": extras,
        "resumable": True,
    }


def create_proof_package(song: dict[str, Any], target_dir: Path) -> dict[str, Any]:
    target_dir = target_dir.expanduser().resolve(); target_dir.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    package = target_dir / f"DOKAZ-{sanitize_filename(str(song.get('title') or song.get('id')), 90)}-{stamp}.zip"
    manifest: dict[str, Any] = {"format": "suno-pesme-proof-v1", "created_at": now_iso(), "song": {k: song.get(k) for k in ("id", "title", "display_name", "created_at", "updated_at", "model_version", "prompt", "lyrics", "source_url", "youtube_url", "youtube_published_at")}, "files": []}
    with zipfile.ZipFile(package, "w", compression=zipfile.ZIP_DEFLATED, allowZip64=True) as z:
        for field, path in _song_paths(song):
            if not path.exists() or not path.is_file(): continue
            digest = sha256_file(path); arc = f"files/{field.replace(':', '-')}/{path.name}"
            z.write(path, arcname=arc); manifest["files"].append({"field": field, "name": path.name, "archive_path": arc, "sha256": digest, "size": path.stat().st_size, "modified_at": datetime.fromtimestamp(path.stat().st_mtime, timezone.utc).isoformat(timespec="seconds")})
        manifest_raw = json.dumps(manifest, ensure_ascii=False, indent=2, default=str).encode("utf-8")
        manifest_hash = hashlib.sha256(manifest_raw).hexdigest(); z.writestr("manifest.json", manifest_raw); z.writestr("manifest.sha256", manifest_hash + "  manifest.json\n")
        z.writestr("VAZNO.txt", "Ovaj paket čuva tehničku evidenciju, datume i SHA-256 otiske. Ne predstavlja automatski pravni dokaz vlasništva.\n")
    return {"path": str(package), "sha256": sha256_file(package), "files": len(manifest["files"]), "manifest_sha256": manifest_hash}


PANAKO_LICENSE_NOTICE = (
    "Panako (Joren Six, panako.be) je odvojen projekat pod GPL-3.0 licencom i NIJE deo ovog "
    "programa niti se automatski preuzima. Ako ga želiš kao dodatni (opcioni) fingerprint motor, "
    "sam preuzmi panako.jar sa zvaničnog izvora, pa ga ovde poveži -- program samo proverava da "
    "li stvarno radi, ne šalje ga niti ga menja."
)


def _panako_paths(root: Path) -> tuple[Path, Path]:
    jar = root / "tools" / "panako" / "panako.jar"
    marker = root / "tools" / "panako" / ".verified"
    return jar, marker


def _panako_java() -> str | None:
    return shutil.which("java")


def panako_status(root: Path) -> dict[str, Any]:
    jar, marker = _panako_paths(root)
    legacy_candidates = [root / "tools" / "panako" / "panako.bat", root / "tools" / "panako" / "Panako.jar", root / "plugins" / "panako" / "panako.bat"]
    executable = jar if jar.exists() else next((p for p in legacy_candidates if p.exists()), None)
    java = _panako_java()
    verified = False
    if executable and executable.exists() and marker.exists():
        try:
            verified = marker.read_text(encoding="utf-8").strip() == sha256_file(executable)
        except Exception:
            verified = False
    if not executable:
        message = "Panako još nije instaliran. Opcioni dodatak -- osnovne funkcije rade i bez njega."
    elif not java:
        message = "Panako fajl postoji, ali Java runtime nije pronađen na ovom računaru."
    elif not verified:
        message = "Panako fajl postoji, ali još nije stvarno testiran. Klikni „Instaliraj / testiraj Panako“."
    else:
        message = "Panako je instaliran i stvarni test je uspeo."
    return {
        "ready": bool(executable and java and verified),
        "executable": str(executable or ""), "java": java or "",
        "verified": verified, "license": PANAKO_LICENSE_NOTICE,
        "message": message,
    }


def install_panako_jar(root: Path, source_jar: Path) -> dict[str, Any]:
    """User-supplied panako.jar only -- this never downloads Panako itself
    (separate GPL-3.0 project, see PANAKO_LICENSE_NOTICE). Copies the file
    in, then runs it for real with Java and only marks it "ready" if that
    real run actually looks like a working jar, not just because a file
    with the right name exists."""
    source_jar = Path(source_jar).expanduser().resolve()
    if not source_jar.is_file():
        raise RuntimeError("Izabrani Panako .jar fajl ne postoji.")
    if source_jar.suffix.lower() != ".jar":
        raise RuntimeError("Izaberi .jar fajl (Panako se distribuira kao Java .jar).")
    if not zipfile.is_zipfile(source_jar):
        raise RuntimeError("Izabrani fajl nije ispravan .jar (nije validan ZIP arhiv).")
    if source_jar.stat().st_size < 200_000:
        raise RuntimeError("Izabrani fajl je premali da bi bio pravi Panako .jar -- proveri da li je preuzimanje završeno.")
    java = _panako_java()
    if not java:
        raise RuntimeError("Java runtime (java.exe) nije pronađen na ovom računaru. Instaliraj Java 17+ pre Panaka.")
    jar, marker = _panako_paths(root)
    jar.parent.mkdir(parents=True, exist_ok=True)
    # Verify against the SOURCE file first, before touching the installed
    # jar -- a bad candidate must never overwrite (and thereby break) a
    # previously working install.
    proc = subprocess.run(
        [java, "-jar", str(source_jar)], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
        encoding="utf-8", errors="replace", timeout=60, cwd=str(source_jar.parent),
        creationflags=(subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0),
    )
    output = proc.stdout or ""
    if re.search(r"no main manifest attribute|unable to access jarfile|error: could not find or load main class", output, re.I):
        raise RuntimeError("Java je pokrenuo fajl, ali ovo ne izgleda kao pravi izvršni Panako .jar:\n" + output[-1500:])
    shutil.copy2(source_jar, jar)
    marker.write_text(sha256_file(jar), encoding="utf-8")
    return {"installed": True, "executable": str(jar), "java": java, "output": output[-3000:], "exit_code": proc.returncode}


def _panako_command(root: Path) -> list[str]:
    status = panako_status(root)
    if not status["ready"]: raise RuntimeError(status["message"])
    executable = Path(status["executable"])
    if executable.suffix.lower() == ".bat":
        if os.name != "nt":
            raise RuntimeError("Panako BAT wrapper može da se pokrene samo na Windowsu.")
        return [os.environ.get("COMSPEC", "cmd.exe"), "/d", "/c", str(executable)]
    return [str(status["java"]), "-jar", str(executable)]


def _panako_data_dir(root: Path, data_dir: Path | None = None) -> Path:
    """Panako's own index must live in the writable per-user data folder,
    never under Program Files -- passed as the subprocess cwd since that's
    where a Java CLI tool with relative-path storage will write."""
    base = (data_dir or (root / "data")).expanduser().resolve() / "panako_index"
    base.mkdir(parents=True, exist_ok=True)
    return base


def panako_index(root: Path, files: list[Path], data_dir: Path | None = None) -> dict[str, Any]:
    existing = [x.expanduser().resolve() for x in files if x.expanduser().is_file()]
    if not existing:
        raise RuntimeError("Nema postojećih audio fajlova za Panako indeksiranje.")
    command = _panako_command(root) + ["store"] + [str(x) for x in existing]
    proc = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace", timeout=7200, cwd=str(_panako_data_dir(root, data_dir)), creationflags=(subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0))
    if proc.returncode != 0: raise RuntimeError("Panako indeksiranje nije uspelo: " + (proc.stdout or "")[-1800:])
    return {"indexed": len(existing), "output": (proc.stdout or "")[-5000:]}


def panako_query(root: Path, file: Path, data_dir: Path | None = None) -> dict[str, Any]:
    file = file.expanduser().resolve()
    if not file.is_file():
        raise RuntimeError("Audio fajl za Panako proveru ne postoji.")
    command = _panako_command(root) + ["query", str(file)]
    proc = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace", timeout=1800, cwd=str(_panako_data_dir(root, data_dir)), creationflags=(subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0))
    if proc.returncode != 0: raise RuntimeError("Panako provera nije uspela: " + (proc.stdout or "")[-1800:])
    return {"file": str(file), "output": proc.stdout or "", "matched": bool(re.search(r"match|matched|hits?", proc.stdout or "", re.I))}


def create_program_snapshot(root: Path, snapshots_dir: Path, *, include_tools: bool = False) -> dict[str, Any]:
    root = root.resolve(); snapshots_dir = snapshots_dir.expanduser().resolve(); snapshots_dir.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S"); package = snapshots_dir / f"Suno-Pesme-Studio-program-snapshot-{stamp}.zip"
    excluded = {"Preuzete_pesme", "Izvoz", "Azuriranja", "__pycache__"}
    if not include_tools: excluded |= {"python", "tools"}
    manifest = {"format": "suno-program-snapshot-v1", "created_at": now_iso(), "root": str(root), "files": []}
    with zipfile.ZipFile(package, "w", compression=zipfile.ZIP_DEFLATED, allowZip64=True) as z:
        for path in root.rglob("*"):
            if not path.is_file(): continue
            rel = path.relative_to(root)
            if rel.parts and rel.parts[0] in excluded: continue
            if path.suffix.lower() in {".pyc", ".tmp", ".part"}: continue
            z.write(path, arcname=str(rel).replace("\\", "/")); manifest["files"].append({"path": str(rel).replace("\\", "/"), "sha256": sha256_file(path), "size": path.stat().st_size})
        z.writestr("SNAPSHOT_MANIFEST.json", json.dumps(manifest, ensure_ascii=False, indent=2))
    return {"path": str(package), "sha256": sha256_file(package), "files": len(manifest["files"]), "include_tools": include_tools}


def storage_report(root: Path, data_dir: Path, download_dir: Path) -> dict[str, Any]:
    categories = {"program": root, "data": data_dir, "downloads": download_dir, "tools": root / "tools", "python": root / "python", "youtube_cache": data_dir / "youtube_audio_cache", "waveforms": data_dir / "waveforms", "backups": root / "Izvoz"}
    result = {}
    for name, folder in categories.items():
        total = 0; files = 0
        if folder.exists():
            for p in folder.rglob("*"):
                try:
                    if p.is_file(): total += p.stat().st_size; files += 1
                except OSError: pass
        result[name] = {"path": str(folder), "bytes": total, "gb": round(total / 1024**3, 3), "files": files}
    usage = shutil.disk_usage(download_dir)
    return {"categories": result, "disk": {"total": usage.total, "used": usage.used, "free": usage.free, "free_gb": round(usage.free / 1024**3, 2)}, "created_at": now_iso()}


def cleanup_storage(root: Path, data_dir: Path, download_dir: Path, *, cache_days: int = 30, keep_rollbacks: int = 5) -> dict[str, Any]:
    """Bezbedno uklanja samo regenerabilni keš, stare delimične fajlove i višak rollback kopija."""
    root = root.resolve(); data_dir = data_dir.resolve(); download_dir = download_dir.resolve()
    cutoff = time.time() - max(1, cache_days) * 86400
    removed: list[dict[str, Any]] = []
    errors: list[dict[str, str]] = []

    def remove_file(path: Path, category: str) -> None:
        try:
            size = path.stat().st_size
            path.unlink()
            removed.append({"path": str(path), "bytes": size, "category": category})
        except Exception as exc:
            errors.append({"path": str(path), "error": str(exc)})

    for folder, category in ((data_dir / "youtube_audio_cache", "youtube-cache"), (data_dir / "waveforms", "waveform-cache")):
        if folder.exists():
            for path in folder.rglob("*"):
                try:
                    if path.is_file() and path.stat().st_mtime < cutoff:
                        remove_file(path, category)
                except OSError:
                    pass
    if download_dir.exists():
        for path in download_dir.rglob("*.part"):
            try:
                if path.is_file() and path.stat().st_mtime < cutoff:
                    remove_file(path, "old-part")
            except OSError:
                pass
    for log_name in ("program.log", "watchdog.log", "bootstrap.log"):
        log_path = data_dir / log_name
        try:
            if log_path.is_file() and log_path.stat().st_size > 20 * 1024 * 1024:
                with log_path.open("rb") as fh:
                    fh.seek(-5 * 1024 * 1024, os.SEEK_END)
                    tail = fh.read()
                atomic_write(log_path, tail)
                removed.append({"path": str(log_path), "bytes": 15 * 1024 * 1024, "category": "log-trim"})
        except Exception as exc:
            errors.append({"path": str(log_path), "error": str(exc)})
    rollback_root = data_dir / "rollback"
    if rollback_root.exists():
        snapshots = sorted((x for x in rollback_root.iterdir() if x.is_dir()), key=lambda x: x.stat().st_mtime, reverse=True)
        for folder in snapshots[max(1, keep_rollbacks):]:
            try:
                size = sum(p.stat().st_size for p in folder.rglob("*") if p.is_file())
                shutil.rmtree(folder)
                removed.append({"path": str(folder), "bytes": size, "category": "old-rollback"})
            except Exception as exc:
                errors.append({"path": str(folder), "error": str(exc)})
    freed = sum(int(item.get("bytes") or 0) for item in removed)
    return {"ok": not errors, "removed": removed, "removed_count": len(removed), "freed_bytes": freed, "freed_mb": round(freed / 1024**2, 2), "errors": errors, "created_at": now_iso()}


def align_original_lyrics(lyrics: str, transcription: dict[str, Any], duration: float = 0.0) -> list[dict[str, Any]]:
    """Poravnaj originalne stihove na prepoznate reči; koristi vremensku raspodelu kao siguran fallback."""
    import difflib
    lines = [x.strip() for x in str(lyrics or "").replace("\r", "").splitlines() if x.strip()]
    lines = [x for x in lines if not re.fullmatch(r"\[[^\]]{1,80}\]", x)]
    if not lines:
        return []
    words = [w for w in (transcription.get("words") or []) if str(w.get("word") or "").strip()]
    segments = [s for s in (transcription.get("segments") or []) if str(s.get("text") or "").strip()]
    start = float((words[0] if words else segments[0]).get("start") or 0) if (words or segments) else 0.0
    end = float((words[-1] if words else segments[-1]).get("end") or duration or 0) if (words or segments) else float(duration or 0)
    if end <= start:
        end = max(start + len(lines) * 3.0, float(duration or 0))

    def norm(value: str) -> str:
        value = value.casefold().translate(str.maketrans("čćžšđ", "cczsd"))
        return re.sub(r"[^a-z0-9]+", " ", value).strip()

    cues: list[dict[str, Any]] = []
    cursor = 0
    for line in lines:
        target = norm(line)
        target_words = target.split()
        best: tuple[float, int, int] | None = None
        if words and target_words:
            max_window = min(len(words) - cursor, max(3, len(target_words) + 5))
            search_end = min(len(words), cursor + max(30, len(target_words) * 5))
            for i in range(cursor, search_end):
                for length in range(max(1, len(target_words) - 3), max_window + 1):
                    j = min(len(words), i + length)
                    if j <= i: continue
                    candidate = norm(" ".join(str(w.get("word") or "") for w in words[i:j]))
                    score = difflib.SequenceMatcher(None, target, candidate).ratio()
                    if best is None or score > best[0]: best = (score, i, j)
            if best and best[0] >= 0.28:
                _, i, j = best
                cue_start = float(words[i].get("start") or start)
                cue_end = float(words[j-1].get("end") or cue_start + 2)
                cues.append({"start": cue_start, "end": max(cue_start + .25, cue_end), "text": line, "source": "original-lyrics-word-match", "confidence": round(best[0],3)})
                cursor = max(cursor, j)
                continue
        cues.append({"start": None, "end": None, "text": line, "source": "original-lyrics-distributed", "confidence": 0.0})

    # Popuni nepovezane redove proporcionalno dužini, između poznatih tačaka.
    unknown = [i for i,c in enumerate(cues) if c["start"] is None]
    if unknown:
        total_weight = sum(max(1, len(c["text"])) for c in cues)
        current = start
        for i,cue in enumerate(cues):
            if cue["start"] is not None:
                current = max(current, float(cue["end"]))
                continue
            remaining_weight = sum(max(1, len(x["text"])) for x in cues[i:])
            available = max(0.5, end-current)
            share = available * max(1,len(cue["text"])) / max(1,remaining_weight)
            cue["start"] = current; cue["end"] = min(end, current + max(1.0,share)); current = float(cue["end"])
    for i in range(len(cues)-1):
        cues[i]["end"] = min(float(cues[i]["end"]), max(float(cues[i]["start"])+.25, float(cues[i+1]["start"])-.02))
    return cues
