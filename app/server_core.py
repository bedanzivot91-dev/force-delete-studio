Warning: truncated output (original token count: 92995)
Total output lines: 6520

from __future__ import annotations

import csv
import hashlib
import html
import io
import ipaddress
import json
import mimetypes
import os
import platform
import re
import shutil
import socket
import sqlite3
import subprocess
import sys
import threading
import tempfile
import time
import traceback
import urllib.parse
import urllib.request
import urllib.error
import uuid
import webbrowser
import zipfile
from datetime import datetime, timezone
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any, Callable
from concurrent.futures import ThreadPoolExecutor, as_completed

# The shipped embeddable Python interpreter has a <ver>._pth file next to
# python.exe, which puts the interpreter in "isolated path" mode: sys.path
# is built ENTIRELY from that file's contents, and the normal CPython
# behavior of prepending the running script's own directory to sys.path is
# skipped entirely (see https://docs.python.org/3/using/windows.html
# #windows-embeddable, "isolated" -- confirmed as the real cause of a
# ModuleNotFoundError: No module named 'cdp' a real user hit on Windows:
# watchdog.py spawns `python.exe .../app/server.py` directly, and without
# this line, sys.path never contains app/ at all under that ._pth, so
# every local import below fails immediately). tests/import_smoke_test.py
# already worked around this itself (sys.path.insert(0, str(APP_DIR))),
# which is exactly why CI's import-smoke-test step never caught this --
# only running server.py itself the way watchdog.py really does exposes
# it. Doing it here, not just in the packaging step's ._pth patch, means
# server.py is correct however it's actually invoked.
sys.path.insert(0, str(Path(__file__).resolve().parent))

from cdp import ChromeConnector
from database import LibraryDB
from audio_tools import AudioCancelled, ensure_ffmpeg, ffmpeg_path, ffprobe_path, has_chromaprint, probe_audio, process_audio, waveform_peaks, status as audio_tools_status
from id3 import embed_mp3_metadata
from suno_client import SunoAPIError, SunoClient, extract_items, format_lrc, format_srt, sanitize_filename
from suno_compat import validate_fixture
from youtube_tools import (YouTubeAPIError, resolve_channel, list_channel_videos, search_videos, build_search_queries, match_song_to_video, contact_message, google_search_url, bing_search_url, youtube_search_url)
from youtube_oauth import YouTubeOAuthError, YouTubeOAuthManager
from platform_intelligence import (
    analyze_comments, build_review_queue, build_song_report, fetch_comments, quota_estimate, review_priority,
    suno_snapshot_diff, visual_video_analysis, youtube_analytics, youtube_analytics_suite, youtube_retention,
)
from audio_match import (
    AudioMatchCancelled, AudioMatchError, ALGORITHM_VERSION as AUDIO_MATCH_VERSION,
    analyze_audio_pair, closest_duration_candidates, compare_signatures, cleanup_youtube_audio_cache,
    download_youtube_audio, ensure_ytdlp, ensure_deno, search_youtube_with_ytdlp, extract_signature, inspect_youtube_video, pack_signature,
    SHORT_CLIP_MIN_MATCH_SECONDS, PHASE_SEARCH_BELOW_SECONDS, source_identity, unpack_signature, ytdlp_status, ytdlp_path,
    extract_query_signatures, required_match_seconds,
)
from advanced_features import (
    analyze_audio_quality, check_update, compare_lyrics_transcript, create_cloud_backup, restore_cloud_backup,
    create_incremental_backup, download_update, has_manual_subtitle_cues, install_ai_plugin, list_incremental_backups, load_subtitle_cues,
    plugin_status, relocate_missing_files, restore_incremental_item, run_stem_separation,
    run_transcription, save_subtitle_files,
)
from security_lock import AppSecurity
from music_recognition import MusicRecognitionError, decode_audio_base64, recognize_audd
from v3_features import (
    align_original_lyrics, build_release_package, create_program_snapshot, create_proof_package, create_youtube_package, duplicate_detection,
    library_integrity_scan, organize_library, install_panako_jar, match_smart_collection, panako_index, panako_query, panako_status,
    release_readiness, render_lyric_video, render_short_clip, sha256_file, SMART_LIBRARY_FIELDS, storage_report as v3_storage_report, cleanup_storage,
    suggest_short_clips, suggest_teaser_clips, teaser_clip_from_text, system_preflight, youtube_metadata, youtube_resumable_upload,
)
import song_finder
from fingerprint_index import FingerprintIndex


APP_VERSION = "3.3.2"
ROOT = Path(__file__).resolve().parent.parent
WEB_DIR = ROOT / "app" / "web"
USER_DATA_ROOT = Path(os.environ.get("SUNO_STUDIO_USER_DIR") or ROOT).expanduser().resolve()
DATA_DIR = Path(os.environ.get("SUNO_STUDIO_DATA_DIR") or (USER_DATA_ROOT / "data")).expanduser().resolve()
DEFAULT_DOWNLOAD_DIR = Path(os.environ.get("SUNO_STUDIO_DOWNLOAD_DIR") or (USER_DATA_ROOT / "Preuzete_pesme")).expanduser().resolve()
EXPORT_DIR = Path(os.environ.get("SUNO_STUDIO_EXPORT_DIR") or (USER_DATA_ROOT / "Izvoz")).expanduser().resolve()
# Automatic updates: the program ships knowing where its own update manifest
# lives, so a user never has to find and paste a URL to receive a fix. CI
# publishes this file on every successful build of the release branch.
DEFAULT_UPDATE_MANIFEST_URL = (
    "https://raw.githubusercontent.com/bedanzivot91-dev/force-delete-studio/"
    "claude/suno-pesme-studio-full-build-ljk5m7/docs/updates.json"
)
UPDATE_CHECK_INTERVAL_SECONDS = 6 * 60 * 60
UPDATE_STOP = threading.Event()
UPDATE_THREAD: threading.Thread | None = None
UPDATE_STATE: dict[str, Any] = {"checked_at": "", "available": False, "latest": "", "notes": "", "message": "", "error": ""}

PUBLISHED_DIR = Path(os.environ.get("SUNO_STUDIO_PUBLISHED_DIR") or (USER_DATA_ROOT / "OBRAĐENO NA YOUTUBE")).expanduser().resolve()
YOUTUBE_PROCESSED_STATUSES = ("complete", "almost_complete", "partial", "short_clip")
LOCAL_LIBRARY_DIR = Path(os.environ.get("SUNO_STUDIO_LIBRARY_DIR") or (USER_DATA_ROOT / "Biblioteka_pesama")).expanduser().resolve()
RECOGNITION_ROOT = Path(os.environ.get("SUNO_STUDIO_RECOGNITION_DIR") or (USER_DATA_ROOT / "Pronalazac_pesme")).expanduser().resolve()
RECOGNITION_INPUT_DIR = RECOGNITION_ROOT / "Ulazni_isecci"
RECOGNITION_AUDIO_DIR = RECOGNITION_ROOT / "Pripremljeni_audio"
RECOGNITION_RESULTS_DIR = RECOGNITION_ROOT / "Rezultati"
RECOGNITION_UNKNOWN_DIR = RECOGNITION_ROOT / "Neidentifikovane"
WAVEFORM_DIR = DATA_DIR / "waveforms"
WATCHDOG_STOP_FILE = DATA_DIR / "watchdog.stop"
for _folder in (DATA_DIR, DEFAULT_DOWNLOAD_DIR, EXPORT_DIR, PUBLISHED_DIR, LOCAL_LIBRARY_DIR, RECOGNITION_INPUT_DIR, RECOGNITION_AUDIO_DIR, RECOGNITION_RESULTS_DIR, RECOGNITION_UNKNOWN_DIR, WAVEFORM_DIR):
    _folder.mkdir(parents=True, exist_ok=True)



def get_youtube_processed_dir() -> Path:
    raw = str(DB.get_setting("youtube_processed_dir", "") or "").strip()
    return Path(raw).expanduser() if raw else PUBLISHED_DIR


def copy_song_to_published_folder(song: dict[str, Any], video: dict[str, Any] | None = None, status: str = "") -> dict[str, Any]:
    """Copy the FULL original Suno audio into
    OBRAĐENO NA YOUTUBE/<kanal>/<status>/<naziv> [<suno-id>]/<video-id>/,
    downloading it via the (refreshed if needed) Suno audio_url when no
    local file exists yet -- never silently skipping just because nothing
    is downloaded locally. Never touches/moves/deletes the song's own
    original files. Re-running on the same song+video updates the
    manifest in place instead of duplicating the folder."""
    video = video or {}
    channel = sanitize_filename(str(video.get("channel_title") or "Nepoznat kanal"), 80)
    status_folder = str(status or video.get("completeness_status") or "complete")
    if status_folder not in YOUTUBE_PROCESSED_STATUSES:
        status_folder = "complete"
    song_id = str(song.get("id") or "")
    video_id = str(video.get("video_id") or video.get("id") or "")
    title = sanitize_filename(str(song.get("title") or song.get("display_name") or song_id or "pesma"), 100)
    base = get_youtube_processed_dir() / channel / status_folder / f"{title} [{song_id[:8]}]" / (video_id or "video")
    base.mkdir(parents=True, exist_ok=True)

    audio_path = _existing_audio_path(song)
    if audio_path is None:
        def refresh_url() -> str:
            try:
                detail = get_client().get_clip(song_id)
                if isinstance(detail, dict):
                    DB.upsert_song(detail)
                    return str((DB.get_song(song_id) or {}).get("audio_url") or "")
            except Exception:
                pass
            return ""
        remote = str(song.get("audio_url") or "").strip() or refresh_url()
        if remote:
            candidate = base / f"{title}.mp3"
            try:
                get_client().download_file(remote, candidate, refresh_url=refresh_url)
                if float(probe_audio(candidate).get("duration") or 0) <= 0:
                    raise RuntimeError("preuzeti fajl nema čitljivo trajanje (verovatno HTML/JSON greška)")
                audio_path = candidate
            except Exception as exc:
                candidate.unlink(missing_ok=True)
                runtime_log(f"OBRAĐENO NA YOUTUBE: preuzimanje punog originala nije uspelo za {song_id}: {exc}", "warning")

    copied: list[str] = []
    if audio_path is not None and Path(audio_path).exists():
        audio_path = Path(audio_path)
        dst = base / f"{title}{audio_path.suffix.lower()}"
        if not dst.exists() or audio_path.stat().st_size != dst.stat().st_size:
            shutil.copy2(audio_path, dst)
        copied.append(str(dst))
    for key, out_name in (("local_cover", None), ("local_lyrics", "lyrics.txt")):
        raw = str(song.get(key) or "").strip()
        if not raw:
            continue
        src = Path(raw)
        if not src.exists() or not src.is_file():
            continue
        dst = base / (out_name or f"cover{src.suffix.lower()}")
        if not dst.exists() or src.stat().st_size != dst.stat().st_size:
            shutil.copy2(src, dst)
        copied.append(str(dst))

    video_url = str(video.get("video_url") or video.get("url") or (f"https://www.youtube.com/watch?v={video_id}" if video_id else ""))
    if video_url:
        (base / "YouTube.url").write_text(f"[InternetShortcut]\r\nURL={video_url}\r\n", encoding="utf-8")

    file_hash = ""
    if copied:
        try:
            file_hash = sha256_file(Path(copied[0]))
        except Exception:
            file_hash = ""
    manifest = {
        "suno_song_id": song_id, "suno_url": str(song.get("source_url") or ""),
        "youtube_video_id": video_id, "youtube_url": video_url, "channel": channel,
        "video_title": str(video.get("title") or ""), "status": status_folder,
        "score": float(video.get("audio_score") or video.get("score") or 0),
        "match_start_s": video.get("video_start"), "match_end_s": video.get("video_end"),
        "checked_at": now_iso(), "engine_version": AUDIO_MATCH_VERSION, "audio_file_sha256": file_hash,
        "copied_files": copied, "has_full_audio": bool(copied and copied[0].lower().endswith((".mp3", ".wav", ".m4a", ".flac"))),
    }
    (base / "match.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    return {"folder": str(base), "copied": copied, "has_full_audio": manifest["has_full_audio"]}

DB = LibraryDB(DATA_DIR / "suno_biblioteka.db")
DB.mark_interrupted_jobs()
CONNECTOR = ChromeConnector(DATA_DIR)
SESSION_TOKEN: str | None = None
SESSION_USER_AGENT: str = ""
SESSION_TOKEN_SOURCE: str = ""
SESSION_TOKEN_UPDATED_MONOTONIC: float = 0.0
SESSION_TOKEN_UPDATED_AT: str = ""
SESSION_DEVICE_ID: str = DB.get_setting("suno_device_id", "") or str(uuid.uuid4())
DB.set_setting("suno_device_id", SESSION_DEVICE_ID)
STATE_LOCK = threading.RLock()
SESSION_REFRESH_LOCK = threading.RLock()
SESSION_KEEPALIVE_STOP = threading.Event()
SESSION_KEEPALIVE_THREAD: threading.Thread | None = None
SCHEDULER_STOP = threading.Event()
SCHEDULER_THREAD: threading.Thread | None = None
SESSION_KEEPALIVE_STATE: dict[str, Any] = {
    "status": "waiting",
    "message": "Čeka se Suno povezivanje.",
    "last_attempt_at": "",
    "last_success_at": "",
    "consecutive_failures": 0,
    "touched": False,
}
RUNTIME_LOG = DATA_DIR / "program.log"
CONNECT_LAUNCH_STATE: dict[str, Any] = {"status": "idle", "message": "", "updated_at": ""}
SERVER_PORT = int(os.environ.get("SUNO_STUDIO_PORT") or 8765)
YOUTUBE_OAUTH = YouTubeOAuthManager(DATA_DIR, port=SERVER_PORT)
SECURITY = AppSecurity(DB)


def maintain_runtime_files() -> None:
    """Ograniči logove i ukloni bezbedne privremene ostatke prethodnih pokretanja."""
    try:
        max_bytes = 5 * 1024 * 1024
        if RUNTIME_LOG.is_file() and RUNTIME_LOG.stat().st_size > max_bytes:
            for index in range(3, 0, -1):
                src = DATA_DIR / ("program.log" if index == 1 else f"program.log.{index - 1}")
                dst = DATA_DIR / f"program.log.{index}"
                if src.exists():
                    if dst.exists():
                        dst.unlink()
                    src.replace(dst)
        cutoff = time.time() - (7 * 24 * 60 * 60)
        for folder_name in ("tmp", "temp", "cache"):
            folder = DATA_DIR / folder_name
            if not folder.is_dir():
                continue
            for item in folder.rglob("*"):
                try:
                    if item.is_file() and item.stat().st_mtime < cutoff and item.suffix.lower() in {".tmp", ".part", ".download", ".cache"}:
                        item.unlink()
                except OSError:
                    continue
    except Exception:
        pass


maintain_runtime_files()


def runtime_log(message: str, level: str = "info") -> None:
    line = f"{datetime.now().isoformat(timespec='seconds')} [{level.upper()}] {message}\n"
    try:
        with RUNTIME_LOG.open("a", encoding="utf-8") as fh:
            fh.write(line)
    except Exception:
        pass


def set_connect_launch_state(status: str, message: str) -> None:
    with STATE_LOCK:
        CONNECT_LAUNCH_STATE.update({"status": status, "message": message, "updated_at": now_iso()})
    runtime_log(f"CONNECT {status}: {message}", "error" if status == "error" else "info")


class TaskState:
    def __init__(self, task_type: str, title: str, job_id: int | None = None):
        self.id = uuid.uuid4().hex[:12]
        self.type = task_type
        self.title = title
        self.status = "running"
        self.message = "Pokretanje..."
        self.current = ""
        self.total = 0
        self.done = 0
        self.percent = 0
        self.errors: list[str] = []
        self.logs: list[dict[str, str]] = []
        self.started_at = now_iso()
        self.finished_at = ""
        self.cancel_event = threading.Event()
        self.pause_event = threading.Event()
        self.job_id = job_id
        self._lock = threading.RLock()

    def log(self, message: str, level: str = "info") -> None:
        item = {"time": datetime.now().strftime("%H:%M:%S"), "level": level, "message": str(message)}
        with self._lock:
            self.logs.append(item)
            self.logs = self.logs[-300:]
            self.message = str(message)
        DB.add_log(level, str(message))
        if self.job_id:
            DB.update_job(self.job_id, message=str(message))
        runtime_log(str(message), level)

    def set_progress(self, done: int | None = None, total: int | None = None, current: str | None = None) -> None:
        with self._lock:
            if done is not None:
                self.done = done
            if total is not None:
                self.total = total
            if current is not None:
                self.current = current
            self.percent = int((self.done / self.total) * 100) if self.total else 0
        if self.job_id:
            DB.update_job(self.job_id, progress=self.percent, message=self.message)

    def fail(self, message: str) -> None:
        with self._lock:
            self.status = "error"
            self.message = message
            self.errors.append(message)
            self.finished_at = now_iso()
        if self.job_id:
            DB.update_job(self.job_id, status="error", progress=self.percent, message=message, finished_at=self.finished_at)
        self.log(message, "error")

    def finish(self, message: str, status: str = "done") -> None:
        final_status = "cancelled" if self.cancel_event.is_set() else str(status or "done")
        if final_status not in {"done", "partial", "error", "cancelled"}:
            final_status = "done"
        with self._lock:
            self.status = final_status
            self.message = message
            if final_status in {"done", "partial"}:
                self.percent = 100
            self.finished_at = now_iso()
        if self.job_id:
            DB.update_job(self.job_id, status=final_status, progress=self.percent, message=message, finished_at=self.finished_at)
        level = "success" if final_status == "done" else ("warning" if final_status in {"partial", "cancelled"} else "error")
        self.log(message, level)

    def finish_partial(self, message: str) -> None:
        self.finish(message, status="partial")

    def as_dict(self) -> dict[str, Any]:
        with self._lock:
            return {
                "id": self.id,
                "type": self.type,
                "title": self.title,
                "status": self.status,
                "message": self.message,
                "current": self.current,
                "total": self.total,
                "done": self.done,
                "percent": self.percent,
                "errors": list(self.errors),
                "logs": list(self.logs),
                "started_at": self.started_at,
                "finished_at": self.finished_at,
                "paused": self.pause_event.is_set(),
                "job_id": self.job_id,
            }


ACTIVE_TASK: TaskState | None = None
LAST_TASK: TaskState | None = None


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def _ensure_writable_directory(path: Path, label: str = "folder") -> Path:
    resolved = path.expanduser().resolve()
    try:
        resolved.mkdir(parents=True, exist_ok=True)
        probe = resolved / f".suno-studio-write-test-{uuid.uuid4().hex}.tmp"
        probe.write_bytes(b"ok")
        probe.unlink(missing_ok=True)
    except Exception as exc:
        raise RuntimeError(f"Izabrani {label} nije dostupan za upis: {resolved}. Greška: {exc}") from exc
    return resolved


def get_download_dir() -> Path:
    raw = DB.get_setting("download_dir", str(DEFAULT_DOWNLOAD_DIR))
    return _ensure_writable_directory(Path(raw), "folder za preuzimanje")


def _suno_keepalive_enabled() -> bool:
    return DB.get_setting("suno_keepalive_enabled", "1") != "0"


def _suno_keepalive_minutes() -> int:
    try:
        value = int(DB.get_setting("suno_keepalive_minutes", "4") or 4)
    except Exception:
        value = 4
    return max(2, min(value, 30))


def _suno_auto_reopen_browser() -> bool:
    return DB.get_setting("suno_auto_reopen_browser", "0") == "1"


def _store_suno_token(token: str, user_agent: str = "", source: str = "browser") -> None:
    global SESSION_TOKEN, SESSION_USER_AGENT, SESSION_TOKEN_SOURCE
    global SESSION_TOKEN_UPDATED_MONOTONIC, SESSION_TOKEN_UPDATED_AT
    clean = str(token or "").strip()
    if not clean:
        return
    with STATE_LOCK:
        SESSION_TOKEN = clean
        if user_agent:
            SESSION_USER_AGENT = str(user_agent)
        SESSION_TOKEN_SOURCE = source
        SESSION_TOKEN_UPDATED_MONOTONIC = time.monotonic()
        SESSION_TOKEN_UPDATED_AT = now_iso()
        SESSION_KEEPALIVE_STATE.update({
            "status": "warning" if source == "manual" else "active",
            "message": "Ručni Suno token je povezan, ali se ne može automatski obnavljati." if source == "manual" else "Suno sesija je osvežena.",
            "last_success_at": SESSION_TOKEN_UPDATED_AT,
            "consecutive_failures": 0,
        })


def _clear_suno_token(message: str = "Suno veza je uklonjena.") -> None:
    global SESSION_TOKEN, SESSION_USER_AGENT, SESSION_TOKEN_SOURCE
    global SESSION_TOKEN_UPDATED_MONOTONIC, SESSION_TOKEN_UPDATED_AT
    with STATE_LOCK:
        SESSION_TOKEN = None
        SESSION_USER_AGENT = ""
        SESSION_TOKEN_SOURCE = ""
        SESSION_TOKEN_UPDATED_MONOTONIC = 0.0
        SESSION_TOKEN_UPDATED_AT = ""
        SESSION_KEEPALIVE_STATE.update({"status": "waiting", "message": message, "touched": False})


def ensure_suno_session_token(force: bool = False, touch: bool = False) -> str | None:
    """Return a current Suno bearer token and refresh the browser JWT as needed."""
    with STATE_LOCK:
        token = SESSION_TOKEN
        source = SESSION_TOKEN_SOURCE
        age = time.monotonic() - SESSION_TOKEN_UPDATED_MONOTONIC if SESSION_TOKEN_UPDATED_MONOTONIC else 10_000.0
    # A manually pasted token cannot be refreshed without its browser session.
    if source == "manual":
        return token
    if token and not force and age < 45.0:
        return token

    with SESSION_REFRESH_LOCK:
        with STATE_LOCK:
            token = SESSION_TOKEN
            source = SESSION_TOKEN_SOURCE
            age = time.monotonic() - SESSION_TOKEN_UPDATED_MONOTONIC if SESSION_TOKEN_UPDATED_MONOTONIC else 10_000.0
        if source == "manual":
            return token
        if token and not force and age < 45.0:
            return token

        with STATE_LOCK:
            SESSION_KEEPALIVE_STATE.update({
                "status": "refreshing",
                "message": "Osvežavam Suno sesiju...",
                "last_attempt_at": now_iso(),
            })
        try:
            result = CONNECTOR.refresh_session_activity(touch=bool(touch or force))
            fresh = str(result.get("token") or "").strip() if isinstance(result, dict) else ""
            if not fresh:
                raise RuntimeError(str(result.get("reason") or "Suno browser nema aktivnu prijavu."))
            _store_suno_token(fresh, CONNECTOR.get_user_agent() or "", "browser")
            with STATE_LOCK:
                SESSION_KEEPALIVE_STATE.update({
                    "touched": bool(result.get("touched")),
                    "session_status": str(result.get("status") or ""),
                    "message": "Suno sesija je aktivna i token je automatski obnovljen.",
                })
            return fresh
        except Exception as exc:
            with STATE_LOCK:
                failures = int(SESSION_KEEPALIVE_STATE.get("consecutive_failures") or 0) + 1
                SESSION_KEEPALIVE_STATE.update({
                    "status": "warning",
                    "message": f"Automatsko osvežavanje nije uspelo: {exc}",
                    "consecutive_failures": failures,
                    "touched": False,
                })
            runtime_log(f"SUNO KEEPALIVE WARNING: {exc}", "warning")
            # For an ordinary pre-request check, allow the request to try the
            # cached token. A forced refresh (usually after 401) must not return
            # the stale token again.
            return None if force else token


def refresh_suno_session_now(*, verify_api: bool = False) -> dict[str, Any]:
    token = ensure_suno_session_token(force=True, touch=True)
    if not token:
        raise SunoAPIError("Suno sesija nije osvežena. Otvori Suno prozor i prijavi se ponovo.", 401)
    result: dict[str, Any] = {"ok": True, "connected": True, "message": "Suno sesija je osvežena.", "session": suno_session_public_state()}
    if verify_api:
        client = SunoClient(token, user_agent=SESSION_USER_AGENT or None, device_id=SESSION_DEVICE_ID)
        check = client.test_connection()
        result["feed_mode"] = check.get("mode")
        result["message"] = f"Suno sesija je osvežena i biblioteka {check.get('mode')} odgovara."
    return result


def suno_session_public_state() -> dict[str, Any]:
    with STATE_LOCK:
        state = dict(SESSION_KEEPALIVE_STATE)
        state.update({
            "connected": bool(SESSION_TOKEN),
            "source": SESSION_TOKEN_SOURCE,
            "token_updated_at": SESSION_TOKEN_UPDATED_AT,
            "keepalive_enabled": _suno_keepalive_enabled(),
            "keepalive_minutes": _suno_keepalive_minutes(),
            "auto_reopen_browser": _suno_auto_reopen_browser(),
        })
    return state


def suno_session_keepalive_loop() -> None:
    runtime_log("Suno session keeper je pokrenut.")
    while not SESSION_KEEPALIVE_STOP.is_set():
        enabled = _suno_keepalive_enabled()
        interval_seconds = _suno_keepalive_minutes() * 60
        with STATE_LOCK:
            connected = bool(SESSION_TOKEN)
            source = SESSION_TOKEN_SOURCE
        if enabled and source == "manual":
            with STATE_LOCK:
                SESSION_KEEPALIVE_STATE.update({"status": "warning", "message": "Ručni token ne može automatski da se obnovi. Za trajnu sesiju koristi poseban Suno browser."})
            wait_for = interval_seconds
        elif enabled and (connected or CONNECTOR._connector_alive()):
            if not CONNECTOR._connector_alive() and source == "browser" and _suno_auto_reopen_browser():
                try:
                    CONNECTOR.launch()
                except Exception as exc:
                    runtime_log(f"Suno browser nije automatski ponovo otvoren: {exc}", "warning")
            try:
                ensure_suno_session_token(force=True, touch=True)
            except Exception as exc:
                runtime_log(f"Suno session keeper: {exc}", "warning")
            wait_for = interval_seconds
        else:
            with STATE_LOCK:
                if not connected:
                    SESSION_KEEPALIVE_STATE.update({"status": "waiting", "message": "Čeka se Suno povezivanje."})
            wait_for = min(30, interval_seconds)
        SESSION_KEEPALIVE_STOP.wait(max(5, wait_for))
    runtime_log("Suno session keeper je zaustavljen.")


def get_client() -> SunoClient:
    token = ensure_suno_session_token(force=False, touch=False)
    with STATE_LOCK:
        user_agent = SESSION_USER_AGENT
        device_id = SESSION_DEVICE_ID
        source = SESSION_TOKEN_SOURCE
    if not token:
        raise SunoAPIError("Suno nalog nije povezan. Klikni „Poveži Suno“.")
    provider = (lambda force=False: ensure_suno_session_token(force=bool(force), touch=bool(force))) if source == "browser" else None
    return SunoClient(token, user_agent=user_agent or None, device_id=device_id, token_provider=provider)


def start_task(task_type: str, title: str, runner: Callable[[TaskState], None], *, persistent_payload: dict[str, Any] | None = None, job_id: int | None = None) -> TaskState:
    global ACTIVE_TASK, LAST_TASK
    with STATE_LOCK:
        if ACTIVE_TASK and ACTIVE_TASK.status == "running":
            raise RuntimeError("Drugi posao je već u toku. Sačekaj da se završi ili ga zaustavi.")
        if persistent_payload is not None and job_id is None:
            job_id = DB.enqueue_job(task_type, title, persistent_payload, status="running")
            DB.update_job(job_id, status="running", attempts=1, started_at=now_iso(), finished_at="", progress=0, message="Pokretanje...")
        elif job_id is not None:
            job = DB.get_job(job_id)
            attempts = int((job or {}).get("attempts") or 0) + 1
            DB.update_job(job_id, status="running", attempts=attempts, started_at=now_iso(), finished_at="", progress=0, message="Pokretanje...")
        task = TaskState(task_type, title, job_id=job_id)
        ACTIVE_TASK = task
        LAST_TASK = task

    def wrapped() -> None:
        global ACTIVE_TASK
        try:
            runner(task)
            if task.status == "running":
                task.finish("Posao je završen.")
        except Exception as exc:
            task.fail(str(exc))
            task.log(traceback.format_exc(), "debug")
        finally:
            with STATE_LOCK:
                ACTIVE_TASK = None

    threading.Thread(target=wrapped, daemon=True, name=f"task-{task.id}").start()
    return task


def wait_if_paused(task: TaskState) -> None:
    while task.pause_event.is_set() and not task.cancel_event.is_set():
        time.sleep(0.25)

def launch_suno_browser_background() -> None:
    set_connect_launch_state("starting", "Pokrećem poseban Suno browser...")
    try:
        result = CONNECTOR.launch()
        if result.get("ok"):
            set_connect_launch_state("ready", str(result.get("message") or "Suno browser je otvoren."))
        else:
            set_connect_launch_state("error", str(result.get("message") or "Browser nije pokrenut."))
    except Exception as exc:
        set_connect_launch_state("error", f"Ne mogu da pokrenem Suno browser: {exc}")


def check_new_songs(task: TaskState, options: dict[str, Any]) -> None:
    client = get_client()
    max_pages = max(1, min(int(options.get("max_pages") or 10), 100))
    include_main = bool(options.get("include_main", True))
    include_workspaces = bool(options.get("include_workspaces", True))
    refresh_details = bool(options.get("refresh_details", False))
    known = DB.all_song_ids()
    found_ids: list[str] = []
    scanned = 0
    projects: list[dict[str, Any]] = []
    errors: list[str] = []

    if include_workspaces:
        try:
            projects = client.list_all_projects(max_pages=min(max_pages, 30))
            task.log(f"Provera novih pesama: pronađeno {len(projects)} Workspaces/Projects.")
        except Exception as exc:
            errors.append(f"Workspaces: {exc}")
            task.log(f"Workspaces nisu provereni: {exc}", "warning")

    sources: list[tuple[str, Callable[[str | None], tuple[list[dict[str, Any]], str | None, bool, str]]]] = []
    if include_main:
        sources.append(("Glavna biblioteka", lambda cursor: client.list_library_cursor(cursor)))
    for project in projects:
        pid = str(project.get("id") or project.get("project_id") or project.get("workspace_id") or "").strip()
        name = str(project.get("name") or project.get("title") or project.get("display_name") or "My Workspace").strip()
        if pid:
            sources.append((f"Workspace „{name}“", lambda cursor, project_id=pid: client.list_workspace_cursor(project_id, cursor)))

    task.total = max(1, len(sources) * max_pages)
    done_pages = 0
    for label, fetch_page in sources:
        cursor: str | None = None
        no_new_pages = 0
        seen_cursors: set[str] = set()
        for page_no in range(1, max_pages + 1):
            if task.cancel_event.is_set():
                break
            task.set_progress(done_pages, task.total, f"{label} — paket {page_no}")
            try:
                items, next_cursor, has_more, mode = fetch_page(cursor)
            except Exception as exc:
                errors.append(f"{label}: {exc}")
                task.log(f"{label} nije proverena: {exc}", "error")
                break
            scanned += len(items)
            page_new = 0
            for item in items:
                if not isinstance(item, dict):
                    continue
                sid = str(item.get("id") or item.get("clip_id") or item.get("song_id") or "").strip()
                if not sid or sid in known:
                    continue
                candidate = item
                if refresh_details or not item.get("title") or not isinstance(item.get("metadata"), dict):
                    try:
                        candidate = client.get_clip(sid)
                    except Exception as exc:
                        task.log(f"Detalji nisu dopunjeni za {sid}: {exc}", "warning")
                if DB.upsert_song(candidate, source_group=label):
                    known.add(sid)
                    found_ids.append(sid)
                    page_new += 1
            done_pages += 1
            task.set_progress(done_pages, task.total, f"{label} — novih {page_new}")
            task.log(f"{label}, paket {page_no} ({mode}): pročitano {len(items)}, novih {page_new}.")
            no_new_pages = no_new_pages + 1 if page_new == 0 else 0
            if no_new_pages >= 2:
                task.log(f"{label}: dva uzastopna paketa nemaju nove pesme; završavam brzu proveru.")
                break
            if not has_more or not next_cursor or next_cursor in seen_cursors:
                break
            seen_cursors.add(next_cursor)
            cursor = next_cursor
            time.sleep(0.15)

    checked_at = now_iso()
    DB.set_setting("last_new_check_at", checked_at)
    DB.set_setting("last_new_count", str(len(found_ids)))
    DB.set_setting("last_new_ids", json.dumps(found_ids, ensure_ascii=False))
    if task.cancel_event.is_set():
        task.finish(f"Provera je zaustavljena. Do tada je pronađeno {len(found_ids)} novih pesama.")
        return
    summary = f"Provera završena: pregledano {scanned} zapisa, pronađeno i uvezeno {len(found_ids)} novih pesama."
    if errors:
        summary += f" Neuspešni izvori: {len(errors)} — pogledaj Dnevnik."
    task.finish(summary)


def quick_audio_preset_task(task: TaskState, song_ids: list[str], preset: str) -> None:
    preset = str(preset or "").lower()
    options: dict[str, Any] = {
        "start": 0, "end": 30, "format": "mp3", "bitrate": "320k", "processing_mode": "quick",
        "fade_in": 0, "fade_out": 0, "volume_db": 0, "speed": 1, "sample_rate": 0, "channels": 0,
        "normalize": False, "remove_silence": False, "label": "Shorts 30s",
    }
    if preset == "shorts60":
        options.update({"end": 60, "label": "Shorts 60s"})
    elif preset == "normalize":
        options.update({"end": 999999, "processing_mode": "precise", "normalize": True, "label": "Normalizovana verzija"})
    elif preset == "fade":
        options.update({"end": 999999, "processing_mode": "precise", "fade_in": 1.0, "fade_out": 1.5, "label": "Fade verzija"})
    process_audio_batch_task(task, song_ids, options)


def export_m3u(ids: list[str] | None = None) -> Path:
    rows = DB.export_rows(ids)
    path = _unique_path(EXPORT_DIR / f"Suno-plejlista-{datetime.now().strftime('%Y%m%d-%H%M%S-%f')}.m3u8")
    lines = ["#EXTM3U"]
    for row in rows:
        audio = str(row.get("local_audio") or row.get("local_wav") or "")
        if not audio or not Path(audio).exists():
            continue
        lines.append(f"#EXTINF:{int(float(row.get('duration') or 0))},{row.get('display_name') or 'Suno'} - {row.get('title') or row.get('id')}")
        lines.append(audio)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8-sig")
    return path


def export_lyrics_bundle(ids: list[str] | None = None) -> Path:
    rows = DB.export_rows(ids)
    path = _unique_path(EXPORT_DIR / f"Suno-tekstovi-{datetime.now().strftime('%Y%m%d-%H%M%S-%f')}.zip")
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED, allowZip64=True) as z:
        for row in rows:
            title = sanitize_filename(str(row.get("title") or row.get("id")), 100)
            sid = str(row.get("id") or "")[:8]
            base = f"{title} [{sid}]"
            lyrics = str(row.get("lyrics") or "")
            prompt = str(row.get("prompt") or "")
            z.writestr(f"{base}.txt", lyrics)
            z.writestr(f"{base}-prompt.txt", prompt)
            for key, ext in (("local_lrc", ".lrc"), ("local_srt", ".srt")):
                raw = str(row.get(key) or "")
                if raw and Path(raw).exists():
                    z.write(raw, arcname=f"{base}{ext}")
    return path


def full_backup() -> Path:
    """Napravi prenosiv backup baze i svih poznatih lokalnih fajlova.

    Manifest je obavezan kako bi vraćanje moglo da prepiše stare apsolutne
    putanje novim lokacijama na drugom računaru ili posle brisanja foldera.
    """
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S-%f")
    p = _unique_path(EXPORT_DIR / f"Suno-Pesme-Studio-KOMPLETAN-backup-{stamp}.zip")
    rows = DB.export_rows()
    manifest: dict[str, Any] = {
        "format": "suno-pesme-studio-full-backup",
        "manifest_version": 1,
        "app_version": APP_VERSION,
        "created_at": now_iso(),
        "files": [],
    }
    with tempfile.TemporaryDirectory(prefix="suno_full_backup_") as tmp_dir:
        snapshot = Path(tmp_dir) / "suno_biblioteka.db"
        DB.backup_to(snapshot)
        with zipfile.ZipFile(p, "w", compression=zipfile.ZIP_DEFLATED, allowZip64=True) as z:
            z.write(snapshot, arcname="data/suno_biblioteka.db")
            z.writestr("biblioteka.json", json.dumps(rows, ensure_ascii=False, indent=2))
            used: set[str] = set()
            for row in rows:
                song_id = str(row.get("id") or "")
                folder = f"audio/{sanitize_filename(str(row.get('title') or song_id), 90)} [{song_id[:8]}]"
                for field in ("local_audio", "local_wav", "local_video", "local_cover", "local_lyrics", "local_lrc", "local_srt"):
                    raw = str(row.get(field) or "")
                    file_path = Path(raw) if raw else None
                    if not file_path or not file_path.exists() or not file_path.is_file():
                        continue
                    arc = f"{folder}/{file_path.name}"
                    n = 2
                    while arc in used:
                        arc = f"{folder}/{file_path.stem} ({n}){file_path.suffix}"
                        n += 1
                    used.add(arc)
                    z.write(file_path, arcname=arc)
                    manifest["files"].append({
                        "kind": "song_field", "song_id": song_id, "field": field,
                        "archive_path": arc, "original_path": raw, "size": file_path.stat().st_size, "sha256": _sha256(file_path),
                    })
                for item in row.get("derived_files") or []:
                    raw = str(item.get("path") or "")
                    file_path = Path(raw) if raw else None
                    if not file_path or not file_path.exists() or not file_path.is_file():
                        continue
                    arc = f"{folder}/{file_path.name}"
                    n = 2
                    while arc in used:
                        arc = f"{folder}/{file_path.stem} ({n}){file_path.suffix}"
                        n += 1
                    used.add(arc)
                    z.write(file_path, arcname=arc)
                    manifest["files"].append({
                        "kind": "derived", "song_id": song_id, "derived_id": int(item.get("id") or 0),
                        "archive_path": arc, "original_path": raw, "size": file_path.stat().st_size, "sha256": _sha256(file_path),
                    })
            z.writestr("backup_manifest.json", json.dumps(manifest, ensure_ascii=False, indent=2))
            z.writestr(
                "README_RESTORE.txt",
                "Kompletan backup sadrži bazu, tekstove, omote, MP3/WAV/MP4 i obrađene verzije. "
                "Vraćanje kroz Podešavanja raspakuje fajlove i automatski ažurira njihove putanje.\n",
            )
    return p


def youtube_cookie_browser() -> str:
    value = DB.get_setting("youtube_cookies_browser", "none").strip().lower()
    return value if value in {"chrome", "edge", "brave", "firefox", "opera", "vivaldi"} else ""


def get_youtube_api_key() -> str:
    return DB.get_setting("youtube_api_key", "").strip()


def find_downloaded_google_oauth_json() -> str:
    """Pronađi najnoviji Google Desktop OAuth JSON u uobičajenim korisničkim folderima."""
    homes = [Path.home() / "Downloads", Path.home() / "Desktop", Path.home() / "Preuzimanja", Path.home() / "Radna površina"]
    candidates: list[Path] = []
    patterns = ("client_secret*.json", "oauth_client*.json", "google_oauth*.json")
    for folder in homes:
        if not folder.exists() or not folder.is_dir():
            continue
        for pattern in patterns:
            try:
                candidates.extend(path for path in folder.glob(pattern) if path.is_file())
            except Exception:
                continue
    candidates.sort(key=lambda path: path.stat().st_mtime if path.exists() else 0, reverse=True)
    for candidate in candidates:
        try:
            raw = json.loads(candidate.read_text(encoding="utf-8-sig"))
            installed = raw.get("installed") if isinstance(raw, dict) else None
            client_id = str(installed.get("client_id") or "") if isinstance(installed, dict) else ""
            if client_id.endswith(".apps.googleusercontent.com"):
                return str(candidate)
        except Exception:
            continue
    return ""


def get_youtube_access_token(profile_id: str = "", required: bool = False) -> str:
    try:
        return YOUTUBE_OAUTH.get_access_token(profile_id)
    except Exception:
        if required:
            raise
        return ""


def youtube_credentials(profile_id: str = "") -> tuple[str, str]:
    """Vrati (api_key, access_token), sa OAuth vezom kao prvim izborom."""
    access_token = get_youtube_access_token(profile_id, required=False)
    return ("" if access_token else get_youtube_api_key(), access_token)


def _setting_json(key: str, fallback: Any) -> Any:
    try:
        return json.loads(DB.get_setting(key, "") or "")
    except Exception:
        return fallback


def platform_monitor_task(task: TaskState, options: dict[str, Any]) -> None:
    """Small, quota-safe health pass used by the persistent scheduler."""
    profile_id = str(options.get("profile_id") or "")
    token = get_youtube_access_token(profile_id, required=True)
    task.log("Čitam zbirnu YouTube analitiku za poslednjih 28 dana...")
    report = youtube_analytics_suite(token)
    report["checked_at"] = now_iso()
    DB.set_setting("youtube_intelligence_last", json.dumps(report, ensure_ascii=False))
    current = DB.export_rows()
    previous = _setting_json("suno_snapshot_json", [])
    snapshot = suno_snapshot_diff(previous if isinstance(previous, list) else [], current)
    snapshot["checked_at"] = now_iso()
    DB.set_setting("suno_snapshot_last_diff", json.dumps(snapshot, ensure_ascii=False))
    DB.set_setting("suno_snapshot_json", json.dumps(current, ensure_ascii=False))
    task.set_progress(1, 1, "YouTube i Suno stanje")
    task.log(f"Završena proverena YouTube analitika; novih Suno zapisa {len(snapshot['new'])}.", "success")


def save_oauth_channels(channels: list[dict[str, Any]], prune_profiles: bool = True) -> list[dict[str, Any]]:
    """Sačuvaj kanale koje Google vrati i ukloni zastarele OAuth kanale istog profila."""
    saved: list[dict[str, Any]] = []
    by_profile: dict[str, set[str]] = {}
    for channel in channels:
        if not isinstance(channel, dict) or not channel.get("channel_id"):
            continue
        profile_id = str(channel.get("oauth_profile_id") or "")
        if profile_id:
            by_profile.setdefault(profile_id, set()).add(str(channel.get("channel_id") or ""))
        saved.append(DB.upsert_youtube_channel(channel, is_owned=True))
    if prune_profiles:
        for profile_id, current_ids in by_profile.items():
            for existing in DB.list_youtube_channels():
                if str(existing.get("oauth_profile_id") or "") != profile_id:
                    continue
                channel_id = str(existing.get("channel_id") or "")
                if channel_id and channel_id not in current_ids:
                    DB.delete_youtube_channel(channel_id)
    return saved


def _best_song_match(video: dict[str, Any], songs: list[dict[str, Any]], owned_ids: set[str]) -> tuple[dict[str, Any] | None, dict[str, Any] | None]:
    best_song: dict[str, Any] | None = None
    best_match: dict[str, Any] | None = None
    for song in songs:
        match = match_song_to_video(song, video, owned_ids)
        if best_match is None or float(match.get("score") or 0) > float(best_match.get("score") or 0):
            best_song, best_match = song, match
    return best_song, best_match


def _connected_channels_payload() -> dict[str, Any]:
    channels = DB.list_youtube_channels()
    for channel in channels:
        channel["shorts_report"] = DB.channel_shorts_report(str(channel.get("channel_id") or ""))
    return {"ok": True, "channels": channels, "has_api_key": bool(get_youtube_api_key()), "oauth": YOUTUBE_OAUTH.status(), "summary": DB.youtube_summary()}


def scan_owned_youtube_channels(task: TaskState, options: dict[str, Any]) -> None:
    channels = [c for c in DB.list_youtube_channels() if int(c.get("is_owned") or 0) == 1]
    if not channels:
        raise RuntimeError("Dodaj najmanje jedan svoj YouTube kanal u Pametnim alatima.")
    max_pages = max(1, min(int(options.get("max_pages") or 20), 100))
    include_private_unlisted = bool(options.get("include_private_unlisted", True))
    scan_mode = str(options.get("scan_mode") or "new").strip().lower()
    songs = DB.export_rows()
    if not songs:
        raise RuntimeError("Suno biblioteka je prazna. Prvo uvezi pesme.")
    owned_ids = {str(c.get("channel_id") or "") for c in channels}
    run_id = DB.start_youtube_scan_run("owned_channels", len(channels), len(songs))
    task.total = len(channels)
    matched = 0
    errors = 0
    found_collection = DB.get_collection_by_slug("youtube-pronađene-objave")
    try:
        for index, channel in enumerate(channels, 1):
            if task.cancel_event.is_set():
                break
            label = str(channel.get("title") or channel.get("channel_id"))
            task.set_progress(index - 1, len(channels), label)
            task.log(f"Čitam YouTube kanal: {label}")
            try:
                profile_id = str(channel.get("oauth_profile_id") or "")
                if profile_id:
                    api_key, access_token = "", get_youtube_access_token(profile_id, required=True)
                else:
                    api_key, access_token = youtube_credentials()
                known_ids = DB.list_youtube_video_ids(str(channel.get("channel_id") or "")) if scan_mode == "new" else None
                videos = list_channel_videos(channel, api_key=api_key, access_token=access_token, max_pages=max_pages, known_ids=known_ids)
                if scan_mode == "new" and known_ids:
                    task.log(f"{label}: brza provera — zaustavljeno čim je stigla do već poznatih videa ({len(videos)} novih/proverenih).")
            except Exception as exc:
                errors += 1
                task.log(f"Kanal {label} nije pročitan: {exc}", "error")
                task.set_progress(index, len(channels), label)
                continue
            latest = ""
            channel_matches = 0
            for video in videos:
                if not include_private_unlisted and str(video.get("privacy_status") or "public") != "public":
                    continue
                published_at = str(video.get("published_at") or "")
                if published_at and (not latest or published_at > latest):
                    latest = published_at
                DB.upsert_youtube_video(video, is_owned_channel=True)
                song, match = _best_song_match(video, songs, owned_ids)
                if not song or not match or float(match.get("score") or 0) < float(options.get("threshold") or 68):
                    continue
                DB.upsert_youtube_match(str(song["id"]), str(video["video_id"]), match)
                channel_matches += 1
                matched += 1
                # Naslov i trajanje daju samo kandidata. Ne proglašavaj pesmu kompletnom
                # niti menjaj njen glavni datum/link dok audio analiza to ne potvrdi.
                review_collection = DB.get_collection_by_slug("youtube-audio-provera")
                if review_collection:
                    DB.add_songs_to_collection(int(review_collection["id"]), [str(song["id"])])
                if found_collection:
                    DB.add_songs_to_collection(int(found_collection["id"]), [str(song["id"])])
            DB.update_youtube_channel_scan(str(channel.get("channel_id") or ""), latest, len(videos))
            task.log(f"{label}: {len(videos)} videa, povezano sa pesmama: {channel_matches}.", "success")
            task.set_progress(index, len(channels), label)
        summary = f"YouTube kanali provereni: {len(channels)}. Pronađene objave tvojih pesama: {matched}. Greške: {errors}."
        DB.finish_youtube_scan_run(run_id, matched, errors, summary)
        task.finish(summary)
    except Exception:
        DB.finish_youtube_scan_run(run_id, matched, errors + 1, "Skeniranje je prekinuto greškom.")
        raise


def scan_global_youtube(task: TaskState, options: dict[str, Any]) -> None:
    api_key, access_token = youtube_credentials()
    ids = [str(x) for x in (options.get("ids") or []) if str(x)]
    all_songs = DB.export_rows(ids if ids else None)
    max_songs = max(1, min(int(options.get("max_songs") or 50), 500))
    songs = all_songs[:max_songs]
    if not songs:
        raise RuntimeError("Nema pesama za pretragu.")
    channels = DB.list_youtube_channels()
    owned_ids = {str(c.get("channel_id") or "") for c in channels if int(c.get("is_owned") or 0) == 1}
    include_owned = bool(options.get("include_owned_channels", True))
    threshold = max(35.0, min(float(options.get("threshold") or 62), 100.0))
    results_per_query = max(5, min(int(options.get("results_per_song") or 20), 50))
    max_queries = max(1, min(int(options.get("query_variants") or 3), 5))
    max_pages = max(1, min(int(options.get("max_pages") or 1), 2))
    # Off by default: a title match is only ever a candidate, and audio
    # confirmation normally needs an explicit "Potvrdi audio" click precisely
    # because downloading+fingerprinting is real bandwidth/time cost per
    # video. When the user explicitly opts in, this budget lets the scan
    # itself confirm the top few candidates instead of requiring N clicks.
    auto_confirm_budget = max(0, min(int(options.get("auto_confirm_budget") or 0), 20))
    api_call_budget = max(1, min(int(options.get("api_call_budget") or 80), 95))
    cookie_browser = DB.get_setting("youtube_cookies_browser", "none")
    run_id = DB.start_youtube_scan_run("global_search", len(owned_ids), len(songs))
    task.total = len(songs)
    candidates = 0
    errors = 0
    api_calls = 0
    fallback_searches = 0
    suspicious_collection = DB.get_collection_by_slug("moguće-neovlašćene-objave")
    found_collection = DB.get_collection_by_slug("youtube-pronađene-objave")
    try:
        if not api_key and not access_token:
            task.log("Google/YouTube API nije povezan. Koristim ugrađeni yt-dlp + Deno režim pretrage.", "warning")
            ensure_ytdlp()
            ensure_deno()
        for index, song in enumerate(songs, 1):
            if task.cancel_event.is_set():
                break
            title = str(song.get("title") or "").strip()
            task.set_progress(index - 1, len(songs), title)
            if not title:
                task.set_progress(index, len(songs), "Bez naslova")
                continue
            queries = build_search_queries(song, max_queries=max_queries)
            task.log(f"YouTube pretraga: {title} · upita: {len(queries)}")
            by_id: dict[str, dict[str, Any]] = {}
            song_had_error = False
            for q_index, query in enumerate(queries, 1):
                if task.cancel_event.is_set():
                    break
                videos: list[dict[str, Any]] = []
                used_api = False
                if (api_key or access_token) and api_calls < api_call_budget:
                    pages_for_query = max_pages if q_index == 1 else 1
                    try:
                        videos = search_videos(
                            api_key, query, max_results=results_per_query, access_token=access_token,
                            max_pages=pages_for_query,
                        )
                        api_calls += pages_for_query
                        used_api = True
                    except Exception as exc:
                        song_had_error = True
                        task.log(f"YouTube API nije uspeo za upit {query!r}: {exc}. Pokušavam yt-dlp rezervni režim.", "warning")
                if not videos:
                    try:
                        videos = search_youtube_with_ytdlp(query, max_results=results_per_query, cookie_browser=cookie_browser)
                        fallback_searches += 1
                    except Exception as exc:
                        song_had_error = True
                        mode = "posle API greške" if used_api else "u rezervnom režimu"
                        task.log(f"yt-dlp pretraga {mode} nije uspela za {query!r}: {exc}", "error")
                        continue
                for video in videos:
                    vid = str(video.get("video_id") or "")
                    if vid:
                        by_id.setdefault(vid, video)
                # Ako tačan naslov već daje dovoljno kandidata, ne troši dodatnu kvotu.
                promising = sum(1 for v in by_id.values() if float(match_song_to_video(song, v, owned_ids).get("score") or 0) >= threshold)
                if promising >= 5:
                    break
            if song_had_error and not by_id:
                errors += 1
            local_count = 0
            external_count = 0
            for video in by_id.values():
                is_owned = str(video.get("channel_id") or "") in owned_ids
                if is_owned and not include_owned:
                    continue
                match = match_song_to_video(song, video, owned_ids)
                if float(match.get("score") or 0) < threshold:
                    continue
                DB.upsert_youtube_video(video, is_owned_channel=is_owned)
                match_row = DB.upsert_youtube_match(str(song["id"]), str(video["video_id"]), match)
                local_count += 1
                candidates += 1
                if not is_owned:
                    external_count += 1
                    if suspicious_collection:
                        DB.add_songs_to_collection(int(suspicious_collection["id"]), [str(song["id"])])
                if found_collection:
                    DB.add_songs_to_collection(int(found_collection["id"]), [str(song["id"])])
                if auto_confirm_budget > 0 and not str(match_row.get("audio_checked_at") or ""):
                    try:
                        task.log(f"Automatska audio potvrda: {video.get('title')}", "info")
                        _analyse_video_against_songs(task, video, [song], owned_ids, options)
                        auto_confirm_budget -= 1
                    except (AudioMatchCancelled, AudioCancelled):
                        raise
                    except Exception as exc:
                        task.log(f"Audio potvrda nije uspela za „{video.get('title')}“: {exc}", "warning")
            task.log(
                f"„{title}“: pregledano {len(by_id)} jedinstvenih videa, pronađeno {local_count} kandidata ({external_count} van tvojih kanala).",
                "success" if local_count else "info",
            )
            task.set_progress(index, len(songs), title)
        summary = (
            f"YouTube pretraga završena: {len(songs)} pesama, {candidates} kandidata, {errors} neuspelih pesama, "
            f"API pozivi ≈ {api_calls}, yt-dlp rezervne pretrage: {fallback_searches}. "
            "Kandidat nije automatski dokaz povrede autorskog prava."
        )
        DB.finish_youtube_scan_run(run_id, candidates, errors, summary)
        if errors and candidates:
            task.finish_partial(summary)
        elif errors == len(songs) and not candidates:
            task.fail(summary)
        else:
            task.finish(summary)
    except Exception:
        DB.finish_youtube_scan_run(run_id, candidates, errors + 1, "Globalna pretraga je prekinuta greškom.")
        raise



def _song_audio_source_for_match(song: dict[str, Any]) -> str | Path | None:
    local = _existing_audio_path(song)
    if local is not None:
        return local
    remote = str(song.get("audio_url") or "").strip()
    if remote:
        return remote
    song_id = str(song.get("id") or "")
    if song_id and not song_id.startswith("local-"):
        try:
            detail = get_client().get_clip(song_id)
            if isinstance(detail, dict):
                DB.upsert_song(detail, source_group=str(song.get("source_group") or "Suno"))
                refreshed = DB.get_song(song_id) or song
                local = _existing_audio_path(refreshed)
                if local is not None:
                    return local
                remote = str(refreshed.get("audio_url") or "").strip()
                if remote:
                    return remote
        except Exception as exc:
            runtime_log(f"Audio match: nije osvežen Suno izvor za {song_id}: {exc}", "warning")
    return None


def _signature_for_source(
    source_type: str,
    source_id: str,
    source: str | Path,
    task: TaskState | None = None,
    label: str = "",
    force: bool = False,
) -> dict[str, Any]:
    identity = source_identity(source)
    cached = None if force else DB.get_audio_fingerprint(source_type, source_id, AUDIO_MATCH_VERSION)
    if cached:
        cached_identity = str(cached.get("source_identity") or "")
        # Remote Suno URLs can rotate while the clip ID stays the same. Reuse the stable clip fingerprint.
        remote = str(source).startswith(("http://", "https://"))
        if remote or (cached_identity and cached_identity == identity.get("identity")):
            try:
                return unpack_signature(cached.get("payload") or b"")
            except Exception as exc:
                runtime_log(f"Audio fingerprint cache nije pročitan ({source_type}:{source_id}): {exc}", "warning")

    def progress(message: str, percent: int) -> None:
        if task:
            with task._lock:
                task.message = f"{label}: {message}" if label else message

    signature = extract_signature(source, progress, (lambda: task.cancel_event.is_set()) if task else None)
    DB.save_audio_fingerprint(
        source_type, source_id, AUDIO_MATCH_VERSION,
        float(signature.get("duration") or 0), float(signature.get("interval") or 0.5),
        pack_signature(signature), str(identity.get("identity") or ""),
        float(identity.get("mtime") or 0), int(identity.get("size") or 0),
    )
    return signature


DUPLICATE_AUDIO_CONFIRM_LIMIT = 40


def _duplicate_audio_confirm(probable: list[dict[str, Any]], limit: int = DUPLICATE_AUDIO_CONFIRM_LIMIT) -> dict[str, Any]:
    """Take duplicate_detection()'s cheap title+duration 'probable' pairs and
    confirm the ones we actually CAN check with real audio fingerprints
    (Chromaprint via audio_match, the same engine as Pronalazac mojih pesama).
    Only pairs where BOTH songs already have a local audio file are checked --
    this reuses the same fingerprint cache song-finder indexing already fills,
    and is capped so a 3000+ song library never blocks the HTTP response.
    Tempo/pitch-shifted re-uploads of the same song can still score low here;
    that limitation is shown to the user in the UI, not hidden."""
    checked = 0
    confirmed: list[dict[str, Any]] = []
    skipped_no_local_audio = 0
    for pair in probable:
        if checked >= limit:
            break
        song_a = DB.get_song(str(pair.get("a", {}).get("id") or "")) or {}
        song_b = DB.get_song(str(pair.get("b", {}).get("id") or "")) or {}
        source_a = _existing_audio_path(song_a)
        source_b = _existing_audio_path(song_b)
        if not source_a…62995 tokens truncated…3_youtube_upload_task(t,payload),persistent_payload=payload); self._send_json({"ok":True,"task":task.as_dict()}); return
            if path == "/api/v3/proof":
                song_id=str(body.get("id") or ""); song=DB.get_song(song_id)
                if not song: raise RuntimeError("Pesma nije pronađena.")
                result=create_proof_package(song,EXPORT_DIR / "Dokazni_paketi"); self._send_json({"ok":True,**result,"download_url":"/api/export/download?path="+urllib.parse.quote(str(result["path"]))}); return
            if path == "/api/v3/panako/index":
                payload=dict(body); task=start_task("v3_panako_index","Panako Content-ID indeks",lambda t:v3_panako_index_task(t,payload),persistent_payload=payload); self._send_json({"ok":True,"task":task.as_dict()}); return
            if path == "/api/v3/panako/query":
                result=panako_query(ROOT,Path(str(body.get("path") or "")),DATA_DIR); self._send_json({"ok":True,"result":result}); return
            if path == "/api/v3/panako/install":
                source_path = str(body.get("source_path") or "").strip()
                if not source_path:
                    raise RuntimeError("Izaberi Panako .jar fajl koji si sam preuzeo/preuzela.")
                result = install_panako_jar(ROOT, Path(source_path))
                self._send_json({"ok": True, "result": result, "status": panako_status(ROOT)}); return
            if path == "/api/v3/snapshot":
                payload=dict(body); task=start_task("v3_snapshot","Snapshot cele verzije programa",lambda t:v3_snapshot_task(t,payload),persistent_payload=payload); self._send_json({"ok":True,"task":task.as_dict()}); return
            if path == "/api/v3/watch/save":
                options=body.get("options") if isinstance(body.get("options"),dict) else {"include_owned_channels":True,"max_songs":50}
                saved=DB.upsert_schedule("youtube_global","Kontinuirana YouTube provera",max(60,int(body.get("interval_minutes") or 360)),bool(body.get("enabled",True)),options)
                self._send_json({"ok":True,"schedule":saved,"schedules":DB.list_schedules()}); return
            if path == "/api/jobs/resume":
                task = run_persistent_job(int(body.get("id") or 0)); self._send_json({"ok": True, "task": task.as_dict()}); return
            if path == "/api/jobs/delete":
                self._send_json({"ok": True, "deleted": DB.delete_job(int(body.get("id") or 0))}); return
            if path == "/api/schedule/save":
                saved = DB.upsert_schedule(str(body.get("task_type") or "youtube_owned"), str(body.get("name") or "Automatska provera"), int(body.get("interval_minutes") or 60), bool(body.get("enabled", True)), body.get("options") if isinstance(body.get("options"), dict) else {})
                self._send_json({"ok": True, "schedule": saved, "schedules": DB.list_schedules()}); return
            if path == "/api/sync/checkpoints/clear":
                self._send_json({"ok": True, "deleted": DB.clear_sync_checkpoints(str(body.get("prefix") or ""))}); return
            if path == "/api/song/history/undo":
                song = DB.undo_song_history(str(body.get("id") or ""), int(body.get("history_id") or 0) or None)
                if not song: raise RuntimeError("Nema prethodne verzije za vraćanje.")
                self._send_json({"ok": True, "song": song, "history": DB.list_song_history(str(body.get("id") or ""))}); return
            if path == "/api/backup/incremental":
                payload = dict(body); task = start_task("incremental_backup", "Inkrementalni backup", lambda t: incremental_backup_task(t, payload), persistent_payload=payload)
                self._send_json({"ok": True, "task": task.as_dict()}); return
            if path == "/api/backup/incremental/restore-item":
                result = restore_incremental_item(DB, Path(str(body.get("snapshot") or "")), song_id=str(body.get("song_id") or ""), field=str(body.get("field") or ""))
                self._send_json({"ok": True, **result}); return
            if path == "/api/files/relocate":
                payload = dict(body); task = start_task("relocate_files", "Pronalaženje premeštenih fajlova", lambda t: relocate_files_task(t, payload), persistent_payload=payload)
                self._send_json({"ok": True, "task": task.as_dict()}); return
            if path == "/api/audio/quality":
                payload = dict(body); task = start_task("audio_quality", "Analiza kvaliteta pre YouTube objave", lambda t: advanced_quality_task(t, payload), persistent_payload=payload)
                self._send_json({"ok": True, "task": task.as_dict()}); return
            if path == "/api/subtitles/save":
                song_id=str(body.get("id") or ""); song=DB.get_song(song_id)
                if not song: raise RuntimeError("Pesma nije pronađena.")
                cues=body.get("cues") if isinstance(body.get("cues"),list) else []
                paths=save_subtitle_files(song,cues,DB,Path(str(body.get("target") or "")) if str(body.get("target") or "") else None, get_download_dir() / "Tekstovi")
                self._send_json({"ok":True,"paths":paths,"cues":DB.list_subtitle_cues(song_id)}); return
            if path == "/api/text/compare":
                song_id=str(body.get("song_id") or ""); video_url=str(body.get("video_url") or ""); video_id=str(body.get("video_id") or "")
                song=DB.get_song(song_id)
                if not song: raise RuntimeError("Suno pesma nije pronađena.")
                transcript=str(body.get("transcript") or "") or youtube_transcript(video_url)
                result=compare_lyrics_transcript(str(song.get("lyrics") or ""),transcript); DB.save_text_comparison(song_id,video_id,result)
                self._send_json({"ok":True,"result":result}); return
            if path == "/api/cloud-backup":
                result=create_cloud_backup(DB,Path(str(body.get("folder") or "")),include_files=bool(body.get("include_files",False)))
                self._send_json({"ok":True,**result}); return
            if path == "/api/cloud-backup/restore":
                package_path=Path(str(body.get("path") or "")); result=restore_cloud_backup(DB,package_path,restore_files=bool(body.get("restore_files",True)),restore_root=Path(str(body.get("restore_root") or "")) if str(body.get("restore_root") or "") else (get_download_dir() / "Vraceni_cloud_backup" / sanitize_filename(package_path.stem,80)),preserve_original_paths=bool(body.get("preserve_original_paths",False)))
                self._send_json({"ok":True,**result}); return
            if path == "/api/plugins/stems/run":
                payload=dict(body); task=start_task("stems","Izdvajanje vokala i instrumentala",lambda t:stem_task(t,payload),persistent_payload=payload); self._send_json({"ok":True,"task":task.as_dict()}); return
            if path == "/api/plugins/transcription/run":
                payload=dict(body); task=start_task("transcription","Lokalna transkripcija",lambda t:transcription_task(t,payload),persistent_payload=payload); self._send_json({"ok":True,"task":task.as_dict()}); return
            if path == "/api/plugins/stems/install":
                payload={"component":"stems"}; task=start_task("plugin_install","Instalacija: izdvajanje vokala i instrumentala",lambda t:install_ai_plugin_task(t,payload),persistent_payload=payload); self._send_json({"ok":True,"task":task.as_dict()}); return
            if path == "/api/plugins/transcription/install":
                payload={"component":"transcription"}; task=start_task("plugin_install","Instalacija: lokalna transkripcija",lambda t:install_ai_plugin_task(t,payload),persistent_payload=payload); self._send_json({"ok":True,"task":task.as_dict()}); return
            if path == "/api/update/settings":
                DB.set_setting("update_manifest_url",str(body.get("manifest_url") or "").strip()); self._send_json({"ok":True,"update":run_update_check(download_if_available=False)}); return
            if path == "/api/update/download":
                result=download_update(ROOT,get_update_manifest_url(),APP_VERSION); self._send_json({"ok":True,**result}); return
            if path == "/api/connect/start":
                browser = CONNECTOR.find_browser()
                if not browser:
                    self._send_json({"ok": False, "message": "Nisam pronašao Chrome, Edge ili Brave. Instaliraj jedan od njih ili koristi ručni Suno token."}, 400)
                    return
                with STATE_LOCK:
                    current = str(CONNECT_LAUNCH_STATE.get("status") or "idle")
                if current != "starting":
                    threading.Thread(target=launch_suno_browser_background, daemon=True, name="suno-browser-launch").start()
                self._send_json({"ok": True, "message": "Pokretanje Suno prozora je započeto. Ovaj odgovor se vraća odmah da lokalni program ne bi prijavio ‘Failed to fetch’."})
                return
            if path == "/api/connect/check":
                result = CONNECTOR.connection_status()
                if result.get("connected") and result.get("token"):
                    _store_suno_token(
                        str(result.pop("token") or ""),
                        str(result.pop("user_agent", "") or ""),
                        "browser",
                    )
                    try:
                        client = get_client()
                        check = client.test_connection()
                        result["ok"] = True
                        result["connected"] = True
                        result["feed_mode"] = check.get("mode")
                        result["message"] = (
                            f"Suno nalog je povezan preko biblioteke {check.get('mode')}. "
                            f"Glavna biblioteka: {check.get('items')} zapisa u prvom paketu; "
                            f"Workspaces/Projects pronađeno: {check.get('projects', 0)}."
                        )
                    except Exception as exc:
                        _clear_suno_token("Suno API nije prihvatio pronađenu prijavu.")
                        result = {
                            "ok": False,
                            "connected": False,
                            "message": f"Prijava je pronađena, ali Suno biblioteka nije prihvatila vezu: {exc}",
                            "diagnostics": result.get("diagnostics", {}),
                        }
                self._send_json(result, 200 if result.get("ok") else 400)
                return
            if path == "/api/connect/keepalive":
                result = refresh_suno_session_now(verify_api=bool(body.get("verify_api", False)))
                self._send_json(result)
                return
            if path == "/api/connect/token":
                token = str(body.get("token") or "").strip()
                if token.lower().startswith("bearer "):
                    token = token[7:].strip()
                if len(token) < 40:
                    raise RuntimeError("Suno token nije ispravan ili je prekratak.")
                candidate = SunoClient(token, user_agent=str(body.get("user_agent") or SESSION_USER_AGENT or ""), device_id=SESSION_DEVICE_ID)
                check = candidate.test_connection()
                _store_suno_token(token, str(body.get("user_agent") or SESSION_USER_AGENT or ""), "manual")
                self._send_json({"ok": True, "connected": True, "message": f"Ručna Suno veza radi preko {check.get('mode')}; prvi paket ima {check.get('items')} zapisa."})
                return
            if path == "/api/connect/disconnect":
                _clear_suno_token("Veza je uklonjena iz memorije programa.")
                self._send_json({"ok": True, "message": "Veza je uklonjena iz memorije programa."})
                return
            if path == "/api/youtube/oauth/import-client":
                selected = str(body.get("path") or "").strip()
                auto_found = False
                if not selected:
                    selected = find_downloaded_google_oauth_json()
                    auto_found = bool(selected)
                if not selected:
                    selected = choose_file_dialog(str(Path.home() / "Downloads"), "Google OAuth JSON (*.json)|*.json|Svi fajlovi (*.*)|*.*")
                if not selected:
                    raise RuntimeError("Nijedan Google OAuth JSON fajl nije izabran.")
                summary = YOUTUBE_OAUTH.import_client_config(selected)
                message = "Google OAuth JSON je automatski pronađen u Downloads folderu. Sada izaberi svoj mejl." if auto_found else "Google prijava je pripremljena. Sada klikni „Poveži YouTube — izaberi svoj mejl“."
                self._send_json({"ok": True, "oauth": YOUTUBE_OAUTH.status(), "summary": summary, "auto_found": auto_found, "message": message})
                return
            if path == "/api/youtube/analytics/run":
                token = get_youtube_access_token(str(body.get("profile_id") or ""), required=True)
                video_id = str(body.get("video_id") or "").strip()
                report = youtube_analytics(token, str(body.get("start_date") or ""), str(body.get("end_date") or ""), video_id)
                if video_id:
                    report["retention"] = youtube_retention(token, video_id)
                report["checked_at"] = now_iso()
                DB.set_setting("youtube_intelligence_last", json.dumps(report, ensure_ascii=False))
                self._send_json({"ok": True, "report": report, "message": "YouTube analitika je učitana."})
                return
            if path == "/api/youtube/analytics-suite/run":
                token = get_youtube_access_token(str(body.get("profile_id") or ""), required=True)
                report = youtube_analytics_suite(token, str(body.get("start_date") or ""), str(body.get("end_date") or ""), str(body.get("video_id") or "").strip())
                report["checked_at"] = now_iso()
                DB.set_setting("youtube_intelligence_last", json.dumps(report, ensure_ascii=False))
                self._send_json({"ok": True, "report": report, "message": "Detaljni YouTube Analytics izveštaji su učitani; nedostupne stavke su posebno označene."})
                return
            if path == "/api/youtube/comments/analyze":
                video_id = str(body.get("video_id") or "").strip()
                if not video_id:
                    raise RuntimeError("Unesi YouTube video ID.")
                token = get_youtube_access_token(str(body.get("profile_id") or ""), required=True)
                rows = fetch_comments(token, video_id, int(body.get("max_pages") or 3))
                result = {"video_id": video_id, "analysis": analyze_comments(rows), "comments": rows, "checked_at": now_iso()}
                DB.set_setting("youtube_comments_last", json.dumps(result, ensure_ascii=False))
                self._send_json({"ok": True, "result": result, "message": f"Analizirano komentara: {len(rows)}."})
                return
            if path == "/api/youtube/comments/export":
                saved = _setting_json("youtube_comments_last", {})
                rows = saved.get("comments") if isinstance(saved, dict) and isinstance(saved.get("comments"), list) else []
                if not rows: raise RuntimeError("Prvo učitaj i analiziraj komentare za video.")
                target = EXPORT_DIR / f"YouTube-komentari-{sanitize_filename(str(saved.get('video_id') or 'video'), 40)}-{datetime.now().strftime('%Y%m%d-%H%M%S')}.csv"
                target.parent.mkdir(parents=True, exist_ok=True)
                with target.open("w", encoding="utf-8-sig", newline="") as handle:
                    writer = csv.DictWriter(handle, fieldnames=["id", "author", "text", "likes", "replies", "published_at"])
                    writer.writeheader()
                    for row in rows: writer.writerow({k: row.get(k, "") for k in writer.fieldnames})
                self._send_json({"ok": True, "path": str(target), "download_url": "/api/export/download?path=" + urllib.parse.quote(str(target)), "count": len(rows), "message": "Izvezeni su stvarni komentari koje je vratio YouTube API."})
                return
            if path == "/api/youtube/video/visual-analyze":
                selected = str(body.get("path") or "").strip()
                if not selected:
                    selected = choose_file_dialog(str(Path.home()), "Video fajlovi (*.mp4;*.mkv;*.mov;*.webm)|*.mp4;*.mkv;*.mov;*.webm|Svi fajlovi (*.*)|*.*")
                target = Path(selected).expanduser().resolve() if selected else None
                if not target or not target.is_file():
                    raise RuntimeError("Video fajl nije izabran ili više ne postoji.")
                ensure_ffmpeg()
                result = visual_video_analysis(target, str(ffmpeg_path()), str(ffprobe_path()))
                result["checked_at"] = now_iso()
                DB.set_setting("youtube_visual_last", json.dumps(result, ensure_ascii=False))
                self._send_json({"ok": True, "result": result, "message": "Vizuelna tehnička analiza videa je završena."})
                return
            if path == "/api/suno/snapshot/compare":
                current = DB.export_rows()
                previous = _setting_json("suno_snapshot_json", [])
                result = suno_snapshot_diff(previous if isinstance(previous, list) else [], current)
                result["checked_at"] = now_iso()
                DB.set_setting("suno_snapshot_last_diff", json.dumps(result, ensure_ascii=False))
                DB.set_setting("suno_snapshot_json", json.dumps(current, ensure_ascii=False))
                self._send_json({"ok": True, "result": result, "message": f"Suno arhiva: novih {len(result['new'])}, nestalih {len(result['missing'])}, promenjenih {len(result['changed'])}."})
                return
            if path == "/api/platform-intelligence/schedule":
                minutes = max(60, int(body.get("interval_minutes") or 720))
                enabled = bool(body.get("enabled", True))
                options = {"profile_id": str(body.get("profile_id") or "")}
                saved = DB.upsert_schedule("platform_intelligence", "YouTube analitika i Suno kontrola", minutes, enabled, options)
                self._send_json({"ok": True, "schedule": saved, "message": "Automatska analitika je sačuvana."})
                return
            if path == "/api/platform-intelligence/report/export":
                song_id = str(body.get("song_id") or "").strip()
                video_id = str(body.get("video_id") or "").strip()
                song = DB.get_song(song_id)
                if not song: raise RuntimeError("Izaberi postojeću pesmu.")
                candidates = [x for x in DB.list_youtube_audio_analyses(limit=10000) if str(x.get("song_id") or "") == song_id]
                match = next((x for x in candidates if str(x.get("video_id") or "") == video_id), None) if video_id else (candidates[0] if candidates else None)
                analytics = _setting_json("youtube_intelligence_last", {})
                comments = _setting_json("youtube_comments_last", {})
                visual = _setting_json("youtube_visual_last", {})
                if match and str(analytics.get("video_id") or "") != str(match.get("video_id") or ""): analytics = None
                if match and str(comments.get("video_id") or "") != str(match.get("video_id") or ""): comments = None
                report = build_song_report(song, match, analytics, comments, visual)
                target = EXPORT_DIR / f"Izvestaj-{sanitize_filename(str(song.get('title') or song_id), 80)}-{datetime.now().strftime('%Y%m%d-%H%M%S')}.json"
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_text(json.dumps(report, ensure_ascii=False, indent=2, default=str) + "\n", encoding="utf-8")
                self._send_json({"ok": True, "path": str(target), "download_url": "/api/export/download?path=" + urllib.parse.quote(str(target)), "report": report, "message": "Izveštaj je izvezen bez dopunjavanja nedostajućih podataka."})
                return
            if path == "/api/youtube/oauth/start":
                auth = YOUTUBE_OAUTH.start_authorization(str(body.get("email") or "").strip())
                opened = webbrowser.open(str(auth.get("url") or ""), new=1)
                self._send_json({"ok": True, "opened": bool(opened), "url": auth.get("url"), "message": "Otvoren je Google izbor naloga. Izaberi mejl i dozvoli samo čitanje YouTube podataka."})
                return
            if path == "/api/youtube/oauth/refresh-channels":
                channels = YOUTUBE_OAUTH.refresh_channels(str(body.get("profile_id") or ""))
                saved = save_oauth_channels(channels)
                self._send_json({"ok": True, "channels": saved, "oauth": YOUTUBE_OAUTH.status(), "message": f"Osveženo je {len(saved)} YouTube kanala sa povezanih Google naloga."})
                return
            if path == "/api/youtube/oauth/disconnect":
                profile_id = str(body.get("profile_id") or "").strip()
                if not profile_id:
                    raise RuntimeError("Google nalog nije izabran.")
                YOUTUBE_OAUTH.revoke_and_disconnect_profile(profile_id)
                removed = 0
                for channel in DB.list_youtube_channels():
                    if str(channel.get("oauth_profile_id") or "") == profile_id:
                        DB.delete_youtube_channel(str(channel.get("channel_id") or ""))
                        removed += 1
                self._send_json({"ok": True, "oauth": YOUTUBE_OAUTH.status(), "message": f"Google/YouTube nalog je odvojen. Uklonjeno povezanih kanala: {removed}. Sačuvani rezultati i pesme nisu obrisani."})
                return
            if path == "/api/youtube/oauth/remove-config":
                oauth_channels = [c for c in DB.list_youtube_channels() if str(c.get("oauth_profile_id") or "")]
                YOUTUBE_OAUTH.remove_client_config()
                for channel in oauth_channels:
                    DB.delete_youtube_channel(str(channel.get("channel_id") or ""))
                self._send_json({"ok": True, "oauth": YOUTUBE_OAUTH.status(), "message": f"Google OAuth podešavanje i sačuvane prijave su uklonjeni. Uklonjeno povezanih kanala: {len(oauth_channels)}. Rezultati analiza nisu obrisani."})
                return
            if path == "/api/youtube/settings":
                if "api_key" in body:
                    api_key = str(body.get("api_key") or "").strip()
                    DB.set_setting("youtube_api_key", api_key)
                if "copyright_owner_name" in body:
                    DB.set_setting("copyright_owner_name", str(body.get("copyright_owner_name") or "").strip())
                if "cookies_browser" in body:
                    value = str(body.get("cookies_browser") or "none").strip().lower()
                    if value not in {"none", "chrome", "edge", "brave", "firefox", "opera", "vivaldi"}:
                        raise RuntimeError("Nepodržan browser za YouTube kolačiće.")
                    DB.set_setting("youtube_cookies_browser", value)
                self._send_json({"ok": True, "has_api_key": bool(get_youtube_api_key()), "owner_name": DB.get_setting("copyright_owner_name", ""), "cookies_browser": DB.get_setting("youtube_cookies_browser", "none"), "message": "YouTube podešavanja su sačuvana."})
                return
            if path == "/api/youtube/channel/add":
                reference = str(body.get("reference") or "").strip()
                if not reference:
                    raise RuntimeError("Upiši YouTube kanal ili @handle.")
                api_key, access_token = youtube_credentials()
                channel = resolve_channel(reference, api_key, access_token=access_token)
                saved = DB.upsert_youtube_channel(channel, is_owned=bool(body.get("is_owned", True)))
                self._send_json({"ok": True, "channel": saved, "channels": DB.list_youtube_channels(), "message": f"Kanal „{saved.get('title')}“ je dodat."})
                return
            if path == "/api/youtube/channel/delete":
                channel_id = str(body.get("channel_id") or "")
                DB.delete_youtube_channel(channel_id)
                self._send_json({"ok": True, "channels": DB.list_youtube_channels(), "message": "Kanal je uklonjen iz programa."})
                return
            if path == "/api/youtube/scan-owned":
                task = start_task("youtube_owned", "Provera mojih YouTube kanala", lambda t: scan_owned_youtube_channels(t, body), persistent_payload=dict(body))
                self._send_json({"ok": True, "task": task.as_dict()})
                return
            if path == "/api/youtube/scan-global":
                task = start_task("youtube_global", "Globalna pretraga mojih pesama na YouTube-u", lambda t: scan_global_youtube(t, body), persistent_payload=dict(body))
                self._send_json({"ok": True, "task": task.as_dict()})
                return
            if path == "/api/youtube/audio-analyze-owned":
                task = start_task("youtube_audio_owned", "YouTube ↔ Suno audio analiza", lambda t: analyze_owned_youtube_audio(t, body), persistent_payload=dict(body))
                self._send_json({"ok": True, "task": task.as_dict()})
                return
            if path == "/api/youtube/channel/scan-shorts":
                payload = dict(body); payload["shorts_only"] = True
                task = start_task("youtube_audio_owned", "Audio provera Shorts videa", lambda t: analyze_owned_youtube_audio(t, payload), persistent_payload=payload)
                self._send_json({"ok": True, "task": task.as_dict()})
                return
            if path == "/api/youtube/fingerprint-index":
                task = start_task("youtube_suno_index", "Pravljenje Suno audio indeksa", lambda t: build_suno_fingerprint_index(t, body))
                self._send_json({"ok": True, "task": task.as_dict()})
                return
            if path == "/api/youtube/audio-analyze-url":
                task = start_task("youtube_audio_url", "Analiza jednog YouTube videa", lambda t: analyze_youtube_url_task(t, body))
                self._send_json({"ok": True, "task": task.as_dict()})
                return
            if path == "/api/youtube/audio-analyze-match":
                match_id = int(body.get("id") or 0)
                if not match_id:
                    raise RuntimeError("Izaberi YouTube rezultat za audio analizu.")
                task = start_task("youtube_audio_one", "Audio provera YouTube objave", lambda t: analyze_one_youtube_match(t, match_id, body))
                self._send_json({"ok": True, "task": task.as_dict()})
                return
            if path == "/api/youtube/manual-link":
                song_id = str(body.get("song_id") or "").strip()
                video_id = str(body.get("video_id") or "").strip()
                song = DB.get_song(song_id)
                video = DB.get_youtube_video(video_id)
                if not song or not video:
                    raise RuntimeError("Suno pesma ili YouTube video nisu pronađeni.")
                payload = {
                    "match_type": "owned_publication" if int(video.get("is_owned_channel") or 0) == 1 else "external_candidate",
                    "score": 100, "reason": "Ručno povezano sa Suno originalom.",
                }
                linked = DB.upsert_youtube_match(song_id, video_id, payload)
                linked = DB.update_youtube_match(int(linked["id"]), {"manual_link": 1}) or linked
                self._send_json({"ok": True, "match": linked, "message": "YouTube video je ručno povezan sa Suno originalom. Pokreni audio proveru za potvrdu kompletnosti."})
                return
            if path == "/api/youtube/audio-report/export":
                package = create_youtube_audio_report()
                self._send_json({"ok": True, "path": str(package), "download_url": "/api/export/download?path=" + urllib.parse.quote(str(package)), "message": "YouTube ↔ Suno audio izveštaj je napravljen."})
                return
            if path == "/api/youtube/coverage-report/export":
                package = create_youtube_coverage_report()
                self._send_json({"ok": True, "path": str(package), "download_url": "/api/export/download?path=" + urllib.parse.quote(str(package)), "message": "Zbirni izveštaj kompletnosti je napravljen."})
                return
            if path == "/api/youtube/audio-cache/cleanup":
                result = cleanup_youtube_audio_cache(max_age_days=max(1, int(body.get("days") or 30)), max_size_gb=max(1.0, float(body.get("gb") or 10)))
                self._send_json({"ok": True, **result, "message": f"Očišćeno audio-keš fajlova: {result.get('removed', 0)}."})
                return
            if path == "/api/tools/ytdlp/install":
                force_update = bool(body.get("force_update"))
                result = ensure_ytdlp(force_update=force_update)
                deno = ensure_deno(force_update=force_update)
                self._send_json({"ok": True, **result, "deno": deno, "message": "yt-dlp i Deno su spremni za YouTube pretragu i audio analizu."})
                return
            if path == "/api/youtube/calendar/export":
                package = export_youtube_publication_calendar()
                self._send_json({"ok": True, "path": str(package), "download_url": "/api/export/download?path=" + urllib.parse.quote(str(package)), "message": "CSV sa datumima objava je napravljen."})
                return
            if path == "/api/youtube/evidence":
                match_id = int(body.get("id") or 0)
                owner_name = str(body.get("owner_name") or DB.get_setting("copyright_owner_name", ""))
                package = create_youtube_evidence_package(match_id, owner_name)
                self._send_json({"ok": True, "path": str(package), "download_url": "/api/export/download?path=" + urllib.parse.quote(str(package)), "message": "Dokazni paket je napravljen za ručnu proveru."})
                return
            if path == "/api/youtube/match/update":
                match_id = int(body.get("id") or 0)
                fields = body.get("fields") if isinstance(body.get("fields"), dict) else {}
                updated = DB.update_youtube_match(match_id, fields)
                if not updated:
                    raise RuntimeError("YouTube slučaj nije pronađen.")
                self._send_json({"ok": True, "match": updated})
                return
            if path == "/api/youtube/open-url":
                open_external_url(str(body.get("url") or ""))
                self._send_json({"ok": True})
                return
            if path == "/api/sync/check-new":
                task = start_task("check_new", "Provera novih Suno pesama", lambda t: check_new_songs(t, body), persistent_payload=dict(body))
                self._send_json({"ok": True, "task": task.as_dict()})
                return
            if path == "/api/sync/start":
                if DB.get_setting("auto_backup_before_sync", "0") == "1":
                    try:
                        stamp = datetime.now().strftime("%Y%m%d-%H%M%S-%f")
                        backup_path = _unique_path(EXPORT_DIR / f"auto-backup-pre-sync-{stamp}.db")
                        DB.backup_to(backup_path)
                        runtime_log(f"Automatski backup pre sinhronizacije: {backup_path}")
                    except Exception as exc:
                        runtime_log(f"Automatski backup nije uspeo: {exc}", "warning")
                task = start_task("sync", "Sinhronizacija Suno biblioteke", lambda t: sync_library(t, body), persistent_payload=dict(body))
                self._send_json({"ok": True, "task": task.as_dict()})
                return
            if path == "/api/import/urls":
                text = str(body.get("urls") or "")
                urls = [line.strip() for line in text.replace(",", "\n").splitlines() if line.strip()]
                task = start_task("import_urls", "Uvoz Suno linkova", lambda t: import_urls(t, urls))
                self._send_json({"ok": True, "task": task.as_dict()})
                return
            if path == "/api/import/folder":
                folder = str(body.get("folder") or "")
                task = start_task("import_folder", "Uvoz i trajno pamćenje lokalnog foldera", lambda t: import_local_folder(t, folder))
                self._send_json({"ok": True, "task": task.as_dict()})
                return
            if path == "/api/import/rescan-watched":
                task = start_task("rescan_watched_folders", "Provera svih zapamćenih foldera", rescan_watched_folders)
                self._send_json({"ok": True, "task": task.as_dict()})
                return
            if path == "/api/import/watched-folder/update":
                folder = str(body.get("path") or "").strip()
                if not folder:
                    raise RuntimeError("Folder nije naveden.")
                if bool(body.get("remove")):
                    DB.remove_watched_folder(folder)
                elif "enabled" in body:
                    DB.set_watched_folder_enabled(folder, bool(body.get("enabled")))
                self._send_json({"ok": True, "folders": DB.list_watched_folders()})
                return
            if path == "/api/download/start":
                ids = body.get("ids") if isinstance(body.get("ids"), list) else []
                ids = [str(x) for x in ids]
                options = body.get("options") if isinstance(body.get("options"), dict) else {}
                task = start_task("download", "Preuzimanje pesama", lambda t: download_songs(t, ids, options), persistent_payload={"ids": ids, "options": options})
                self._send_json({"ok": True, "task": task.as_dict()})
                return

            if path == "/api/choose-folder":
                selected = choose_folder_dialog(str(body.get("initial") or get_download_dir()))
                self._send_json({"ok": True, "path": selected, "cancelled": not bool(selected)})
                return
            if path == "/api/choose-file":
                selected = choose_file_dialog(str(body.get("initial") or EXPORT_DIR), str(body.get("filter") or "Backup ZIP (*.zip)|*.zip|SQLite baza (*.db)|*.db|Svi fajlovi (*.*)|*.*"))
                self._send_json({"ok": True, "path": selected, "cancelled": not bool(selected)})
                return
            if path == "/api/choose-files":
                selected = choose_files_dialog(str(body.get("initial") or EXPORT_DIR), str(body.get("filter") or "Svi fajlovi (*.*)|*.*"))
                self._send_json({"ok": True, "paths": selected, "cancelled": not bool(selected)})
                return
            if path == "/api/save-to-folder":
                ids = [str(x) for x in (body.get("ids") or []) if str(x)]
                target = str(body.get("target_folder") or "").strip()
                options = body.get("options") if isinstance(body.get("options"), dict) else {}
                if not target:
                    raise RuntimeError("Izaberi folder na računaru.")
                task = start_task("save_folder", "Čuvanje pesama u izabrani folder", lambda t: save_songs_to_folder(t, ids, target, options))
                self._send_json({"ok": True, "task": task.as_dict()})
                return
            if path == "/api/audio-tools/install":
                def install_runner(t: TaskState) -> None:
                    t.total = 100
                    def prog(message: str, percent: int) -> None:
                        t.set_progress(percent, 100, message)
                        t.log(message)
                    ensure_ffmpeg(prog)
                    t.finish("FFmpeg audio alati su spremni.")
                task = start_task("audio_tools", "Priprema audio alata", install_runner)
                self._send_json({"ok": True, "task": task.as_dict()})
                return
            if path == "/api/audio/process":
                song_id = str(body.get("id") or "")
                options = body.get("options") if isinstance(body.get("options"), dict) else {}
                task = start_task("audio_process", "Skraćivanje i obrada pesme", lambda t: process_audio_task(t, song_id, options), persistent_payload={"id": song_id, "options": options})
                self._send_json({"ok": True, "task": task.as_dict()})
                return
            if path == "/api/audio/process-batch":
                song_ids = [str(x) for x in (body.get("ids") or []) if str(x)]
                options = body.get("options") if isinstance(body.get("options"), dict) else {}
                task = start_task("audio_process_batch", "Masovna audio obrada", lambda t: process_audio_batch_task(t, song_ids, options), persistent_payload={"ids": song_ids, "options": options})
                self._send_json({"ok": True, "task": task.as_dict()})
                return
            if path == "/api/audio/quick-preset":
                ids = [str(x) for x in (body.get("ids") or []) if str(x)]
                preset = str(body.get("preset") or "shorts30")
                task = start_task("audio_quick_preset", "Brza masovna audio radnja", lambda t: quick_audio_preset_task(t, ids, preset))
                self._send_json({"ok": True, "task": task.as_dict()})
                return
            if path == "/api/song/write-tags":
                song_id = str(body.get("id") or "")
                result = write_song_tags(song_id, backup=bool(body.get("backup", True)))
                self._send_json({"ok": True, **result, "song": DB.get_song(song_id)})
                return
            if path == "/api/song/rename-files":
                song_id = str(body.get("id") or "")
                self._send_json({"ok": True, "song": rename_song_files(song_id)})
                return
            if path == "/api/derived/delete":
                file_id = int(body.get("file_id") or 0)
                DB.delete_derived_file(file_id, delete_from_disk=bool(body.get("delete_from_disk", True)))
                self._send_json({"ok": True})
                return
            if path == "/api/collection/create":
                collection = DB.create_collection(str(body.get("name") or ""), str(body.get("color") or "#7c3aed"))
                self._send_json({"ok": True, "collection": collection, "collections": DB.list_collections()})
                return
            if path == "/api/collection/delete":
                DB.delete_collection(int(body.get("collection_id") or 0))
                self._send_json({"ok": True, "collections": DB.list_collections()})
                return
            if path == "/api/collection/add":
                count = DB.add_songs_to_collection(int(body.get("collection_id") or 0), body.get("ids") or [])
                self._send_json({"ok": True, "count": count, "collections": DB.list_collections()})
                return
            if path == "/api/collection/remove":
                count = DB.remove_songs_from_collection(int(body.get("collection_id") or 0), body.get("ids") or [])
                self._send_json({"ok": True, "count": count, "collections": DB.list_collections()})
                return
            if path == "/api/song/collections":
                song_id = str(body.get("id") or "")
                collection_ids = body.get("collection_ids") if isinstance(body.get("collection_ids"), list) else []
                DB.set_song_collections(song_id, [int(x) for x in collection_ids])
                self._send_json({"ok": True, "song": DB.get_song(song_id), "collections": DB.list_collections()})
                return
            if path == "/api/smart-library/preview":
                rules = body.get("rules") if isinstance(body.get("rules"), list) else []
                match_mode = str(body.get("match_mode") or "all")
                matched = match_smart_collection(DB.export_rows(), rules, match_mode)
                preview = [{"id": s.get("id"), "title": s.get("title"), "display_name": s.get("display_name"), "duration": s.get("duration")} for s in matched[:200]]
                self._send_json({"ok": True, "count": len(matched), "songs": preview, "truncated": len(matched) > 200})
                return
            if path == "/api/smart-library/save":
                name = str(body.get("name") or "").strip()
                if not name:
                    raise RuntimeError("Naziv pametne kolekcije je prazan.")
                rules = body.get("rules") if isinstance(body.get("rules"), list) else []
                if not rules:
                    raise RuntimeError("Dodaj bar jedno pravilo.")
                match_mode = str(body.get("match_mode") or "all")
                collection_id = int(body.get("id") or 0) or None
                saved = DB.save_smart_collection(name, match_mode, rules, collection_id)
                self._send_json({"ok": True, "collection": saved})
                return
            if path == "/api/smart-library/delete":
                deleted = DB.delete_smart_collection(int(body.get("id") or 0))
                self._send_json({"ok": True, "deleted": deleted})
                return
            if path == "/api/version-lab/create":
                ids = [str(x) for x in (body.get("ids") or []) if str(x)]
                group = DB.create_version_group(str(body.get("name") or ""), ids)
                self._send_json({"ok": True, "group": group}); return
            if path == "/api/version-lab/add":
                ids = [str(x) for x in (body.get("ids") or []) if str(x)]
                added = DB.add_to_version_group(int(body.get("group_id") or 0), ids)
                self._send_json({"ok": True, "added": added, "group": DB.get_version_group(int(body.get("group_id") or 0))}); return
            if path == "/api/version-lab/remove":
                removed = DB.remove_from_version_group(int(body.get("group_id") or 0), str(body.get("song_id") or ""))
                self._send_json({"ok": True, "removed": removed, "group": DB.get_version_group(int(body.get("group_id") or 0))}); return
            if path == "/api/version-lab/master":
                DB.set_version_master(int(body.get("group_id") or 0), str(body.get("song_id") or ""), bool(body.get("is_master", True)))
                self._send_json({"ok": True, "group": DB.get_version_group(int(body.get("group_id") or 0))}); return
            if path == "/api/version-lab/delete":
                deleted = DB.delete_version_group(int(body.get("id") or 0))
                self._send_json({"ok": True, "deleted": deleted}); return
            if path == "/api/release/build":
                payload = dict(body)
                task = start_task("release_build", "Pravljenje release paketa", lambda t: release_build_task(t, payload), persistent_payload=payload)
                self._send_json({"ok": True, "task": task.as_dict()}); return
            if path == "/api/bulk/update":
                ids = [str(x) for x in (body.get("ids") or []) if str(x)]
                fields = body.get("fields") if isinstance(body.get("fields"), dict) else {}
                count = DB.bulk_update_user_fields(ids, fields)
                self._send_json({"ok": True, "count": count})
                return
            if path == "/api/bulk/tags":
                ids = [str(x) for x in (body.get("ids") or []) if str(x)]
                mode = str(body.get("mode") or "append")
                if mode == "clear":
                    count = DB.bulk_update_user_fields(ids, {"custom_tags": ""})
                else:
                    count = DB.append_custom_tags(ids, str(body.get("tags") or ""))
                self._send_json({"ok": True, "count": count})
                return
            if path == "/api/bulk/published":
                ids = [str(x) for x in (body.get("ids") or []) if str(x)]
                published = bool(body.get("published", True))
                fields = {"youtube_published_at": now_iso()[:10] if published else "", "youtube_url": "" if not published else str(body.get("youtube_url") or "")}
                count = DB.bulk_update_user_fields(ids, fields)
                collection = DB.get_collection_by_slug("objavljene-na-kanalu")
                if collection:
                    if published:
                        DB.add_songs_to_collection(int(collection["id"]), ids)
                    else:
                        DB.remove_songs_from_collection(int(collection["id"]), ids)
                self._send_json({"ok": True, "count": count, "collections": DB.list_collections()})
                return
            if path == "/api/bulk/collection":
                ids = [str(x) for x in (body.get("ids") or []) if str(x)]
                slug = str(body.get("slug") or "")
                collection = DB.get_collection_by_slug(slug)
                if not collection:
                    raise RuntimeError("Traženi folder ne postoji.")
                count = DB.add_songs_to_collection(int(collection["id"]), ids)
                self._send_json({"ok": True, "count": count, "collections": DB.list_collections()})
                return
            if path == "/api/task/cancel":
                with STATE_LOCK:
                    task = ACTIVE_TASK
                if task:
                    task.cancel_event.set()
                    task.log("Zatraženo je zaustavljanje. Završavam trenutni fajl...", "warning")
                self._send_json({"ok": True, "message": "Zaustavljanje je zatraženo."})
                return
            if path == "/api/song/update":
                song_id = str(body.get("id") or "")
                fields = body.get("fields") if isinstance(body.get("fields"), dict) else {}
                DB.update_song_user_fields(song_id, fields)
                if str(fields.get("youtube_url") or "").strip() or str(fields.get("youtube_published_at") or "").strip():
                    published = DB.get_collection_by_slug("objavljene-na-kanalu")
                    if published:
                        DB.add_songs_to_collection(int(published["id"]), [song_id])
                self._send_json({"ok": True, "song": DB.get_song(song_id), "collections": DB.list_collections()})
                return
            if path == "/api/song/reset-user-edits":
                song_id = str(body.get("id") or "")
                fields = body.get("fields") if isinstance(body.get("fields"), list) else ["title", "display_name", "lyrics"]
                DB.reset_user_locks(song_id, [str(x) for x in fields])
                if SESSION_TOKEN and song_id and not song_id.startswith("local-"):
                    detail = get_client().get_clip(song_id)
                    DB.upsert_song(detail)
                self._send_json({"ok": True, "song": DB.get_song(song_id)})
                return
            if path == "/api/song/delete":
                song_id = str(body.get("id") or "")
                master_groups = DB.master_group_names_for_song(song_id)
                if master_groups and not bool(body.get("force_master_delete", False)):
                    raise RuntimeError(
                        "Ova pesma je označena kao MASTER verzija u Version Lab-u (" + ", ".join(master_groups) + "). "
                        "Ako si siguran/sigurna, potvrdi brisanje ponovo (force_master_delete)."
                    )
                deleted = DB.delete_song(song_id, delete_files=bool(body.get("delete_files", False)))
                if not deleted:
                    raise RuntimeError("Pesma nije pronađena u biblioteci.")
                self._send_json({"ok": True, "message": "Pesma je uklonjena iz lokalne biblioteke."})
                return
            if path == "/api/library/repair":
                self._send_json({"ok": True, "health": DB.local_file_health(repair=True)})
                return
            if path == "/api/restore":
                with STATE_LOCK:
                    if ACTIVE_TASK and ACTIVE_TASK.status == "running":
                        raise RuntimeError("Sačekaj da se trenutni posao završi pre vraćanja backup-a.")
                result = restore_backup_file(str(body.get("path") or ""))
                self._send_json({"ok": True, **result})
                return
            if path == "/api/settings":
                if "download_dir" in body:
                    value = str(body.get("download_dir") or "").strip()
                    if value:
                        Path(value).expanduser().mkdir(parents=True, exist_ok=True)
                        DB.set_setting("download_dir", value)
                if "theme" in body:
                    DB.set_setting("theme", str(body.get("theme") or "default"))
                if "auto_update_enabled" in body:
                    DB.set_setting("auto_update_enabled", "1" if body.get("auto_update_enabled") else "0")
                if "auto_update_download" in body:
                    DB.set_setting("auto_update_download", "1" if body.get("auto_update_download") else "0")
                if "youtube_processed_dir" in body:
                    value = str(body.get("youtube_processed_dir") or "").strip()
                    if value:
                        Path(value).expanduser().mkdir(parents=True, exist_ok=True)
                    DB.set_setting("youtube_processed_dir", value)
                if "auto_check_minutes" in body:
                    DB.set_setting("auto_check_minutes", str(max(0, min(int(body.get("auto_check_minutes") or 0), 1440))))
                if "auto_backup_before_sync" in body:
                    DB.set_setting("auto_backup_before_sync", "1" if body.get("auto_backup_before_sync") else "0")
                if "suno_keepalive_enabled" in body:
                    DB.set_setting("suno_keepalive_enabled", "1" if body.get("suno_keepalive_enabled") else "0")
                if "suno_keepalive_minutes" in body:
                    DB.set_setting("suno_keepalive_minutes", str(max(2, min(int(body.get("suno_keepalive_minutes") or 4), 30))))
                if "suno_auto_reopen_browser" in body:
                    DB.set_setting("suno_auto_reopen_browser", "1" if body.get("suno_auto_reopen_browser") else "0")
                if "youtube_api_key" in body:
                    DB.set_setting("youtube_api_key", str(body.get("youtube_api_key") or "").strip())
                if "copyright_owner_name" in body:
                    DB.set_setting("copyright_owner_name", str(body.get("copyright_owner_name") or "").strip())
                if "youtube_cookies_browser" in body:
                    value = str(body.get("youtube_cookies_browser") or "none").strip().lower()
                    if value not in {"none", "chrome", "edge", "brave", "firefox", "opera", "vivaldi"}:
                        raise RuntimeError("Nepodržan browser za YouTube kolačiće.")
                    DB.set_setting("youtube_cookies_browser", value)
                self._send_json({"ok": True, "download_dir": str(get_download_dir()), "youtube_processed_dir": str(get_youtube_processed_dir()), "auto_update_enabled": auto_update_enabled(), "auto_update_download": auto_update_download_enabled(), "update_manifest_url": get_update_manifest_url(), "theme": DB.get_setting("theme", "default"), "auto_check_minutes": int(DB.get_setting("auto_check_minutes", "0") or 0), "auto_backup_before_sync": DB.get_setting("auto_backup_before_sync", "0") == "1", "suno_keepalive_enabled": _suno_keepalive_enabled(), "suno_keepalive_minutes": _suno_keepalive_minutes(), "suno_auto_reopen_browser": _suno_auto_reopen_browser(), "suno_session": suno_session_public_state(), "has_youtube_api_key": bool(get_youtube_api_key()), "youtube_oauth": YOUTUBE_OAUTH.status(), "copyright_owner_name": DB.get_setting("copyright_owner_name", ""), "youtube_cookies_browser": DB.get_setting("youtube_cookies_browser", "none")})
                return
            if path == "/api/open-folder":
                target = Path(str(body.get("path") or get_download_dir()))
                if not target.exists():
                    target.mkdir(parents=True, exist_ok=True)
                if os.name == "nt":
                    os.startfile(str(target))  # type: ignore[attr-defined]
                elif sys.platform == "darwin":
                    subprocess.Popen(["open", str(target)])
                else:
                    subprocess.Popen(["xdg-open", str(target)])
                self._send_json({"ok": True})
                return
            if path == "/api/export/m3u":
                ids = body.get("ids") if isinstance(body.get("ids"), list) else None
                p = export_m3u(ids)
                self._send_json({"ok": True, "path": str(p), "download_url": "/api/export/download?path=" + urllib.parse.quote(str(p))})
                return
            if path == "/api/export/lyrics":
                ids = body.get("ids") if isinstance(body.get("ids"), list) else None
                p = export_lyrics_bundle(ids)
                self._send_json({"ok": True, "path": str(p), "download_url": "/api/export/download?path=" + urllib.parse.quote(str(p))})
                return
            if path == "/api/export":
                ids = body.get("ids") if isinstance(body.get("ids"), list) else None
                fmt = str(body.get("format") or "json").lower()
                p = export_library(ids, fmt)
                self._send_json({"ok": True, "path": str(p), "download_url": "/api/export/download?path=" + urllib.parse.quote(str(p))})
                return
            if path == "/api/backup":
                stamp = datetime.now().strftime("%Y%m%d-%H%M%S-%f")
                p = _unique_path(EXPORT_DIR / f"Suno-Pesme-Studio-backup-{stamp}.zip")
                with tempfile.TemporaryDirectory(prefix="suno_backup_") as tmp_dir:
                    snapshot = Path(tmp_dir) / "suno_biblioteka.db"
                    DB.backup_to(snapshot)
                    rows = DB.export_rows()
                    with zipfile.ZipFile(p, "w", compression=zipfile.ZIP_DEFLATED, allowZip64=True) as z:
                        z.write(snapshot, arcname="data/suno_biblioteka.db")
                        z.writestr("biblioteka.json", json.dumps(rows, ensure_ascii=False, indent=2))
                        z.writestr("README_RESTORE.txt", "Backup sadrži sigurnu SQLite kopiju baze. Audio fajlovi nisu uključeni zbog veličine. Vrati ga kroz Podešavanja > Vrati backup.\n")
                self._send_json({"ok": True, "path": str(p), "download_url": "/api/export/download?path=" + urllib.parse.quote(str(p))})
                return
            if path == "/api/backup/full":
                p = full_backup()
                self._send_json({"ok": True, "path": str(p), "download_url": "/api/export/download?path=" + urllib.parse.quote(str(p))})
                return
            if path == "/api/self-test":
                report = run_self_test(include_audio=bool(body.get("include_audio", True)))
                self._send_json({"ok": True, "report": report})
                return
            if path == "/api/diagnostics/export":
                package = create_diagnostics_package(include_audio_test=bool(body.get("include_audio", True)))
                self._send_json({"ok": True, "path": str(package), "download_url": "/api/export/download?path=" + urllib.parse.quote(str(package)), "message": "Dijagnostički paket je napravljen bez prijavnih tokena i vrednosti API ključa."})
                return
            if path == "/api/logs/clear":
                count = DB.clear_logs()
                self._send_json({"ok": True, "count": count})
                return
            if path == "/api/shutdown":
                if os.environ.get("SUNO_WATCHDOG") == "1":
                    try: WATCHDOG_STOP_FILE.write_text(now_iso(), encoding="utf-8")
                    except Exception as exc: runtime_log(f"Watchdog stop marker: {exc}", "warning")
                self._send_json({"ok": True, "message": "Program se zatvara."})
                threading.Thread(target=self.server.shutdown, daemon=True).start()
                return
            self._send_json({"ok": False, "message": "Nepoznata komanda."}, 404)
        except RequestBodyError as exc:
            self._send_json({"ok": False, "message": str(exc)}, 400)
        except RuntimeError as exc:
            self._send_json({"ok": False, "message": str(exc)}, 409)
        except Exception as exc:
            DB.add_log("error", f"{path}: {exc}")
            self._send_json({"ok": False, "message": str(exc)}, 500)


class AppHTTPServer(ThreadingHTTPServer):
    daemon_threads = True
    # SO_REUSEADDR is intentionally left off on Windows. Combined with
    # Windows' looser reuse semantics, SO_REUSEADDR there can let a second
    # process silently bind the same 127.0.0.1:8765 address instead of
    # raising the clear "port already in use" OSError main() below already
    # handles -- turning a real startup failure (e.g. a previous crashed
    # instance still holding the port) into unpredictable request routing
    # between two processes, which is consistent with launcher/main.go's
    # /api/health check timing out with no obvious cause in the log.
    # SO_EXCLUSIVEADDRUSE (set in server_bind below) is the Windows-
    # documented replacement: still allows rebinding after a clean
    # shutdown, but fails loudly if another process actually owns the port.
    allow_reuse_address = os.name != "nt"

    def server_bind(self) -> None:
        if os.name == "nt":
            try:
                self.socket.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
            except (AttributeError, OSError):
                pass
        super().server_bind()


def main() -> None:
    global SESSION_KEEPALIVE_THREAD, SCHEDULER_THREAD, UPDATE_THREAD
    port = SERVER_PORT
    url = f"http://127.0.0.1:{port}/"
    runtime_log(f"Pokretanje Suno Pesme Studio v{APP_VERSION} na {url}")
    try:
        server = AppHTTPServer(("127.0.0.1", port), Handler)
    except OSError as exc:
        runtime_log(f"Port {port} nije dostupan: {exc}", "error")
        raise RuntimeError(f"Lokalni port {port} je zauzet. Zatvori staru verziju programa i pokreni ponovo.") from exc
    print("=" * 70)
    print(f" SUNO PESME STUDIO v{APP_VERSION}")
    print(f" Program radi na: {url}")
    print(f" Log: {RUNTIME_LOG}")
    print("=" * 70)
    SESSION_KEEPALIVE_STOP.clear()
    SESSION_KEEPALIVE_THREAD = threading.Thread(target=suno_session_keepalive_loop, daemon=True, name="suno-session-keeper")
    SESSION_KEEPALIVE_THREAD.start()
    SCHEDULER_STOP.clear()
    SCHEDULER_THREAD = threading.Thread(target=scheduler_loop, daemon=True, name="persistent-scheduler")
    SCHEDULER_THREAD.start()
    UPDATE_STOP.clear()
    UPDATE_THREAD = threading.Thread(target=update_check_loop, daemon=True, name="auto-update-check")
    UPDATE_THREAD.start()
    threading.Thread(
        target=fingerprint_index_backfill_startup, daemon=True, name="fingerprint-index-backfill"
    ).start()
    if os.environ.get("SUNO_AUTO_OPEN", "0") == "1":
        threading.Timer(1.0, lambda: webbrowser.open(url)).start()
    try:
        server.serve_forever(poll_interval=0.25)
    except KeyboardInterrupt:
        pass
    except Exception as exc:
        runtime_log(traceback.format_exc(), "error")
        raise
    finally:
        SESSION_KEEPALIVE_STOP.set()
        SCHEDULER_STOP.set()
        UPDATE_STOP.set()
        server.server_close()
        runtime_log("Lokalni server je zaustavljen.")


if __name__ == "__main__":
    main()
