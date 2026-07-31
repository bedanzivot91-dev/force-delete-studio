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
from audio_tools import AudioCancelled, ensure_ffmpeg, ffmpeg_path, ffprobe_path, probe_audio, process_audio, waveform_peaks, status as audio_tools_status
from id3 import embed_mp3_metadata
from suno_client import SunoAPIError, SunoClient, extract_items, format_lrc, format_srt, sanitize_filename
from suno_compat import validate_fixture
from youtube_tools import (YouTubeAPIError, resolve_channel, list_channel_videos, search_videos, build_search_queries, match_song_to_video, contact_message, google_search_url, bing_search_url, youtube_search_url)
from youtube_oauth import YouTubeOAuthError, YouTubeOAuthManager
from audio_match import (
    AudioMatchCancelled, AudioMatchError, ALGORITHM_VERSION as AUDIO_MATCH_VERSION,
    analyze_audio_pair, closest_duration_candidates, compare_signatures, cleanup_youtube_audio_cache,
    download_youtube_audio, ensure_ytdlp, ensure_deno, search_youtube_with_ytdlp, extract_signature, inspect_youtube_video, pack_signature, source_identity,
    unpack_signature, ytdlp_status, ytdlp_path,
)
from advanced_features import (
    analyze_audio_quality, check_update, compare_lyrics_transcript, create_cloud_backup, restore_cloud_backup,
    create_incremental_backup, download_update, install_ai_plugin, list_incremental_backups, load_subtitle_cues,
    plugin_status, relocate_missing_files, restore_incremental_item, run_stem_separation,
    run_transcription, save_subtitle_files,
)
from security_lock import AppSecurity
from music_recognition import MusicRecognitionError, decode_audio_base64, recognize_audd
from v3_features import (
    align_original_lyrics, create_program_snapshot, create_proof_package, create_youtube_package, duplicate_detection,
    library_integrity_scan, organize_library, panako_index, panako_query, panako_status,
    render_lyric_video, render_short_clip, sha256_file, storage_report as v3_storage_report, cleanup_storage,
    suggest_short_clips, suggest_teaser_clips, teaser_clip_from_text, system_preflight, youtube_metadata, youtube_resumable_upload,
)
import song_finder


APP_VERSION = "3.3.2"
ROOT = Path(__file__).resolve().parent.parent
WEB_DIR = ROOT / "app" / "web"
USER_DATA_ROOT = Path(os.environ.get("SUNO_STUDIO_USER_DIR") or ROOT).expanduser().resolve()
DATA_DIR = Path(os.environ.get("SUNO_STUDIO_DATA_DIR") or (USER_DATA_ROOT / "data")).expanduser().resolve()
DEFAULT_DOWNLOAD_DIR = Path(os.environ.get("SUNO_STUDIO_DOWNLOAD_DIR") or (USER_DATA_ROOT / "Preuzete_pesme")).expanduser().resolve()
EXPORT_DIR = Path(os.environ.get("SUNO_STUDIO_EXPORT_DIR") or (USER_DATA_ROOT / "Izvoz")).expanduser().resolve()
PUBLISHED_DIR = Path(os.environ.get("SUNO_STUDIO_PUBLISHED_DIR") or (USER_DATA_ROOT / "Objavljene_pesme")).expanduser().resolve()
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



def copy_song_to_published_folder(song: dict[str, Any], video: dict[str, Any] | None = None) -> dict[str, Any]:
    target_root = PUBLISHED_DIR
    target_root.mkdir(parents=True, exist_ok=True)
    title = sanitize_filename(str(song.get("title") or song.get("display_name") or song.get("id") or "pesma"))
    copied = []
    for key in ("local_audio", "local_wav", "local_video", "local_cover", "local_lyrics", "local_lrc", "local_srt"):
        raw = str(song.get(key) or "").strip()
        if not raw:
            continue
        src = Path(raw)
        if not src.exists() or not src.is_file():
            continue
        dst = target_root / f"{title}{src.suffix.lower()}"
        if dst.exists() and dst.resolve() != src.resolve():
            dst = target_root / f"{title}-{str(song.get('id') or '')[:8]}{src.suffix.lower()}"
        if not dst.exists() or src.stat().st_mtime_ns != dst.stat().st_mtime_ns or src.stat().st_size != dst.stat().st_size:
            shutil.copy2(src, dst)
        copied.append(str(dst))
    manifest = {
        "song_id": str(song.get("id") or ""),
        "title": str(song.get("title") or ""),
        "youtube_url": str((video or {}).get("url") or (video or {}).get("video_url") or song.get("youtube_url") or ""),
        "published_at": str((video or {}).get("published_at") or song.get("youtube_published_at") or ""),
        "copied_files": copied,
        "updated_at": now_iso(),
    }
    (target_root / f"{title}-{str(song.get('id') or '')[:8]}.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    return {"folder": str(target_root), "copied": copied}

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


def scan_owned_youtube_channels(task: TaskState, options: dict[str, Any]) -> None:
    channels = [c for c in DB.list_youtube_channels() if int(c.get("is_owned") or 0) == 1]
    if not channels:
        raise RuntimeError("Dodaj najmanje jedan svoj YouTube kanal u Pametnim alatima.")
    max_pages = max(1, min(int(options.get("max_pages") or 20), 100))
    include_private_unlisted = bool(options.get("include_private_unlisted", True))
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
                videos = list_channel_videos(channel, api_key=api_key, access_token=access_token, max_pages=max_pages)
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
                DB.upsert_youtube_match(str(song["id"]), str(video["video_id"]), match)
                local_count += 1
                candidates += 1
                if not is_owned:
                    external_count += 1
                    if suspicious_collection:
                        DB.add_songs_to_collection(int(suspicious_collection["id"]), [str(song["id"])])
                if found_collection:
                    DB.add_songs_to_collection(int(found_collection["id"]), [str(song["id"])])
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


def _metadata_candidates_for_video(
    video: dict[str, Any], songs: list[dict[str, Any]], owned_ids: set[str], limit: int = 12, deep: bool = False,
) -> list[tuple[dict[str, Any], dict[str, Any]]]:
    scored: list[tuple[float, dict[str, Any], dict[str, Any]]] = []
    for song in songs:
        metadata_match = match_song_to_video(song, video, owned_ids)
        scored.append((float(metadata_match.get("score") or 0), song, metadata_match))
    scored.sort(key=lambda item: item[0], reverse=True)
    selected: list[tuple[dict[str, Any], dict[str, Any]]] = []
    used: set[str] = set()
    for score, song, metadata_match in scored[:max(4, limit)]:
        sid = str(song.get("id") or "")
        if sid and sid not in used:
            selected.append((song, metadata_match))
            used.add(sid)
    for song in closest_duration_candidates(songs, float(video.get("duration") or 0), limit=max(4, limit // 2)):
        sid = str(song.get("id") or "")
        if sid and sid not in used:
            selected.append((song, match_song_to_video(song, video, owned_ids)))
            used.add(sid)
    if deep and len(songs) <= 120:
        for score, song, metadata_match in scored:
            sid = str(song.get("id") or "")
            if sid and sid not in used:
                selected.append((song, metadata_match))
                used.add(sid)
    return selected[:max(limit, 120 if deep and len(songs) <= 120 else limit)]


def _apply_youtube_completeness_collection(song_id: str, status: str = "") -> None:
    """Postavi najbolji ukupni YouTube status pesme preko svih povezanih kanala.

    Jedna pesma može imati ceo video na jednom kanalu i samo Shorts isečak na drugom.
    Folder zato prati najbolji potvrđeni rezultat, dok matrica zadržava detalje po kanalu.
    """
    mapping = {
        "complete": "youtube-kompletno-objavljene",
        "almost_complete": "youtube-kompletno-objavljene",
        "partial": "youtube-delimično-objavljene",
        "short_clip": "youtube-kratki-isečci",
        "different_audio": "youtube-audio-provera",
        "": "youtube-audio-provera",
    }
    rank = {"complete": 5, "almost_complete": 4, "partial": 3, "short_clip": 2, "different_audio": 1, "": 0}
    best_status = str(status or "")
    try:
        for row in DB.list_youtube_audio_analyses(limit=10000):
            if str(row.get("song_id") or "") != str(song_id):
                continue
            candidate = str(row.get("completeness_status") or "")
            if rank.get(candidate, 0) > rank.get(best_status, 0):
                best_status = candidate
    except Exception as exc:
        runtime_log(f"Nije izračunat zbirni YouTube status za {song_id}: {exc}", "warning")
    all_slugs = {
        "youtube-kompletno-objavljene", "youtube-delimično-objavljene",
        "youtube-kratki-isečci", "youtube-audio-provera", "youtube-nisu-pronađene",
    }
    target_slug = mapping.get(best_status, "youtube-audio-provera")
    for slug in all_slugs:
        collection = DB.get_collection_by_slug(slug)
        if not collection:
            continue
        if slug == target_slug:
            DB.add_songs_to_collection(int(collection["id"]), [song_id])
        else:
            DB.remove_songs_from_collection(int(collection["id"]), [song_id])


def _range_overlap_ratio(a0: float, a1: float, b0: float, b1: float) -> float:
    overlap = max(0.0, min(a1, b1) - max(a0, b0))
    base = min(max(0.0, a1 - a0), max(0.0, b1 - b0))
    return overlap / base if base > 1e-9 else 0.0


def _classify_youtube_version(video: dict[str, Any], song: dict[str, Any], analysis: dict[str, Any], video_song_count: int = 1) -> str:
    title = re.sub(r"\s+", " ", str(video.get("title") or "").lower())
    duration = float(video.get("duration") or 0)
    status = str(analysis.get("completeness_status") or "")
    if video_song_count > 1 or any(word in title for word in ("mix", "playlist", "kompilacija", "album", "medley")):
        return "mix / više pesama"
    if any(word in title for word in ("remaster", "remastered", "remasterovana", "remasterovano")):
        return "remaster"
    if any(word in title for word in ("extended", "duža verzija", "duza verzija", "long version", "produžena", "produzena")):
        return "produžena verzija"
    if any(word in title for word in ("instrumental", "karaoke", "bez vokala")):
        return "instrumental"
    if any(word in title for word in ("lyric", "tekst pesme", "lyrics", "sa tekstom")):
        return "lyric video"
    if any(word in title for word in ("uživo", "uzivo", "live", "koncert")):
        return "live verzija"
    if any(word in title for word in ("acoustic", "akustična", "akusticna")):
        return "akustična verzija"
    if any(word in title for word in ("remix", "sped up", "slowed", "nightcore", "reverb")):
        return "izmenjena brzina / remix"
    if duration and duration <= 70:
        return "kratak klip / Shorts"
    if status == "complete":
        return "kompletna verzija"
    if status == "almost_complete":
        return "skoro kompletna verzija"
    if status in ("partial", "short_clip"):
        return "isečena verzija"
    return "nepoznata verzija"


def _analysis_recommendation(analysis: dict[str, Any], version_type: str, video_song_count: int = 1) -> str:
    status = str(analysis.get("completeness_status") or "")
    missing = analysis.get("missing_intervals") or []
    if video_song_count > 1:
        base = "Video sadrži više Suno pesama. Proveri mapu segmenata za svaku pesmu."
    elif status == "complete":
        base = "Kompletna pesma je audio-potvrđena. Nije potrebna nova puna objava."
    elif status == "almost_complete":
        base = "Skoro cela pesma je objavljena. Proveri nedostajući početak, kraj ili unutrašnju prazninu."
    elif status == "partial":
        base = "Objavljen je samo deo pesme. Preporuka: objavi kompletnu verziju ili jasno označi isečak."
    elif status == "short_clip":
        base = "Pronađen je samo kratak klip. Kompletna verzija nije potvrđena na ovom videu."
    else:
        base = "Audio nije dovoljno pouzdano povezan. Potrebna je ručna provera ili dublja analiza."
    if missing and status in ("almost_complete", "partial", "short_clip"):
        readable = ", ".join(f"{float(a):.1f}–{float(b):.1f} s" for a, b in missing[:5] if isinstance(a, (int, float)) and isinstance(b, (int, float)))
        if readable:
            base += f" Nedostajući delovi: {readable}."
    if version_type not in ("kompletna verzija", "nepoznata verzija"):
        base += f" Prepoznat tip: {version_type}."
    return base


def _analyse_video_against_songs(
    task: TaskState,
    video: dict[str, Any],
    songs: list[dict[str, Any]],
    owned_ids: set[str],
    options: dict[str, Any],
) -> dict[str, Any] | None:
    video_id = str(video.get("video_id") or "")
    video_url = str(video.get("url") or f"https://www.youtube.com/watch?v={video_id}")
    if not video_id:
        return None
    reuse_cache = bool(options.get("reuse_cache", True))
    force = bool(options.get("force", False))
    candidate_limit = max(4, min(int(options.get("candidate_limit") or 12), 40))
    deep = bool(options.get("deep", False))
    detect_multiple = bool(options.get("detect_multiple", True))
    max_songs_per_video = max(1, min(int(options.get("max_songs_per_video") or 6), 20))

    def dl_progress(message: str, percent: int) -> None:
        with task._lock:
            task.message = f"{video.get('title')}: {message}"

    cached_video = DB.get_youtube_video(video_id) or {}
    cached_path = Path(str(cached_video.get("audio_cache_path") or "")) if cached_video.get("audio_cache_path") else None
    if reuse_cache and cached_path and cached_path.exists() and cached_path.stat().st_size > 1024:
        youtube_audio = cached_path
    else:
        youtube_audio = download_youtube_audio(video_url, dl_progress, lambda: task.cancel_event.is_set(), reuse_cache=reuse_cache, cookie_browser=youtube_cookie_browser())
        DB.update_youtube_video_audio_cache(video_id, str(youtube_audio), _sha256(youtube_audio))
    video_signature = _signature_for_source("youtube", video_id, youtube_audio, task, str(video.get("title") or video_id), force=force)

    candidates = _metadata_candidates_for_video(video, songs, owned_ids, candidate_limit, deep)
    analysed: list[dict[str, Any]] = []
    skipped = 0
    for song, metadata_match in candidates:
        if task.cancel_event.is_set():
            raise AudioMatchCancelled("YouTube audio analiza je zaustavljena.")
        source = _song_audio_source_for_match(song)
        if source is None:
            skipped += 1
            continue
        try:
            signature = _signature_for_source("suno", str(song.get("id") or ""), source, task, str(song.get("title") or song.get("id")), force=force)
            analysis = compare_signatures(signature, video_signature)
        except Exception as exc:
            skipped += 1
            task.log(f"Nije analiziran Suno kandidat „{song.get('title')}“: {exc}", "warning")
            continue
        combined = float(analysis.get("audio_score") or 0) * 0.86 + float(metadata_match.get("score") or 0) * 0.14
        analysed.append({"song": song, "metadata": metadata_match, "analysis": analysis, "combined": combined})
    if not analysed:
        task.log(f"„{video.get('title')}“: nije pronađen Suno original; preskočeno kandidata bez audio izvora: {skipped}.", "warning")
        return None

    analysed.sort(key=lambda c: (float(c["combined"]), float(c["analysis"].get("coverage_percent") or 0), float(c["analysis"].get("matched_seconds") or 0)), reverse=True)
    primary = analysed[0]
    selected = [primary]
    if detect_multiple:
        for candidate in analysed[1:]:
            analysis = candidate["analysis"]
            if float(analysis.get("audio_score") or 0) < 62 or float(analysis.get("matched_seconds") or 0) < 18 or float(candidate.get("combined") or 0) < 58:
                continue
            c0, c1 = float(analysis.get("video_start") or 0), float(analysis.get("video_end") or 0)
            if c1 <= c0:
                continue
            overlaps = []
            for existing in selected:
                ea = existing["analysis"]
                overlaps.append(_range_overlap_ratio(c0, c1, float(ea.get("video_start") or 0), float(ea.get("video_end") or 0)))
            if overlaps and max(overlaps) >= 0.38:
                continue
            selected.append(candidate)
            if len(selected) >= max_songs_per_video:
                break

    video_song_count = len(selected)
    saved_results: list[dict[str, Any]] = []
    for position, candidate in enumerate(selected):
        song = candidate["song"]
        metadata_match = candidate["metadata"]
        analysis = dict(candidate["analysis"])
        analysis["metadata_score"] = float(metadata_match.get("score") or 0)
        analysis["combined_score"] = round(float(candidate["combined"]), 1)
        analysis["match_role"] = "primary" if position == 0 else "additional"
        analysis["video_song_count"] = video_song_count
        version_type = _classify_youtube_version(video, song, analysis, video_song_count)
        analysis["version_type"] = version_type
        analysis["recommendation"] = _analysis_recommendation(analysis, version_type, video_song_count)
        reason = f"{metadata_match.get('reason') or ''}; {analysis.get('reason') or ''}".strip("; ")
        if video_song_count > 1:
            reason += f"; video sadrži najmanje {video_song_count} prepoznate Suno pesme"
        match_payload = dict(metadata_match)
        match_payload.update({
            "match_type": "owned_publication",
            "score": round(max(float(metadata_match.get("score") or 0), float(candidate["combined"])), 1),
            "reason": reason,
        })
        DB.upsert_youtube_match(str(song["id"]), video_id, match_payload)
        result = DB.save_youtube_audio_analysis(str(song["id"]), video_id, analysis)
        saved_results.append(result)
        _apply_youtube_completeness_collection(str(song["id"]), str(analysis.get("completeness_status") or ""))
        found_collection = DB.get_collection_by_slug("youtube-pronađene-objave")
        if found_collection:
            DB.add_songs_to_collection(int(found_collection["id"]), [str(song["id"])])
        status = str(analysis.get("completeness_status") or "")
        if status in ("complete", "almost_complete"):
            published = DB.get_collection_by_slug("objavljene-na-kanalu")
            if published:
                DB.add_songs_to_collection(int(published["id"]), [str(song["id"])])
            published_at = str(video.get("published_at") or "")
            current_date = str(song.get("youtube_published_at") or "")
            if not current_date or (published_at and published_at[:10] < current_date[:10]):
                DB.update_song_user_fields(str(song["id"]), {"youtube_published_at": published_at[:10], "youtube_url": video_url})
            refreshed_song = DB.get_song(str(song["id"])) or song
            copied = copy_song_to_published_folder(refreshed_song, {**video, "video_url": video_url})
            task.log(f"Prebačeno u Objavljene_pesme: {len(copied.get('copied') or [])} fajlova.", "success")
        task.log(
            f"„{video.get('title')}“ → Suno „{song.get('title')}“: audio {analysis.get('audio_score')}%, "
            f"pokrivenost {analysis.get('coverage_percent')}%, status {status}, tip {version_type}.",
            "success" if status in ("complete", "almost_complete") else "info",
        )
    if video_song_count > 1:
        task.log(f"„{video.get('title')}“ je prepoznat kao video sa više pesama: {video_song_count} Suno originala.", "success")
    primary_result = dict(saved_results[0])
    primary_result["matched_song_count"] = video_song_count
    primary_result["matched_songs"] = [str(x["song"].get("title") or x["song"].get("id")) for x in selected]
    return primary_result

def analyze_owned_youtube_audio(task: TaskState, options: dict[str, Any]) -> None:
    channels = [c for c in DB.list_youtube_channels() if int(c.get("is_owned") or 0) == 1]
    if not channels:
        raise RuntimeError("Dodaj najmanje jedan svoj YouTube kanal u Pametnim alatima.")
    songs = DB.export_rows([str(x) for x in (options.get("song_ids") or []) if str(x)] or None)
    if not songs:
        raise RuntimeError("Suno biblioteka je prazna ili nema izabranih pesama.")
    ensure_ffmpeg(lambda m, p: task.log(m, "info") if p in (0, 100) else None)
    ensure_ytdlp(lambda m, p: task.log(m, "info") if p in (0, 100) else None)
    max_pages = max(1, min(int(options.get("max_pages") or 10), 100))
    max_videos = max(1, min(int(options.get("max_videos_per_channel") or 100), 500))
    owned_ids = {str(c.get("channel_id") or "") for c in channels}
    all_videos: list[dict[str, Any]] = []
    seen: set[str] = set()
    task.log("Čitam video-spiskove sa povezanih YouTube kanala...")
    for channel in channels:
        try:
            profile_id = str(channel.get("oauth_profile_id") or "")
            if profile_id:
                api_key, access_token = "", get_youtube_access_token(profile_id, required=True)
            else:
                api_key, access_token = youtube_credentials()
            videos = list_channel_videos(channel, api_key=api_key, access_token=access_token, max_pages=max_pages)[:max_videos]
        except Exception as exc:
            task.log(f"Kanal „{channel.get('title')}“ nije pročitan: {exc}", "error")
            continue
        for video in videos:
            video_id = str(video.get("video_id") or "")
            if video_id and video_id not in seen and float(video.get("duration") or 0) >= 12:
                seen.add(video_id)
                DB.upsert_youtube_video(video, is_owned_channel=True)
                all_videos.append(video)
    scan_mode = str(options.get("scan_mode") or "new").strip().lower()
    existing_analyses = DB.list_youtube_audio_analyses(limit=10000)
    analysed_ids = {str(row.get("video_id") or "") for row in existing_analyses if str(row.get("audio_checked_at") or "")}
    uncertain_ids = {str(row.get("video_id") or "") for row in existing_analyses if str(row.get("completeness_status") or "") in ("", "different_audio") or str(row.get("confidence") or "") == "low"}
    if scan_mode == "new":
        before = len(all_videos)
        all_videos = [video for video in all_videos if str(video.get("video_id") or "") not in analysed_ids]
        task.log(f"Brza inkrementalna provera: preskočeno je {before-len(all_videos)} već analiziranih videa.")
    elif scan_mode == "uncertain":
        before = len(all_videos)
        all_videos = [video for video in all_videos if str(video.get("video_id") or "") in uncertain_ids or str(video.get("video_id") or "") not in analysed_ids]
        task.log(f"Ponovna provera nepouzdanih rezultata: izabrano {len(all_videos)} od {before} videa.")
    if not all_videos:
        if scan_mode == "new":
            task.finish("Nema novih YouTube videa za analizu. Svi pronađeni video-snimci već imaju audio rezultat.")
            return
        raise RuntimeError("Na povezanim kanalima nisu pronađeni javni video-snimci za audio analizu.")
    task.total = len(all_videos)
    matched = 0
    errors = 0
    run_id = DB.start_youtube_scan_run("owned_audio_analysis", len(channels), len(songs))
    for index, video in enumerate(all_videos, 1):
        if task.cancel_event.is_set():
            break
        title = str(video.get("title") or video.get("video_id") or "YouTube video")
        task.set_progress(index - 1, len(all_videos), title)
        try:
            result = _analyse_video_against_songs(task, video, songs, owned_ids, options)
            if result:
                matched += 1
        except (AudioMatchCancelled, AudioCancelled):
            task.cancel_event.set()
            break
        except Exception as exc:
            errors += 1
            task.log(f"Audio analiza nije uspela za „{title}“: {exc}", "error")
        task.set_progress(index, len(all_videos), title)
    # Mark songs with no owned-channel match as not found, without touching archived songs.
    analyses = DB.list_youtube_audio_analyses(limit=10000)
    found_song_ids = {str(row.get("song_id") or "") for row in analyses if str(row.get("completeness_status") or "") in ("complete", "almost_complete", "partial", "short_clip")}
    not_found_collection = DB.get_collection_by_slug("youtube-nisu-pronađene")
    if not_found_collection:
        for song in songs:
            sid = str(song.get("id") or "")
            if sid in found_song_ids:
                DB.remove_songs_from_collection(int(not_found_collection["id"]), [sid])
            else:
                DB.add_songs_to_collection(int(not_found_collection["id"]), [sid])
    cleanup = cleanup_youtube_audio_cache(
        max_age_days=max(1, int(options.get("cache_days") or 30)),
        max_size_gb=max(1.0, float(options.get("cache_gb") or 10)),
    )
    summary = (
        f"Audio analiza završena: {len(all_videos)} YouTube videa, {matched} povezanih sa Suno originalima, "
        f"{errors} grešaka. Očišćeno iz keša: {cleanup.get('removed', 0)} fajlova."
    )
    DB.finish_youtube_scan_run(run_id, matched, errors, summary)
    task.finish(summary)


def start_automatic_youtube_pipeline(delay_seconds: float = 1.5) -> None:
    """Metadata scan first, then incremental audio verification and published-folder copy."""
    def orchestrate() -> None:
        if delay_seconds > 0:
            time.sleep(delay_seconds)
        try:
            metadata_options = {"max_pages": 100, "include_private_unlisted": True, "threshold": 68}
            metadata_task = start_task(
                "youtube_owned",
                "Automatsko skeniranje povezanih YouTube kanala",
                lambda t: scan_owned_youtube_channels(t, metadata_options),
                persistent_payload=metadata_options,
            )
            while metadata_task.status == "running":
                time.sleep(0.5)
            if metadata_task.status not in {"done", "partial"}:
                runtime_log(f"Automatska YouTube metadata provera nije završena: {metadata_task.message}", "warning")
                return
            audio_options = {
                "scan_mode": "new",
                "max_pages": 100,
                "max_videos_per_channel": 500,
                "cache_days": 30,
                "cache_gb": 10,
            }
            audio_task = start_task(
                "youtube_audio_owned",
                "Automatska potvrda objavljenih pesama",
                lambda t: analyze_owned_youtube_audio(t, audio_options),
                persistent_payload=audio_options,
            )
            while audio_task.status == "running":
                time.sleep(0.5)
            runtime_log(f"Automatska YouTube provera završena: {audio_task.message}", "info" if audio_task.status == "done" else "warning")
        except Exception as exc:
            runtime_log(f"Automatska YouTube provera nije pokrenuta: {exc}", "warning")

    threading.Thread(target=orchestrate, daemon=True, name="youtube-auto-pipeline").start()


def analyze_one_youtube_match(task: TaskState, match_id: int, options: dict[str, Any]) -> None:
    match = DB.get_youtube_match(match_id)
    if not match:
        raise RuntimeError("YouTube rezultat nije pronađen.")
    song = DB.get_song(str(match.get("song_id") or ""))
    video = DB.get_youtube_video(str(match.get("video_id") or ""))
    if not song or not video:
        raise RuntimeError("Nedostaje Suno pesma ili YouTube video za poređenje.")
    if int(video.get("is_owned_channel") or 0) != 1 and not bool(options.get("confirm_rights")):
        raise RuntimeError("Audio preuzimanje je podrazumevano dozvoljeno samo za kanale označene kao tvoji. Za ručnu proveru tuđeg videa potvrdi da imaš pravo da ga analiziraš.")
    ensure_ffmpeg(lambda m, p: task.log(m, "info") if p in (0, 100) else None)
    ensure_ytdlp(lambda m, p: task.log(m, "info") if p in (0, 100) else None)
    source = _song_audio_source_for_match(song)
    if source is None:
        raise RuntimeError("Suno original nema dostupan audio-fajl niti audio-adresu.")
    task.total = 1
    video_url = str(video.get("url") or f"https://www.youtube.com/watch?v={video.get('video_id')}")
    youtube_audio = download_youtube_audio(video_url, lambda m, p: task.set_progress(0, 1, m), lambda: task.cancel_event.is_set(), reuse_cache=not bool(options.get("force")), cookie_browser=youtube_cookie_browser())
    DB.update_youtube_video_audio_cache(str(video["video_id"]), str(youtube_audio), _sha256(youtube_audio))
    source_sig = _signature_for_source("suno", str(song["id"]), source, task, str(song.get("title")), force=bool(options.get("force")))
    video_sig = _signature_for_source("youtube", str(video["video_id"]), youtube_audio, task, str(video.get("title")), force=bool(options.get("force")))
    analysis = compare_signatures(source_sig, video_sig)
    DB.save_youtube_audio_analysis(str(song["id"]), str(video["video_id"]), analysis)
    _apply_youtube_completeness_collection(str(song["id"]), str(analysis.get("completeness_status") or ""))
    task.set_progress(1, 1, str(video.get("title") or "YouTube video"))
    task.finish(str(analysis.get("reason") or "Audio analiza je završena."))



def build_suno_fingerprint_index(task: TaskState, options: dict[str, Any]) -> None:
    song_ids = [str(x) for x in (options.get("song_ids") or []) if str(x)]
    songs = DB.export_rows(song_ids or None)
    if not songs:
        raise RuntimeError("Suno biblioteka je prazna ili nema izabranih pesama.")
    ensure_ffmpeg(lambda m, p: task.log(m, "info") if p in (0, 100) else None)
    force = bool(options.get("force", False))
    task.total = len(songs)
    ready = 0
    missing = 0
    errors = 0
    for index, song in enumerate(songs, 1):
        if task.cancel_event.is_set():
            break
        title = str(song.get("title") or song.get("id") or "Suno pesma")
        task.set_progress(index - 1, len(songs), title)
        source = _song_audio_source_for_match(song)
        if source is None:
            missing += 1
            task.log(f"„{title}“ nema dostupan audio izvor za otisak.", "warning")
        else:
            try:
                _signature_for_source("suno", str(song.get("id") or ""), source, task, title, force=force)
                ready += 1
            except Exception as exc:
                errors += 1
                task.log(f"Audio otisak nije napravljen za „{title}“: {exc}", "error")
        task.set_progress(index, len(songs), title)
    task.finish(f"Suno audio indeks je spreman: {ready}/{len(songs)} otisaka, bez audio izvora {missing}, greške {errors}.")


def analyze_youtube_url_task(task: TaskState, options: dict[str, Any]) -> None:
    url = str(options.get("url") or "").strip()
    if not url:
        raise RuntimeError("Nalepi YouTube adresu videa.")
    songs = DB.export_rows([str(x) for x in (options.get("song_ids") or []) if str(x)] or None)
    if not songs:
        raise RuntimeError("Suno biblioteka je prazna ili nema izabranih pesama.")
    ensure_ffmpeg(lambda m, p: task.log(m, "info") if p in (0, 100) else None)
    ensure_ytdlp(lambda m, p: task.log(m, "info") if p in (0, 100) else None)
    task.log("Čitam podatke o YouTube videu...")
    video = inspect_youtube_video(url, lambda: task.cancel_event.is_set(), cookie_browser=youtube_cookie_browser())
    channel_id = str(video.get("channel_id") or "")
    owned_ids = {str(c.get("channel_id") or "") for c in DB.list_youtube_channels() if int(c.get("is_owned") or 0) == 1}
    is_owned = bool(options.get("is_owned", channel_id in owned_ids))
    DB.upsert_youtube_video(video, is_owned_channel=is_owned)
    if is_owned and channel_id and channel_id not in owned_ids:
        DB.upsert_youtube_channel({
            "channel_id": channel_id, "title": video.get("channel_title") or channel_id,
            "url": f"https://www.youtube.com/channel/{channel_id}", "uploads_playlist_id": "",
        }, is_owned=True)
        owned_ids.add(channel_id)
    task.total = 1
    result = _analyse_video_against_songs(task, video, songs, owned_ids, options)
    task.set_progress(1, 1, str(video.get("title") or "YouTube video"))
    if not result:
        task.finish("YouTube video je analiziran, ali Suno original nije pronađen.")
    else:
        task.finish(f"YouTube video je povezan sa {int(result.get('matched_song_count') or 1)} Suno originala.")


def create_youtube_coverage_report() -> Path:
    coverage = DB.youtube_coverage_report()
    extras = DB.youtube_duplicate_mix_report()
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    package = _unique_path(EXPORT_DIR / f"YouTube-Suno-pokrivenost-{stamp}.zip")
    csv_buffer = io.StringIO()
    writer = csv.writer(csv_buffer)
    writer.writerow([
        "Suno pesma", "Kanal", "Status", "Zbirna pokrivenost %", "Pokrivene sekunde",
        "Broj videa", "Duplikati", "Mix videi", "Ponovljena pojavljivanja", "Prvi datum",
        "Poslednji datum", "Nedostajući delovi", "Preporuka",
    ])
    for row in coverage.get("rows") or []:
        song = row.get("song") or {}
        channel = row.get("channel") or {}
        missing = "; ".join(f"{float(a):.1f}-{float(b):.1f}s" for a, b in (row.get("missing_intervals") or []))
        writer.writerow([
            song.get("title") or song.get("id"), channel.get("title") or channel.get("channel_id"),
            row.get("status"), row.get("coverage_percent"), row.get("covered_seconds"), row.get("video_count"),
            row.get("duplicate_uploads"), row.get("mixed_video_count"), row.get("repeated_occurrences"),
            row.get("first_published_at"), row.get("last_published_at"), missing, row.get("recommendation"),
        ])
    html_rows = []
    labels = {"complete":"KOMPLETNA", "almost_complete":"SKORO KOMPLETNA", "partial":"DELIMIČNA", "short_clip":"SAMO ISEČAK", "missing":"NIJE PRONAĐENA"}
    for row in coverage.get("rows") or []:
        song = row.get("song") or {}; channel = row.get("channel") or {}
        gaps = ", ".join(f"{float(a):.1f}–{float(b):.1f} s" for a,b in (row.get("missing_intervals") or [])) or "nema"
        html_rows.append("<tr>"+"".join(f"<td>{escape_html(str(v))}</td>" for v in [
            song.get("title") or song.get("id"), channel.get("title") or channel.get("channel_id"),
            labels.get(str(row.get("status") or ""), row.get("status") or ""), f"{float(row.get('coverage_percent') or 0):.1f}%",
            row.get("video_count"), gaps, row.get("recommendation") or "",
        ])+"</tr>")
    html = """<!doctype html><html lang='sr'><meta charset='utf-8'><title>YouTube Suno pokrivenost</title>
    <style>body{font-family:Segoe UI,Arial;background:#0b1020;color:#eef2ff;padding:24px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #334155;padding:8px;text-align:left}th{background:#1e293b}tr:nth-child(even){background:#111827}</style>
    <h1>Da li su Suno pesme kompletno objavljene?</h1><p>Zbirna pokrivenost spaja sve potvrđene video-segmente iste pesme na istom kanalu.</p>
    <table><thead><tr><th>Pesma</th><th>Kanal</th><th>Status</th><th>Pokrivenost</th><th>Videa</th><th>Nedostaje</th><th>Preporuka</th></tr></thead><tbody>""" + "".join(html_rows) + "</tbody></table></html>"
    payload = {"version": APP_VERSION, "created_at": now_iso(), "coverage": coverage, "duplicates_and_mixes": extras}
    with zipfile.ZipFile(package, "w", compression=zipfile.ZIP_DEFLATED, allowZip64=True) as zf:
        zf.writestr("Zbirna-pokrivenost.csv", "\ufeff" + csv_buffer.getvalue())
        zf.writestr("Zbirna-pokrivenost.json", json.dumps(payload, ensure_ascii=False, indent=2, default=str))
        zf.writestr("IZVESTAJ.html", html)
        zf.writestr("PROCITAJ.txt", "Zbirna pokrivenost spaja više YouTube videa. Audio rezultat je tehnička procena i treba ga ručno poslušati kod važnih odluka.\n")
    return package

def json_list(value: Any) -> list[Any]:
    if isinstance(value, list):
        return value
    if not value:
        return []
    try:
        parsed = json.loads(str(value))
        return parsed if isinstance(parsed, list) else []
    except Exception:
        return []

def create_youtube_audio_report() -> Path:
    rows = DB.list_youtube_audio_analyses(limit=10000)
    matrix = DB.youtube_publication_matrix()
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    package = _unique_path(EXPORT_DIR / f"YouTube-Suno-audio-analiza-{stamp}.zip")
    csv_buffer = io.StringIO()
    writer = csv.writer(csv_buffer)
    writer.writerow([
        "Suno pesma", "YouTube video", "Kanal", "Datum", "Suno trajanje", "Video trajanje",
        "Audio %", "Pokrivenost originala %", "Podudarne sekunde", "Status", "Pouzdanost",
        "Tip verzije", "Broj segmenata", "Broj pojavljivanja", "Pesama u videu", "Uloga u videu",
        "Nedostaje početak s", "Nedostaje kraj s", "Unutrašnje praznine s", "Nedostajući delovi",
        "Uvod na videu", "Završetak na videu", "Preporuka", "YouTube URL",
    ])
    for row in rows:
        writer.writerow([
            row.get("song_title") or "", row.get("video_title") or "", row.get("channel_title") or "",
            row.get("published_at") or "", row.get("song_duration") or 0, row.get("video_duration") or 0,
            row.get("audio_score") or 0, row.get("coverage_percent") or 0, row.get("matched_seconds") or 0,
            row.get("completeness_status") or "nije provereno", row.get("confidence") or "",
            row.get("version_type") or "", row.get("segment_count") or 0, row.get("occurrence_count") or 0,
            row.get("video_song_count") or 1, row.get("match_role") or "primary",
            row.get("missing_start") or 0, row.get("missing_end") or 0, row.get("internal_gap_seconds") or 0,
            "; ".join(f"{float(a):.1f}-{float(b):.1f}s" for a,b in json_list(row.get("missing_intervals_json"))),
            row.get("has_intro") or 0, row.get("has_outro") or 0, row.get("recommendation") or "", row.get("video_url") or "",
        ])
    matrix_buffer = io.StringIO()
    matrix_writer = csv.writer(matrix_buffer)
    channels = matrix.get("channels") or []
    matrix_writer.writerow(["Suno pesma", *[str(c.get("title") or c.get("channel_id")) for c in channels]])
    for item in matrix.get("rows") or []:
        song = item.get("song") or {}
        cells = item.get("channels") or {}
        values = []
        for channel in channels:
            cell = cells.get(str(channel.get("channel_id") or ""))
            if not cell:
                values.append("NIJE PRONAĐENA")
            else:
                combined = cell.get("combined_status") or cell.get("completeness_status") or "nije provereno"
                combined_cov = float(cell.get("combined_coverage_percent") or cell.get("coverage_percent") or 0)
                count = int(cell.get("combined_video_count") or 1)
                values.append(f"{combined} | audio {float(cell.get('audio_score') or 0):.0f}% | zbirno {combined_cov:.0f}% | videa {count}")
        matrix_writer.writerow([song.get("title") or song.get("id"), *values])
    html_rows = []
    labels = {"complete": "KOMPLETNA", "almost_complete": "SKORO KOMPLETNA", "partial": "DELIMIČNA", "short_clip": "KRATAK ISEČAK", "different_audio": "DRUGI AUDIO", "": "NIJE PROVERENO"}
    for row in rows:
        html_rows.append(
            "<tr>" + "".join(f"<td>{escape_html(str(value))}</td>" for value in [
                row.get("song_title") or "", row.get("video_title") or "", row.get("channel_title") or "",
                labels.get(str(row.get("completeness_status") or ""), str(row.get("completeness_status") or "")),
                f"{float(row.get('audio_score') or 0):.1f}%", f"{float(row.get('coverage_percent') or 0):.1f}%",
                f"{float(row.get('matched_seconds') or 0):.1f} s", row.get("version_type") or "",
                row.get("segment_count") or 0, row.get("occurrence_count") or 0,
                row.get("recommendation") or "", row.get("published_at") or "", row.get("video_url") or "",
            ]) + "</tr>"
        )
    html = """<!doctype html><html lang='sr'><meta charset='utf-8'><title>YouTube ↔ Suno audio analiza</title>
    <style>body{font-family:Arial;background:#0b1020;color:#eef2ff;padding:24px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #334155;padding:8px;text-align:left}th{background:#1e293b}tr:nth-child(even){background:#111827}a{color:#a78bfa}</style>
    <h1>YouTube ↔ Suno audio analiza</h1><p>Rezultat je tehnička pomoć. Konačnu odluku uvek proveri slušanjem.</p>
    <table><thead><tr><th>Suno</th><th>YouTube</th><th>Kanal</th><th>Status</th><th>Audio</th><th>Pokrivenost</th><th>Podudaranje</th><th>Verzija</th><th>Segmenti</th><th>Pojavljivanja</th><th>Preporuka</th><th>Datum</th><th>URL</th></tr></thead><tbody>""" + "".join(html_rows) + "</tbody></table></html>"
    payload = {"version": APP_VERSION, "created_at": now_iso(), "summary": DB.youtube_audio_summary(), "analyses": rows, "matrix": matrix}
    with zipfile.ZipFile(package, "w", compression=zipfile.ZIP_DEFLATED) as zf:
        zf.writestr("YouTube-Suno-audio-analiza.csv", "\ufeff" + csv_buffer.getvalue())
        zf.writestr("Matrica-kanali-pesme.csv", "\ufeff" + matrix_buffer.getvalue())
        zf.writestr("YouTube-Suno-audio-analiza.json", json.dumps(payload, ensure_ascii=False, indent=2, default=str))
        zf.writestr("IZVESTAJ.html", html)
        zf.writestr("PROCITAJ.txt", "Status KOMPLETNA znači da je program pronašao najmanje oko 92% Suno originala u YouTube zvuku. Uvek ručno poslušaj rezultat pre važnih odluka.\n")
    return package


def escape_html(value: str) -> str:
    return value.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace('"', "&quot;").replace("'", "&#39;")

def open_external_url(url: str) -> None:
    value = str(url or "").strip()
    parsed = urllib.parse.urlparse(value)
    if parsed.scheme not in ("http", "https"):
        raise RuntimeError("Dozvoljene su samo http/https adrese.")
    allowed_hosts = {
        "youtube.com", "www.youtube.com", "studio.youtube.com", "support.google.com",
        "google.com", "www.google.com", "bing.com", "www.bing.com",
    }
    host = (parsed.hostname or "").lower()
    if host not in allowed_hosts and not host.endswith(".youtube.com") and not host.endswith(".google.com"):
        raise RuntimeError("Ova internet adresa nije dozvoljena iz programa.")
    webbrowser.open(value)


def export_youtube_publication_calendar() -> Path:
    rows = DB.publication_calendar(limit=5000)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    path = _unique_path(EXPORT_DIR / f"YouTube-datumi-objava-{stamp}.csv")
    with path.open("w", encoding="utf-8-sig", newline="") as fh:
        writer = csv.writer(fh)
        writer.writerow(["Datum objave", "Suno pesma", "YouTube video", "Kanal", "Video URL", "Pregledi", "Podudaranje %", "Status"])
        for row in rows:
            writer.writerow([
                row.get("published_at") or "", row.get("song_title") or "", row.get("video_title") or "",
                row.get("channel_title") or "", row.get("video_url") or "", row.get("view_count") or 0,
                row.get("score") or 0, row.get("status") or "",
            ])
    return path


def create_youtube_evidence_package(match_id: int, owner_name: str = "") -> Path:
    match = DB.get_youtube_match(match_id)
    if not match:
        raise RuntimeError("Pronađeni YouTube slučaj ne postoji.")
    song = DB.get_song(str(match.get("song_id") or "")) or {
        "id": match.get("song_id"), "title": match.get("song_title"),
        "youtube_url": match.get("original_url"), "source_url": match.get("source_url"),
    }
    video = {
        "video_id": match.get("video_id"), "url": match.get("video_url"),
        "title": match.get("video_title"), "channel_id": match.get("channel_id"),
        "channel_title": match.get("channel_title"), "published_at": match.get("published_at"),
        "view_count": match.get("view_count"), "duration": match.get("video_duration"),
    }
    draft = contact_message(song, video, owner_name=owner_name or DB.get_setting("copyright_owner_name", ""))
    created_at = now_iso()
    evidence = {
        "notice": "Ovo je tehnički paket za ručnu proveru. Nije automatski dokaz povrede autorskog prava niti pravni savet.",
        "created_at": created_at,
        "rights_owner": owner_name or DB.get_setting("copyright_owner_name", ""),
        "song": {
            "id": song.get("id"), "title": song.get("title"), "artist": song.get("display_name"),
            "duration": song.get("duration"), "created_at": song.get("created_at"),
            "suno_url": song.get("source_url"), "original_youtube_url": song.get("youtube_url"),
            "original_youtube_published_at": song.get("youtube_published_at"),
        },
        "candidate_video": video,
        "match": {
            "score": match.get("score"), "reason": match.get("reason"), "status": match.get("status"),
            "first_found_at": match.get("first_found_at"), "last_seen_at": match.get("last_seen_at"),
        },
        "official_tools": {
            "youtube_studio": "https://studio.youtube.com/",
            "copyright_removal_help": "https://support.google.com/youtube/answer/2807622",
        },
    }
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    base = sanitize_filename(str(song.get("title") or match.get("song_id") or "pesma"), 80)
    path = _unique_path(EXPORT_DIR / f"YouTube-dokazi-{base}-{stamp}.zip")
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED, allowZip64=True) as archive:
        archive.writestr("DOKAZI.json", json.dumps(evidence, ensure_ascii=False, indent=2, default=str))
        archive.writestr("KONTAKT_PORUKA.txt", draft)
        archive.writestr(
            "UPUTSTVO.txt",
            "1. Ručno pregledaj ceo video.\n"
            "2. Proveri da li postoji dozvola, licenca, fair use ili drugi zakonit osnov.\n"
            "3. Sačuvaj dodatne snimke ekrana i datume ako su potrebni.\n"
            "4. Prvo možeš poslati pripremljenu poruku kanalu.\n"
            "5. Zvaničan zahtev YouTube-u šalji tek posle sopstvene provere.\n"
            "Program ne šalje pravne zahteve automatski.\n"
        )
    return path


def retry(action: Callable[[], Any], attempts: int = 3, delay: float = 1.5) -> Any:
    last: Exception | None = None
    for index in range(attempts):
        try:
            return action()
        except Exception as exc:
            last = exc
            if index + 1 < attempts:
                time.sleep(delay * (index + 1))
    assert last is not None
    raise last


def sync_library(task: TaskState, options: dict[str, Any]) -> None:
    client = get_client()
    max_pages = max(1, min(int(options.get("max_pages") or 100), 1000))
    liked = bool(options.get("liked"))
    include_main = bool(options.get("include_main", True))
    include_workspaces = bool(options.get("include_workspaces", True))
    refresh_details = bool(options.get("refresh_details", False))
    resume_enabled = bool(options.get("resume", True))
    if bool(options.get("reset_checkpoints", False)):
        DB.clear_sync_checkpoints()
    if not include_main and not include_workspaces:
        raise RuntimeError("Izaberi glavnu biblioteku ili Workspaces / Studio Projects.")

    before_count = DB.count_songs()
    processed = 0
    stored = 0
    failed_items = 0
    source_failures: list[str] = []
    completed_pages = 0
    projects: list[dict[str, Any]] = []

    if include_workspaces:
        task.log("Tražim tvoje Suno Workspaces / Studio Projects...")
        try:
            projects = client.list_all_projects(max_pages=min(max_pages, 100))
            if projects:
                task.log(f"Pronađeno je {len(projects)} Workspace/Project stavki.", "success")
            else:
                task.log("Nije pronađen poseban Workspace/Project. Nastavljam sa glavnom bibliotekom.", "warning")
        except Exception as exc:
            source_failures.append(f"Spisak Workspaces: {exc}")
            task.log(f"Nisam mogao da pročitam spisak Workspaces: {exc}", "warning")

    source_count = (1 if include_main else 0) + len(projects)
    task.total = max(1, source_count * max_pages)

    def store_batch(items: list[dict[str, Any]], source_label: str) -> tuple[int, int, int]:
        nonlocal processed, stored, failed_items
        batch_stored = 0
        batch_failed = 0
        for raw_item in items:
            if task.cancel_event.is_set():
                break
            processed += 1
            item = raw_item
            song_id = str(item.get("id") or item.get("clip_id") or item.get("song_id") or "").strip() if isinstance(item, dict) else ""
            try:
                incomplete = not isinstance(item, dict) or not item.get("title") or not isinstance(item.get("metadata"), dict)
                if refresh_details or incomplete:
                    if song_id:
                        try:
                            detail = client.get_clip(song_id)
                            if isinstance(detail, dict) and detail.get("id"):
                                item = detail
                        except Exception as detail_exc:
                            task.log(f"Detalji nisu dopunjeni za {song_id}: {detail_exc}", "warning")
                if DB.upsert_song(item, source_group=source_label):
                    stored += 1
                    batch_stored += 1
                else:
                    raise ValueError("zapis nema Suno ID")
            except Exception as exc:
                failed_items += 1
                batch_failed += 1
                title = item.get("title") if isinstance(item, dict) else ""
                task.log(f"Preskočen zapis iz {source_label}: {title or song_id or 'bez ID-a'} — {exc}", "error")
        return batch_stored, batch_failed, batch_stored + batch_failed

    def sync_source(source_key: str, source_label: str, fetch_page: Callable[[str | None], tuple[list[dict[str, Any]], str | None, bool, str]]) -> None:
        nonlocal completed_pages
        checkpoint = DB.get_sync_checkpoint(source_key) if resume_enabled else None
        cursor: str | None = None
        page_no = 1
        source_records = 0
        if checkpoint and not int(checkpoint.get("completed") or 0):
            cursor = str(checkpoint.get("cursor") or "") or None
            page_no = int(checkpoint.get("page_no") or 0) + 1
            source_records = int(checkpoint.get("records_processed") or 0)
            task.log(f"{source_label}: nastavljam od paketa {page_no} ({source_records} ranije pročitanih zapisa).", "success")
        elif checkpoint and int(checkpoint.get("completed") or 0):
            cursor = None
            page_no = 1
            source_records = 0
        first_active_page = page_no
        pages_this_run = 0
        seen_cursors: set[str] = set()
        seen_page_signatures: set[str] = set()
        stopped_by_limit = False
        while pages_this_run < max_pages:
            if task.cancel_event.is_set():
                break
            wait_if_paused(task)
            if task.cancel_event.is_set():
                break
            input_cursor = cursor
            records_before_page = source_records
            task.set_progress(completed_pages, task.total, f"{source_label} — paket {page_no}")
            task.log(f"Čitam {source_label} — paket {page_no}...")
            try:
                items, next_cursor, has_more, mode = fetch_page(cursor)
            except Exception as exc:
                if cursor and checkpoint:
                    task.log(f"{source_label}: sačuvani kursor više nije važeći ({exc}). Pokušavam iz početka.", "warning")
                    DB.clear_sync_checkpoints(source_key)
                    cursor = None
                    page_no = 1
                    first_active_page = 1
                    source_records = 0
                    checkpoint = None
                    pages_this_run = 0
                    items, next_cursor, has_more, mode = fetch_page(None)
                    input_cursor = None
                    records_before_page = 0
                else:
                    raise
            if page_no == first_active_page:
                task.log(f"{source_label}: koristi se {mode}; pronađeno {len(items)} zapisa u prvom aktivnom paketu.", "success" if items else "warning")
            page_ids = {str(x.get("id") or x.get("clip_id") or x.get("song_id") or "").strip() for x in items if isinstance(x, dict)}
            page_ids.discard("")
            page_signature = hashlib.sha256(json.dumps({"input": input_cursor or "", "next": next_cursor or "", "ids": sorted(page_ids)}, ensure_ascii=False, sort_keys=True).encode("utf-8")).hexdigest()
            if page_signature in seen_page_signatures:
                task.log(f"{source_label}: Suno je vratio potpuno isti paket i isti kursor; izvor označavam završenim da se ne vrti u krug.", "warning")
                DB.save_sync_checkpoint(source_key, source_label, input_cursor, max(0, page_no - 1), source_records, mode, completed=True)
                break
            seen_page_signatures.add(page_signature)
            batch_stored, batch_failed, batch_processed = store_batch(items, source_label)
            if batch_processed < len(items):
                # Cancellation happened inside a page. Re-fetch the same page on resume; upsert is idempotent.
                DB.save_sync_checkpoint(source_key, source_label, input_cursor, max(0, page_no - 1), records_before_page, mode, completed=False)
                task.log(f"{source_label}: paket {page_no} je prekinut posle {batch_processed}/{len(items)} zapisa. Nastavak će ponovo otvoriti isti paket.", "warning")
                break
            source_records += len(items)
            completed_pages += 1
            pages_this_run += 1
            task.set_progress(completed_pages, task.total, f"{source_label} — {source_records} zapisa")
            task.log(f"{source_label}: paket {page_no} — pročitano {len(items)}, sačuvano {batch_stored}, greške {batch_failed}.")
            completed = not has_more or not next_cursor
            if not completed and next_cursor in seen_cursors:
                task.log(f"{source_label}: Suno je ponovio isti kursor; izvor označavam završenim da se ne vrti u krug.", "warning")
                completed = True
            DB.save_sync_checkpoint(source_key, source_label, next_cursor, page_no, source_records, mode, completed=completed)
            if completed:
                break
            seen_cursors.add(str(next_cursor))
            cursor = next_cursor
            page_no += 1
            if pages_this_run >= max_pages:
                stopped_by_limit = True
                break
            time.sleep(0.25)
        if stopped_by_limit:
            task.log(f"{source_label}: dostignuto je ograničenje od {max_pages} paketa za ovaj prolaz. Sledeća sinhronizacija nastavlja od paketa {page_no}.", "warning")
        elif task.cancel_event.is_set():
            task.log(f"{source_label}: zaustavljeno na zahtev korisnika; nastavak je sačuvan.", "warning")
        else:
            task.log(f"{source_label}: završeno, ukupno pročitano {source_records} zapisa.", "success")


    if include_main and not task.cancel_event.is_set():
        try:
            sync_source(f"main:liked={int(liked)}", "glavnu Suno biblioteku", lambda cursor: client.list_library_cursor(cursor, liked=liked))
        except Exception as exc:
            source_failures.append(f"Glavna biblioteka: {exc}")
            task.log(f"Glavna biblioteka nije završena: {exc}", "error")

    for project in projects:
        if task.cancel_event.is_set():
            break
        project_id = str(project.get("id") or project.get("project_id") or project.get("workspace_id") or "").strip()
        project_name = str(project.get("name") or project.get("title") or project.get("display_name") or "My Workspace").strip()
        if not project_id:
            continue
        label = f'Workspace „{project_name}“'
        try:
            sync_source(f"workspace:{project_id}:liked={int(liked)}", label, lambda cursor, pid=project_id: client.list_workspace_cursor(pid, cursor, liked=liked))
        except Exception as exc:
            source_failures.append(f"{label}: {exc}")
            task.log(f"{label} nije završen: {exc}", "error")

    after_count = DB.count_songs()
    new_count = max(0, after_count - before_count)
    if processed == 0 and source_failures and not task.cancel_event.is_set():
        raise RuntimeError("Nijedan Suno zapis nije uvezen. " + " | ".join(source_failures[:4]))

    summary = (
        f"Sinhronizacija završena: pročitano {processed}, uspešno obrađeno {stored}, "
        f"novih {new_count}, greške {failed_items}; u bazi je ukupno {after_count} pesama."
    )
    if source_failures:
        summary += f" Nezavršeni izvori: {len(source_failures)} — pogledaj Dnevnik."
    task.finish(summary)


def import_urls(task: TaskState, urls: list[str]) -> None:
    client = get_client()
    cleaned = [u.strip() for u in urls if u and u.strip()]
    task.total = len(cleaned)
    imported = 0
    for idx, url in enumerate(cleaned, 1):
        if task.cancel_event.is_set():
            break
        task.set_progress(idx - 1, len(cleaned), url)
        song_match = __import__("re").search(r"suno\.com/song/([0-9a-fA-F-]{16,})", url)
        playlist_match = __import__("re").search(r"suno\.com/playlist/([0-9a-fA-F-]{16,})", url)
        if song_match:
            detail = client.get_clip(song_match.group(1))
            DB.upsert_song(detail)
            imported += 1
            task.log(f"Uvezena pesma: {detail.get('title') or song_match.group(1)}", "success")
        elif playlist_match:
            playlist_id = playlist_match.group(1)
            page = 1
            while page <= 500 and not task.cancel_event.is_set():
                info, items = client.list_playlist(playlist_id, page)
                if not items:
                    break
                for item in items:
                    DB.upsert_song(item)
                    imported += 1
                task.log(f"Plejlista: učitana strana {page} ({len(items)} pesama)")
                page += 1
                time.sleep(0.35)
        else:
            task.errors.append(f"Neprepoznat Suno link: {url}")
            task.log(f"Neprepoznat link: {url}", "error")
        task.set_progress(idx, len(cleaned), url)
    task.finish(f"Uvoz završen. Dodato ili osveženo: {imported} pesama.")


def _cover_bytes(client: SunoClient, url: str, target: Path, task: TaskState) -> bytes:
    if not url:
        return b""
    try:
        retry(lambda: client.download_file(url, target, cancel=task.cancel_event.is_set, auth=False), 2)
        return target.read_bytes()
    except Exception as exc:
        task.log(f"Omot nije preuzet: {exc}", "warning")
        return b""


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as fh:
        for chunk in iter(lambda: fh.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _media_content_sha256(path: Path) -> str:
    """Stabilniji identitet lokalnog MP3-a: ID3 promene ne prave duplikat pri ponovnom uvozu."""
    path = Path(path)
    digest = hashlib.sha256()
    with path.open("rb") as fh:
        start = 0
        end = path.stat().st_size
        if path.suffix.lower() == ".mp3":
            header = fh.read(10)
            if len(header) == 10 and header[:3] == b"ID3":
                tag_size = ((header[6] & 0x7F) << 21) | ((header[7] & 0x7F) << 14) | ((header[8] & 0x7F) << 7) | (header[9] & 0x7F)
                start = 10 + tag_size + (10 if (header[5] & 0x10) else 0)
            if end >= 128:
                fh.seek(end - 128)
                if fh.read(3) == b"TAG":
                    end -= 128
        fh.seek(start)
        remaining = max(0, end - start)
        while remaining:
            chunk = fh.read(min(1024 * 1024, remaining))
            if not chunk:
                break
            digest.update(chunk)
            remaining -= len(chunk)
    return digest.hexdigest()


def _download_one(task: TaskState, client: SunoClient, song_id: str, options: dict[str, Any], index: int, total: int) -> None:
    existing = DB.get_song(song_id)
    if not existing:
        raise SunoAPIError(f"Pesma {song_id} nije u lokalnoj bazi.")
    title = existing.get("title") or song_id
    task.set_progress(index - 1, total, title)
    task.log(f"Pripremam: {title}")

    detail = retry(lambda: client.get_clip(song_id), 3)
    DB.upsert_song(detail)
    row = DB.get_song(song_id) or existing
    metadata = detail.get("metadata") or {}
    # Posle upsert-a red iz baze je izvor istine: čuva korisnički ispravljen naslov, autora i tekst.
    title = row.get("title") or detail.get("title") or song_id
    lyrics = row.get("lyrics") or metadata.get("lyrics") or metadata.get("text") or metadata.get("prompt") or ""
    prompt = row.get("prompt") or metadata.get("prompt") or ""
    tags = row.get("tags") or metadata.get("tags") or ""
    artist = row.get("display_name") or detail.get("display_name") or "Nedostaješ PUNOO"
    created_at = row.get("created_at") or detail.get("created_at") or ""
    month = created_at[:7] if len(created_at) >= 7 and bool(options.get("folders_by_month", True)) else ""
    target_root = _ensure_writable_directory(Path(str(options.get("target_dir") or get_download_dir())), "odredišni folder")
    collection_subfolder = ""
    collection_id = options.get("collection_subfolder_id")
    if collection_id:
        for collection in DB.list_collections():
            if int(collection.get("id") or 0) == int(collection_id):
                collection_subfolder = sanitize_filename(str(collection.get("name") or ""))
                break
    base_dir = target_root
    if collection_subfolder:
        base_dir = base_dir / collection_subfolder
    if month:
        base_dir = base_dir / month
    base_dir = _ensure_writable_directory(base_dir, "folder pesme")
    base = f"{sanitize_filename(title)} [{song_id[:8]}]"
    overwrite = bool(options.get("overwrite"))

    cover_path = base_dir / f"{base}.jpg"
    cover_url = detail.get("image_large_url") or detail.get("image_url") or row.get("image_url") or ""
    cover_bytes = b""
    if bool(options.get("cover", True)):
        if cover_path.exists() and not overwrite:
            cover_bytes = cover_path.read_bytes()
        else:
            cover_bytes = _cover_bytes(client, cover_url, cover_path, task)

    text_path = base_dir / f"{base}.txt"
    if bool(options.get("lyrics", True)) and lyrics:
        text_path.write_text(str(lyrics), encoding="utf-8")

    aligned = client.get_aligned_lyrics(song_id) if bool(options.get("synced_lyrics", True)) else []
    lrc_path = base_dir / f"{base}.lrc"
    srt_path = base_dir / f"{base}.srt"
    if aligned:
        lrc_path.write_text(format_lrc(aligned), encoding="utf-8")
        srt_path.write_text(format_srt(aligned), encoding="utf-8")

    json_path = base_dir / f"{base}.json"
    if bool(options.get("metadata", True)):
        json_path.write_text(json.dumps(detail, ensure_ascii=False, indent=2), encoding="utf-8")

    fmt = str(options.get("format") or "mp3").lower()
    local_audio = row.get("local_audio") or ""
    local_wav = row.get("local_wav") or ""
    audio_path: Path | None = None

    def refresh_clip_url(field: str) -> str | None:
        fresh = client.get_clip(song_id)
        DB.upsert_song(fresh)
        value = fresh.get(field)
        if not value and isinstance(fresh.get("metadata"), dict):
            value = fresh["metadata"].get(field)
        return str(value or "").strip() or None

    if fmt in ("mp3", "both"):
        mp3_url = detail.get("audio_url") or row.get("audio_url")
        if not mp3_url:
            mp3_url = refresh_clip_url("audio_url")
        if not mp3_url:
            raise SunoAPIError(f"Suno nije vratio MP3 adresu za „{title}“.")
        mp3_path = base_dir / f"{base}.mp3"
        existing_valid = False
        if mp3_path.exists() and not overwrite:
            try:
                existing_valid = float(probe_audio(mp3_path).get("duration") or 0) > 0
            except Exception:
                task.log(f"Postojeći MP3 za „{title}“ je neispravan; preuzimam ga ponovo.", "warning")
                mp3_path.unlink(missing_ok=True)
        if overwrite or not existing_valid:
            def progress(done: int, size: int) -> None:
                if size:
                    task.message = f"{title}: MP3 {int(done * 100 / size)}%"
            max_bps = int(float(options.get("rate_limit_mbps") or 0) * 1024 * 1024 / 8)
            client.download_file(
                str(mp3_url), mp3_path, progress=progress, cancel=task.cancel_event.is_set,
                max_bytes_per_second=max_bps, refresh_url=lambda: refresh_clip_url("audio_url"),
            )
            try:
                info = probe_audio(mp3_path)
                if float(info.get("duration") or 0) <= 0:
                    raise RuntimeError("trajanje je 0 sekundi")
            except Exception as exc:
                mp3_path.unlink(missing_ok=True)
                raise SunoAPIError(f"Preuzeti MP3 nije prošao završnu audio proveru: {exc}") from exc
        if bool(options.get("embed_tags", True)):
            try:
                embed_mp3_metadata(
                    mp3_path,
                    title=title,
                    artist=artist,
                    album=str(row.get("album") or "Suno pesme"),
                    genre=str(row.get("genre") or tags),
                    year=str(row.get("year") or created_at[:4]),
                    lyrics=lyrics,
                    comment=prompt,
                    cover_bytes=cover_bytes,
                    track_number=str(row.get("track_number") or ""),
                    copyright_text=str(row.get("copyright") or ""),
                    website=str(row.get("website") or row.get("youtube_url") or ""),
                    bpm=str(row.get("bpm") or ""),
                    key_signature=str(row.get("key_signature") or ""),
                )
            except Exception as exc:
                task.log(f"ID3 podaci nisu upisani za „{title}“: {exc}", "warning")
        local_audio = str(mp3_path)
        audio_path = mp3_path

    if fmt in ("wav", "both"):
        wav_path = base_dir / f"{base}.wav"
        if overwrite or not wav_path.exists():
            wav_url = SunoClient._find_wav_url(detail)
            if not wav_url:
                task.log(f"Tražim WAV verziju za „{title}“...", "info")
                wav_url = client.request_wav(song_id)
            if wav_url:
                max_bps = int(float(options.get("rate_limit_mbps") or 0) * 1024 * 1024 / 8)
                client.download_file(
                    str(wav_url), wav_path, cancel=task.cancel_event.is_set, max_bytes_per_second=max_bps,
                    refresh_url=lambda: client.request_wav(song_id),
                )
            else:
                task.log(f"WAV nije dostupan za „{title}“. MP3 i ostali podaci su sačuvani.", "warning")
        if wav_path.exists():
            local_wav = str(wav_path)
            audio_path = wav_path

    local_video = row.get("local_video") or ""
    if bool(options.get("video")) and (detail.get("video_url") or row.get("video_url")):
        video_path = base_dir / f"{base}.mp4"
        if overwrite or not video_path.exists():
            max_bps = int(float(options.get("rate_limit_mbps") or 0) * 1024 * 1024 / 8)
            client.download_file(
                str(detail.get("video_url") or row.get("video_url")), video_path, cancel=task.cancel_event.is_set,
                max_bytes_per_second=max_bps, refresh_url=lambda: refresh_clip_url("video_url"),
            )
        local_video = str(video_path)

    DB.update_song_files(
        song_id,
        local_audio=local_audio,
        local_wav=local_wav,
        local_video=local_video,
        local_cover=str(cover_path) if cover_path.exists() else "",
        local_lyrics=str(text_path) if text_path.exists() else "",
        local_lrc=str(lrc_path) if lrc_path.exists() else "",
        local_srt=str(srt_path) if srt_path.exists() else "",
        file_sha256=_sha256(audio_path) if audio_path and audio_path.exists() else "",
        downloaded_at=now_iso() if audio_path and audio_path.exists() else row.get("downloaded_at") or "",
    )
    task.log(f"Sačuvano: {title}", "success")
    task.set_progress(index, total, title)


def download_songs(task: TaskState, ids: list[str], options: dict[str, Any]) -> None:
    if not ids:
        raise RuntimeError("Nije izabrana nijedna pesma.")
    task.total = len(ids)
    max_workers = max(1, min(int(options.get("parallel_downloads") or 1), 4))
    success = 0
    completed = 0
    lock = threading.Lock()
    throttle_lock = threading.Lock()
    throttle_until = [0.0]

    def wait_for_rate_limit() -> None:
        while True:
            with throttle_lock:
                remaining = throttle_until[0] - time.monotonic()
            if remaining <= 0:
                return
            if task.cancel_event.wait(min(remaining, 1.0)):
                return

    def worker(index: int, song_id: str) -> tuple[str, str]:
        if task.cancel_event.is_set():
            return song_id, "cancelled"
        wait_if_paused(task)
        wait_for_rate_limit()
        client = get_client()
        for attempt in range(2):
            try:
                _download_one(task, client, song_id, options, index, len(ids))
                return song_id, "ok"
            except SunoAPIError as exc:
                if getattr(exc, "status_code", None) == 429 and attempt == 0:
                    with throttle_lock:
                        throttle_until[0] = max(throttle_until[0], time.monotonic() + 45)
                    task.log("Suno je ograničio zahteve. Svi novi download radnici su pauzirani 45 sekundi, zatim se pesma pokušava ponovo.", "warning")
                    wait_for_rate_limit()
                    continue
                raise
        return song_id, "error"

    with ThreadPoolExecutor(max_workers=max_workers, thread_name_prefix="suno-download") as pool:
        futures = {pool.submit(worker, idx, sid): sid for idx, sid in enumerate(ids, 1)}
        for future in as_completed(futures):
            song_id = futures[future]
            try:
                _, status = future.result()
                if status == "ok":
                    success += 1
            except Exception as exc:
                song = DB.get_song(song_id) or {}
                message = f"{song.get('title') or song_id}: {exc}"
                with lock:
                    task.errors.append(message)
                task.log(message, "error")
            with lock:
                completed += 1
                task.set_progress(completed, len(ids), str((DB.get_song(song_id) or {}).get("title") or song_id))

    summary = f"Preuzimanje završeno: {success}/{len(ids)} pesama. Greške: {len(task.errors)}. Paralelno: {max_workers}. Folder: {get_download_dir()}"
    if task.cancel_event.is_set():
        task.finish(summary)
    elif success == len(ids):
        task.finish(summary)
    elif success > 0:
        task.finish_partial(summary)
    else:
        task.fail(summary)


def _copy_imported_bundle_to_library(audio: Path, content_digest: str) -> tuple[Path, dict[str, str]]:
    """Kopira lokalnu pesmu i istoimene prateće fajlove u trajnu biblioteku programa."""
    safe_stem = sanitize_filename(audio.stem, 120)
    bundle_dir = LOCAL_LIBRARY_DIR / content_digest[:2] / f"{safe_stem} [{content_digest[:10]}]"
    bundle_dir.mkdir(parents=True, exist_ok=True)
    target_audio = bundle_dir / f"{safe_stem}{audio.suffix.lower()}"
    if not target_audio.exists() or target_audio.stat().st_size != audio.stat().st_size or target_audio.stat().st_mtime_ns != audio.stat().st_mtime_ns:
        shutil.copy2(audio, target_audio)
    sidecars: dict[str, str] = {}
    mapping = {
        ".json": "json", ".txt": "lyrics", ".lrc": "lrc", ".srt": "srt",
        ".jpg": "cover", ".jpeg": "cover", ".png": "cover", ".webp": "cover",
    }
    for suffix, key in mapping.items():
        source = audio.with_suffix(suffix)
        if not source.exists() or not source.is_file():
            continue
        destination = bundle_dir / f"{safe_stem}{suffix}"
        if not destination.exists() or destination.stat().st_size != source.stat().st_size or destination.stat().st_mtime_ns != source.stat().st_mtime_ns:
            shutil.copy2(source, destination)
        sidecars[key] = str(destination)
    if "cover" not in sidecars:
        for name in ("cover.jpg", "cover.jpeg", "cover.png", "folder.jpg"):
            source = audio.parent / name
            if source.exists() and source.is_file():
                destination = bundle_dir / source.name
                if not destination.exists() or destination.stat().st_size != source.stat().st_size:
                    shutil.copy2(source, destination)
                sidecars["cover"] = str(destination)
                break
    return target_audio, sidecars


def _import_local_folder_impl(task: TaskState, folder: str, remember: bool = True) -> dict[str, Any]:
    path = Path(folder).expanduser().resolve()
    if not path.exists() or not path.is_dir():
        raise RuntimeError("Izabrani lokalni folder ne postoji.")
    if remember:
        DB.remember_watched_folder(str(path), recursive=True)

    def is_importable_audio(candidate: Path) -> bool:
        if not candidate.is_file() or candidate.suffix.lower() not in (".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg"):
            return False
        lowered = candidate.name.lower()
        return not (lowered.endswith(".pre-tag-backup.mp3") or ".part." in lowered or lowered.startswith("~$"))

    files = [p for p in path.rglob("*") if is_importable_audio(p)]
    task.total = max(1, len(files))
    added = 0
    updated = 0
    skipped = 0
    copy_to_library = DB.get_setting("copy_imported_to_library", "1") != "0"
    for idx, source_audio in enumerate(files, 1):
        if task.cancel_event.is_set():
            break
        digest = _sha256(source_audio)
        content_digest = _media_content_sha256(source_audio) if source_audio.suffix.lower() == ".mp3" else digest
        song_id = "local-" + content_digest[:32]
        existing_content = DB.get_song(song_id)
        if existing_content:
            existing_path = _existing_audio_path(existing_content)
            if existing_path is not None and existing_path.exists() and str(existing_path.resolve()) != str(source_audio.resolve()):
                skipped += 1
                task.log(f"Već zapamćena ista pesma: {source_audio.name}", "info")
                task.set_progress(idx, len(files), source_audio.name)
                continue

        audio = source_audio
        copied_sidecars: dict[str, str] = {}
        if copy_to_library:
            audio, copied_sidecars = _copy_imported_bundle_to_library(source_audio, content_digest)

        sidecar = source_audio.with_suffix(".json")
        raw: dict[str, Any] = {}
        if sidecar.exists():
            try:
                raw = json.loads(sidecar.read_text(encoding="utf-8"))
            except Exception:
                raw = {}
        metadata = raw.get("metadata") if isinstance(raw.get("metadata"), dict) else {}
        txt_source = source_audio.with_suffix(".txt")
        txt_path = Path(copied_sidecars.get("lyrics") or txt_source)
        lyrics = txt_path.read_text(encoding="utf-8", errors="replace") if txt_path.exists() else metadata.get("lyrics") or metadata.get("prompt") or ""
        audio_info: dict[str, Any] = {}
        try:
            if ffprobe_path():
                audio_info = probe_audio(audio)
        except Exception as exc:
            task.log(f"Ne mogu da pročitam tehničke podatke za {source_audio.name}: {exc}", "warning")
        id3_tags = audio_info.get("tags") if isinstance(audio_info.get("tags"), dict) else {}
        title = raw.get("title") or id3_tags.get("title") or source_audio.stem
        artist = raw.get("display_name") or id3_tags.get("artist") or "Lokalni uvoz"
        pseudo = {
            "id": song_id,
            "title": title,
            "created_at": datetime.fromtimestamp(source_audio.stat().st_mtime, timezone.utc).isoformat(),
            "display_name": artist,
            "duration": audio_info.get("duration") or metadata.get("duration") or 0,
            "audio_url": "", "image_url": "", "source_url": str(source_audio),
            "metadata": {**metadata, "duration": audio_info.get("duration") or metadata.get("duration") or 0, "lyrics": lyrics, "type": "local_import", "source_folder": str(path)},
        }
        was_new = DB.upsert_song(pseudo, source_group="Lokalni uvoz")
        DB.update_song_user_fields(song_id, {
            "album": raw.get("album") or id3_tags.get("album") or "",
            "genre": raw.get("genre") or id3_tags.get("genre") or metadata.get("tags") or "",
            "year": raw.get("year") or id3_tags.get("year") or id3_tags.get("date") or "",
            "track_number": raw.get("track_number") or id3_tags.get("track") or "",
            "copyright": raw.get("copyright") or id3_tags.get("copyright") or "",
        })
        cover_source = next((c for c in (source_audio.with_suffix(".jpg"), source_audio.with_suffix(".jpeg"), source_audio.with_suffix(".png"), source_audio.parent / "cover.jpg", source_audio.parent / "cover.png") if c.exists() and c.is_file()), None)
        lrc_source = source_audio.with_suffix(".lrc")
        srt_source = source_audio.with_suffix(".srt")
        DB.update_song_files(
            song_id,
            local_audio=str(audio) if audio.suffix.lower() != ".wav" else "",
            local_wav=str(audio) if audio.suffix.lower() == ".wav" else "",
            local_cover=str(copied_sidecars.get("cover") or cover_source or ""),
            local_lyrics=str(copied_sidecars.get("lyrics") or (txt_source if txt_source.exists() else "")),
            local_lrc=str(copied_sidecars.get("lrc") or (lrc_source if lrc_source.exists() else "")),
            local_srt=str(copied_sidecars.get("srt") or (srt_source if srt_source.exists() else "")),
            file_sha256=digest, content_sha256=content_digest, downloaded_at=now_iso(),
        )
        if was_new:
            added += 1
            task.log(f"Trajno zapamćena pesma: {source_audio.name}", "success")
        else:
            updated += 1
            task.log(f"Osvežena zapamćena pesma: {source_audio.name}", "info")
        task.set_progress(idx, len(files), source_audio.name)
    DB.update_watched_folder_scan(str(path), len(files), added)
    return {"folder": str(path), "files": len(files), "added": added, "updated": updated, "skipped": skipped, "copied_to_library": copy_to_library}


def import_local_folder(task: TaskState, folder: str) -> None:
    result = _import_local_folder_impl(task, folder, remember=True)
    task.finish(
        f"Lokalni folder je zapamćen. Pronađeno {result['files']} audio-fajlova; "
        f"novih {result['added']}, osveženih {result['updated']}, duplikata {result['skipped']}."
    )


def rescan_watched_folders(task: TaskState) -> None:
    folders = DB.list_watched_folders(enabled_only=True)
    if not folders:
        raise RuntimeError("Nema zapamćenih lokalnih foldera. Prvo uvezi najmanje jedan folder.")
    totals = {"files": 0, "added": 0, "updated": 0, "skipped": 0, "errors": 0}
    task.total = len(folders)
    for index, item in enumerate(folders, 1):
        if task.cancel_event.is_set():
            break
        folder = str(item.get("path") or "")
        task.set_progress(index - 1, len(folders), folder)
        try:
            result = _import_local_folder_impl(task, folder, remember=False)
            for key in ("files", "added", "updated", "skipped"):
                totals[key] += int(result.get(key) or 0)
        except Exception as exc:
            totals["errors"] += 1
            task.log(f"Folder nije skeniran: {folder}: {exc}", "error")
        task.set_progress(index, len(folders), folder)
    summary = (
        f"Zapamćeni folderi provereni: {len(folders)}. Fajlova {totals['files']}, novih pesama {totals['added']}, "
        f"osveženih {totals['updated']}, duplikata {totals['skipped']}, grešaka {totals['errors']}."
    )
    if totals["errors"] and totals["added"]:
        task.finish_partial(summary)
    elif totals["errors"] == len(folders):
        task.fail(summary)
    else:
        task.finish(summary)


def _recognition_store_input(source: Path) -> Path:
    source = Path(source).expanduser().resolve()
    if not source.exists() or not source.is_file():
        raise RuntimeError("Izabrani audio/video fajl ne postoji.")
    safe = sanitize_filename(source.stem, 100)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S-%f")
    destination = RECOGNITION_INPUT_DIR / f"{stamp}-{safe}{source.suffix.lower()}"
    shutil.copy2(source, destination)
    return destination


def _prepare_recognition_audio(source: Path, start_seconds: float = 0.0, duration_seconds: float = 25.0) -> Path:
    source = Path(source).resolve()
    executable = ffmpeg_path()
    if not executable:
        raise RuntimeError("FFmpeg nije spreman. U paketu mora postojati tools\\ffmpeg\\bin\\ffmpeg.exe.")
    safe = sanitize_filename(source.stem, 90)
    destination = RECOGNITION_AUDIO_DIR / f"{source.stem[:24]}-{uuid.uuid4().hex[:8]}-{safe}.mp3"
    command = [
        str(executable), "-hide_banner", "-loglevel", "error", "-y",
        "-ss", str(max(0.0, float(start_seconds))), "-i", str(source),
        "-t", str(max(8.0, min(float(duration_seconds), 45.0))),
        "-vn", "-ac", "1", "-ar", "44100", "-b:a", "128k", str(destination),
    ]
    completed = subprocess.run(
        command, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120,
        creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" and hasattr(subprocess, "CREATE_NO_WINDOW") else 0,
    )
    if completed.returncode != 0 or not destination.exists() or destination.stat().st_size < 1024:
        destination.unlink(missing_ok=True)
        raise RuntimeError("Ne mogu da izdvojim audio iz isečka: " + (completed.stderr or completed.stdout or "nepoznata FFmpeg greška")[-1200:])
    if destination.stat().st_size > 10 * 1024 * 1024:
        destination.unlink(missing_ok=True)
        raise RuntimeError("Pripremljeni audio je i dalje veći od 10 MB. Izaberi kraći isečak.")
    return destination


def recognize_music_file(source: Path, start_seconds: float = 0.0, duration_seconds: float = 25.0) -> dict[str, Any]:
    stored_input = _recognition_store_input(source)
    prepared = _prepare_recognition_audio(stored_input, start_seconds=start_seconds, duration_seconds=duration_seconds)
    try:
        result = recognize_audd(prepared, DB.get_setting("audd_api_token", ""))
        status = "recognized" if result.get("found") else "not_found"
        error_message = ""
    except Exception as exc:
        result = {"found": False, "provider": "AudD", "message": str(exc)}
        status = "error"
        error_message = str(exc)
    record = DB.add_recognition({
        "original_filename": source.name,
        "input_path": str(stored_input),
        "prepared_audio_path": str(prepared),
        "result": result,
        "status": status,
        "error_message": error_message,
    })
    result_payload = {**result, "history_id": int(record.get("id") or 0), "input_path": str(stored_input), "prepared_audio_path": str(prepared)}
    target = RECOGNITION_RESULTS_DIR / f"prepoznavanje-{int(record.get('id') or 0):06d}.json"
    target.write_text(json.dumps({"record": record, "result": result_payload}, ensure_ascii=False, indent=2), encoding="utf-8")
    if not result.get("found"):
        try:
            unknown = RECOGNITION_UNKNOWN_DIR / stored_input.name
            if not unknown.exists():
                shutil.copy2(stored_input, unknown)
        except Exception:
            pass
    return result_payload


def add_recognition_to_library(recognition_id: int) -> dict[str, Any]:
    record = DB.get_recognition(recognition_id)
    if not record:
        raise RuntimeError("Rezultat Pronalazača pesme nije pronađen.")
    if not int(record.get("found") or 0):
        raise RuntimeError("Neprepoznat isečak ne može da se doda u Biblioteku.")
    existing_song_id = str(record.get("library_song_id") or "")
    if existing_song_id:
        existing = DB.get_song(existing_song_id)
        if existing:
            return existing
    identity = "|".join((str(record.get("artist") or ""), str(record.get("title") or ""), str(record.get("song_link") or ""))).encode("utf-8")
    song_id = "recognized-" + hashlib.sha256(identity).hexdigest()[:32]
    audio_source = Path(str(record.get("prepared_audio_path") or record.get("input_path") or ""))
    if not audio_source.exists():
        raise RuntimeError("Sačuvani audio isečak više ne postoji.")
    title = str(record.get("title") or audio_source.stem)
    artist = str(record.get("artist") or "Pronalazač pesme")
    pseudo = {
        "id": song_id, "title": title, "display_name": artist, "created_at": now_iso(),
        "duration": 0, "audio_url": "", "image_url": "", "source_url": str(record.get("song_link") or ""),
        "metadata": {
            "type": "music_recognition", "album": str(record.get("album") or ""),
            "release_date": str(record.get("release_date") or ""), "label": str(record.get("label") or ""),
            "recognition_id": int(recognition_id), "timecode": str(record.get("timecode") or ""),
        },
    }
    DB.upsert_song(pseudo, source_group="Pronalazač pesme")
    destination_dir = LOCAL_LIBRARY_DIR / "Pronalazac_pesme"
    destination_dir.mkdir(parents=True, exist_ok=True)
    destination = destination_dir / f"{sanitize_filename(artist + ' - ' + title, 130)} [{song_id[-8:]}].mp3"
    if not destination.exists() or destination.stat().st_size != audio_source.stat().st_size:
        shutil.copy2(audio_source, destination)
    DB.update_song_user_fields(song_id, {
        "album": str(record.get("album") or ""), "year": str(record.get("release_date") or "")[:4],
        "website": str(record.get("song_link") or ""), "custom_tags": "pronađena pesma, audio prepoznavanje",
    })
    DB.update_song_files(song_id, local_audio=str(destination), file_sha256=_sha256(destination), content_sha256=_media_content_sha256(destination), downloaded_at=now_iso())
    DB.set_recognition_library_song(recognition_id, song_id)
    return DB.get_song(song_id) or {"id": song_id, "title": title, "display_name": artist}


def song_finder_status() -> dict[str, Any]:
    """Pronalazač mojih pesama: local-only index status, no token, no internet."""
    songs = DB.export_rows()
    with_audio = [s for s in songs if _existing_audio_path(s)]
    indexed = sum(1 for s in with_audio if DB.get_audio_fingerprint("suno", str(s.get("id") or ""), AUDIO_MATCH_VERSION))
    return {
        "ok": True,
        "songs_total": len(songs),
        "songs_with_audio": len(with_audio),
        "songs_indexed": indexed,
        "songs_not_indexed": len(with_audio) - indexed,
        "algorithm_version": AUDIO_MATCH_VERSION,
        "last_indexed_at": DB.get_setting("song_finder_last_index_at", ""),
    }


def song_finder_index_task(task: TaskState, options: dict[str, Any]) -> None:
    force = bool(options.get("force"))
    all_songs = DB.export_rows()
    songs = [s for s in all_songs if _existing_audio_path(s)]
    task.total = len(songs)
    ok = 0
    failed = 0
    for idx, song in enumerate(songs, 1):
        if task.cancel_event.is_set():
            break
        wait_if_paused(task)
        song_id = str(song.get("id") or "")
        path = _existing_audio_path(song)
        title = str(song.get("title") or song.get("display_name") or song_id)
        try:
            # _signature_for_source already skips recomputation when the
            # file's SHA-256/mtime identity hasn't changed since the last
            # index (force=False here just means "don't force a redo of
            # unchanged songs" -- true force=True, from "OBRIŠI I PONOVO
            # NAPRAVI INDEKS", recomputes every song regardless).
            _signature_for_source("suno", song_id, path, task, title, force=force)
            ok += 1
        except Exception as exc:
            failed += 1
            task.log(f"{title}: {exc}", "warning")
        task.set_progress(idx, len(songs), title)
    DB.set_setting("song_finder_last_index_at", now_iso())
    no_audio = len(all_songs) - len(songs)
    task.finish(f"Indeksiranje završeno: {ok} obrađeno, {failed} neuspešno, {no_audio} pesama nema lokalni audio fajl.")


def song_finder_analyze(path: Path) -> dict[str, Any]:
    """Compare an uploaded Shorts/video/audio file against the local song
    index only -- no AudD, no YouTube, no Google, no Suno token, no
    network call of any kind."""
    path = Path(path).expanduser().resolve()
    if not path.exists() or not path.is_file():
        raise RuntimeError("Fajl nije pronađen.")
    if not song_finder.is_supported_file(path):
        raise RuntimeError(f"Format {path.suffix or '?'} nije podržan za Pronalazač mojih pesama.")
    file_sha256 = sha256_file(path)
    upload_signature = extract_signature(path)
    songs = [s for s in DB.export_rows() if _existing_audio_path(s)]
    candidates: list[dict[str, Any]] = []
    checked = 0
    for song in songs:
        song_id = str(song.get("id") or "")
        cached = DB.get_audio_fingerprint("suno", song_id, AUDIO_MATCH_VERSION)
        if not cached:
            continue
        try:
            song_signature = unpack_signature(cached.get("payload") or b"")
            analysis = compare_signatures(song_signature, upload_signature)
        except (AudioMatchError, Exception):
            continue
        checked += 1
        status = song_finder.classify_match(analysis)
        if status == song_finder.STATUS_NOT_FOUND:
            continue
        candidates.append({
            "song_id": song_id,
            "title": str(song.get("title") or song.get("display_name") or song_id),
            "display_name": str(song.get("display_name") or ""),
            "status": status,
            "status_label": song_finder.STATUS_LABELS_SR.get(status, status),
            "confidence": song_finder.confidence_percent(analysis),
            "audio_score": analysis.get("audio_score"),
            "matched_seconds": analysis.get("matched_seconds"),
            "song_start": analysis.get("source_start"), "song_end": analysis.get("source_end"),
            "clip_start": analysis.get("video_start"), "clip_end": analysis.get("video_end"),
            "reason": analysis.get("reason"),
        })
    ranked = song_finder.rank_song_candidates(candidates)
    primary = ranked[0] if ranked else None
    overall_status = primary["status"] if primary else song_finder.STATUS_NOT_FOUND
    result = {
        "found": bool(primary),
        "provider": "local_library",
        "artist": str(primary.get("display_name") or "") if primary else "",
        "title": str(primary.get("title") or "") if primary else "",
        "file": str(path), "file_sha256": file_sha256, "duration": upload_signature.get("duration"),
        "engine": "chromaprint+spectral", "algorithm_version": AUDIO_MATCH_VERSION,
        "status": overall_status, "status_label": song_finder.STATUS_LABELS_SR.get(overall_status, overall_status),
        "songs_checked": checked, "songs_indexed_total": len(songs),
        "primary": primary, "alternatives": ranked[1:4], "matches": ranked,
    }
    record = DB.add_recognition({
        "original_filename": path.name, "input_path": str(path), "prepared_audio_path": "",
        "library_song_id": str(primary.get("song_id") or "") if primary else "",
        "status": "recognized" if primary else "not_found",
        "result": result,
    })
    result["history_id"] = int(record.get("id") or 0)
    return result


def _unique_path(path: Path) -> Path:
    if not path.exists():
        return path
    for index in range(2, 10000):
        candidate = path.with_name(f"{path.stem} ({index}){path.suffix}")
        if not candidate.exists():
            return candidate
    raise RuntimeError("Ne mogu da napravim jedinstveno ime fajla.")


def choose_folder_dialog(initial: str = "") -> str:
    """Otvori standardni Windows izbor foldera bez dodatnih Python paketa."""
    if os.name == "nt":
        safe_initial = str(initial or get_download_dir()).replace("'", "''")
        script = (
            "Add-Type -AssemblyName System.Windows.Forms; "
            "$d=New-Object System.Windows.Forms.FolderBrowserDialog; "
            "$d.Description='Izaberi folder za Suno pesme'; "
            f"$d.SelectedPath='{safe_initial}'; "
            "if($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK){"
            "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; Write-Output $d.SelectedPath}"
        )
        result = subprocess.run(
            ["powershell.exe", "-NoProfile", "-STA", "-ExecutionPolicy", "Bypass", "-Command", script],
            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120,
            creationflags=subprocess.CREATE_NO_WINDOW if hasattr(subprocess, "CREATE_NO_WINDOW") else 0,
        )
        selected = (result.stdout or "").strip().splitlines()
        return selected[-1].strip() if selected else ""
    # Razvojni/test fallback za Linux/macOS.
    for command in (["zenity", "--file-selection", "--directory"], ["osascript", "-e", 'POSIX path of (choose folder)']):
        if shutil.which(command[0]):
            result = subprocess.run(command, capture_output=True, text=True, timeout=120)
            if result.returncode == 0:
                return (result.stdout or "").strip()
    return ""


def choose_file_dialog(initial: str = "", filter_text: str = "Backup ZIP (*.zip)|*.zip|SQLite baza (*.db)|*.db|Svi fajlovi (*.*)|*.*") -> str:
    if os.name == "nt":
        safe_initial = str(initial or EXPORT_DIR).replace("'", "''")
        safe_filter = str(filter_text).replace("'", "''")
        script = (
            "Add-Type -AssemblyName System.Windows.Forms; "
            "$d=New-Object System.Windows.Forms.OpenFileDialog; "
            f"$d.InitialDirectory='{safe_initial}'; $d.Filter='{safe_filter}'; "
            "if($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK){"
            "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; Write-Output $d.FileName}"
        )
        result = subprocess.run(
            ["powershell.exe", "-NoProfile", "-STA", "-ExecutionPolicy", "Bypass", "-Command", script],
            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120,
            creationflags=subprocess.CREATE_NO_WINDOW if hasattr(subprocess, "CREATE_NO_WINDOW") else 0,
        )
        selected = (result.stdout or "").strip().splitlines()
        return selected[-1].strip() if selected else ""
    return ""


def _safe_archive_member(name: str) -> bool:
    value = str(name or "").replace("\\", "/")
    parts = [part for part in value.split("/") if part]
    return bool(parts) and not value.startswith("/") and ".." not in parts and ":" not in parts[0]


def _legacy_backup_manifest(archive: zipfile.ZipFile) -> dict[str, Any]:
    """Napravi manifest za stari v1.4.0 kompletan backup koji ga nije imao."""
    names = set(archive.namelist())
    if "biblioteka.json" not in names:
        return {"files": []}
    try:
        rows = json.loads(archive.read("biblioteka.json").decode("utf-8"))
    except Exception:
        return {"files": []}
    files: list[dict[str, Any]] = []
    for row in rows if isinstance(rows, list) else []:
        if not isinstance(row, dict):
            continue
        song_id = str(row.get("id") or "")
        folder = f"audio/{sanitize_filename(str(row.get('title') or song_id), 90)} [{song_id[:8]}]"
        candidates = [name for name in names if name.startswith(folder + "/") and _safe_archive_member(name)]
        unused = set(candidates)
        for field in ("local_audio", "local_wav", "local_video", "local_cover", "local_lyrics", "local_lrc", "local_srt"):
            raw = str(row.get(field) or "")
            base = Path(raw).name if raw else ""
            match = next((name for name in unused if Path(name).name == base), None) if base else None
            if match:
                unused.discard(match)
                files.append({"kind": "song_field", "song_id": song_id, "field": field, "archive_path": match, "original_path": raw})
        for item in row.get("derived_files") or []:
            if not isinstance(item, dict):
                continue
            raw = str(item.get("path") or "")
            base = Path(raw).name if raw else ""
            match = next((name for name in unused if Path(name).name == base), None) if base else None
            if match:
                unused.discard(match)
                files.append({"kind": "derived", "song_id": song_id, "derived_id": int(item.get("id") or 0), "archive_path": match, "original_path": raw})
    return {"format": "legacy-v1.4.0", "manifest_version": 0, "files": files}


def restore_backup_file(path: str) -> dict[str, Any]:
    source = Path(path).expanduser()
    if not source.exists() or not source.is_file():
        raise RuntimeError("Izabrani backup fajl ne postoji.")
    restored_files = 0
    skipped_files = 0
    restore_folder = ""
    with tempfile.TemporaryDirectory(prefix="suno_restore_") as tmp_dir:
        tmp = Path(tmp_dir)
        db_source: Path | None = None
        manifest: dict[str, Any] = {"files": []}
        archive: zipfile.ZipFile | None = None
        try:
            if source.suffix.lower() == ".zip":
                archive = zipfile.ZipFile(source)
                bad = archive.testzip()
                if bad:
                    raise RuntimeError(f"Backup ZIP je oštećen kod fajla: {bad}")
                candidates = [name for name in archive.namelist() if name.endswith("suno_biblioteka.db") or name.endswith("biblioteka.db")]
                if not candidates:
                    raise RuntimeError("Backup ZIP ne sadrži SQLite bazu.")
                member = candidates[0]
                if not _safe_archive_member(member):
                    raise RuntimeError("Backup sadrži nedozvoljenu putanju baze.")
                db_source = tmp / "suno_biblioteka.db"
                with archive.open(member) as src, db_source.open("wb") as dst:
                    shutil.copyfileobj(src, dst, length=1024 * 1024)
                if "backup_manifest.json" in archive.namelist():
                    try:
                        manifest = json.loads(archive.read("backup_manifest.json").decode("utf-8"))
                    except Exception as exc:
                        raise RuntimeError(f"Manifest kompletnog backup-a nije ispravan: {exc}") from exc
                else:
                    manifest = _legacy_backup_manifest(archive)
            elif source.suffix.lower() in (".db", ".sqlite", ".sqlite3"):
                db_source = source
            else:
                raise RuntimeError("Izaberi backup ZIP ili SQLite .db fajl.")

            # Sačuvaj lokaciju pre vraćanja baze, jer backup može sadržati staru putanju download foldera.
            restore_root = get_download_dir() / "Vraceni_backup" / sanitize_filename(source.stem, 80)
            files = manifest.get("files") if isinstance(manifest, dict) else []
            if archive is not None and isinstance(files, list) and files:
                # Pre vraćanja baze proveri da folder može da se napravi i da ima dovoljno slobodnog prostora.
                restore_root.mkdir(parents=True, exist_ok=True)
                probe = restore_root / f".restore-write-test-{uuid.uuid4().hex}.tmp"
                probe.write_bytes(b"ok")
                probe.unlink(missing_ok=True)
                required_bytes = sum(max(0, int(item.get("size") or 0)) for item in files if isinstance(item, dict))
                if required_bytes:
                    free_bytes = shutil.disk_usage(restore_root).free
                    if required_bytes > max(0, free_bytes - 64 * 1024 * 1024):
                        raise RuntimeError(
                            f"Nema dovoljno slobodnog prostora za kompletan backup. Potrebno je najmanje {required_bytes} bajtova, "
                            f"a dostupno je {free_bytes} bajtova."
                        )
                restore_folder = str(restore_root)
                # Preflight kompletnog manifesta pre zamene baze. Tako oštećen ili
                # ručno izmenjen audio-fajl ne ostavlja obnovljenu bazu sa pola fajlova.
                for item in files:
                    if not isinstance(item, dict):
                        raise RuntimeError("Backup manifest sadrži neispravnu stavku.")
                    arcname = str(item.get("archive_path") or "")
                    if not _safe_archive_member(arcname) or arcname not in archive.namelist():
                        raise RuntimeError(f"Backup manifest upućuje na nedostajući ili nedozvoljen fajl: {arcname}")
                    info = archive.getinfo(arcname)
                    expected_size = max(0, int(item.get("size") or 0))
                    if expected_size and info.file_size != expected_size:
                        raise RuntimeError(f"Veličina fajla u backup-u nije ispravna: {arcname}")
                    expected_sha = str(item.get("sha256") or "").strip().lower()
                    if expected_sha:
                        if not re.fullmatch(r"[0-9a-f]{64}", expected_sha):
                            raise RuntimeError(f"SHA-256 u manifestu nije ispravan: {arcname}")
                        digest = hashlib.sha256()
                        with archive.open(arcname) as src:
                            for chunk in iter(lambda: src.read(1024 * 1024), b""):
                                digest.update(chunk)
                        if digest.hexdigest().lower() != expected_sha:
                            raise RuntimeError(f"SHA-256 fajla u backup-u nije ispravan: {arcname}")

            DB.restore_from(db_source)

            if archive is not None and isinstance(files, list) and files:
                for index, item in enumerate(files, 1):
                    if not isinstance(item, dict):
                        skipped_files += 1
                        continue
                    arcname = str(item.get("archive_path") or "")
                    if not _safe_archive_member(arcname) or arcname not in archive.namelist():
                        skipped_files += 1
                        continue
                    try:
                        song_id = str(item.get("song_id") or "")
                        rel_parts = [sanitize_filename(part, 100) for part in Path(arcname.replace("\\", "/")).parts[1:]]
                        if not rel_parts:
                            rel_parts = [f"fajl-{index}"]
                        target = restore_root.joinpath(*rel_parts)
                        target.parent.mkdir(parents=True, exist_ok=True)
                        target = _unique_path(target) if target.exists() else target
                        with archive.open(arcname) as src, target.open("wb") as dst:
                            shutil.copyfileobj(src, dst, length=1024 * 1024)
                        expected_size = max(0, int(item.get("size") or 0))
                        if expected_size and target.stat().st_size != expected_size:
                            target.unlink(missing_ok=True)
                            raise RuntimeError(f"Veličina vraćenog fajla nije ispravna: {arcname}")
                        expected_sha = str(item.get("sha256") or "").strip().lower()
                        if expected_sha and (not re.fullmatch(r"[0-9a-f]{64}", expected_sha) or _sha256(target).lower() != expected_sha):
                            target.unlink(missing_ok=True)
                            raise RuntimeError(f"SHA-256 vraćenog fajla nije ispravan: {arcname}")
                        if item.get("kind") == "song_field" and str(item.get("field") or "") in {
                            "local_audio", "local_wav", "local_video", "local_cover", "local_lyrics", "local_lrc", "local_srt"
                        }:
                            DB.update_song_files(song_id, **{str(item["field"]): str(target)})
                            restored_files += 1
                        elif item.get("kind") == "derived" and int(item.get("derived_id") or 0):
                            if DB.update_derived_file_path(int(item["derived_id"]), str(target)):
                                restored_files += 1
                            else:
                                skipped_files += 1
                        else:
                            skipped_files += 1
                    except Exception as exc:
                        skipped_files += 1
                        runtime_log(f"Backup restore: preskočen {arcname}: {exc}", "warning")
        finally:
            if archive is not None:
                archive.close()
    health = DB.local_file_health(repair=False)
    message = "Backup je vraćen. Biblioteka je ponovo učitana."
    if restored_files:
        message += f" Vraćeno je {restored_files} lokalnih fajlova u: {restore_folder}."
    if skipped_files:
        message += f" Preskočeno stavki iz manifesta: {skipped_files}."
    return {
        "songs": DB.count_songs(), "message": message, "restored_files": restored_files,
        "skipped_files": skipped_files, "restore_folder": restore_folder, "health": health,
    }


def _existing_audio_path(song: dict[str, Any]) -> Path | None:
    for key in ("local_audio", "local_wav"):
        value = str(song.get(key) or "")
        if value:
            path = Path(value)
            if path.exists() and path.is_file():
                return path
    return None


def save_songs_to_folder(task: TaskState, ids: list[str], target_folder: str, options: dict[str, Any]) -> None:
    if not ids:
        raise RuntimeError("Nije izabrana nijedna pesma.")
    target = Path(target_folder).expanduser()
    target.mkdir(parents=True, exist_ok=True)
    fmt = str(options.get("format") or "mp3").lower()
    if fmt not in ("mp3", "wav"):
        fmt = "mp3"
    include_related = bool(options.get("include_related", True))
    include_derived = bool(options.get("include_derived", False))
    task.total = len(ids)
    copied = 0
    failures = 0
    client: SunoClient | None = None
    for index, song_id in enumerate(ids, 1):
        if task.cancel_event.is_set():
            break
        song = DB.get_song(song_id)
        title = str(song.get("title") or song_id) if song else song_id
        task.set_progress(index - 1, len(ids), title)
        try:
            if not song:
                raise RuntimeError("Pesma nije pronađena u biblioteci.")
            preferred_raw = str(song.get("local_wav") or "") if fmt == "wav" else str(song.get("local_audio") or "")
            preferred = Path(preferred_raw) if preferred_raw else None
            if preferred and preferred.exists() and preferred.is_file():
                destination = _unique_path(target / preferred.name)
                shutil.copy2(preferred, destination)
                if include_related:
                    for key in ("local_cover", "local_lyrics", "local_lrc", "local_srt", "local_video"):
                        related_raw = str(song.get(key) or "")
                        if related_raw:
                            related = Path(related_raw)
                            if related.exists() and related.is_file():
                                shutil.copy2(related, _unique_path(target / related.name))
                if include_derived:
                    for item in song.get("derived_files") or []:
                        derived = Path(str(item.get("path") or ""))
                        if derived.exists() and derived.is_file():
                            shutil.copy2(derived, _unique_path(target / derived.name))
                copied += 1
                task.log(f"Kopirano u izabrani folder: {title}", "success")
            else:
                source = _existing_audio_path(song)
                if source:
                    ensure_ffmpeg(lambda msg, pct: task.set_progress(index - 1, len(ids), msg))
                    extension = ".wav" if fmt == "wav" else ".mp3"
                    destination = _unique_path(target / f"{sanitize_filename(title)} [{song_id[:8]}]{extension}")
                    process_audio(source, destination, {"format": fmt, "start": 0, "end": song.get("duration") or "", "bitrate": "320k"})
                    copied += 1
                    task.log(f"Konvertovano i sačuvano: {title}", "success")
                else:
                    if client is None:
                        client = get_client()
                    direct_options = {
                        "format": fmt, "cover": include_related, "lyrics": include_related, "synced_lyrics": include_related,
                        "metadata": include_related, "embed_tags": True, "video": False, "folders_by_month": False,
                        "overwrite": False, "target_dir": str(target),
                    }
                    _download_one(task, client, song_id, direct_options, index, len(ids))
                    copied += 1
        except Exception as exc:
            failures += 1
            task.errors.append(f"{title}: {exc}")
            task.log(f"Nije sačuvano „{title}“: {exc}", "error")
        task.set_progress(index, len(ids), title)
    task.finish(f"Sačuvano u folder: {copied}/{len(ids)} pesama; greške {failures} — {target}")


def _song_processing_source(song: dict[str, Any]) -> str | Path | None:
    local = _existing_audio_path(song)
    if local is not None:
        return local
    remote = str(song.get("audio_url") or "").strip()
    return remote or None


def _process_audio_one(
    task: TaskState,
    song_id: str,
    options: dict[str, Any],
    item_index: int = 1,
    item_total: int = 1,
) -> dict[str, Any]:
    song = DB.get_song(song_id)
    if not song:
        raise RuntimeError("Pesma nije pronađena u biblioteci.")
    title = str(song.get("title") or song_id)
    source = _song_processing_source(song)
    if source is None and not str(song_id).startswith("local-"):
        try:
            detail = get_client().get_clip(song_id)
            if isinstance(detail, dict):
                DB.upsert_song(detail, source_group=str(song.get("source_group") or "Suno"))
                song = DB.get_song(song_id) or song
                source = _song_processing_source(song)
        except Exception as exc:
            task.log(f"Nije osvežena Suno audio-adresa za „{title}“: {exc}", "warning")
    if source is None:
        raise RuntimeError("Nije pronađen audio-fajl niti Suno audio-adresa za obradu.")

    def mapped_progress(message: str, percent: int) -> None:
        local_percent = max(0, min(100, int(percent)))
        overall = int((((item_index - 1) + local_percent / 100) / max(1, item_total)) * 100)
        with task._lock:
            task.current = f"{item_index}/{item_total} · {title} · {message}"
            task.message = message
            task.done = item_index - 1
            task.total = item_total
            task.percent = max(task.percent, overall)

    ensure_ffmpeg(lambda msg, pct: mapped_progress(msg, min(8, int(pct * 0.08))))
    output_format = str(options.get("format") or "mp3").lower()
    extension = {"wav": ".wav", "flac": ".flac", "m4a": ".m4a", "aac": ".m4a"}.get(output_format, ".mp3")
    output_dir = Path(str(options.get("target_dir") or (get_download_dir() / "Obradjene_verzije"))).expanduser()
    output_dir.mkdir(parents=True, exist_ok=True)
    start = float(options.get("start") or 0)
    end = float(options.get("end") or song.get("duration") or 0)
    if end <= start:
        raise ValueError("Kraj mora da bude posle početka isečka.")
    label = str(options.get("label") or "").strip()
    if not label:
        mode_label = "Brzi isečak" if str(options.get("processing_mode") or "auto") in ("quick", "copy", "lossless") else "Isečak"
        label = f"{mode_label} {start:.1f}s–{end:.1f}s"
    safe_label = sanitize_filename(label, 60)
    output = _unique_path(output_dir / f"{sanitize_filename(title)} - {safe_label}{extension}")

    known_info = {
        "duration": float(song.get("duration") or 0),
        "codec": "mp3" if (str(source).lower().endswith(".mp3") or str(song.get("audio_url") or "").lower().endswith(".mp3")) else "",
        "sample_rate": 0,
        "channels": 0,
        "bitrate": 0,
        "bitrate_kbps": 0,
    }
    if isinstance(source, Path):
        try:
            local_info = probe_audio(source)
            known_info.update(local_info)
        except Exception as exc:
            task.log(f"Tehnički podaci nisu pročitani pre obrade: {exc}", "warning")

    def do_process(active_source: str | Path) -> dict[str, Any]:
        return process_audio(
            active_source,
            output,
            options,
            mapped_progress,
            cancel_check=task.cancel_event.is_set,
            known_info=known_info,
        )

    try:
        if isinstance(source, str) and source.startswith(("http://", "https://")):
            task.log(f"„{title}“ se obrađuje direktno sa Suno servera, bez prethodnog punog preuzimanja.", "info")
        result = do_process(source)
    except AudioCancelled:
        output.unlink(missing_ok=True)
        raise
    except Exception as first_error:
        if isinstance(source, str) and source.startswith(("http://", "https://")):
            task.log(f"Direktna obrada nije uspela ({first_error}). Preuzimam lokalnu kopiju i pokušavam ponovo...", "warning")
            client = get_client()
            _download_one(task, client, song_id, {
                "format": "mp3", "cover": True, "lyrics": True, "synced_lyrics": True,
                "metadata": True, "embed_tags": True, "video": False, "folders_by_month": False,
                "overwrite": False,
            }, 1, 1)
            song = DB.get_song(song_id) or song
            local_source = _existing_audio_path(song)
            if local_source is None:
                raise first_error
            try:
                known_info.update(probe_audio(local_source))
            except Exception:
                pass
            result = do_process(local_source)
        else:
            raise

    if task.cancel_event.is_set():
        output.unlink(missing_ok=True)
        raise AudioCancelled("Audio obrada je zaustavljena.")

    if output_format == "mp3":
        cover_bytes = b""
        cover_raw = str(song.get("local_cover") or "")
        if cover_raw and Path(cover_raw).exists():
            cover_bytes = Path(cover_raw).read_bytes()
        embed_mp3_metadata(
            output,
            title=str(song.get("title") or ""), artist=str(song.get("display_name") or "Nedostaješ PUNOO"),
            album=str(song.get("album") or "Suno pesme"), genre=str(song.get("genre") or song.get("tags") or ""),
            year=str(song.get("year") or str(song.get("created_at") or "")[:4]), lyrics=str(song.get("lyrics") or ""),
            comment=str(song.get("prompt") or song.get("notes") or ""), cover_bytes=cover_bytes,
            track_number=str(song.get("track_number") or ""), copyright_text=str(song.get("copyright") or ""),
            website=str(song.get("website") or song.get("youtube_url") or ""), bpm=str(song.get("bpm") or ""),
            key_signature=str(song.get("key_signature") or ""),
        )

    result_options = dict(options)
    result_options["processing_method"] = result.get("method")
    result_options["elapsed_seconds"] = result.get("elapsed_seconds")
    full_duration = float(song.get("duration") or end)
    kind = "trimmed" if start > 0 or end < full_duration else "processed"
    file_id = DB.add_derived_file(
        song_id, kind, label, str(output), output_format, float(result.get("duration") or 0), result_options,
    )
    if options.get("replace_primary"):
        if output_format == "wav":
            DB.update_song_files(song_id, local_wav=str(output), file_sha256=_sha256(output), downloaded_at=now_iso())
        elif output_format == "mp3":
            DB.update_song_files(song_id, local_audio=str(output), file_sha256=_sha256(output), downloaded_at=now_iso())
    collection_id = options.get("add_to_collection_id")
    if collection_id:
        DB.add_songs_to_collection(int(collection_id), [song_id])
    if options.get("mark_long_done"):
        collection = DB.get_collection_by_slug("odrađene-duže-pesme")
        if collection:
            DB.add_songs_to_collection(int(collection["id"]), [song_id])
    method_text = "brzo isecanje bez ponovnog kodiranja" if result.get("method") == "stream_copy" else "precizna obrada sa ponovnim kodiranjem"
    elapsed = float(result.get("elapsed_seconds") or 0)
    task.log(f"Završeno „{title}“: {method_text}, {elapsed:.2f} s.", "success")
    return {"file_id": file_id, "path": str(output), "title": title, **result}


def process_audio_task(task: TaskState, song_id: str, options: dict[str, Any]) -> None:
    try:
        result = _process_audio_one(task, song_id, options, 1, 1)
    except AudioCancelled:
        task.finish("Audio obrada je zaustavljena.")
        return
    method = "brzo isecanje" if result.get("method") == "stream_copy" else "obrada sa efektima"
    task.current = str(result.get("file_id") or "")
    task.set_progress(1, 1, str(result.get("title") or "Audio obrada"))
    task.finish(f"Nova audio-verzija je napravljena ({method}, {float(result.get('elapsed_seconds') or 0):.2f} s): {Path(str(result['path'])).name}")


def process_audio_batch_task(task: TaskState, song_ids: list[str], options: dict[str, Any]) -> None:
    unique_ids = list(dict.fromkeys(str(x) for x in song_ids if str(x)))
    if not unique_ids:
        raise RuntimeError("Izaberi bar jednu pesmu za masovnu obradu.")
    successful = 0
    fast = 0
    failed = 0
    elapsed_total = 0.0
    task.total = len(unique_ids)
    for index, song_id in enumerate(unique_ids, 1):
        if task.cancel_event.is_set():
            break
        try:
            result = _process_audio_one(task, song_id, dict(options), index, len(unique_ids))
            successful += 1
            fast += 1 if result.get("method") == "stream_copy" else 0
            elapsed_total += float(result.get("elapsed_seconds") or 0)
        except AudioCancelled:
            task.cancel_event.set()
            break
        except Exception as exc:
            failed += 1
            song = DB.get_song(song_id) or {}
            title = str(song.get("title") or song_id)
            task.errors.append(f"{title}: {exc}")
            task.log(f"Nije obrađena „{title}“: {exc}", "error")
        task.set_progress(index, len(unique_ids), str((DB.get_song(song_id) or {}).get("title") or song_id))
    if task.cancel_event.is_set():
        task.finish(f"Masovna obrada je zaustavljena. Završeno {successful}/{len(unique_ids)} pesama.")
        return
    task.finish(
        f"Masovna obrada završena: uspešno {successful}/{len(unique_ids)}, brzo isečeno {fast}, "
        f"greške {failed}, ukupno FFmpeg vreme {elapsed_total:.2f} s."
    )

def write_song_tags(song_id: str, backup: bool = True) -> dict[str, Any]:
    song = DB.get_song(song_id)
    if not song:
        raise RuntimeError("Pesma nije pronađena.")
    audio_raw = str(song.get("local_audio") or "")
    if not audio_raw or not Path(audio_raw).exists():
        raise RuntimeError("Za upis MP3 podataka prvo preuzmi MP3 verziju pesme.")
    audio = Path(audio_raw)
    if backup:
        backup_path = _unique_path(audio.with_suffix(".pre-tag-backup.mp3"))
        shutil.copy2(audio, backup_path)
    cover_bytes = b""
    cover_raw = str(song.get("local_cover") or "")
    if cover_raw and Path(cover_raw).exists():
        cover_bytes = Path(cover_raw).read_bytes()
    elif song.get("image_url"):
        try:
            req = urllib.request.Request(str(song["image_url"]), headers={"User-Agent": SESSION_USER_AGENT or "Mozilla/5.0"})
            with urllib.request.urlopen(req, timeout=30) as response:
                cover_bytes = response.read(12 * 1024 * 1024)
        except Exception:
            cover_bytes = b""
    embed_mp3_metadata(
        audio,
        title=str(song.get("title") or ""),
        artist=str(song.get("display_name") or "Nedostaješ PUNOO"),
        album=str(song.get("album") or "Suno pesme"),
        genre=str(song.get("genre") or song.get("tags") or ""),
        year=str(song.get("year") or str(song.get("created_at") or "")[:4]),
        lyrics=str(song.get("lyrics") or ""),
        comment=str(song.get("prompt") or song.get("notes") or ""),
        cover_bytes=cover_bytes,
        track_number=str(song.get("track_number") or ""),
        copyright_text=str(song.get("copyright") or ""),
        website=str(song.get("website") or song.get("youtube_url") or ""),
        bpm=str(song.get("bpm") or ""),
        key_signature=str(song.get("key_signature") or ""),
    )
    DB.update_song_files(song_id, file_sha256=_sha256(audio), downloaded_at=str(song.get("downloaded_at") or now_iso()))
    return {"path": str(audio), "backup": str(backup_path) if backup else ""}


def rename_song_files(song_id: str) -> dict[str, Any]:
    song = DB.get_song(song_id)
    if not song:
        raise RuntimeError("Pesma nije pronađena.")
    base = f"{sanitize_filename(str(song.get('title') or song_id))} [{song_id[:8]}]"
    updates: dict[str, str] = {}
    for key in ("local_audio", "local_wav", "local_video", "local_cover", "local_lyrics", "local_lrc", "local_srt"):
        raw = str(song.get(key) or "")
        if not raw:
            continue
        old = Path(raw)
        if not old.exists() or not old.is_file():
            continue
        new = old.with_name(base + old.suffix.lower())
        if new.resolve() != old.resolve():
            new = _unique_path(new)
            old.rename(new)
        updates[key] = str(new)
    if updates:
        DB.update_song_files(song_id, **updates)
    return DB.get_song(song_id) or song

def safe_media_path(raw: str) -> Path:
    if not raw:
        raise PermissionError("Nije navedena putanja fajla.")
    path = Path(raw).resolve()
    allowed_roots = [get_download_dir().resolve(), DEFAULT_DOWNLOAD_DIR.resolve(), EXPORT_DIR.resolve()]
    inside_program = any(path == root or root in path.parents for root in allowed_roots)
    if not inside_program and not DB.is_known_local_file(str(path)):
        raise PermissionError("Fajl nije u dozvoljenom folderu programa niti je zabeležen u biblioteci.")
    return path


def export_library(ids: list[str] | None, fmt: str) -> Path:
    rows = DB.export_rows(ids)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S-%f")
    if fmt == "csv":
        path = _unique_path(EXPORT_DIR / f"Suno-biblioteka-{stamp}.csv")
        fields = [
            "id", "title", "display_name", "album", "genre", "year", "track_number", "bpm", "key_signature",
            "created_at", "duration", "model_version", "tags", "prompt", "lyrics", "source_url", "source_group",
            "is_liked", "is_public", "favorite", "rating", "notes", "custom_tags", "youtube_url", "youtube_published_at",
            "local_audio", "local_wav", "local_video", "local_cover", "local_lyrics", "local_lrc", "local_srt",
            "collections", "derived_files",
        ]
        with path.open("w", newline="", encoding="utf-8-sig") as fh:
            writer = csv.DictWriter(fh, fieldnames=fields, extrasaction="ignore")
            writer.writeheader()
            for row in rows:
                item = dict(row)
                item["collections"] = json.dumps(item.get("collections") or [], ensure_ascii=False)
                item["derived_files"] = json.dumps(item.get("derived_files") or [], ensure_ascii=False)
                writer.writerow(item)
        return path
    if fmt == "zip":
        path = _unique_path(EXPORT_DIR / f"Suno-paket-{stamp}.zip")
        used_names: set[str] = set()
        with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED, allowZip64=True) as archive:
            archive.writestr("biblioteka.json", json.dumps(rows, ensure_ascii=False, indent=2))
            archive.writestr("README.txt", "Kompletan izvoz biblioteke. Svaka pesma je u posebnom folderu prema naslovu i ID-u.\n")
            for row in rows:
                folder = f"fajlovi/{sanitize_filename(str(row.get('title') or row.get('id')), 90)} [{str(row.get('id') or '')[:8]}]"
                paths: list[str] = []
                for key in ("local_audio", "local_wav", "local_video", "local_cover", "local_lyrics", "local_lrc", "local_srt"):
                    value = str(row.get(key) or "")
                    if value:
                        paths.append(value)
                for item in row.get("derived_files") or []:
                    value = str(item.get("path") or "")
                    if value:
                        paths.append(value)
                for value in paths:
                    file_path = Path(value)
                    if not file_path.exists() or not file_path.is_file():
                        continue
                    arcname = f"{folder}/{file_path.name}"
                    candidate = arcname
                    index = 2
                    while candidate in used_names:
                        candidate = f"{folder}/{file_path.stem} ({index}){file_path.suffix}"
                        index += 1
                    used_names.add(candidate)
                    archive.write(file_path, arcname=candidate)
        return path
    path = _unique_path(EXPORT_DIR / f"Suno-biblioteka-{stamp}.json")
    path.write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding="utf-8")
    return path


def _dir_size(path: Path, limit_files: int = 200000) -> tuple[int, int]:
    total = 0
    count = 0
    try:
        for item in path.rglob("*"):
            if count >= limit_files:
                break
            if item.is_file():
                count += 1
                try:
                    total += item.stat().st_size
                except OSError:
                    pass
    except Exception:
        pass
    return total, count


def storage_summary() -> dict[str, Any]:
    download_size, download_files = _dir_size(get_download_dir())
    export_size, export_files = _dir_size(EXPORT_DIR)
    data_size, data_files = _dir_size(DATA_DIR)
    return {
        "download_dir": str(get_download_dir()), "download_bytes": download_size, "download_files": download_files,
        "export_dir": str(EXPORT_DIR), "export_bytes": export_size, "export_files": export_files,
        "data_dir": str(DATA_DIR), "data_bytes": data_size, "data_files": data_files,
        "database_bytes": DB.db_path.stat().st_size if DB.db_path.exists() else 0,
    }


def run_self_test(include_audio: bool = True) -> dict[str, Any]:
    checks: list[dict[str, Any]] = []

    def add(name: str, status: str, detail: str, **extra: Any) -> None:
        checks.append({"name": name, "status": status, "detail": detail, **extra})

    # POKRENI_PROGRAM.bat / ZAUSTAVI_PROGRAM.bat / PRIMENI_AZURIRANJE.bat
    # used to be checked here too, but that .bat-launcher architecture was
    # replaced by the native Go launcher/installer/uninstaller
    # (windows_build/) -- those files are never created by the current
    # installer (installer/SunoPesmeStudio.iss, which referenced them, was
    # deleted for the same reason), so this check was permanently
    # reporting "fail" against files the shipped app was never going to
    # have, confirmed live via CI's http_integration_v300.py self-test run.
    required = [WEB_DIR / "index.html", WEB_DIR / "app.js", WEB_DIR / "style.css", ROOT / "app" / "youtube_oauth.py", ROOT / "app" / "youtube_tools.py", ROOT / "app" / "advanced_features.py", ROOT / "app" / "suno_compat.py", ROOT / "plugins" / "stems_worker.py", ROOT / "plugins" / "transcribe_worker.py"]
    missing = [str(path.relative_to(ROOT)) for path in required if not path.exists()]
    add("Programski fajlovi", "pass" if not missing else "fail", "Svi obavezni fajlovi postoje." if not missing else "Nedostaje: " + ", ".join(missing))


    try:
        fixtures = sorted((ROOT / "tests" / "fixtures").glob("suno_*.json"))
        fixture_results = []
        for fixture in fixtures:
            payload = json.loads(fixture.read_text(encoding="utf-8"))
            result = validate_fixture(payload, extract_items)
            if result.get("count", 0) < 1:
                raise RuntimeError(f"{fixture.name}: nije pronađena test pesma")
            fixture_results.append({"name": fixture.name, **result})
        add("Suno API kompatibilni sloj", "pass" if fixture_results else "warn", f"Provereno testnih Suno formata: {len(fixture_results)}." if fixture_results else "Nema testnih Suno fixture fajlova.", data=fixture_results)
    except Exception as exc:
        add("Suno API kompatibilni sloj", "fail", str(exc))

    try:
        with tempfile.TemporaryDirectory(prefix="suno_db_features_") as tmp_db:
            test_db = LibraryDB(Path(tmp_db) / "test.db")
            test_db.save_sync_checkpoint("fixture", "Test fixture", "cursor-1", 2, 50, mode="test", completed=False)
            job_id = test_db.enqueue_job("test", "Test posao", {"x": 1})
            test_db.update_job(job_id, status="paused", progress=25, message="test")
            test_db.upsert_schedule("test", "Test raspored", 30, True, {"x": 1})
            if not test_db.list_sync_checkpoints() or not test_db.list_jobs() or not test_db.list_schedules():
                raise RuntimeError("Napredne tabele nisu vratile test podatke.")
        add("Nastavak, red poslova i raspored", "pass", "Checkpoint, persistent job queue i scheduler tabele rade u čistoj test bazi.")
    except Exception as exc:
        add("Nastavak, red poslova i raspored", "fail", str(exc))

    try:
        with tempfile.TemporaryDirectory(prefix="suno_old_schema_") as tmp_old:
            old_path = Path(tmp_old) / "stara.db"
            conn = sqlite3.connect(old_path)
            try:
                conn.executescript("CREATE TABLE songs(id TEXT PRIMARY KEY,title TEXT); CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT); INSERT INTO songs VALUES('old-test','Stara pesma');")
                conn.commit()
            finally:
                conn.close()
            migrated = LibraryDB(old_path)
            migrated_song = migrated.get_song("old-test")
            migrated_integrity = migrated.integrity_check(quick=False)
            if not migrated_song or not migrated_integrity.get("ok"):
                raise RuntimeError("Migracija stare baze nije sačuvala pesmu ili integritet.")
        add("Migracija stare baze", "pass", "Vrlo stara minimalna SQLite šema je uspešno proširena bez gubitka pesme.")
    except Exception as exc:
        add("Migracija stare baze", "fail", str(exc))

    integrity = DB.integrity_check(quick=True)
    add("SQLite baza", "pass" if integrity.get("ok") else "fail", "Baza je ispravna." if integrity.get("ok") else "; ".join(integrity.get("messages") or ["Nepoznata greška"]), data=integrity)

    for label, folder in (("Data folder", DATA_DIR), ("Folder za preuzimanje", get_download_dir()), ("Folder za izvoz", EXPORT_DIR)):
        try:
            folder.mkdir(parents=True, exist_ok=True)
            probe = folder / f".suno-write-test-{uuid.uuid4().hex}.tmp"
            probe.write_bytes(b"ok")
            probe.unlink()
            add(label, "pass", f"Upis radi: {folder}")
        except Exception as exc:
            add(label, "fail", f"Nema dozvole za upis: {folder} — {exc}")

    health = DB.local_file_health(repair=False)
    add("Lokalne putanje", "pass" if not health.get("missing_count") else "warn", "Sve evidentirane putanje postoje." if not health.get("missing_count") else f"Nedostaje {health.get('missing_count')} lokalnih fajlova.", data=health)

    tools = audio_tools_status()
    if tools.get("ready"):
        add("FFmpeg/FFprobe", "pass", str(tools.get("version") or "Audio alati su spremni."))
    else:
        add("FFmpeg/FFprobe", "warn", "Audio alati još nisu preuzeti. Biće preuzeti pri prvom korišćenju audio-obrade.")

    if include_audio and tools.get("ready"):
        try:
            with tempfile.TemporaryDirectory(prefix="suno_selftest_audio_") as tmp_raw:
                tmp = Path(tmp_raw)
                source = tmp / "ulaz.mp3"
                output = tmp / "isecak.mp3"
                ffmpeg = str(ffmpeg_path())
                command = [ffmpeg, "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "sine=frequency=440:duration=2", "-c:a", "libmp3lame", "-b:a", "128k", "-y", str(source)]
                completed = subprocess.run(command, capture_output=True, text=True, timeout=30, creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" and hasattr(subprocess, "CREATE_NO_WINDOW") else 0)
                if completed.returncode != 0 or not source.exists():
                    raise RuntimeError((completed.stderr or "FFmpeg nije napravio test audio.")[-800:])
                result = process_audio(source, output, {"start": 0.25, "end": 1.25, "format": "mp3", "processing_mode": "quick", "fade_in": 0, "fade_out": 0, "volume_db": 0, "speed": 1, "normalize": False, "remove_silence": False})
                info = probe_audio(output)
                if not output.exists() or output.stat().st_size < 1024 or float(info.get("duration") or 0) <= 0:
                    raise RuntimeError("Test isecanja nije napravio ispravan izlazni MP3.")
                add("Audio reprodukcija/obrada", "pass", f"Test MP3 je napravljen i isečen ({float(result.get('elapsed_seconds') or 0):.2f} s).", data=info)
        except Exception as exc:
            add("Audio reprodukcija/obrada", "fail", str(exc))

    if include_audio and tools.get("ready"):
        try:
            import math as _math
            import wave as _wave
            with tempfile.TemporaryDirectory(prefix="suno_selftest_match_") as tmp_raw:
                tmp = Path(tmp_raw)
                original_wav = tmp / "original.wav"
                original_mp3 = tmp / "original.mp3"
                youtube_full = tmp / "youtube-full.m4a"
                youtube_part = tmp / "youtube-part.m4a"
                sample_rate = 8000
                duration = 30
                with _wave.open(str(original_wav), "wb") as wav:
                    wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(sample_rate)
                    data = bytearray()
                    for index in range(sample_rate * duration):
                        t = index / sample_rate
                        segment = int(t // 3)
                        freq = 170 + (segment * 83) % 900
                        amp = 0.22 + 0.08 * _math.sin(t * 0.7)
                        value = amp * _math.sin(2 * _math.pi * freq * t) + 0.06 * _math.sin(2 * _math.pi * (freq * 1.7) * t)
                        data += int(max(-0.9, min(0.9, value)) * 32767).to_bytes(2, "little", signed=True)
                    wav.writeframes(data)
                ffmpeg = str(ffmpeg_path())
                for command in (
                    [ffmpeg, "-hide_banner", "-loglevel", "error", "-i", str(original_wav), "-c:a", "libmp3lame", "-b:a", "128k", "-y", str(original_mp3)],
                    [ffmpeg, "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "sine=frequency=950:duration=3", "-i", str(original_mp3), "-f", "lavfi", "-i", "sine=frequency=310:duration=2", "-filter_complex", "[0:a][1:a][2:a]concat=n=3:v=0:a=1[a]", "-map", "[a]", "-c:a", "aac", "-b:a", "128k", "-y", str(youtube_full)],
                    [ffmpeg, "-hide_banner", "-loglevel", "error", "-ss", "8", "-i", str(original_mp3), "-t", "15", "-c:a", "aac", "-b:a", "128k", "-y", str(youtube_part)],
                ):
                    done = subprocess.run(command, capture_output=True, text=True, timeout=60, creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" and hasattr(subprocess, "CREATE_NO_WINDOW") else 0)
                    if done.returncode != 0:
                        raise RuntimeError((done.stderr or "FFmpeg test audio greška")[-800:])
                source_signature = extract_signature(original_mp3)
                full_result = compare_signatures(source_signature, extract_signature(youtube_full))
                part_result = compare_signatures(source_signature, extract_signature(youtube_part))
                if full_result.get("completeness_status") != "complete" or float(full_result.get("coverage_percent") or 0) < 92:
                    raise RuntimeError(f"Kompletna test pesma nije prepoznata: {full_result}")
                if part_result.get("completeness_status") not in ("partial", "short_clip") or float(part_result.get("coverage_percent") or 100) >= 80:
                    raise RuntimeError(f"Test isečak nije pravilno klasifikovan: {part_result}")
                add("YouTube ↔ Suno audio prepoznavanje", "pass", f"Kompletna test pesma: {full_result.get('coverage_percent')}%; isečak: {part_result.get('coverage_percent')}%.")
        except Exception as exc:
            add("YouTube ↔ Suno audio prepoznavanje", "fail", str(exc))

    ytdlp = ytdlp_status()
    add("yt-dlp", "pass" if ytdlp.get("ready") else "warn", f"yt-dlp je spreman: {ytdlp.get('version') or ytdlp.get('path')}" if ytdlp.get("ready") else "yt-dlp još nije preuzet. Priprema se iz Pametnih alata pre prve YouTube audio analize.")

    plugins = plugin_status(ROOT)
    ready_optional = sum(1 for item in plugins.values() if item.get("installed"))
    add("Opcioni AI/fingerprint dodaci", "pass" if ready_optional else "warn", f"Instalirano opcionih dodataka: {ready_optional}/4. Osnovni program radi i bez njih.", data=plugins)

    browser = CONNECTOR.find_browser()
    add("Chrome/Edge/Brave", "pass" if browser else "warn", f"Pronađen browser: {browser}" if browser else "Browser nije pronađen u standardnim Windows lokacijama. Ručni Suno token ostaje rezervna opcija.")
    add("Suno sesija", "pass" if SESSION_TOKEN else "warn", "Suno nalog je povezan u ovoj sesiji." if SESSION_TOKEN else "Suno nalog trenutno nije povezan; to nije kvar lokalnog programa.")
    keeper_alive = bool(SESSION_KEEPALIVE_THREAD and SESSION_KEEPALIVE_THREAD.is_alive())
    add("Suno automatsko održavanje sesije", "pass" if keeper_alive else "fail", f"Session keeper radi; interval {_suno_keepalive_minutes()} min, uključeno: {_suno_keepalive_enabled()}." if keeper_alive else "Pozadinski session keeper nije pokrenut.", data=suno_session_public_state())
    oauth = YOUTUBE_OAUTH.status()
    if oauth.get("connected"):
        add("YouTube Google prijava", "pass", f"Povezano Google naloga: {oauth.get('profile_count', 0)}; kanala: {oauth.get('channel_count', 0)}.")
    elif oauth.get("configured"):
        add("YouTube Google prijava", "warn", "Google OAuth je pripremljen, ali nalog još nije povezan. Klikni „Poveži YouTube preko Google naloga“.")
    else:
        add("YouTube Google prijava", "warn", "Google OAuth još nije pripremljen. Jednom izaberi Desktop OAuth JSON, zatim se povezivanje radi izborom mejla.")
    add("YouTube napredni API ključ", "pass" if get_youtube_api_key() else "warn", "Rezervni API ključ je sačuvan." if get_youtube_api_key() else "API ključ nije potreban kada je Google nalog povezan preko OAuth-a.")

    failed = sum(1 for item in checks if item["status"] == "fail")
    warnings = sum(1 for item in checks if item["status"] == "warn")
    passed = sum(1 for item in checks if item["status"] == "pass")
    return {
        "ok": failed == 0, "version": APP_VERSION, "created_at": now_iso(), "passed": passed,
        "warnings": warnings, "failed": failed, "checks": checks, "database_counts": DB.database_counts(),
        "storage": storage_summary(), "python": platform.python_version(), "platform": platform.platform(),
    }


def create_diagnostics_package(include_audio_test: bool = True) -> Path:
    report = run_self_test(include_audio=include_audio_test)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S-%f")
    path = _unique_path(EXPORT_DIR / f"Suno-Pesme-Studio-dijagnostika-{stamp}.zip")
    safe_settings = {
        "download_dir": str(get_download_dir()), "theme": DB.get_setting("theme", "default"),
        "auto_check_minutes": DB.get_setting("auto_check_minutes", "0"),
        "auto_backup_before_sync": DB.get_setting("auto_backup_before_sync", "0"),
        "suno_keepalive_enabled": _suno_keepalive_enabled(), "suno_keepalive_minutes": _suno_keepalive_minutes(), "suno_auto_reopen_browser": _suno_auto_reopen_browser(),
        "has_youtube_api_key": bool(get_youtube_api_key()), "youtube_oauth": YOUTUBE_OAUTH.status(), "copyright_owner_name_set": bool(DB.get_setting("copyright_owner_name", "")),
        "suno_connected": bool(SESSION_TOKEN), "suno_session": suno_session_public_state(), "ytdlp": ytdlp_status(),
        "youtube_audio_summary": DB.youtube_audio_summary(),
    }
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED, allowZip64=True) as archive:
        archive.writestr("DIJAGNOSTIKA.json", json.dumps({"self_test": report, "settings": safe_settings}, ensure_ascii=False, indent=2, default=str))
        archive.writestr("APP_LOG.json", json.dumps(DB.logs(limit=1000), ensure_ascii=False, indent=2, default=str))
        if RUNTIME_LOG.exists():
            raw = RUNTIME_LOG.read_bytes()
            archive.writestr("program.log", raw[-2_000_000:])
        archive.writestr(
            "README.txt",
            "Paket ne sadrži Suno token, Google OAuth tokene niti vrednost YouTube API ključa. Sadrži tehničke provere, verziju, logove i redigovana podešavanja.\n",
        )
    return path



def advanced_quality_task(task: TaskState, options: dict[str, Any]) -> None:
    ids = [str(x) for x in (options.get("ids") or []) if str(x)]
    if not ids:
        ids = [str(r.get("id")) for r in DB.export_rows() if str(r.get("local_audio") or r.get("local_wav") or "")]
    task.total = len(ids)
    rows = []
    for idx, song_id in enumerate(ids, 1):
        if task.cancel_event.is_set(): break
        wait_if_paused(task)
        song = DB.get_song(song_id) or {}
        raw = str(song.get("local_audio") or song.get("local_wav") or "")
        if not raw or not Path(raw).exists():
            task.log(f"{song.get('title') or song_id}: nema lokalni audio.", "warning")
            task.set_progress(idx, len(ids), str(song.get("title") or song_id)); continue
        try:
            result = analyze_audio_quality(Path(raw))
            rows.append({"id": song_id, "title": song.get("title"), **result})
            task.log(f"{song.get('title')}: kvalitet {result.get('score')}/100.", "success" if result.get("ready") else "warning")
        except Exception as exc:
            task.errors.append(str(exc)); task.log(f"{song.get('title') or song_id}: {exc}", "error")
        task.set_progress(idx, len(ids), str(song.get("title") or song_id))
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S-%f")
    report = _unique_path(EXPORT_DIR / f"Audio-kvalitet-{stamp}.json")
    report.write_text(json.dumps(rows, ensure_ascii=False, indent=2, default=str), encoding="utf-8")
    task.finish(f"Analiza kvaliteta završena: {len(rows)}/{len(ids)}. Izveštaj: {report.name}")


def incremental_backup_task(task: TaskState, options: dict[str, Any]) -> None:
    target = Path(str(options.get("target") or options.get("folder") or DB.get_setting("incremental_backup_dir", str(EXPORT_DIR / "Inkrementalni_backup"))))
    DB.set_setting("incremental_backup_dir", str(target))
    def progress(name: str, done: int, total: int) -> None:
        task.set_progress(done, total, name)
    result = create_incremental_backup(DB, target, include_files=bool(options.get("include_files", True)), progress=progress)
    task.finish(f"Inkrementalni backup je napravljen: {result['copied']} novih, {result['reused']} već postojećih fajlova.")


def relocate_files_task(task: TaskState, options: dict[str, Any]) -> None:
    raw_roots = options.get("roots") or ([options.get("root")] if options.get("root") else [])
    roots = [Path(str(x)) for x in raw_roots if str(x)]
    if not roots:
        raise RuntimeError("Izaberi folder u kojem program treba da traži premeštene fajlove.")
    def progress(name: str, done: int, total: int) -> None:
        task.set_progress(done, total, name)
    result = relocate_missing_files(DB, roots, progress=progress)
    task.finish(f"Popravka putanja završena: pronađeno {result['fixed']} od {result['missing']} nestalih fajlova.")


def install_ai_plugin_task(task: TaskState, options: dict[str, Any]) -> None:
    component = str(options.get("component") or "")
    task.total = 1
    result = install_ai_plugin(ROOT, component, progress=lambda message: task.log(message))
    task.set_progress(1, 1, component)
    task.finish(f"{component}: instalacija je završena ({result['target']}).")


def stem_task(task: TaskState, options: dict[str, Any]) -> None:
    ids = [str(x) for x in (options.get("ids") or []) if str(x)]
    if not ids and options.get("id"):
        ids = [str(options.get("id"))]
    if not ids:
        raise RuntimeError("Izaberi bar jednu pesmu.")
    task.total = len(ids)
    made = 0
    for idx, song_id in enumerate(ids, 1):
        if task.cancel_event.is_set(): break
        wait_if_paused(task)
        song = DB.get_song(song_id) or {}
        source = Path(str(song.get("local_audio") or song.get("local_wav") or ""))
        if not source.exists():
            task.log(f"{song.get('title') or song_id}: pesma prvo mora da bude preuzeta lokalno.", "warning")
            task.set_progress(idx, len(ids), str(song.get('title') or song_id)); continue
        output = Path(str(options.get("target") or (get_download_dir() / "Stemovi" / sanitize_filename(str(song.get('title') or song_id)))))
        task.log(f"{song.get('title')}: pokrećem stem model. Prva obrada može dugo da traje.")
        result = run_stem_separation(ROOT, source, output, model=str(options.get("model") or "htdemucs"))
        for name, raw in (result.get("files") or {}).items():
            path = Path(str(raw))
            if path.exists():
                DB.add_derived_file(song_id, "stem", str(name), str(path), path.suffix.lstrip('.'), float(song.get("duration") or 0), {"model": options.get("model") or "htdemucs"})
                made += 1
        task.set_progress(idx, len(ids), str(song.get('title') or song_id))
    task.finish(f"Stem obrada je završena: {made} izlaznih fajlova za {len(ids)} pesama.")


def transcription_task(task: TaskState, options: dict[str, Any]) -> None:
    ids = [str(x) for x in (options.get("ids") or []) if str(x)]
    if not ids and options.get("id"):
        ids = [str(options.get("id"))]
    if not ids:
        raise RuntimeError("Izaberi bar jednu pesmu.")
    task.total = len(ids)
    completed = 0
    for idx, song_id in enumerate(ids, 1):
        if task.cancel_event.is_set(): break
        wait_if_paused(task)
        song = DB.get_song(song_id) or {}
        source = Path(str(song.get("local_audio") or song.get("local_wav") or ""))
        if not source.exists():
            task.log(f"{song.get('title') or song_id}: nema lokalni audio.", "warning")
            task.set_progress(idx, len(ids), str(song.get('title') or song_id)); continue
        result = run_transcription(ROOT, source, language=str(options.get("language") or "auto"), model=str(options.get("model") or "small"))
        text = str(result.get("text") or "").strip()
        if not text:
            task.log(f"{song.get('title') or song_id}: model nije vratio tekst.", "warning")
            task.set_progress(idx, len(ids), str(song.get('title') or song_id)); continue
        target = source.with_name(source.stem + ".transkripcija.txt")
        target.write_text(text, encoding="utf-8")
        metadata = {"language": result.get("language") or options.get("language") or "auto", "model": options.get("model") or "small", "device": result.get("device") or "", "language_probability": result.get("language_probability") or 0}
        DB.add_derived_file(song_id, "transcription", "Lokalna transkripcija", str(target), "txt", float(song.get("duration") or 0), metadata)
        if bool(options.get("align_original_lyrics", True)) and str(song.get("lyrics") or "").strip():
            cues = align_original_lyrics(str(song.get("lyrics") or ""), result, float(song.get("duration") or 0))
            if cues:
                paths = save_subtitle_files(song, cues, DB, source.parent, get_download_dir() / "Tekstovi")
                task.log(f"{song.get('title') or song_id}: originalni tekst je vremenski poravnat; LRC/SRT su sačuvani.", "success")
        if bool(options.get("replace_lyrics", False)):
            DB.update_song_user_fields(song_id, {"lyrics": text})
        completed += 1
        task.set_progress(idx, len(ids), str(song.get('title') or song_id))
    task.finish(f"Lokalna transkripcija je završena: {completed}/{len(ids)}. Originalni Suno tekst nije zamenjen bez izričite opcije.")


def _vtt_to_text(raw: str) -> str:
    lines: list[str] = []
    last = ""
    skip_note = False
    for line in raw.replace("\r", "").splitlines():
        clean = line.strip()
        if clean.startswith("NOTE"):
            skip_note = True
            continue
        if skip_note:
            if not clean:
                skip_note = False
            continue
        if not clean or clean.startswith(("WEBVTT", "Kind:", "Language:", "STYLE", "REGION")) or "-->" in clean or clean.isdigit():
            continue
        clean = html.unescape(re.sub(r"<[^>]+>", "", clean)).strip()
        if clean and clean != last:
            lines.append(clean)
            last = clean
    return "\n".join(lines)


def youtube_transcript(video_url: str, languages: str = "sr,en") -> str:
    tool = ytdlp_path() or str(ensure_ytdlp().get("path") or "")
    if not tool: raise RuntimeError("yt-dlp nije spreman.")
    with tempfile.TemporaryDirectory(prefix="suno_subs_") as tmp:
        template = str(Path(tmp) / "subtitle.%(ext)s")
        cookie_args = ["--cookies-from-browser", youtube_cookie_browser()] if youtube_cookie_browser() else []
        cmd=[tool,"--no-playlist","--skip-download","--write-subs","--write-auto-subs","--sub-langs",languages,"--sub-format","vtt","-o",template,*cookie_args,video_url]
        proc=subprocess.run(cmd,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,encoding="utf-8",errors="replace",timeout=300,creationflags=(subprocess.CREATE_NO_WINDOW if os.name=="nt" else 0))
        files=list(Path(tmp).glob("*.vtt"))
        if not files: raise RuntimeError("YouTube video nema dostupne titlove/transkript. " + (proc.stdout or "")[-300:])
        return _vtt_to_text(files[0].read_text(encoding="utf-8",errors="replace"))


def run_persistent_job(job_id: int) -> TaskState:
    job = DB.get_job(job_id)
    if not job: raise RuntimeError("Posao ne postoji.")
    payload = job.get("payload") or {}
    task_type = str(job.get("task_type") or "")
    mapping: dict[str, tuple[str, Callable[[TaskState], None]]] = {
        "sync": ("Sinhronizacija Suno biblioteke", lambda t: sync_library(t, payload)),
        "download": ("Preuzimanje pesama", lambda t: download_songs(t, [str(x) for x in payload.get("ids") or []], payload.get("options") or {})),
        "audio_process": ("Skraćivanje i obrada pesme", lambda t: process_audio_task(t, str(payload.get("id") or ""), payload.get("options") or {})),
        "audio_process_batch": ("Masovna audio obrada", lambda t: process_audio_batch_task(t, [str(x) for x in payload.get("ids") or []], payload.get("options") or {})),
        "youtube_owned": ("Provera mojih YouTube kanala", lambda t: scan_owned_youtube_channels(t, payload)),
        "youtube_audio_owned": ("YouTube ↔ Suno audio analiza", lambda t: analyze_owned_youtube_audio(t, payload)),
        "incremental_backup": ("Inkrementalni backup", lambda t: incremental_backup_task(t, payload)),
        "audio_quality": ("Analiza kvaliteta", lambda t: advanced_quality_task(t, payload)),
        "check_new": ("Provera novih Suno pesama", lambda t: check_new_songs(t, payload)),
        "relocate_files": ("Pronalaženje premeštenih fajlova", lambda t: relocate_files_task(t, payload)),
        "stems": ("Izdvajanje vokala i instrumentala", lambda t: stem_task(t, payload)),
        "transcription": ("Lokalna transkripcija", lambda t: transcription_task(t, payload)),
        "youtube_global": ("Globalna YouTube pretraga", lambda t: scan_global_youtube(t, payload)),
        "v3_integrity": ("Kompletna kontrola integriteta", lambda t: v3_integrity_task(t, payload)),
        "v3_organize": ("Organizovanje biblioteke", lambda t: v3_organize_task(t, payload)),
        "v3_shorts": ("Pravljenje Shorts isečaka", lambda t: v3_shorts_render_task(t, payload)),
        "v3_lyric_video": ("Pravljenje lyric videa", lambda t: v3_lyric_video_task(t, payload)),
        "v3_youtube_upload": ("YouTube upload", lambda t: v3_youtube_upload_task(t, payload)),
        "v3_panako_index": ("Panako Content-ID indeks", lambda t: v3_panako_index_task(t, payload)),
        "v3_snapshot": ("Snapshot cele verzije programa", lambda t: v3_snapshot_task(t, payload)),
    }
    if task_type not in mapping: raise RuntimeError(f"Nastavak za tip posla „{task_type}“ nije podržan.")
    title, runner = mapping[task_type]
    return start_task(task_type, title, runner, job_id=job_id)


def scheduler_loop() -> None:
    runtime_log("Planer poslova je pokrenut.")
    while not SCHEDULER_STOP.wait(30):
        try:
            with STATE_LOCK:
                busy = bool(ACTIVE_TASK and ACTIVE_TASK.status == "running")
            if busy: continue
            now = datetime.now(timezone.utc)
            for schedule in DB.list_schedules():
                if not int(schedule.get("enabled") or 0): continue
                last_raw = str(schedule.get("last_run_at") or "")
                last = None
                if last_raw:
                    try:
                        last = datetime.fromisoformat(last_raw.replace("Z", "+00:00"))
                        if last.tzinfo is None:
                            last = last.replace(tzinfo=timezone.utc)
                    except Exception: last = None
                interval = max(5, int(schedule.get("interval_minutes") or 60))
                if last and (now - last).total_seconds() < interval * 60: continue
                task_type = str(schedule.get("task_type") or "")
                options = schedule.get("options") or {}
                if task_type == "youtube_owned":
                    start_task("youtube_owned", "Automatska provera YouTube objava", lambda t, o=options: scan_owned_youtube_channels(t, o), persistent_payload=options)
                elif task_type == "check_new":
                    start_task("check_new", "Automatska provera novih Suno pesama", lambda t, o=options: check_new_songs(t, o))
                elif task_type == "incremental_backup":
                    start_task("incremental_backup", "Automatski inkrementalni backup", lambda t, o=options: incremental_backup_task(t, o), persistent_payload=options)
                elif task_type == "youtube_global":
                    start_task("youtube_global", "Kontinuirana YouTube provera", lambda t, o=options: scan_global_youtube(t, o), persistent_payload=options)
                elif task_type == "v3_integrity":
                    start_task("v3_integrity", "Automatska kontrola integriteta", lambda t, o=options: v3_integrity_task(t, o), persistent_payload=options)
                DB.mark_schedule_run(int(schedule.get("id") or 0))
                break
        except Exception as exc:
            runtime_log(f"Planer: {exc}", "warning")
    runtime_log("Planer poslova je zaustavljen.")


def _v3_audio_path(song: dict[str, Any]) -> Path:
    for key in ("local_audio", "local_wav"):
        raw = str(song.get(key) or "").strip()
        candidate = Path(raw).expanduser() if raw else None
        if candidate and candidate.exists():
            return candidate.resolve()
    raise RuntimeError("Pesma prvo mora da bude preuzeta kao MP3 ili WAV.")


OPTIONAL_TOOLS = {"panako"}


def v3_tool_status() -> dict[str, Any]:
    status = {"ffmpeg": audio_tools_status(), "yt_dlp": ytdlp_status(), "panako": panako_status(ROOT)}
    try:
        from audio_match import deno_status
        status["deno"] = deno_status()
    except Exception as exc:
        status["deno"] = {"ready": False, "message": str(exc)}
    status["python"] = {"ready": True, "version": platform.python_version(), "path": sys.executable}
    for name, entry in status.items():
        ready = bool(entry.get("ready"))
        optional = name in OPTIONAL_TOOLS
        entry["required"] = not optional
        entry["optional"] = optional
        entry["severity"] = "ok" if ready else ("warning" if optional else "error")
    return status


def v3_integrity_task(task: TaskState, options: dict[str, Any]) -> None:
    def progress(name: str, done: int, total: int) -> None:
        task.set_progress(done, total, name)
    result = library_integrity_scan(DB, repair_hashes=bool(options.get("repair_hashes", True)), probe_audio_files=bool(options.get("probe_audio", True)), progress=progress)
    report = _unique_path(EXPORT_DIR / f"Integritet-biblioteke-{datetime.now().strftime('%Y%m%d-%H%M%S-%f')}.json")
    report.write_text(json.dumps(result, ensure_ascii=False, indent=2, default=str), encoding="utf-8")
    if result.get("missing") or result.get("corrupt"):
        task.finish(f"Provera završena uz probleme: nedostaje {len(result.get('missing') or [])}, oštećeno {len(result.get('corrupt') or [])}. Izveštaj: {report.name}", status="partial")
    else:
        task.finish(f"Biblioteka je proverena: {result.get('checked_files', 0)} fajlova, bez oštećenja. Izveštaj: {report.name}")


def v3_organize_task(task: TaskState, options: dict[str, Any]) -> None:
    target = Path(str(options.get("target") or (get_download_dir() / "Organizovana_biblioteka")))
    def progress(name: str, done: int, total: int) -> None:
        task.set_progress(done, total, name)
    result = organize_library(DB, target, template=str(options.get("template") or "{status}/{godina}/{naslov}"), mode=str(options.get("mode") or "move"), dry_run=bool(options.get("dry_run", True)), ids=[str(x) for x in options.get("ids") or []] or None, progress=progress)
    report = _unique_path(EXPORT_DIR / f"Plan-organizacije-{datetime.now().strftime('%Y%m%d-%H%M%S-%f')}.json")
    report.write_text(json.dumps(result, ensure_ascii=False, indent=2, default=str), encoding="utf-8")
    task.finish(("Plan je napravljen" if result.get("dry_run") else "Biblioteka je organizovana") + f": {result.get('count', 0)} fajlova. Izveštaj: {report.name}")


def v3_shorts_render_task(task: TaskState, options: dict[str, Any]) -> None:
    song_id = str(options.get("id") or ""); song = DB.get_song(song_id)
    if not song: raise RuntimeError("Pesma nije pronađena.")
    source = _v3_audio_path(song)
    suggestions = suggest_short_clips(song, source, [int(x) for x in options.get("durations") or [15,30,45,60]])
    selected = options.get("selected") if isinstance(options.get("selected"), list) else suggestions.get("suggestions", [])[:4]
    target = Path(str(options.get("target") or (get_download_dir() / "Shorts" / sanitize_filename(str(song.get('title') or song_id), 100))))
    target.mkdir(parents=True, exist_ok=True); task.total = len(selected); made = []
    for idx, item in enumerate(selected, 1):
        if task.cancel_event.is_set(): break
        wait_if_paused(task)
        start = float(item.get("start") or 0); duration = float(item.get("duration") or 30)
        out = target / f"{sanitize_filename(str(song.get('title') or song_id), 90)} - {int(duration)}s - {int(start)}s.mp3"
        result = render_short_clip(source, out, start, duration, normalize=bool(options.get("normalize", True)))
        DB.add_derived_file(song_id, "short_clip", f"Shorts {int(duration)}s @ {int(start)}s", str(out), "mp3", duration, {"start":start,"duration":duration})
        made.append(result); task.set_progress(idx, len(selected), out.name)
    task.finish(f"Napravljeno je {len(made)} Shorts audio isečaka.")


def v3_shorts_teasers_task(task: TaskState, options: dict[str, Any]) -> None:
    """Automatically cut 3 Shorts-teaser clips (30-50s, the program's own
    energetic-part pick) so the user does not have to trim anything by hand."""
    song_id = str(options.get("id") or ""); song = DB.get_song(song_id)
    if not song: raise RuntimeError("Pesma nije pronađena.")
    source = _v3_audio_path(song)
    selected = suggest_teaser_clips(song, source, count=3)
    if not selected:
        raise RuntimeError("Program nije mogao da pronađe dovoljno energetski jake delove pesme za najave.")
    target = Path(str(options.get("target") or (get_download_dir() / "Shorts_najave" / sanitize_filename(str(song.get('title') or song_id), 100))))
    target.mkdir(parents=True, exist_ok=True); task.total = len(selected); made = []
    for idx, item in enumerate(selected, 1):
        if task.cancel_event.is_set(): break
        wait_if_paused(task)
        start = float(item.get("start") or 0); duration = float(item.get("duration") or 30)
        out = target / f"{sanitize_filename(str(song.get('title') or song_id), 90)} - najava {idx} - {int(duration)}s.mp3"
        result = render_short_clip(source, out, start, duration, normalize=bool(options.get("normalize", True)))
        DB.add_derived_file(song_id, "short_teaser", f"Shorts najava {idx} ({int(duration)}s, najglasniji deo)", str(out), "mp3", duration, {"start": start, "duration": duration, "score": item.get("score")})
        made.append(result); task.set_progress(idx, len(selected), out.name)
    task.finish(f"Napravljene su {len(made)} kratke najave pesme (30-50 s) za Shorts.")


def v3_lyric_video_task(task: TaskState, options: dict[str, Any]) -> None:
    song_id = str(options.get("id") or ""); song = DB.get_song(song_id)
    if not song: raise RuntimeError("Pesma nije pronađena.")
    source = _v3_audio_path(song); cues = load_subtitle_cues(song, DB)
    aspect = str(options.get("aspect") or "16:9"); suffix = "vertikalni" if aspect == "9:16" else "youtube"
    target_dir = Path(str(options.get("target") or (get_download_dir() / "Lyric_video" / sanitize_filename(str(song.get('title') or song_id), 100))))
    target_dir.mkdir(parents=True, exist_ok=True); output = target_dir / f"{sanitize_filename(str(song.get('title') or song_id), 100)}-{suffix}.mp4"
    background = Path(str(options.get("background") or "")) if str(options.get("background") or "") else None
    task.set_progress(5,100,"Priprema titlova i pozadine")
    result = render_lyric_video(song, source, cues, output, background_path=background, aspect=aspect, font=str(options.get("font") or "Arial"), font_size=int(options.get("font_size") or (64 if aspect == "9:16" else 54)))
    DB.add_derived_file(song_id, "lyric_video", f"Lyric video {aspect}", result["path"], "mp4", float(result.get("duration") or song.get("duration") or 0), {"aspect":aspect,"font":options.get("font") or "Arial"})
    task.set_progress(100,100,output.name); task.finish(f"Lyric video je napravljen: {output.name}")


def v3_youtube_upload_task(task: TaskState, options: dict[str, Any]) -> None:
    if not bool(options.get("confirm_upload")):
        raise RuntimeError("Upload nije pokrenut bez izričite potvrde confirm_upload=true.")
    song_id = str(options.get("id") or ""); song = DB.get_song(song_id)
    if not song: raise RuntimeError("Pesma nije pronađena.")
    video = Path(str(options.get("video_path") or song.get("local_video") or ""))
    if not video.exists(): raise RuntimeError("Izaberi gotov MP4 video za upload.")
    metadata = youtube_metadata(song, str(options.get("channel_profile") or "Nedostaješ PUNOO PESME"))
    for key in ("title","description","tags","category_id","privacy_status","made_for_kids","thumbnail_path","playlist_id","publish_at"):
        if key in options: metadata[key] = options[key]
    def progress(name: str, done: int, total: int) -> None:
        task.set_progress(done, total, name)
    result = youtube_resumable_upload(YOUTUBE_OAUTH, video, metadata, profile_id=str(options.get("profile_id") or ""), progress=progress)
    update_fields = {"youtube_url": result["url"]}
    publish_at = str(metadata.get("publish_at") or "").strip()
    privacy_status = str(metadata.get("privacy_status") or "private").strip().lower()
    if publish_at:
        update_fields["youtube_published_at"] = publish_at[:10]
    elif privacy_status == "public":
        update_fields["youtube_published_at"] = now_iso()[:10]
    DB.update_song_user_fields(song_id, update_fields)
    state_label = "zakazan" if publish_at else privacy_status
    task.finish(f"Video je otpremljen na YouTube ({state_label}): {result['url']}")


def v3_panako_index_task(task: TaskState, options: dict[str, Any]) -> None:
    ids = [str(x) for x in options.get("ids") or []] or [str(r.get("id")) for r in DB.export_rows()]
    files=[]
    for sid in ids:
        song=DB.get_song(sid) or {}
        try: files.append(_v3_audio_path(song))
        except Exception: pass
    task.set_progress(0,max(1,len(files)),"Panako indeks")
    result=panako_index(ROOT,files); task.set_progress(len(files),max(1,len(files)),"Panako indeks završen")
    task.finish(f"Panako je indeksirao {result.get('indexed',0)} fajlova.")


def v3_snapshot_task(task: TaskState, options: dict[str, Any]) -> None:
    task.set_progress(1,100,"Pravim bezbedan snapshot programa")
    result=create_program_snapshot(ROOT,EXPORT_DIR / "Program_snapshots",include_tools=bool(options.get("include_tools",False)))
    task.set_progress(100,100,Path(result["path"]).name); task.finish(f"Snapshot programa je napravljen: {Path(result['path']).name}")

class RequestBodyError(ValueError):
    pass


def _loopback_host_allowed(value: str) -> bool:
    host = str(value or "").strip().lower()
    if not host:
        return False
    if host.startswith("["):
        hostname = host.split("]", 1)[0].lstrip("[")
    else:
        hostname = host.rsplit(":", 1)[0] if host.count(":") == 1 else host
    return hostname in {"127.0.0.1", "localhost", "::1"}


def _validated_public_remote_url(value: str) -> str:
    raw = str(value or "").strip()
    parsed = urllib.parse.urlparse(raw)
    if parsed.scheme != "https" or not parsed.hostname or parsed.username or parsed.password:
        raise PermissionError("Dozvoljena je samo javna HTTPS audio adresa.")
    host = parsed.hostname.strip().lower()
    if host in {"localhost", "localhost.localdomain"}:
        raise PermissionError("Lokalne audio adrese nisu dozvoljene kroz proxy.")
    try:
        infos = socket.getaddrinfo(host, parsed.port or 443, type=socket.SOCK_STREAM)
    except OSError as exc:
        raise RuntimeError("Audio domen trenutno ne može da se pronađe.") from exc
    for info in infos:
        ip = ipaddress.ip_address(info[4][0].split("%", 1)[0])
        if not ip.is_global:
            raise PermissionError("Privatne i lokalne mrežne adrese nisu dozvoljene kroz proxy.")
    return raw


class Handler(BaseHTTPRequestHandler):
    server_version = "SunoPesmeStudio/3.1.0"

    def log_message(self, fmt: str, *args: Any) -> None:
        return

    def _security_headers(self) -> None:
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Referrer-Policy", "no-referrer")
        self.send_header("X-Frame-Options", "DENY")
        self.send_header("Content-Security-Policy", "default-src 'self'; img-src 'self' data: https:; media-src 'self' blob:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'")

    def _require_loopback_host(self) -> bool:
        if _loopback_host_allowed(self.headers.get("Host", "")):
            return True
        self._send_json({"ok": False, "message": "Zahtev nije poslat lokalnom serveru programa."}, 403)
        return False

    def _send_json(self, data: Any, status: int = 200) -> None:
        payload = json.dumps(data, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Cache-Control", "no-store")
        self._security_headers()
        self.end_headers()
        try:
            self.wfile.write(payload)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def _send_file(self, path: Path, download_name: str | None = None) -> None:
        if not path.exists() or not path.is_file():
            self.send_error(404)
            return
        ctype = mimetypes.guess_type(path.name)[0] or "application/octet-stream"
        size = path.stat().st_size
        range_header = self.headers.get("Range")
        start, end = 0, size - 1
        status = 200
        if range_header and range_header.startswith("bytes="):
            try:
                spec = range_header[6:].split(",", 1)[0].strip()
                left, right = spec.split("-", 1)
                if not left:
                    suffix = int(right)
                    if suffix <= 0:
                        raise ValueError("invalid suffix")
                    start = max(0, size - suffix)
                    end = size - 1
                else:
                    start = int(left)
                    end = int(right) if right else size - 1
                    end = min(end, size - 1)
                if start < 0 or start >= size or end < start:
                    self.send_response(416)
                    self.send_header("Content-Range", f"bytes */{size}")
                    self.send_header("Content-Length", "0")
                    self.end_headers()
                    return
                status = 206
            except ValueError:
                self.send_response(416)
                self.send_header("Content-Range", f"bytes */{size}")
                self.send_header("Content-Length", "0")
                self.end_headers()
                return
        length = end - start + 1
        self.send_response(status)
        self.send_header("Content-Type", ctype)
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("Content-Length", str(length))
        if status == 206:
            self.send_header("Content-Range", f"bytes {start}-{end}/{size}")
        if download_name:
            quoted = urllib.parse.quote(download_name)
            self.send_header("Content-Disposition", f"attachment; filename*=UTF-8''{quoted}")
        self._security_headers()
        self.end_headers()
        try:
            fh_context = path.open("rb")
        except OSError:
            return
        with fh_context as fh:
            fh.seek(start)
            remaining = length
            while remaining > 0:
                chunk = fh.read(min(1024 * 256, remaining))
                if not chunk:
                    break
                try:
                    self.wfile.write(chunk)
                except (BrokenPipeError, ConnectionResetError):
                    break
                remaining -= len(chunk)


    def _send_bytes(self, payload: bytes, content_type: str, download_name: str | None = None) -> None:
        self.send_response(200)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Cache-Control", "no-store")
        if download_name:
            quoted = urllib.parse.quote(download_name)
            self.send_header("Content-Disposition", f"attachment; filename*=UTF-8''{quoted}")
        self._security_headers()
        self.end_headers()
        try:
            self.wfile.write(payload)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def _proxy_remote(self, url: str, download_name: str | None = None) -> None:
        url = _validated_public_remote_url(url)
        headers = {
            "User-Agent": SESSION_USER_AGENT or "Mozilla/5.0 (Windows NT 10.0; Win64; x64)",
            "Referer": "https://suno.com/",
            "Accept": "*/*",
        }
        range_header = self.headers.get("Range")
        if range_header:
            headers["Range"] = range_header
        with STATE_LOCK:
            token = SESSION_TOKEN
        if token and "studio-api.prod.suno.com" in url:
            headers["Authorization"] = f"Bearer {token}"
        req = urllib.request.Request(url, headers=headers, method="GET")
        try:
            response = urllib.request.urlopen(req, timeout=90)
        except urllib.error.HTTPError as exc:
            if exc.code == 416:
                self.send_response(416)
                content_range = exc.headers.get("Content-Range") if exc.headers else None
                if content_range:
                    self.send_header("Content-Range", content_range)
                self.send_header("Content-Length", "0")
                self.end_headers()
                return
            raise RuntimeError(f"Audio server je vratio grešku {exc.code}.") from exc
        with response:
            status = int(getattr(response, "status", 200) or 200)
            self.send_response(status)
            self.send_header("Content-Type", response.headers.get("Content-Type") or "audio/mpeg")
            self.send_header("Accept-Ranges", response.headers.get("Accept-Ranges") or "bytes")
            for name in ("Content-Length", "Content-Range"):
                value = response.headers.get(name)
                if value:
                    self.send_header(name, value)
            self.send_header("Cache-Control", "no-store")
            if download_name:
                quoted = urllib.parse.quote(download_name)
                self.send_header("Content-Disposition", f"attachment; filename*=UTF-8''{quoted}")
            self._security_headers()
            self.end_headers()
            while True:
                chunk = response.read(1024 * 256)
                if not chunk:
                    break
                try:
                    self.wfile.write(chunk)
                except (BrokenPipeError, ConnectionResetError):
                    break

    def _body(self) -> dict[str, Any]:
        try:
            length = int(self.headers.get("Content-Length") or 0)
        except ValueError as exc:
            raise RequestBodyError("Content-Length nije ispravan.") from exc
        if length <= 0:
            return {}
        if length > 15 * 1024 * 1024:
            raise RequestBodyError("Zahtev je veći od dozvoljenih 15 MB.")
        raw = self.rfile.read(length)
        try:
            data = json.loads(raw.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            raise RequestBodyError("Zahtev ne sadrži ispravan JSON.") from exc
        if not isinstance(data, dict):
            raise RequestBodyError("JSON zahtev mora biti objekat.")
        return data

    def _require_unlocked(self, path: str) -> bool:
        public = {
            "/api/health", "/api/v3/security/status", "/api/v3/security/unlock",
            "/api/shutdown", "/oauth/youtube/callback",
        }
        if not path.startswith("/api/") or path in public:
            return True
        if SECURITY.is_locked():
            self._send_json({"ok": False, "locked": True, "message": "Program je zaključan. Unesi PIN."}, 423)
            return False
        SECURITY.touch()
        return True

    def do_GET(self) -> None:
        if not self._require_loopback_host():
            return
        parsed = urllib.parse.urlparse(self.path)
        path = parsed.path
        query = urllib.parse.parse_qs(parsed.query)
        if not self._require_unlocked(path):
            return
        try:
            if path == "/api/music-recognition/status":
                history = DB.list_recognitions(limit=1)
                self._send_json({
                    "ok": True, "provider": "AudD",
                    "configured": bool(DB.get_setting("audd_api_token", "")),
                    "max_file_mb": 10, "folder": str(RECOGNITION_ROOT),
                    "history_count": len(DB.list_recognitions(limit=2000)),
                    "latest": history[0] if history else None,
                }); return
            if path == "/api/music-recognition/history":
                self._send_json({"ok": True, "items": DB.list_recognitions(limit=int((query.get("limit") or [200])[0] or 200)), "folder": str(RECOGNITION_ROOT)}); return
            if path == "/api/song-finder/status":
                self._send_json(song_finder_status()); return
            if path == "/api/song-finder/results":
                items = [r for r in DB.list_recognitions(limit=int((query.get("limit") or [200])[0] or 200)) if str(r.get("provider") or "") == "local_library"]
                self._send_json({"ok": True, "items": items}); return
            if path == "/api/import/watched-folders":
                self._send_json({"ok": True, "folders": DB.list_watched_folders()}); return
            if path == "/api/v3/security/status":
                self._send_json({"ok": True, "security": SECURITY.status()}); return
            if (path == "/oauth/youtube/callback" or path == "/") and ("code" in query or "error" in query) and "state" in query:
                try:
                    result = YOUTUBE_OAUTH.handle_callback(query)
                    saved = save_oauth_channels(result.get("channels") or [])
                    start_automatic_youtube_pipeline(delay_seconds=2.0)
                    email = html.escape(str((result.get("profile") or {}).get("email") or "Google nalog"))
                    titles = html.escape(", ".join(str(c.get("title") or c.get("channel_id")) for c in saved) or "nijedan kanal")
                    page_html = f"""<!doctype html><html lang='sr'><head><meta charset='utf-8'><title>YouTube povezan</title><style>body{{font-family:Segoe UI,Arial;background:#0b1020;color:#eef2ff;display:grid;place-items:center;min-height:100vh;margin:0}}main{{max-width:680px;padding:36px;border:1px solid #334155;border-radius:22px;background:#111827}}h1{{color:#4ade80}}p{{line-height:1.6}}code{{color:#c4b5fd}}</style></head><body><main><h1>✓ YouTube je povezan</h1><p>Google nalog: <strong>{email}</strong></p><p>Pronađeni kanali: <strong>{titles}</strong></p><p>Vrati se u Suno Pesme Studio. Ovaj prozor možeš da zatvoriš.</p></main><script>setTimeout(()=>window.close(),5000)</script></body></html>"""
                    self._send_bytes(page_html.encode("utf-8"), "text/html; charset=utf-8")
                except Exception as exc:
                    page_html = f"""<!doctype html><html lang='sr'><head><meta charset='utf-8'><title>Greška povezivanja</title><style>body{{font-family:Segoe UI,Arial;background:#160b12;color:#fff;display:grid;place-items:center;min-height:100vh;margin:0}}main{{max-width:720px;padding:36px;border:1px solid #7f1d1d;border-radius:22px;background:#1f1118}}h1{{color:#fb7185}}p{{line-height:1.6}}</style></head><body><main><h1>YouTube nije povezan</h1><p>{html.escape(str(exc))}</p><p>Vrati se u program i pokreni povezivanje ponovo.</p></main></body></html>"""
                    runtime_log(f"YOUTUBE OAUTH CALLBACK ERROR: {exc}", "error")
                    self._send_bytes(page_html.encode("utf-8"), "text/html; charset=utf-8")
                return
            if path == "/api/health":
                self._send_json({"ok": True, "app": "Suno Pesme Studio", "version": APP_VERSION, "pid": os.getpid(), "time": now_iso(), "watchdog": os.environ.get("SUNO_WATCHDOG") == "1"})
                return
            if path == "/api/advanced/status":
                self._send_json({"ok": True, "plugins": plugin_status(ROOT), "checkpoints": DB.list_sync_checkpoints(), "jobs": DB.list_jobs(50), "schedules": DB.list_schedules(), "incremental_backups": list_incremental_backups(Path(DB.get_setting("incremental_backup_dir", str(EXPORT_DIR / "Inkrementalni_backup"))))})
                return
            if path == "/api/v3/status":
                self._send_json({"ok": True, "version": APP_VERSION, "preflight": system_preflight(ROOT, DATA_DIR, get_download_dir(), v3_tool_status()), "tools": v3_tool_status(), "storage": v3_storage_report(ROOT, DATA_DIR, get_download_dir()), "panako": panako_status(ROOT), "security": {"youtube_tokens_encrypted": os.name == "nt", "suno_token_persisted": False, "diagnostics_redacted": True}, "schedules": DB.list_schedules(), "watchdog": os.environ.get("SUNO_WATCHDOG") == "1"})
                return
            if path == "/api/v3/duplicates":
                self._send_json({"ok": True, "report": duplicate_detection(DB)})
                return
            if path == "/api/v3/shorts/suggest":
                song_id = (query.get("id") or [""])[0]; song = DB.get_song(song_id)
                if not song: raise RuntimeError("Pesma nije pronađena.")
                self._send_json({"ok": True, "result": suggest_short_clips(song, _v3_audio_path(song))})
                return
            if path == "/api/jobs":
                self._send_json({"ok": True, "jobs": DB.list_jobs(int((query.get("limit") or ["200"])[0]))})
                return
            if path == "/api/schedules":
                self._send_json({"ok": True, "schedules": DB.list_schedules()})
                return
            if path == "/api/sync/checkpoints":
                self._send_json({"ok": True, "checkpoints": DB.list_sync_checkpoints()})
                return
            if path == "/api/song/history":
                song_id = (query.get("id") or [""])[0]
                self._send_json({"ok": True, "history": DB.list_song_history(song_id)})
                return
            if path == "/api/subtitles":
                song_id = (query.get("id") or [""])[0]
                song = DB.get_song(song_id)
                if not song: raise RuntimeError("Pesma nije pronađena.")
                self._send_json({"ok": True, "cues": load_subtitle_cues(song, DB), "song": {"id": song_id, "title": song.get("title"), "duration": song.get("duration")}})
                return
            if path == "/api/update/status":
                self._send_json({"ok": True, "update": check_update(DB.get_setting("update_manifest_url", ""), APP_VERSION)})
                return
            if path == "/api/connect/diagnostics":
                with STATE_LOCK:
                    launch_state = dict(CONNECT_LAUNCH_STATE)
                self._send_json({"ok": True, "launch": launch_state, "connector": CONNECTOR.diagnostics(), "connected": bool(SESSION_TOKEN), "session": suno_session_public_state()})
                return
            if path == "/api/status":
                with STATE_LOCK:
                    connected = bool(SESSION_TOKEN)
                    task = (ACTIVE_TASK or LAST_TASK).as_dict() if (ACTIVE_TASK or LAST_TASK) else None
                self._send_json({
                    "ok": True,
                    "version": APP_VERSION,
                    "connected": connected,
                    "songs": DB.count_songs(),
                    "download_dir": str(get_download_dir()),
                    "task": task,
                    "browser_found": bool(CONNECTOR.find_browser()),
                    "last_new_check_at": DB.get_setting("last_new_check_at", ""),
                    "last_new_count": int(DB.get_setting("last_new_count", "0") or 0),
                    "theme": DB.get_setting("theme", "default"),
                    "auto_check_minutes": int(DB.get_setting("auto_check_minutes", "0") or 0),
                    "auto_backup_before_sync": DB.get_setting("auto_backup_before_sync", "0") == "1",
                    "suno_keepalive_enabled": _suno_keepalive_enabled(),
                    "suno_keepalive_minutes": _suno_keepalive_minutes(),
                    "suno_auto_reopen_browser": _suno_auto_reopen_browser(),
                    "suno_session": suno_session_public_state(),
                    "sources": DB.source_groups(),
                    "youtube": DB.youtube_summary(),
                    "youtube_audio": DB.youtube_audio_summary(),
                    "ytdlp": ytdlp_status(),
                    "has_youtube_api_key": bool(get_youtube_api_key()),
                    "youtube_oauth": YOUTUBE_OAUTH.status(),
                    "copyright_owner_name": DB.get_setting("copyright_owner_name", ""),
                    "youtube_cookies_browser": DB.get_setting("youtube_cookies_browser", "none"),
                })
                return
            if path == "/api/youtube/oauth/status":
                self._send_json({"ok": True, "oauth": YOUTUBE_OAUTH.status(), "channels": DB.list_youtube_channels()})
                return
            if path == "/api/youtube/channels":
                self._send_json({"ok": True, "channels": DB.list_youtube_channels(), "has_api_key": bool(get_youtube_api_key()), "oauth": YOUTUBE_OAUTH.status(), "summary": DB.youtube_summary()})
                return
            if path == "/api/youtube/matches":
                match_type = (query.get("type") or [""])[0]
                status = (query.get("status") or [""])[0]
                rows = DB.list_youtube_matches(match_type=match_type, status=status, limit=int((query.get("limit") or ["1000"])[0]))
                self._send_json({"ok": True, "matches": rows, "count": len(rows), "summary": DB.youtube_summary()})
                return
            if path == "/api/youtube/calendar":
                rows = DB.publication_calendar(limit=int((query.get("limit") or ["1000"])[0]))
                self._send_json({"ok": True, "rows": rows, "count": len(rows)})
                return
            if path == "/api/youtube/audio-analysis":
                completeness = (query.get("completeness") or [""])[0]
                channel_id = (query.get("channel_id") or [""])[0]
                rows = DB.list_youtube_audio_analyses(completeness_status=completeness, channel_id=channel_id, limit=int((query.get("limit") or ["5000"])[0]))
                self._send_json({"ok": True, "rows": rows, "count": len(rows), "summary": DB.youtube_audio_summary(), "ytdlp": ytdlp_status(), "algorithm": AUDIO_MATCH_VERSION})
                return
            if path == "/api/youtube/publication-matrix":
                self._send_json({"ok": True, "matrix": DB.youtube_publication_matrix(), "summary": DB.youtube_audio_summary()})
                return
            if path == "/api/youtube/coverage":
                song_id = (query.get("song_id") or [""])[0]
                channel_id = (query.get("channel_id") or [""])[0]
                self._send_json({"ok": True, "coverage": DB.youtube_coverage_report(song_id=song_id, channel_id=channel_id)})
                return
            if path == "/api/youtube/duplicate-mix-report":
                self._send_json({"ok": True, "report": DB.youtube_duplicate_mix_report()})
                return
            if path == "/api/tools/ytdlp/status":
                self._send_json({"ok": True, **ytdlp_status()})
                return
            if path == "/api/youtube/summary":
                self._send_json({"ok": True, "summary": DB.youtube_summary(), "has_api_key": bool(get_youtube_api_key()), "oauth": YOUTUBE_OAUTH.status()})
                return
            if path == "/api/youtube/search-links":
                song_id = (query.get("song_id") or [""])[0]
                song = DB.get_song(song_id)
                if not song:
                    raise RuntimeError("Pesma nije pronađena.")
                title = str(song.get("title") or "")
                self._send_json({"ok": True, "youtube": youtube_search_url(title), "google": google_search_url(title), "bing": bing_search_url(title)})
                return
            if path == "/api/youtube/contact-draft":
                match_id = int((query.get("id") or ["0"])[0] or 0)
                owner_name = (query.get("owner_name") or [DB.get_setting("copyright_owner_name", "")])[0]
                match = DB.get_youtube_match(match_id)
                if not match:
                    raise RuntimeError("Pronađeni YouTube slučaj ne postoji.")
                song = DB.get_song(str(match.get("song_id") or "")) or {"title": match.get("song_title"), "youtube_url": match.get("original_url"), "source_url": match.get("source_url")}
                video = {"url": match.get("video_url"), "title": match.get("video_title"), "channel_title": match.get("channel_title")}
                draft = contact_message(song, video, owner_name=str(owner_name or ""))
                self._send_json({"ok": True, "draft": draft, "match": match, "channel_about_url": f"https://www.youtube.com/channel/{urllib.parse.quote(str(match.get('channel_id') or ''))}/about", "copyright_help_url": "https://support.google.com/youtube/answer/2807622", "copyright_match_url": "https://studio.youtube.com/"})
                return
            if path == "/api/songs":
                rows = DB.list_songs(
                    search=(query.get("search") or [""])[0],
                    filter_name=(query.get("filter") or ["all"])[0],
                    sort=(query.get("sort") or ["newest"])[0],
                    limit=int((query.get("limit") or ["5000"])[0]),
                    offset=int((query.get("offset") or ["0"])[0]),
                    collection_id=int((query.get("collection_id") or ["0"])[0] or 0) or None,
                    source_group=(query.get("source_group") or [""])[0],
                    date_from=(query.get("date_from") or [""])[0],
                    date_to=(query.get("date_to") or [""])[0],
                    min_duration=float((query.get("min_duration") or ["0"])[0] or 0),
                    max_duration=float((query.get("max_duration") or ["0"])[0] or 0) or None,
                )
                total_filtered = DB.count_songs_filtered(
                    search=(query.get("search") or [""])[0], filter_name=(query.get("filter") or ["all"])[0],
                    collection_id=int((query.get("collection_id") or ["0"])[0] or 0) or None,
                    source_group=(query.get("source_group") or [""])[0], date_from=(query.get("date_from") or [""])[0],
                    date_to=(query.get("date_to") or [""])[0], min_duration=float((query.get("min_duration") or ["0"])[0] or 0),
                    max_duration=float((query.get("max_duration") or ["0"])[0] or 0) or None,
                )
                self._send_json({"ok": True, "songs": rows, "count": len(rows), "total_library": DB.count_songs(), "total_filtered": total_filtered})
                return
            if path == "/api/song-ids":
                ids = DB.list_song_ids(
                    search=(query.get("search") or [""])[0], filter_name=(query.get("filter") or ["all"])[0],
                    collection_id=int((query.get("collection_id") or ["0"])[0] or 0) or None,
                    source_group=(query.get("source_group") or [""])[0], date_from=(query.get("date_from") or [""])[0],
                    date_to=(query.get("date_to") or [""])[0], min_duration=float((query.get("min_duration") or ["0"])[0] or 0),
                    max_duration=float((query.get("max_duration") or ["0"])[0] or 0) or None,
                    limit=int((query.get("limit") or ["20000"])[0]),
                )
                self._send_json({"ok": True, "ids": ids, "count": len(ids)})
                return
            if path == "/api/song":
                song_id = (query.get("id") or [""])[0]
                song = DB.get_song(song_id)
                self._send_json({"ok": bool(song), "song": song}, 200 if song else 404)
                return

            if path == "/api/reports":
                report_type = (query.get("type") or ["missing_lyrics"])[0]
                if report_type == "duplicates":
                    self._send_json({"ok": True, "type": report_type, "report": DB.duplicate_report()})
                elif report_type == "audio_quality":
                    rows = DB.export_rows()
                    result = []
                    for row in rows[:500]:
                        raw = str(row.get("local_audio") or row.get("local_wav") or "")
                        if not raw or not Path(raw).exists():
                            continue
                        try:
                            info = probe_audio(Path(raw))
                            result.append({"id": row.get("id"), "title": row.get("title"), "path": raw, **info})
                        except Exception as exc:
                            result.append({"id": row.get("id"), "title": row.get("title"), "path": raw, "error": str(exc)})
                    self._send_json({"ok": True, "type": report_type, "rows": result, "count": len(result)})
                else:
                    rows = DB.report_rows(report_type)
                    self._send_json({"ok": True, "type": report_type, "rows": rows, "count": len(rows)})
                return
            if path == "/api/collections":
                self._send_json({"ok": True, "collections": DB.list_collections()})
                return
            if path == "/api/audio-tools/status":
                self._send_json({"ok": True, "tools": audio_tools_status()})
                return
            if path == "/api/storage":
                self._send_json({"ok": True, "storage": storage_summary()})
                return
            if path == "/api/song/audio":
                song_id = (query.get("id") or [""])[0]
                file_id = int((query.get("file_id") or ["0"])[0] or 0)
                download = (query.get("download") or ["0"])[0] == "1"
                source_path: Path | None = None
                remote_url = ""
                title = "pesma"
                if file_id:
                    derived = DB.get_derived_file(file_id)
                    if not derived:
                        raise RuntimeError("Obrađena audio-verzija ne postoji.")
                    source_path = safe_media_path(str(derived.get("path") or ""))
                    title = str(derived.get("label") or source_path.stem)
                else:
                    song = DB.get_song(song_id)
                    if not song:
                        raise RuntimeError("Pesma nije pronađena.")
                    title = str(song.get("title") or song_id)
                    source_path = _existing_audio_path(song)
                    remote_url = str(song.get("audio_url") or "")
                    if source_path is None and not remote_url and SESSION_TOKEN:
                        detail = get_client().get_clip(song_id)
                        DB.upsert_song(detail)
                        song = DB.get_song(song_id) or song
                        remote_url = str(song.get("audio_url") or "")
                filename = f"{sanitize_filename(title)}{source_path.suffix if source_path else '.mp3'}" if download else None
                if source_path:
                    self._send_file(source_path, filename)
                elif remote_url:
                    self._proxy_remote(remote_url, filename)
                else:
                    raise RuntimeError("Suno nije vratio audio-adresu za ovu pesmu.")
                return
            if path == "/api/audio/waveform":
                song_id = (query.get("id") or [""])[0]
                width = max(200, min(int((query.get("width") or ["1000"])[0]), 3000))
                song = DB.get_song(song_id)
                if not song:
                    raise RuntimeError("Pesma nije pronađena.")
                if not audio_tools_status().get("ready"):
                    raise RuntimeError("Prvo pripremi FFmpeg audio alate. Talasni prikaz više ne preuzima ceo MP3 u browser.")
                source = _song_processing_source(song)
                if source is None and SESSION_TOKEN and not str(song_id).startswith("local-"):
                    detail = get_client().get_clip(song_id)
                    if isinstance(detail, dict):
                        DB.upsert_song(detail, source_group=str(song.get("source_group") or "Suno"))
                        song = DB.get_song(song_id) or song
                        source = _song_processing_source(song)
                if source is None:
                    raise RuntimeError("Audio izvor nije dostupan za talasni prikaz.")
                if isinstance(source, Path):
                    stat = source.stat()
                    signature = f"{source.resolve()}|{stat.st_size}|{stat.st_mtime_ns}|{width}"
                    source_kind = "local"
                else:
                    signature = f"{source}|{song.get('updated_at') or song.get('created_at')}|{width}"
                    source_kind = "suno"
                cache_key = hashlib.sha256(signature.encode("utf-8", errors="replace")).hexdigest()[:24]
                cache_path = WAVEFORM_DIR / f"{sanitize_filename(song_id, 50)}-{cache_key}.json"
                payload: dict[str, Any]
                if cache_path.exists():
                    try:
                        payload = json.loads(cache_path.read_text(encoding="utf-8"))
                        payload["cached"] = True
                    except Exception:
                        cache_path.unlink(missing_ok=True)
                        payload = waveform_peaks(source, width=width)
                        payload["cached"] = False
                else:
                    payload = waveform_peaks(source, width=width)
                    payload["cached"] = False
                payload.update({"duration": float(song.get("duration") or 0), "source": source_kind})
                if not cache_path.exists():
                    temp_cache = cache_path.with_suffix(".json.part")
                    temp_cache.write_text(json.dumps(payload, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
                    os.replace(temp_cache, cache_path)
                self._send_json({"ok": True, "waveform": payload})
                return
            if path == "/api/song/text":
                song_id = (query.get("id") or [""])[0]
                fmt = (query.get("format") or ["txt"])[0].lower()
                song = DB.get_song(song_id)
                if not song:
                    raise RuntimeError("Pesma nije pronađena.")
                title = sanitize_filename(str(song.get("title") or song_id))
                if fmt in ("lrc", "srt"):
                    local_key = "local_lrc" if fmt == "lrc" else "local_srt"
                    local = str(song.get(local_key) or "")
                    if local and Path(local).exists():
                        self._send_file(safe_media_path(local), f"{title}.{fmt}")
                        return
                    if SESSION_TOKEN:
                        aligned = get_client().get_aligned_lyrics(song_id)
                        if aligned:
                            content = format_lrc(aligned) if fmt == "lrc" else format_srt(aligned)
                            self._send_bytes(content.encode("utf-8-sig"), "text/plain; charset=utf-8", f"{title}.{fmt}")
                            return
                    raise RuntimeError(f"Sinhronizovani {fmt.upper()} tekst nije dostupan za ovu pesmu.")
                if fmt == "json":
                    payload = json.dumps(song, ensure_ascii=False, indent=2).encode("utf-8")
                    self._send_bytes(payload, "application/json; charset=utf-8", f"{title}.json")
                    return
                content = str(song.get("lyrics") or "")
                self._send_bytes(content.encode("utf-8-sig"), "text/plain; charset=utf-8", f"{title}.txt")
                return
            if path == "/api/audio/info":
                song_id = (query.get("id") or [""])[0]
                file_id = int((query.get("file_id") or ["0"])[0] or 0)
                target: Path | None = None
                if file_id:
                    derived = DB.get_derived_file(file_id)
                    if derived:
                        target = Path(str(derived.get("path") or ""))
                else:
                    song = DB.get_song(song_id)
                    if song:
                        target = _existing_audio_path(song)
                if not target or not target.exists():
                    raise RuntimeError("Za detaljnu analizu prvo preuzmi audio-fajl.")
                self._send_json({"ok": True, "info": probe_audio(target), "path": str(target)})
                return
            if path == "/api/stats":
                self._send_json({"ok": True, "stats": DB.stats()})
                return
            if path == "/api/library/health":
                self._send_json({"ok": True, "health": DB.local_file_health(repair=False)})
                return
            if path == "/api/logs":
                self._send_json({"ok": True, "logs": DB.logs()})
                return
            if path == "/api/task":
                with STATE_LOCK:
                    task = ACTIVE_TASK or LAST_TASK
                self._send_json({"ok": True, "task": task.as_dict() if task else None})
                return
            if path == "/media":
                raw = (query.get("path") or [""])[0]
                self._send_file(safe_media_path(raw))
                return
            if path == "/api/export/download":
                raw = (query.get("path") or [""])[0]
                p = safe_media_path(raw)
                self._send_file(p, p.name)
                return
            if path.startswith("/assets/"):
                asset = (WEB_DIR / path[len("/assets/"):]).resolve()
                web_root = WEB_DIR.resolve()
                if asset != web_root and web_root not in asset.parents:
                    raise PermissionError("Nedozvoljena putanja statičkog fajla.")
                self._send_file(asset)
                return
            if path in ("/", "/index.html"):
                self._send_file(WEB_DIR / "index.html")
                return
            self.send_error(404)
        except RequestBodyError as exc:
            self._send_json({"ok": False, "message": str(exc)}, 400)
        except PermissionError as exc:
            self._send_json({"ok": False, "message": str(exc)}, 403)
        except Exception as exc:
            self._send_json({"ok": False, "message": str(exc)}, 500)

    def do_POST(self) -> None:
        global SESSION_TOKEN, SESSION_USER_AGENT
        if not self._require_loopback_host():
            return
        path = urllib.parse.urlparse(self.path).path
        origin = self.headers.get("Origin", "")
        if origin and not (origin.startswith("http://127.0.0.1:") or origin.startswith("http://localhost:")):
            self._send_json({"ok": False, "message": "Zahtev nije došao iz lokalnog interfejsa programa."}, 403)
            return
        if not self._require_unlocked(path):
            return
        try:
            body = self._body()
            if path == "/api/v3/security/unlock":
                self._send_json({"ok": True, "security": SECURITY.unlock(str(body.get("pin") or ""))}); return
            if path == "/api/v3/security/set-pin":
                self._send_json({"ok": True, "security": SECURITY.set_pin(str(body.get("new_pin") or ""), timeout_minutes=int(body.get("timeout_minutes") or 30))}); return
            if path == "/api/v3/security/lock":
                self._send_json({"ok": True, "security": SECURITY.lock_now()}); return
            if path == "/api/v3/security/disable":
                self._send_json({"ok": True, "security": SECURITY.disable(str(body.get("pin") or ""))}); return
            if path == "/api/task/pause":
                with STATE_LOCK:
                    task = ACTIVE_TASK
                if not task or task.status != "running": raise RuntimeError("Nema aktivnog posla.")
                task.pause_event.set()
                if task.job_id: DB.update_job(task.job_id, status="paused", progress=task.percent, message="Posao je pauziran.")
                task.log("Posao je pauziran.", "warning")
                self._send_json({"ok": True, "task": task.as_dict()}); return
            if path == "/api/task/resume":
                with STATE_LOCK:
                    task = ACTIVE_TASK
                if not task or task.status != "running": raise RuntimeError("Nema aktivnog posla.")
                task.pause_event.clear()
                if task.job_id: DB.update_job(task.job_id, status="running", progress=task.percent, message="Posao je nastavljen.")
                task.log("Posao je nastavljen.", "success")
                self._send_json({"ok": True, "task": task.as_dict()}); return
            if path == "/api/v3/open-maintenance":
                # ALATI_I_ODRZAVANJE (a folder of .bat maintenance scripts)
                # was the old architecture; the native Go launcher/installer
                # replaced it and never creates that folder, so opening it
                # was a dead end (FileNotFoundError). USER_DATA_ROOT -- logs,
                # backups, rollback snapshots -- is the real, current
                # equivalent, and is exactly what launcher/main.go's own
                # --otvori-podatke flag opens.
                target = USER_DATA_ROOT
                target.mkdir(parents=True, exist_ok=True)
                if os.name == "nt": os.startfile(str(target))  # type: ignore[attr-defined]
                elif sys.platform == "darwin": subprocess.Popen(["open", str(target)])
                else: subprocess.Popen(["xdg-open", str(target)])
                self._send_json({"ok": True, "path": str(target)}); return
            if path == "/api/v3/open-snapshots":
                target = DATA_DIR / "rollback"; target.mkdir(parents=True, exist_ok=True)
                if os.name == "nt": os.startfile(str(target))  # type: ignore[attr-defined]
                elif sys.platform == "darwin": subprocess.Popen(["open", str(target)])
                else: subprocess.Popen(["xdg-open", str(target)])
                self._send_json({"ok": True, "path": str(target)}); return
            if path == "/api/v3/storage/cleanup":
                if not bool(body.get("confirm")):
                    raise RuntimeError("Čišćenje nije pokrenuto bez confirm=true.")
                report = cleanup_storage(ROOT, DATA_DIR, get_download_dir(), cache_days=max(1, int(body.get("cache_days") or 30)), keep_rollbacks=max(1, int(body.get("keep_rollbacks") or 5)))
                self._send_json({"ok": True, "report": report}); return
            if path == "/api/v3/preflight":
                report = system_preflight(ROOT, DATA_DIR, get_download_dir(), v3_tool_status())
                out = _unique_path(EXPORT_DIR / f"Provera-racunara-{datetime.now().strftime('%Y%m%d-%H%M%S-%f')}.json")
                out.write_text(json.dumps(report, ensure_ascii=False, indent=2, default=str), encoding="utf-8")
                self._send_json({"ok": report.get("ok", False), "report": report, "path": str(out)}); return
            if path == "/api/v3/integrity":
                payload=dict(body); task=start_task("v3_integrity","Kompletna kontrola integriteta",lambda t:v3_integrity_task(t,payload),persistent_payload=payload); self._send_json({"ok":True,"task":task.as_dict()}); return
            if path == "/api/v3/organize":
                payload=dict(body); task=start_task("v3_organize","Organizovanje biblioteke",lambda t:v3_organize_task(t,payload),persistent_payload=payload); self._send_json({"ok":True,"task":task.as_dict()}); return
            if path == "/api/v3/shorts/render":
                payload=dict(body); task=start_task("v3_shorts","Pravljenje Shorts isečaka",lambda t:v3_shorts_render_task(t,payload),persistent_payload=payload); self._send_json({"ok":True,"task":task.as_dict()}); return
            if path == "/api/v3/shorts/teasers":
                payload=dict(body); task=start_task("v3_shorts_teasers","Pravljenje 3 kratke najave pesme",lambda t:v3_shorts_teasers_task(t,payload),persistent_payload=payload); self._send_json({"ok":True,"task":task.as_dict()}); return
            if path == "/api/v3/shorts/text-clip":
                song_id = str(body.get("id") or ""); song = DB.get_song(song_id)
                if not song: raise RuntimeError("Pesma nije pronađena.")
                query_text = str(body.get("text") or "").strip()
                if not query_text: raise RuntimeError("Upiši deo teksta pesme koji želiš da isečeš.")
                duration = max(10.0, min(60.0, float(body.get("duration") or 30)))
                source = _v3_audio_path(song)
                total = float(probe_audio(source).get("duration") or song.get("duration") or 0)
                cues = load_subtitle_cues(song, DB)
                hit = teaser_clip_from_text(cues, query_text, total, duration)
                if not hit:
                    raise RuntimeError("Taj tekst nije pronađen u sačuvanim titlovima ove pesme. Proveri da li je tekst tačno napisan ili prvo sačuvaj LRC/SRT u editoru titlova.")
                target = get_download_dir() / "Shorts_najave" / sanitize_filename(str(song.get('title') or song_id), 100)
                target.mkdir(parents=True, exist_ok=True)
                out = target / f"{sanitize_filename(str(song.get('title') or song_id), 90)} - po tekstu - {int(hit['duration'])}s.mp3"
                result = render_short_clip(source, out, hit["start"], hit["duration"], normalize=True)
                DB.add_derived_file(song_id, "short_teaser", f"Shorts po tekstu ({int(hit['duration'])}s): „{hit['matched_text'][:40]}“", str(out), "mp3", hit["duration"], {"start": hit["start"], "duration": hit["duration"], "matched_text": hit["matched_text"]})
                self._send_json({"ok": True, "result": {**result, **hit}}); return
            if path == "/api/v3/lyric-video":
                payload=dict(body); task=start_task("v3_lyric_video","Pravljenje lyric videa",lambda t:v3_lyric_video_task(t,payload),persistent_payload=payload); self._send_json({"ok":True,"task":task.as_dict()}); return
            if path == "/api/v3/youtube/package":
                song_id=str(body.get("id") or ""); song=DB.get_song(song_id)
                if not song: raise RuntimeError("Pesma nije pronađena.")
                result=create_youtube_package(
                    song, EXPORT_DIR,
                    channel=str(body.get("channel_profile") or "Nedostaješ PUNOO PESME"),
                    video_path=str(body.get("video_path") or song.get("local_video") or ""),
                    thumbnail_path=str(body.get("thumbnail_path") or song.get("local_cover") or ""),
                    extra_metadata={k: body.get(k) for k in ("privacy_status", "playlist_id", "publish_at", "made_for_kids") if k in body},
                )
                self._send_json({"ok":True,**result}); return
            if path == "/api/music-recognition/settings":
                token = str(body.get("audd_token") or "").strip()
                if token:
                    DB.set_setting("audd_api_token", token)
                elif bool(body.get("clear")):
                    DB.set_setting("audd_api_token", "")
                self._send_json({"ok": True, "configured": bool(DB.get_setting("audd_api_token", "")), "message": "Podešavanje je sačuvano."}); return
            if path == "/api/music-recognition/recognize":
                source_path = str(body.get("source_path") or "").strip()
                temporary: Path | None = None
                if source_path:
                    source = Path(source_path).expanduser().resolve()
                else:
                    audio_b64 = str(body.get("audio_base64") or "")
                    filename = str(body.get("filename") or "clip.mp3")
                    suffix = Path(filename).suffix or ".mp3"
                    temporary = decode_audio_base64(audio_b64, suffix)
                    source = temporary
                try:
                    result = recognize_music_file(
                        source,
                        start_seconds=float(body.get("start_seconds") or 0),
                        duration_seconds=float(body.get("duration_seconds") or 25),
                    )
                finally:
                    if temporary is not None:
                        temporary.unlink(missing_ok=True)
                self._send_json({"ok": True, "result": result, "history": DB.list_recognitions(limit=200)}); return
            if path == "/api/music-recognition/retry":
                recognition_id = int(body.get("id") or 0)
                record = DB.get_recognition(recognition_id)
                if not record:
                    raise RuntimeError("Sačuvani rezultat Pronalazača pesme nije pronađen.")
                source = Path(str(record.get("input_path") or ""))
                result = recognize_music_file(
                    source,
                    start_seconds=float(body.get("start_seconds") or 0),
                    duration_seconds=float(body.get("duration_seconds") or 25),
                )
                self._send_json({"ok": True, "result": result, "history": DB.list_recognitions(limit=200)}); return
            if path == "/api/music-recognition/add-to-library":
                song = add_recognition_to_library(int(body.get("id") or 0))
                self._send_json({"ok": True, "song": song, "history": DB.list_recognitions(limit=200), "message": "Prepoznata pesma je trajno dodata u Biblioteku."}); return
            if path == "/api/song-finder/index":
                payload = {"force": bool(body.get("force"))}
                task = start_task("song_finder_index", "Indeksiranje mojih pesama", lambda t: song_finder_index_task(t, payload), persistent_payload=payload)
                self._send_json({"ok": True, "task": task.as_dict()}); return
            if path == "/api/song-finder/analyze-file":
                source_path = str(body.get("source_path") or "").strip()
                if not source_path:
                    raise RuntimeError("Izaberi Shorts, video ili audio fajl.")
                result = song_finder_analyze(Path(source_path))
                self._send_json({"ok": True, "result": result}); return
            if path == "/api/song-finder/result/confirm":
                recognition_id = int(body.get("id") or 0)
                song_id = str(body.get("song_id") or "")
                if not recognition_id or not song_id:
                    raise RuntimeError("Nedostaje ID rezultata ili pesme.")
                DB.set_recognition_library_song(recognition_id, song_id)
                self._send_json({"ok": True, "history": DB.list_recognitions(limit=200)}); return
            if path == "/api/song-finder/result/retry":
                recognition_id = int(body.get("id") or 0)
                record = DB.get_recognition(recognition_id)
                if not record:
                    raise RuntimeError("Sačuvani rezultat nije pronađen.")
                result = song_finder_analyze(Path(str(record.get("input_path") or "")))
                self._send_json({"ok": True, "result": result}); return
            if path == "/api/v3/youtube/upload":
                payload=dict(body); task=start_task("v3_youtube_upload","YouTube upload",lambda t:v3_youtube_upload_task(t,payload),persistent_payload=payload); self._send_json({"ok":True,"task":task.as_dict()}); return
            if path == "/api/v3/proof":
                song_id=str(body.get("id") or ""); song=DB.get_song(song_id)
                if not song: raise RuntimeError("Pesma nije pronađena.")
                result=create_proof_package(song,EXPORT_DIR / "Dokazni_paketi"); self._send_json({"ok":True,**result,"download_url":"/api/export/download?path="+urllib.parse.quote(str(result["path"]))}); return
            if path == "/api/v3/panako/index":
                payload=dict(body); task=start_task("v3_panako_index","Panako Content-ID indeks",lambda t:v3_panako_index_task(t,payload),persistent_payload=payload); self._send_json({"ok":True,"task":task.as_dict()}); return
            if path == "/api/v3/panako/query":
                result=panako_query(ROOT,Path(str(body.get("path") or ""))); self._send_json({"ok":True,"result":result}); return
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
                DB.set_setting("update_manifest_url",str(body.get("manifest_url") or "").strip()); self._send_json({"ok":True,"update":check_update(DB.get_setting("update_manifest_url", ""),APP_VERSION)}); return
            if path == "/api/update/download":
                result=download_update(ROOT,DB.get_setting("update_manifest_url", ""),APP_VERSION); self._send_json({"ok":True,**result}); return
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
                self._send_json({"ok": True, "download_dir": str(get_download_dir()), "theme": DB.get_setting("theme", "default"), "auto_check_minutes": int(DB.get_setting("auto_check_minutes", "0") or 0), "auto_backup_before_sync": DB.get_setting("auto_backup_before_sync", "0") == "1", "suno_keepalive_enabled": _suno_keepalive_enabled(), "suno_keepalive_minutes": _suno_keepalive_minutes(), "suno_auto_reopen_browser": _suno_auto_reopen_browser(), "suno_session": suno_session_public_state(), "has_youtube_api_key": bool(get_youtube_api_key()), "youtube_oauth": YOUTUBE_OAUTH.status(), "copyright_owner_name": DB.get_setting("copyright_owner_name", ""), "youtube_cookies_browser": DB.get_setting("youtube_cookies_browser", "none")})
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
    global SESSION_KEEPALIVE_THREAD, SCHEDULER_THREAD
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
        server.server_close()
        runtime_log("Lokalni server je zaustavljen.")


if __name__ == "__main__":
    main()
