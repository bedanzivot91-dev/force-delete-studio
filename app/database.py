from __future__ import annotations

import json
import sqlite3
import threading
import shutil
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable


BUILTIN_COLLECTIONS = (
    ("odrađene-duže-pesme", "Odrađene duže pesme", "#7c3aed"),
    ("objavljene-na-kanalu", "Objavljena pesma na kanalu", "#16a34a"),
    ("youtube-pronađene-objave", "YouTube pronađene objave", "#2563eb"),
    ("moguće-neovlašćene-objave", "Moguće neovlašćene objave", "#dc2626"),
    ("youtube-kompletno-objavljene", "YouTube — kompletno objavljene", "#059669"),
    ("youtube-delimično-objavljene", "YouTube — delimično objavljene", "#d97706"),
    ("youtube-kratki-isečci", "YouTube — kratki isečci", "#db2777"),
    ("youtube-nisu-pronađene", "YouTube — nisu pronađene", "#64748b"),
    ("youtube-audio-provera", "YouTube — potrebna audio provera", "#7c3aed"),
)


class _ClosingConnection(sqlite3.Connection):
    """sqlite3.Connection used as `with conn: ...` only commits/rolls back
    on exit -- it deliberately does NOT close the connection (documented
    stdlib behavior). Nearly every method below uses that exact pattern
    (`with self._connect() as conn:`), so every one of those ~85 call
    sites was leaking a raw OS file handle to db_path, never explicitly
    closed until CPython's garbage collector happened to finalize it.
    On Windows this is not just untidy: a still-open handle blocks
    replacing/deleting the database file (confirmed via real CI failures
    -- WinError 32, "used by another process" -- in exactly the kind of
    short-lived-LibraryDB-then-cleanup pattern the test suite uses, and
    the same pattern a real backup/restore/migration touches). Closing
    here, once, fixes every call site without having to touch each one.
    """

    def __exit__(self, exc_type, exc_val, exc_tb):
        try:
            return super().__exit__(exc_type, exc_val, exc_tb)
        finally:
            self.close()


class LibraryDB:
    def __init__(self, db_path: Path):
        self.db_path = db_path
        self.db_path.parent.mkdir(parents=True, exist_ok=True)
        self._lock = threading.RLock()
        self._init_db()

    def _connect(self) -> sqlite3.Connection:
        # factory=_ClosingConnection only changes behavior for callers that
        # use `with self._connect() as conn:` (the vast majority). The few
        # call sites that keep the connection across a `with` block (e.g.
        # backup_to/restore_from) already call .close() themselves and are
        # unaffected either way.
        conn = sqlite3.connect(self.db_path, timeout=30, check_same_thread=False, factory=_ClosingConnection)
        conn.row_factory = sqlite3.Row
        conn.execute("PRAGMA journal_mode=WAL")
        conn.execute("PRAGMA foreign_keys=ON")
        return conn

    def _ensure_columns(self, conn: sqlite3.Connection, table: str, columns: dict[str, str]) -> None:
        existing = {row["name"] for row in conn.execute(f"PRAGMA table_info({table})")}
        for name, definition in columns.items():
            if name not in existing:
                conn.execute(f"ALTER TABLE {table} ADD COLUMN {name} {definition}")

    def _init_db(self) -> None:
        with self._connect() as conn:
            # Pre-migrate very old/partial schemas before CREATE INDEX statements run.
            # SQLite's CREATE TABLE IF NOT EXISTS does not add missing columns, and an
            # index that references a missing column aborts the whole startup.
            existing_tables = {row["name"] for row in conn.execute("SELECT name FROM sqlite_master WHERE type='table'")}
            preflight_columns: dict[str, dict[str, str]] = {
                "songs": {
                    "title": "TEXT NOT NULL DEFAULT ''", "display_name": "TEXT DEFAULT ''",
                    "created_at": "TEXT DEFAULT ''", "updated_at": "TEXT DEFAULT ''",
                    "duration": "REAL DEFAULT 0", "model_version": "TEXT DEFAULT ''",
                    "tags": "TEXT DEFAULT ''", "prompt": "TEXT DEFAULT ''", "lyrics": "TEXT DEFAULT ''",
                    "image_url": "TEXT DEFAULT ''", "audio_url": "TEXT DEFAULT ''", "video_url": "TEXT DEFAULT ''",
                    "source_url": "TEXT DEFAULT ''", "is_liked": "INTEGER DEFAULT 0",
                    "is_public": "INTEGER DEFAULT 0", "is_trashed": "INTEGER DEFAULT 0",
                    "clip_type": "TEXT DEFAULT ''", "local_audio": "TEXT DEFAULT ''",
                    "local_wav": "TEXT DEFAULT ''", "local_video": "TEXT DEFAULT ''",
                    "local_cover": "TEXT DEFAULT ''", "local_lyrics": "TEXT DEFAULT ''",
                    "local_lrc": "TEXT DEFAULT ''", "local_srt": "TEXT DEFAULT ''",
                    "file_sha256": "TEXT DEFAULT ''", "notes": "TEXT DEFAULT ''",
                    "custom_tags": "TEXT DEFAULT ''", "rating": "INTEGER DEFAULT 0",
                    "favorite": "INTEGER DEFAULT 0", "downloaded_at": "TEXT DEFAULT ''",
                    "raw_json": "TEXT DEFAULT '{}'",
                },
                "derived_files": {"song_id": "TEXT DEFAULT ''", "created_at": "TEXT DEFAULT CURRENT_TIMESTAMP"},
                "youtube_channels": {"is_owned": "INTEGER DEFAULT 1", "title": "TEXT DEFAULT ''"},
                "youtube_videos": {"channel_id": "TEXT DEFAULT ''", "published_at": "TEXT DEFAULT ''"},
                "youtube_matches": {"status": "TEXT DEFAULT 'new'", "match_type": "TEXT DEFAULT 'external_candidate'", "score": "REAL DEFAULT 0"},
                "audio_fingerprints": {"source_type": "TEXT DEFAULT ''", "source_id": "TEXT DEFAULT ''", "algorithm": "TEXT DEFAULT ''"},
                "song_history": {"song_id": "TEXT DEFAULT ''"},
                "backup_items": {"backup_id": "TEXT DEFAULT ''", "song_id": "TEXT DEFAULT ''"},
                "job_queue": {"status": "TEXT DEFAULT 'pending'"},
                "subtitle_cues": {"song_id": "TEXT DEFAULT ''", "cue_index": "INTEGER DEFAULT 0"},
            }
            for table, columns in preflight_columns.items():
                if table in existing_tables:
                    self._ensure_columns(conn, table, columns)
            conn.executescript(
                """
                CREATE TABLE IF NOT EXISTS songs (
                    id TEXT PRIMARY KEY,
                    title TEXT NOT NULL DEFAULT '',
                    display_name TEXT DEFAULT '',
                    created_at TEXT DEFAULT '',
                    updated_at TEXT DEFAULT '',
                    duration REAL DEFAULT 0,
                    model_version TEXT DEFAULT '',
                    tags TEXT DEFAULT '',
                    prompt TEXT DEFAULT '',
                    lyrics TEXT DEFAULT '',
                    image_url TEXT DEFAULT '',
                    audio_url TEXT DEFAULT '',
                    video_url TEXT DEFAULT '',
                    source_url TEXT DEFAULT '',
                    is_liked INTEGER DEFAULT 0,
                    is_public INTEGER DEFAULT 0,
                    is_trashed INTEGER DEFAULT 0,
                    clip_type TEXT DEFAULT '',
                    local_audio TEXT DEFAULT '',
                    local_wav TEXT DEFAULT '',
                    local_video TEXT DEFAULT '',
                    local_cover TEXT DEFAULT '',
                    local_lyrics TEXT DEFAULT '',
                    local_lrc TEXT DEFAULT '',
                    local_srt TEXT DEFAULT '',
                    file_sha256 TEXT DEFAULT '',
                    notes TEXT DEFAULT '',
                    custom_tags TEXT DEFAULT '',
                    rating INTEGER DEFAULT 0,
                    favorite INTEGER DEFAULT 0,
                    downloaded_at TEXT DEFAULT '',
                    raw_json TEXT DEFAULT '{}'
                );
                CREATE INDEX IF NOT EXISTS idx_songs_created ON songs(created_at DESC);
                CREATE INDEX IF NOT EXISTS idx_songs_title ON songs(title COLLATE NOCASE);
                CREATE INDEX IF NOT EXISTS idx_songs_liked ON songs(is_liked);
                CREATE INDEX IF NOT EXISTS idx_songs_downloaded ON songs(downloaded_at);

                CREATE TABLE IF NOT EXISTS collections (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    slug TEXT NOT NULL UNIQUE,
                    name TEXT NOT NULL UNIQUE,
                    color TEXT DEFAULT '#7c3aed',
                    is_system INTEGER DEFAULT 0,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP
                );
                CREATE TABLE IF NOT EXISTS collection_songs (
                    collection_id INTEGER NOT NULL,
                    song_id TEXT NOT NULL,
                    added_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY(collection_id, song_id),
                    FOREIGN KEY(collection_id) REFERENCES collections(id) ON DELETE CASCADE,
                    FOREIGN KEY(song_id) REFERENCES songs(id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS idx_collection_songs_song ON collection_songs(song_id);

                CREATE TABLE IF NOT EXISTS derived_files (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    song_id TEXT NOT NULL,
                    kind TEXT NOT NULL DEFAULT 'processed',
                    label TEXT NOT NULL DEFAULT '',
                    path TEXT NOT NULL,
                    format TEXT DEFAULT '',
                    duration REAL DEFAULT 0,
                    settings_json TEXT DEFAULT '{}',
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY(song_id) REFERENCES songs(id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS idx_derived_song ON derived_files(song_id, created_at DESC);

                CREATE TABLE IF NOT EXISTS settings (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS app_log (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    level TEXT NOT NULL,
                    message TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS watched_folders (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    path TEXT NOT NULL UNIQUE,
                    recursive INTEGER DEFAULT 1,
                    enabled INTEGER DEFAULT 1,
                    last_scan_at TEXT DEFAULT '',
                    last_file_count INTEGER DEFAULT 0,
                    last_added_count INTEGER DEFAULT 0,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP
                );
                CREATE INDEX IF NOT EXISTS idx_watched_folders_enabled ON watched_folders(enabled, path);

                CREATE TABLE IF NOT EXISTS recognized_tracks (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    original_filename TEXT DEFAULT '',
                    input_path TEXT DEFAULT '',
                    prepared_audio_path TEXT DEFAULT '',
                    provider TEXT DEFAULT 'AudD',
                    found INTEGER DEFAULT 0,
                    artist TEXT DEFAULT '',
                    title TEXT DEFAULT '',
                    album TEXT DEFAULT '',
                    release_date TEXT DEFAULT '',
                    label TEXT DEFAULT '',
                    timecode TEXT DEFAULT '',
                    song_link TEXT DEFAULT '',
                    result_json TEXT DEFAULT '{}',
                    library_song_id TEXT DEFAULT '',
                    status TEXT DEFAULT 'done',
                    error_message TEXT DEFAULT ''
                );
                CREATE INDEX IF NOT EXISTS idx_recognized_tracks_created ON recognized_tracks(created_at DESC);
                CREATE INDEX IF NOT EXISTS idx_recognized_tracks_title ON recognized_tracks(title COLLATE NOCASE, artist COLLATE NOCASE);

                CREATE TABLE IF NOT EXISTS youtube_channels (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    channel_id TEXT NOT NULL UNIQUE,
                    title TEXT NOT NULL DEFAULT '',
                    handle TEXT DEFAULT '',
                    url TEXT DEFAULT '',
                    uploads_playlist TEXT DEFAULT '',
                    is_owned INTEGER DEFAULT 1,
                    source_mode TEXT DEFAULT 'api',
                    subscriber_count INTEGER DEFAULT 0,
                    video_count INTEGER DEFAULT 0,
                    view_count INTEGER DEFAULT 0,
                    oauth_profile_id TEXT DEFAULT '',
                    google_email TEXT DEFAULT '',
                    last_scan_at TEXT DEFAULT '',
                    latest_video_at TEXT DEFAULT '',
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP
                );
                CREATE INDEX IF NOT EXISTS idx_youtube_channels_owned ON youtube_channels(is_owned, title);

                CREATE TABLE IF NOT EXISTS youtube_videos (
                    video_id TEXT PRIMARY KEY,
                    channel_id TEXT NOT NULL DEFAULT '',
                    channel_title TEXT DEFAULT '',
                    title TEXT NOT NULL DEFAULT '',
                    description TEXT DEFAULT '',
                    published_at TEXT DEFAULT '',
                    url TEXT DEFAULT '',
                    thumbnail_url TEXT DEFAULT '',
                    duration REAL DEFAULT 0,
                    view_count INTEGER DEFAULT 0,
                    like_count INTEGER DEFAULT 0,
                    comment_count INTEGER DEFAULT 0,
                    privacy_status TEXT DEFAULT '',
                    is_owned_channel INTEGER DEFAULT 0,
                    first_seen_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    last_seen_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    raw_json TEXT DEFAULT '{}'
                );
                CREATE INDEX IF NOT EXISTS idx_youtube_videos_channel ON youtube_videos(channel_id, published_at DESC);

                CREATE TABLE IF NOT EXISTS youtube_matches (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    song_id TEXT NOT NULL,
                    video_id TEXT NOT NULL,
                    match_type TEXT NOT NULL DEFAULT 'external_candidate',
                    score REAL DEFAULT 0,
                    reason TEXT DEFAULT '',
                    status TEXT DEFAULT 'new',
                    contact_email TEXT DEFAULT '',
                    contact_status TEXT DEFAULT '',
                    contacted_at TEXT DEFAULT '',
                    contact_note TEXT DEFAULT '',
                    first_found_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    last_seen_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE(song_id, video_id),
                    FOREIGN KEY(song_id) REFERENCES songs(id) ON DELETE CASCADE,
                    FOREIGN KEY(video_id) REFERENCES youtube_videos(video_id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS idx_youtube_matches_status ON youtube_matches(status, match_type, score DESC);

                CREATE TABLE IF NOT EXISTS youtube_scan_runs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    scan_type TEXT NOT NULL,
                    started_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    finished_at TEXT DEFAULT '',
                    channel_count INTEGER DEFAULT 0,
                    song_count INTEGER DEFAULT 0,
                    result_count INTEGER DEFAULT 0,
                    error_count INTEGER DEFAULT 0,
                    summary TEXT DEFAULT ''
                );

                CREATE TABLE IF NOT EXISTS audio_fingerprints (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    source_type TEXT NOT NULL,
                    source_id TEXT NOT NULL,
                    algorithm TEXT NOT NULL,
                    duration REAL DEFAULT 0,
                    interval REAL DEFAULT 0.5,
                    source_identity TEXT DEFAULT '',
                    source_mtime REAL DEFAULT 0,
                    source_size INTEGER DEFAULT 0,
                    payload BLOB NOT NULL,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    updated_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE(source_type, source_id, algorithm)
                );
                CREATE INDEX IF NOT EXISTS idx_audio_fingerprints_source ON audio_fingerprints(source_type, source_id, algorithm);

                CREATE TABLE IF NOT EXISTS sync_checkpoints (
                    source_key TEXT PRIMARY KEY,
                    source_label TEXT DEFAULT '',
                    cursor TEXT DEFAULT '',
                    page_no INTEGER DEFAULT 0,
                    records_processed INTEGER DEFAULT 0,
                    mode TEXT DEFAULT '',
                    completed INTEGER DEFAULT 0,
                    updated_at TEXT DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS song_history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    song_id TEXT NOT NULL,
                    action TEXT NOT NULL DEFAULT 'edit',
                    fields_json TEXT NOT NULL DEFAULT '{}',
                    snapshot_json TEXT NOT NULL DEFAULT '{}',
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY(song_id) REFERENCES songs(id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS idx_song_history_song ON song_history(song_id, id DESC);

                CREATE TABLE IF NOT EXISTS backup_items (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    backup_id TEXT NOT NULL,
                    song_id TEXT DEFAULT '',
                    field_name TEXT DEFAULT '',
                    original_path TEXT DEFAULT '',
                    stored_path TEXT DEFAULT '',
                    sha256 TEXT DEFAULT '',
                    size_bytes INTEGER DEFAULT 0,
                    modified_at REAL DEFAULT 0,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE(backup_id, original_path)
                );
                CREATE INDEX IF NOT EXISTS idx_backup_items_backup ON backup_items(backup_id, song_id);

                CREATE TABLE IF NOT EXISTS job_queue (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    task_type TEXT NOT NULL,
                    title TEXT NOT NULL DEFAULT '',
                    payload_json TEXT NOT NULL DEFAULT '{}',
                    status TEXT NOT NULL DEFAULT 'pending',
                    progress REAL DEFAULT 0,
                    message TEXT DEFAULT '',
                    attempts INTEGER DEFAULT 0,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    started_at TEXT DEFAULT '',
                    finished_at TEXT DEFAULT '',
                    updated_at TEXT DEFAULT CURRENT_TIMESTAMP
                );
                CREATE INDEX IF NOT EXISTS idx_job_queue_status ON job_queue(status, id);

                CREATE TABLE IF NOT EXISTS scheduled_tasks (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    task_type TEXT NOT NULL,
                    name TEXT NOT NULL DEFAULT '',
                    interval_minutes INTEGER DEFAULT 60,
                    enabled INTEGER DEFAULT 1,
                    options_json TEXT NOT NULL DEFAULT '{}',
                    last_run_at TEXT DEFAULT '',
                    next_run_at TEXT DEFAULT '',
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS subtitle_cues (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    song_id TEXT NOT NULL,
                    cue_index INTEGER NOT NULL,
                    start_s REAL DEFAULT 0,
                    end_s REAL DEFAULT 0,
                    text TEXT DEFAULT '',
                    source TEXT DEFAULT 'manual',
                    updated_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE(song_id, cue_index),
                    FOREIGN KEY(song_id) REFERENCES songs(id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS idx_subtitle_cues_song ON subtitle_cues(song_id, cue_index);

                CREATE TABLE IF NOT EXISTS text_comparisons (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    song_id TEXT NOT NULL,
                    video_id TEXT DEFAULT '',
                    similarity REAL DEFAULT 0,
                    missing_lines_json TEXT DEFAULT '[]',
                    changed_lines_json TEXT DEFAULT '[]',
                    transcript_text TEXT DEFAULT '',
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE(song_id, video_id),
                    FOREIGN KEY(song_id) REFERENCES songs(id) ON DELETE CASCADE
                );
                """
            )
            self._ensure_columns(conn, "youtube_matches", {
                "audio_score": "REAL DEFAULT 0",
                "coverage_percent": "REAL DEFAULT 0",
                "matched_seconds": "REAL DEFAULT 0",
                "completeness_status": "TEXT DEFAULT ''",
                "confidence": "TEXT DEFAULT ''",
                "source_start": "REAL DEFAULT 0",
                "source_end": "REAL DEFAULT 0",
                "video_start": "REAL DEFAULT 0",
                "video_end": "REAL DEFAULT 0",
                "missing_start": "REAL DEFAULT 0",
                "missing_end": "REAL DEFAULT 0",
                "has_intro": "INTEGER DEFAULT 0",
                "has_outro": "INTEGER DEFAULT 0",
                "audio_checked_at": "TEXT DEFAULT ''",
                "analysis_version": "TEXT DEFAULT ''",
                "manual_link": "INTEGER DEFAULT 0",
                "segments_json": "TEXT DEFAULT '[]'",
                "missing_intervals_json": "TEXT DEFAULT '[]'",
                "segment_count": "INTEGER DEFAULT 0",
                "occurrence_count": "INTEGER DEFAULT 0",
                "total_matched_seconds": "REAL DEFAULT 0",
                "internal_gap_seconds": "REAL DEFAULT 0",
                "version_type": "TEXT DEFAULT ''",
                "recommendation": "TEXT DEFAULT ''",
                "match_role": "TEXT DEFAULT 'primary'",
                "video_song_count": "INTEGER DEFAULT 1",
            })
            self._ensure_columns(conn, "youtube_channels", {
                "oauth_profile_id": "TEXT DEFAULT ''",
                "google_email": "TEXT DEFAULT ''",
                "thumbnail_url": "TEXT DEFAULT ''",
            })
            self._ensure_columns(conn, "recognized_tracks", {
                "manual_override_at": "TEXT DEFAULT ''",
            })
            self._ensure_columns(conn, "youtube_videos", {
                "audio_cache_path": "TEXT DEFAULT ''",
                "audio_cache_sha256": "TEXT DEFAULT ''",
                "audio_analyzed_at": "TEXT DEFAULT ''",
            })

            self._ensure_columns(conn, "songs", {
                "album": "TEXT DEFAULT ''",
                "genre": "TEXT DEFAULT ''",
                "year": "TEXT DEFAULT ''",
                "track_number": "TEXT DEFAULT ''",
                "copyright": "TEXT DEFAULT ''",
                "website": "TEXT DEFAULT ''",
                "bpm": "TEXT DEFAULT ''",
                "key_signature": "TEXT DEFAULT ''",
                "youtube_url": "TEXT DEFAULT ''",
                "youtube_published_at": "TEXT DEFAULT ''",
                "source_group": "TEXT DEFAULT ''",
                "title_locked": "INTEGER DEFAULT 0",
                "artist_locked": "INTEGER DEFAULT 0",
                "lyrics_locked": "INTEGER DEFAULT 0",
                "content_sha256": "TEXT DEFAULT ''",
                "first_seen_at": "TEXT DEFAULT ''",
                "last_seen_at": "TEXT DEFAULT ''",
                "last_checked_at": "TEXT DEFAULT ''",
                "archived": "INTEGER DEFAULT 0",
            })
            conn.execute("UPDATE songs SET first_seen_at=COALESCE(NULLIF(first_seen_at,''), NULLIF(created_at,''), datetime('now')) WHERE first_seen_at='' OR first_seen_at IS NULL")
            conn.execute("UPDATE songs SET last_seen_at=COALESCE(NULLIF(last_seen_at,''), first_seen_at) WHERE last_seen_at='' OR last_seen_at IS NULL")
            for slug, name, color in BUILTIN_COLLECTIONS:
                conn.execute(
                    "INSERT INTO collections(slug,name,color,is_system) VALUES(?,?,?,1) "
                    "ON CONFLICT(slug) DO UPDATE SET name=excluded.name,color=excluded.color,is_system=1",
                    (slug, name, color),
                )

    @staticmethod
    def _b(value: Any) -> int:
        return 1 if bool(value) else 0

    @staticmethod
    def _text(value: Any) -> str:
        if value is None:
            return ""
        if isinstance(value, str):
            return value
        if isinstance(value, (list, tuple, set)):
            return ", ".join(str(x) for x in value if x is not None)
        if isinstance(value, dict):
            return json.dumps(value, ensure_ascii=False, default=str)
        return str(value)

    @staticmethod
    def _duration(value: Any) -> float:
        if isinstance(value, bool) or value is None:
            return 0.0
        if isinstance(value, (int, float)):
            return max(0.0, float(value))
        raw = str(value).strip()
        if not raw:
            return 0.0
        try:
            return max(0.0, float(raw.replace(",", ".")))
        except ValueError:
            pass
        parts = raw.split(":")
        try:
            nums = [float(x.replace(",", ".")) for x in parts]
            if len(nums) == 2:
                return max(0.0, nums[0] * 60 + nums[1])
            if len(nums) == 3:
                return max(0.0, nums[0] * 3600 + nums[1] * 60 + nums[2])
        except (TypeError, ValueError):
            pass
        return 0.0

    def upsert_song(self, song: dict[str, Any], source_group: str = "") -> bool:
        if not isinstance(song, dict):
            return False
        metadata = song.get("metadata") if isinstance(song.get("metadata"), dict) else {}
        reaction = song.get("reaction") if isinstance(song.get("reaction"), dict) else {}
        has_like_signal = "is_liked" in song or "reaction" in song or "vote" in song or "vote" in metadata
        has_public_signal = "is_public" in song
        has_trashed_signal = "is_trashed" in song
        is_liked = bool(song.get("is_liked")) or reaction.get("reaction_type") in ("L", "LIKE") or song.get("vote") == "up" or metadata.get("vote") == "up"
        lyrics = metadata.get("lyrics") or metadata.get("text") or metadata.get("prompt") or song.get("lyrics") or ""
        prompt = metadata.get("prompt") or metadata.get("gpt_description_prompt") or song.get("prompt") or ""
        song_id = self._text(song.get("id") or song.get("clip_id") or song.get("song_id")).strip()
        if not song_id:
            return False
        duration_value = metadata.get("duration")
        if duration_value in (None, "", 0, 0.0):
            duration_value = song.get("duration") or song.get("duration_s") or metadata.get("duration_s")
        row = {
            "id": song_id,
            "title": self._text(song.get("title") or song.get("display_name") or ""),
            "display_name": self._text(song.get("display_name") or song.get("persona_name") or song.get("artist_name") or ""),
            "created_at": self._text(song.get("created_at") or ""),
            "updated_at": self._text(song.get("updated_at") or ""),
            "duration": self._duration(duration_value),
            "model_version": self._text(song.get("major_model_version") or song.get("model_name") or metadata.get("model_version") or ""),
            "tags": self._text(song.get("display_tags") or metadata.get("tags") or ""),
            "prompt": self._text(prompt),
            "lyrics": self._text(lyrics),
            "image_url": self._text(song.get("image_large_url") or song.get("image_url") or ""),
            "audio_url": self._text(song.get("audio_url") or metadata.get("audio_url") or ""),
            "video_url": self._text(song.get("video_url") or metadata.get("video_url") or ""),
            "source_url": self._text(song.get("source_url") or ("" if song_id.startswith("local-") else f"https://suno.com/song/{song_id}")),
            "is_liked": self._b(is_liked),
            "is_public": self._b(song.get("is_public")),
            "is_trashed": self._b(song.get("is_trashed")),
            "clip_type": self._text(metadata.get("type") or song.get("type") or ""),
            "source_group": self._text(source_group or song.get("source_group") or ""),
            "raw_json": json.dumps(song, ensure_ascii=False, default=str),
            "has_like_signal": self._b(has_like_signal),
            "has_public_signal": self._b(has_public_signal),
            "has_trashed_signal": self._b(has_trashed_signal),
            "seen_now": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        }
        with self._lock, self._connect() as conn:
            conn.execute(
                """
                INSERT INTO songs (
                    id,title,display_name,created_at,updated_at,duration,model_version,tags,prompt,lyrics,
                    image_url,audio_url,video_url,source_url,is_liked,is_public,is_trashed,clip_type,source_group,raw_json,
                    first_seen_at,last_seen_at,last_checked_at
                ) VALUES (
                    :id,:title,:display_name,:created_at,:updated_at,:duration,:model_version,:tags,:prompt,:lyrics,
                    :image_url,:audio_url,:video_url,:source_url,:is_liked,:is_public,:is_trashed,:clip_type,:source_group,:raw_json,
                    :seen_now,:seen_now,:seen_now
                )
                ON CONFLICT(id) DO UPDATE SET
                    title=CASE WHEN songs.title_locked=1 THEN songs.title WHEN excluded.title<>'' THEN excluded.title ELSE songs.title END,
                    display_name=CASE WHEN songs.artist_locked=1 THEN songs.display_name WHEN excluded.display_name<>'' THEN excluded.display_name ELSE songs.display_name END,
                    created_at=CASE WHEN excluded.created_at<>'' THEN excluded.created_at ELSE songs.created_at END,
                    updated_at=excluded.updated_at,
                    duration=CASE WHEN excluded.duration>0 THEN excluded.duration ELSE songs.duration END,
                    model_version=CASE WHEN excluded.model_version<>'' THEN excluded.model_version ELSE songs.model_version END,
                    tags=CASE WHEN excluded.tags<>'' THEN excluded.tags ELSE songs.tags END,
                    prompt=CASE WHEN excluded.prompt<>'' THEN excluded.prompt ELSE songs.prompt END,
                    lyrics=CASE WHEN songs.lyrics_locked=1 THEN songs.lyrics WHEN excluded.lyrics<>'' THEN excluded.lyrics ELSE songs.lyrics END,
                    image_url=CASE WHEN excluded.image_url<>'' THEN excluded.image_url ELSE songs.image_url END,
                    audio_url=CASE WHEN excluded.audio_url<>'' THEN excluded.audio_url ELSE songs.audio_url END,
                    video_url=CASE WHEN excluded.video_url<>'' THEN excluded.video_url ELSE songs.video_url END,
                    source_url=excluded.source_url,
                    is_liked=CASE WHEN :has_like_signal=1 THEN excluded.is_liked ELSE songs.is_liked END,
                    is_public=CASE WHEN :has_public_signal=1 THEN excluded.is_public ELSE songs.is_public END,
                    is_trashed=CASE WHEN :has_trashed_signal=1 THEN excluded.is_trashed ELSE songs.is_trashed END,
                    clip_type=CASE WHEN excluded.clip_type<>'' THEN excluded.clip_type ELSE songs.clip_type END,
                    source_group=CASE WHEN excluded.source_group<>'' THEN excluded.source_group ELSE songs.source_group END,
                    raw_json=excluded.raw_json,
                    last_seen_at=:seen_now,
                    last_checked_at=:seen_now
                """,
                row,
            )
        return True

    def update_song_files(self, song_id: str, **fields: str) -> None:
        allowed = {
            "local_audio", "local_wav", "local_video", "local_cover", "local_lyrics",
            "local_lrc", "local_srt", "file_sha256", "content_sha256", "downloaded_at"
        }
        pairs, params = [], {"id": song_id}
        for key, value in fields.items():
            if key in allowed:
                pairs.append(f"{key}=:{key}")
                params[key] = value
        if not pairs:
            return
        with self._lock, self._connect() as conn:
            conn.execute(f"UPDATE songs SET {', '.join(pairs)} WHERE id=:id", params)

    def update_song_user_fields(self, song_id: str, fields: dict[str, Any]) -> None:
        self.add_song_history(song_id, "izmena podataka", fields)
        allowed = {
            "title", "display_name", "lyrics", "notes", "custom_tags", "rating", "favorite",
            "album", "genre", "year", "track_number", "copyright", "website", "bpm",
            "key_signature", "youtube_url", "youtube_published_at", "archived"
        }
        pairs, params = [], {"id": song_id}
        for key, value in fields.items():
            if key in allowed:
                pairs.append(f"{key}=:{key}")
                params[key] = value
        if pairs:
            if "title" in fields:
                pairs.append("title_locked=1")
            if "display_name" in fields:
                pairs.append("artist_locked=1")
            if "lyrics" in fields:
                pairs.append("lyrics_locked=1")
            with self._lock, self._connect() as conn:
                conn.execute(f"UPDATE songs SET {', '.join(pairs)} WHERE id=:id", params)

    def reset_user_locks(self, song_id: str, fields: Iterable[str]) -> None:
        mapping = {"title": "title_locked", "display_name": "artist_locked", "lyrics": "lyrics_locked"}
        columns = [mapping[name] for name in fields if name in mapping]
        if not columns:
            return
        with self._lock, self._connect() as conn:
            conn.execute(f"UPDATE songs SET {', '.join(f'{c}=0' for c in columns)} WHERE id=?", (song_id,))

    def find_song_by_local_path(self, path: str) -> dict[str, Any] | None:
        normalized = str(Path(path).resolve())
        with self._connect() as conn:
            for row in conn.execute("SELECT * FROM songs WHERE local_audio<>'' OR local_wav<>''"):
                for key in ("local_audio", "local_wav"):
                    value = row[key]
                    if value:
                        try:
                            if str(Path(value).resolve()) == normalized:
                                return dict(row)
                        except Exception:
                            continue
        return None

    def delete_song(self, song_id: str, delete_files: bool = False) -> dict[str, Any] | None:
        song = self.get_song(song_id)
        if not song:
            return None
        file_paths: list[Path] = []
        if delete_files:
            for key in ("local_audio", "local_wav", "local_video", "local_cover", "local_lyrics", "local_lrc", "local_srt"):
                value = str(song.get(key) or "")
                if value:
                    file_paths.append(Path(value))
            for item in song.get("derived_files") or []:
                value = str(item.get("path") or "")
                if value:
                    file_paths.append(Path(value))
        with self._lock, self._connect() as conn:
            conn.execute("DELETE FROM songs WHERE id=?", (song_id,))
        if delete_files:
            for file_path in file_paths:
                try:
                    if file_path.exists() and file_path.is_file():
                        file_path.unlink()
                except Exception:
                    pass
        return song

    def local_file_health(self, repair: bool = False) -> dict[str, Any]:
        file_columns = ("local_audio", "local_wav", "local_video", "local_cover", "local_lyrics", "local_lrc", "local_srt")
        missing: list[dict[str, str]] = []
        repaired = 0
        with self._lock, self._connect() as conn:
            rows = conn.execute("SELECT id,title," + ",".join(file_columns) + " FROM songs").fetchall()
            for row in rows:
                updates: list[str] = []
                for column in file_columns:
                    value = str(row[column] or "")
                    if value and not Path(value).exists():
                        missing.append({"song_id": row["id"], "title": row["title"], "field": column, "path": value})
                        if repair:
                            updates.append(f"{column}='' ")
                if updates:
                    conn.execute(f"UPDATE songs SET {', '.join(updates)} WHERE id=?", (row["id"],))
                    repaired += len(updates)
            for row in conn.execute("SELECT id,song_id,path FROM derived_files").fetchall():
                value = str(row["path"] or "")
                if value and not Path(value).exists():
                    missing.append({"song_id": row["song_id"], "title": "Obrađena verzija", "field": "derived_file", "path": value})
                    if repair:
                        conn.execute("DELETE FROM derived_files WHERE id=?", (row["id"],))
                        repaired += 1
        return {"missing_count": len(missing), "repaired_count": repaired, "missing": missing[:500]}

    def backup_to(self, destination: Path) -> Path:
        destination = Path(destination)
        destination.parent.mkdir(parents=True, exist_ok=True)
        temp = destination.with_suffix(destination.suffix + ".part")
        temp.unlink(missing_ok=True)
        with self._lock:
            source = self._connect()
            target = sqlite3.connect(temp)
            try:
                source.backup(target)
                target.commit()
            finally:
                target.close()
                source.close()
        temp.replace(destination)
        return destination

    def integrity_check(self, quick: bool = True) -> dict[str, Any]:
        """Proveri da li je SQLite baza čitljiva i konzistentna."""
        pragma = "quick_check" if quick else "integrity_check"
        try:
            with self._connect() as conn:
                rows = [str(row[0]) for row in conn.execute(f"PRAGMA {pragma}").fetchall()]
                foreign = [dict(row) for row in conn.execute("PRAGMA foreign_key_check").fetchall()]
                tables = [str(row[0]) for row in conn.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name").fetchall()]
            ok = bool(rows) and all(value.lower() == "ok" for value in rows) and not foreign
            return {"ok": ok, "check": pragma, "messages": rows, "foreign_key_errors": foreign, "tables": tables, "path": str(self.db_path)}
        except Exception as exc:
            return {"ok": False, "check": pragma, "messages": [str(exc)], "foreign_key_errors": [], "tables": [], "path": str(self.db_path)}

    def update_derived_file_path(self, file_id: int, path: str) -> bool:
        normalized = str(Path(path).resolve())
        with self._lock, self._connect() as conn:
            cur = conn.execute("UPDATE derived_files SET path=? WHERE id=?", (normalized, int(file_id)))
            return cur.rowcount > 0

    def database_counts(self) -> dict[str, int]:
        names = ("songs", "collections", "collection_songs", "derived_files", "youtube_channels", "youtube_videos", "youtube_matches", "youtube_scan_runs", "app_log")
        result: dict[str, int] = {}
        with self._connect() as conn:
            existing = {str(row[0]) for row in conn.execute("SELECT name FROM sqlite_master WHERE type='table'").fetchall()}
            for name in names:
                result[name] = int(conn.execute(f"SELECT COUNT(*) FROM {name}").fetchone()[0]) if name in existing else 0
        return result

    def restore_from(self, source_db: Path) -> None:
        source_db = Path(source_db)
        if not source_db.exists() or not source_db.is_file():
            raise ValueError("Backup baza ne postoji.")
        probe = sqlite3.connect(source_db)
        try:
            tables = {row[0] for row in probe.execute("SELECT name FROM sqlite_master WHERE type='table'")}
            if "songs" not in tables or "settings" not in tables:
                raise ValueError("Izabrani fajl nije ispravna Suno Pesme Studio baza.")
            integrity = probe.execute("PRAGMA integrity_check").fetchone()
            if not integrity or str(integrity[0]).lower() != "ok":
                raise ValueError("Backup baza je oštećena.")
        finally:
            probe.close()
        with self._lock:
            # Ne menjaj SQLite fajl običnim shutil.copy dok server radi. U WAL režimu
            # to može ostaviti otvorene čitaoce vezane za staru bazu i napraviti
            # poruku "database disk image is malformed". SQLite backup API upisuje
            # snapshot u aktivnu bazu bez cepanja glavnog/WAL/SHM skupa.
            stamp = datetime.now().strftime("%Y%m%d-%H%M%S-%f")
            safety = self.db_path.with_name(f"{self.db_path.stem}-pre-restore-{stamp}{self.db_path.suffix}")
            if self.db_path.exists():
                self.backup_to(safety)
            source = sqlite3.connect(source_db, timeout=30, check_same_thread=False)
            destination = self._connect()
            try:
                source.backup(destination)
                destination.commit()
                check = destination.execute("PRAGMA integrity_check").fetchone()
                if not check or str(check[0]).lower() != "ok":
                    raise ValueError("Vraćena baza nije prošla završnu SQLite proveru.")
            except Exception:
                destination.close()
                source.close()
                if safety.exists():
                    rollback_source = sqlite3.connect(safety, timeout=30, check_same_thread=False)
                    rollback_destination = self._connect()
                    try:
                        rollback_source.backup(rollback_destination)
                        rollback_destination.commit()
                    finally:
                        rollback_destination.close()
                        rollback_source.close()
                raise
            else:
                destination.close()
                source.close()
            self._init_db()

    def get_sync_checkpoint(self, source_key: str) -> dict[str, Any] | None:
        with self._connect() as conn:
            row = conn.execute("SELECT * FROM sync_checkpoints WHERE source_key=?", (str(source_key),)).fetchone()
            return dict(row) if row else None

    def save_sync_checkpoint(self, source_key: str, source_label: str, cursor: str | None, page_no: int, records_processed: int, mode: str = "", completed: bool = False) -> None:
        with self._lock, self._connect() as conn:
            conn.execute(
                """INSERT INTO sync_checkpoints(source_key,source_label,cursor,page_no,records_processed,mode,completed,updated_at)
                   VALUES(?,?,?,?,?,?,?,CURRENT_TIMESTAMP)
                   ON CONFLICT(source_key) DO UPDATE SET source_label=excluded.source_label,cursor=excluded.cursor,
                   page_no=excluded.page_no,records_processed=excluded.records_processed,mode=excluded.mode,
                   completed=excluded.completed,updated_at=CURRENT_TIMESTAMP""",
                (str(source_key), str(source_label), str(cursor or ""), int(page_no), int(records_processed), str(mode or ""), 1 if completed else 0),
            )

    def clear_sync_checkpoints(self, prefix: str = "") -> int:
        with self._lock, self._connect() as conn:
            if prefix:
                cur = conn.execute("DELETE FROM sync_checkpoints WHERE source_key LIKE ?", (str(prefix) + "%",))
            else:
                cur = conn.execute("DELETE FROM sync_checkpoints")
            return cur.rowcount

    def list_sync_checkpoints(self) -> list[dict[str, Any]]:
        with self._connect() as conn:
            return [dict(r) for r in conn.execute("SELECT * FROM sync_checkpoints ORDER BY updated_at DESC")]

    def _history_snapshot(self, conn: sqlite3.Connection, song_id: str) -> dict[str, Any]:
        row = conn.execute("SELECT * FROM songs WHERE id=?", (song_id,)).fetchone()
        if not row:
            return {}
        result = dict(row)
        result["collection_ids"] = [int(r[0]) for r in conn.execute("SELECT collection_id FROM collection_songs WHERE song_id=? ORDER BY collection_id", (song_id,))]
        return result

    def add_song_history(self, song_id: str, action: str, fields: dict[str, Any] | None = None) -> int:
        with self._lock, self._connect() as conn:
            snapshot = self._history_snapshot(conn, song_id)
            if not snapshot:
                return 0
            cur = conn.execute(
                "INSERT INTO song_history(song_id,action,fields_json,snapshot_json) VALUES(?,?,?,?)",
                (song_id, str(action or "edit"), json.dumps(fields or {}, ensure_ascii=False, default=str), json.dumps(snapshot, ensure_ascii=False, default=str)),
            )
            # Keep the newest 100 versions per song.
            conn.execute("DELETE FROM song_history WHERE song_id=? AND id NOT IN (SELECT id FROM song_history WHERE song_id=? ORDER BY id DESC LIMIT 100)", (song_id, song_id))
            return int(cur.lastrowid or 0)

    def list_song_history(self, song_id: str, limit: int = 50) -> list[dict[str, Any]]:
        with self._connect() as conn:
            rows = [dict(r) for r in conn.execute("SELECT id,song_id,action,fields_json,created_at FROM song_history WHERE song_id=? ORDER BY id DESC LIMIT ?", (song_id, max(1, min(int(limit), 100)))).fetchall()]
            for row in rows:
                try: row["fields"] = json.loads(row.pop("fields_json") or "{}")
                except Exception: row["fields"] = {}
            return rows

    def undo_song_history(self, song_id: str, history_id: int | None = None) -> dict[str, Any] | None:
        with self._lock, self._connect() as conn:
            if history_id:
                row = conn.execute("SELECT * FROM song_history WHERE id=? AND song_id=?", (int(history_id), song_id)).fetchone()
            else:
                row = conn.execute("SELECT * FROM song_history WHERE song_id=? ORDER BY id DESC LIMIT 1", (song_id,)).fetchone()
            if not row:
                return None
            current = self._history_snapshot(conn, song_id)
            snapshot = json.loads(row["snapshot_json"] or "{}")
            if current:
                conn.execute("INSERT INTO song_history(song_id,action,fields_json,snapshot_json) VALUES(?,?,?,?)", (song_id, "redo-snapshot", "{}", json.dumps(current, ensure_ascii=False, default=str)))
            columns = {r["name"] for r in conn.execute("PRAGMA table_info(songs)")}
            clean = {k: v for k, v in snapshot.items() if k in columns and k != "id"}
            if clean:
                conn.execute("UPDATE songs SET " + ",".join(f"{k}=?" for k in clean) + " WHERE id=?", [*clean.values(), song_id])
            if "collection_ids" in snapshot:
                conn.execute("DELETE FROM collection_songs WHERE song_id=?", (song_id,))
                for cid in snapshot.get("collection_ids") or []:
                    conn.execute("INSERT OR IGNORE INTO collection_songs(collection_id,song_id) VALUES(?,?)", (int(cid), song_id))
            conn.execute("DELETE FROM song_history WHERE id=?", (int(row["id"]),))
        return self.get_song(song_id)

    def enqueue_job(self, task_type: str, title: str, payload: dict[str, Any], status: str = "pending") -> int:
        with self._lock, self._connect() as conn:
            cur = conn.execute("INSERT INTO job_queue(task_type,title,payload_json,status,message) VALUES(?,?,?,?,?)", (task_type, title, json.dumps(payload or {}, ensure_ascii=False, default=str), status, "Čeka pokretanje"))
            return int(cur.lastrowid or 0)

    def update_job(self, job_id: int, **fields: Any) -> None:
        allowed = {"status", "progress", "message", "attempts", "started_at", "finished_at"}
        clean = {k: v for k, v in fields.items() if k in allowed}
        if not clean: return
        with self._lock, self._connect() as conn:
            conn.execute("UPDATE job_queue SET " + ",".join(f"{k}=?" for k in clean) + ",updated_at=CURRENT_TIMESTAMP WHERE id=?", [*clean.values(), int(job_id)])

    def list_jobs(self, limit: int = 200) -> list[dict[str, Any]]:
        with self._connect() as conn:
            rows = [dict(r) for r in conn.execute("SELECT * FROM job_queue ORDER BY id DESC LIMIT ?", (max(1, min(int(limit), 1000)),)).fetchall()]
            for row in rows:
                try: row["payload"] = json.loads(row.pop("payload_json") or "{}")
                except Exception: row["payload"] = {}
            return rows

    def get_job(self, job_id: int) -> dict[str, Any] | None:
        with self._connect() as conn:
            row = conn.execute("SELECT * FROM job_queue WHERE id=?", (int(job_id),)).fetchone()
            if not row: return None
            result = dict(row)
            try: result["payload"] = json.loads(result.pop("payload_json") or "{}")
            except Exception: result["payload"] = {}
            return result

    def mark_interrupted_jobs(self) -> int:
        with self._lock, self._connect() as conn:
            cur = conn.execute("UPDATE job_queue SET status='paused',message='Program je zatvoren tokom izvršavanja. Posao može da se nastavi.',updated_at=CURRENT_TIMESTAMP WHERE status='running'")
            return cur.rowcount

    def delete_job(self, job_id: int) -> int:
        with self._lock, self._connect() as conn:
            return conn.execute("DELETE FROM job_queue WHERE id=?", (int(job_id),)).rowcount

    def upsert_schedule(self, task_type: str, name: str, interval_minutes: int, enabled: bool, options: dict[str, Any] | None = None) -> dict[str, Any]:
        with self._lock, self._connect() as conn:
            row = conn.execute("SELECT id FROM scheduled_tasks WHERE task_type=?", (task_type,)).fetchone()
            if row:
                conn.execute("UPDATE scheduled_tasks SET name=?,interval_minutes=?,enabled=?,options_json=? WHERE id=?", (name, max(5, int(interval_minutes)), 1 if enabled else 0, json.dumps(options or {}, ensure_ascii=False), int(row[0])))
                schedule_id = int(row[0])
            else:
                cur = conn.execute("INSERT INTO scheduled_tasks(task_type,name,interval_minutes,enabled,options_json) VALUES(?,?,?,?,?)", (task_type, name, max(5, int(interval_minutes)), 1 if enabled else 0, json.dumps(options or {}, ensure_ascii=False)))
                schedule_id = int(cur.lastrowid)
            row = conn.execute("SELECT * FROM scheduled_tasks WHERE id=?", (schedule_id,)).fetchone()
            return dict(row)

    def list_schedules(self) -> list[dict[str, Any]]:
        with self._connect() as conn:
            rows = [dict(r) for r in conn.execute("SELECT * FROM scheduled_tasks ORDER BY id")]
            for row in rows:
                try: row["options"] = json.loads(row.pop("options_json") or "{}")
                except Exception: row["options"] = {}
            return rows

    def mark_schedule_run(self, schedule_id: int) -> None:
        with self._lock, self._connect() as conn:
            conn.execute("UPDATE scheduled_tasks SET last_run_at=CURRENT_TIMESTAMP,next_run_at=datetime('now','+'||interval_minutes||' minutes') WHERE id=?", (int(schedule_id),))

    def save_subtitle_cues(self, song_id: str, cues: list[dict[str, Any]], source: str = "manual") -> int:
        with self._lock, self._connect() as conn:
            conn.execute("DELETE FROM subtitle_cues WHERE song_id=?", (song_id,))
            for idx, cue in enumerate(cues, 1):
                conn.execute("INSERT INTO subtitle_cues(song_id,cue_index,start_s,end_s,text,source,updated_at) VALUES(?,?,?,?,?,?,CURRENT_TIMESTAMP)", (song_id, idx, float(cue.get('start_s') or 0), float(cue.get('end_s') or 0), str(cue.get('text') or ''), source))
            return len(cues)

    def list_subtitle_cues(self, song_id: str) -> list[dict[str, Any]]:
        with self._connect() as conn:
            return [dict(r) for r in conn.execute("SELECT cue_index,start_s,end_s,text,source FROM subtitle_cues WHERE song_id=? ORDER BY cue_index", (song_id,)).fetchall()]

    def save_text_comparison(self, song_id: str, video_id: str, result: dict[str, Any]) -> None:
        with self._lock, self._connect() as conn:
            conn.execute("""INSERT INTO text_comparisons(song_id,video_id,similarity,missing_lines_json,changed_lines_json,transcript_text,created_at)
                VALUES(?,?,?,?,?,?,CURRENT_TIMESTAMP) ON CONFLICT(song_id,video_id) DO UPDATE SET similarity=excluded.similarity,
                missing_lines_json=excluded.missing_lines_json,changed_lines_json=excluded.changed_lines_json,transcript_text=excluded.transcript_text,created_at=CURRENT_TIMESTAMP""",
                (song_id, video_id, float(result.get('similarity') or 0), json.dumps(result.get('missing_lines') or [], ensure_ascii=False), json.dumps(result.get('changed_lines') or [], ensure_ascii=False), str(result.get('transcript_text') or '')))

    def get_song(self, song_id: str) -> dict[str, Any] | None:
        with self._connect() as conn:
            row = conn.execute("SELECT * FROM songs WHERE id=?", (song_id,)).fetchone()
            if not row:
                return None
            result = dict(row)
            result["collections"] = [dict(r) for r in conn.execute(
                "SELECT c.* FROM collections c JOIN collection_songs cs ON cs.collection_id=c.id WHERE cs.song_id=? ORDER BY c.is_system DESC,c.name",
                (song_id,),
            ).fetchall()]
            result["derived_files"] = [dict(r) for r in conn.execute(
                "SELECT * FROM derived_files WHERE song_id=? ORDER BY id DESC", (song_id,)
            ).fetchall()]
            return result

    def list_songs(
        self,
        search: str = "",
        filter_name: str = "all",
        sort: str = "newest",
        limit: int = 5000,
        offset: int = 0,
        collection_id: int | None = None,
        source_group: str = "",
        date_from: str = "",
        date_to: str = "",
        min_duration: float | None = None,
        max_duration: float | None = None,
    ) -> list[dict[str, Any]]:
        where, params = ["1=1"], []
        joins = ""
        if collection_id:
            joins = " JOIN collection_songs cs ON cs.song_id=songs.id "
            where.append("cs.collection_id=?")
            params.append(int(collection_id))
        if source_group:
            where.append("source_group=?")
            params.append(source_group)
        if date_from:
            where.append("substr(created_at,1,10)>=?")
            params.append(date_from[:10])
        if date_to:
            where.append("substr(created_at,1,10)<=?")
            params.append(date_to[:10])
        if min_duration is not None:
            where.append("duration>=?")
            params.append(float(min_duration))
        if max_duration is not None and float(max_duration) > 0:
            where.append("duration<=?")
            params.append(float(max_duration))
        search = search.strip()
        if search:
            like = f"%{search}%"
            where.append("(title LIKE ? OR lyrics LIKE ? OR prompt LIKE ? OR tags LIKE ? OR notes LIKE ? OR custom_tags LIKE ? OR source_group LIKE ?)")
            params.extend([like] * 7)
        if filter_name == "downloaded":
            where.append("(local_audio<>'' OR local_wav<>'')")
        elif filter_name == "not_downloaded":
            where.append("local_audio='' AND local_wav='' ")
        elif filter_name == "liked":
            where.append("is_liked=1")
        elif filter_name == "favorites":
            where.append("favorite=1")
        elif filter_name == "public":
            where.append("is_public=1")
        elif filter_name == "instrumental":
            where.append("(lyrics='' OR clip_type LIKE '%instrument%')")
        elif filter_name == "with_text":
            where.append("lyrics<>''")
        elif filter_name == "long":
            where.append("duration>=240")
        elif filter_name == "missing_text":
            where.append("lyrics=''")
        elif filter_name == "recent_7d":
            where.append("datetime(first_seen_at)>=datetime('now','-7 days')")
        elif filter_name == "new_today":
            where.append("date(first_seen_at)=date('now')")
        elif filter_name == "published":
            where.append("(youtube_url<>'' OR youtube_published_at<>'')")
        elif filter_name == "unpublished":
            where.append("youtube_url='' AND youtube_published_at=''")
        elif filter_name == "archived":
            where.append("archived=1")
        elif filter_name == "active":
            where.append("archived=0")

        order_map = {
            "newest": "created_at DESC, songs.rowid DESC",
            "oldest": "created_at ASC, songs.rowid ASC",
            "title": "title COLLATE NOCASE ASC",
            "duration": "duration DESC",
            "rating": "rating DESC, created_at DESC",
        }
        order_by = order_map.get(sort, order_map["newest"])
        params.extend([max(1, min(limit, 10000)), max(0, offset)])
        query = f"SELECT DISTINCT songs.* FROM songs {joins} WHERE {' AND '.join(where)} ORDER BY {order_by} LIMIT ? OFFSET ?"
        with self._connect() as conn:
            rows = [dict(r) for r in conn.execute(query, params).fetchall()]
            if not rows:
                return rows
            ids = [r["id"] for r in rows]
            marks = ",".join("?" for _ in ids)
            grouped: dict[str, list[dict[str, Any]]] = {sid: [] for sid in ids}
            for r in conn.execute(
                f"SELECT cs.song_id,c.id,c.name,c.slug,c.color,c.is_system FROM collection_songs cs JOIN collections c ON c.id=cs.collection_id WHERE cs.song_id IN ({marks})",
                ids,
            ):
                grouped[r["song_id"]].append({k: r[k] for k in ("id", "name", "slug", "color", "is_system")})
            for row in rows:
                row["collections"] = grouped.get(row["id"], [])
            return rows


    def list_song_ids(
        self, search: str = "", filter_name: str = "all", collection_id: int | None = None,
        source_group: str = "", date_from: str = "", date_to: str = "",
        min_duration: float | None = None, max_duration: float | None = None, limit: int = 20000,
    ) -> list[str]:
        # Reuse the exact filtering logic from list_songs in bounded pages, without
        # sending thousands of full song objects to the browser.
        rows = self.list_songs(
            search=search, filter_name=filter_name, sort="newest", limit=max(1, min(int(limit), 20000)),
            offset=0, collection_id=collection_id, source_group=source_group, date_from=date_from,
            date_to=date_to, min_duration=min_duration, max_duration=max_duration,
        )
        return [str(row.get("id") or "") for row in rows if str(row.get("id") or "")]

    def count_songs_filtered(
        self, search: str = "", filter_name: str = "all", collection_id: int | None = None,
        source_group: str = "", date_from: str = "", date_to: str = "",
        min_duration: float | None = None, max_duration: float | None = None,
    ) -> int:
        where, params = ["1=1"], []
        joins = ""
        if collection_id:
            joins = " JOIN collection_songs cs ON cs.song_id=songs.id "
            where.append("cs.collection_id=?"); params.append(int(collection_id))
        if source_group:
            where.append("source_group=?"); params.append(source_group)
        if date_from:
            where.append("substr(created_at,1,10)>=?"); params.append(date_from[:10])
        if date_to:
            where.append("substr(created_at,1,10)<=?"); params.append(date_to[:10])
        if min_duration is not None:
            where.append("duration>=?"); params.append(float(min_duration))
        if max_duration is not None and float(max_duration) > 0:
            where.append("duration<=?"); params.append(float(max_duration))
        search = search.strip()
        if search:
            like = f"%{search}%"; where.append("(title LIKE ? OR lyrics LIKE ? OR prompt LIKE ? OR tags LIKE ? OR notes LIKE ? OR custom_tags LIKE ? OR source_group LIKE ?)"); params.extend([like] * 7)
        filters = {
            "downloaded": "(local_audio<>'' OR local_wav<>'')", "not_downloaded": "local_audio='' AND local_wav=''",
            "liked": "is_liked=1", "favorites": "favorite=1", "public": "is_public=1",
            "instrumental": "(lyrics='' OR clip_type LIKE '%instrument%')", "with_text": "lyrics<>''",
            "long": "duration>=240", "missing_text": "lyrics=''", "recent_7d": "datetime(first_seen_at)>=datetime('now','-7 days')",
            "new_today": "date(first_seen_at)=date('now')", "published": "(youtube_url<>'' OR youtube_published_at<>'')",
            "unpublished": "youtube_url='' AND youtube_published_at=''", "archived": "archived=1", "active": "archived=0",
        }
        if filter_name in filters: where.append(filters[filter_name])
        with self._connect() as conn:
            return int(conn.execute(f"SELECT COUNT(DISTINCT songs.id) FROM songs {joins} WHERE {' AND '.join(where)}", params).fetchone()[0])

    def all_song_ids(self) -> set[str]:
        with self._connect() as conn:
            return {str(r[0]) for r in conn.execute("SELECT id FROM songs")}

    def source_groups(self) -> list[str]:
        with self._connect() as conn:
            return [str(r[0]) for r in conn.execute("SELECT DISTINCT source_group FROM songs WHERE source_group<>'' ORDER BY source_group COLLATE NOCASE")]

    def bulk_update_user_fields(self, song_ids: Iterable[str], fields: dict[str, Any]) -> int:
        ids = list(dict.fromkeys(str(x) for x in song_ids if str(x)))
        if not ids:
            return 0
        allowed = {"favorite", "rating", "custom_tags", "notes", "youtube_url", "youtube_published_at", "archived"}
        clean = {k: v for k, v in fields.items() if k in allowed}
        if not clean:
            return 0
        for song_id in ids:
            self.add_song_history(song_id, "masovna izmena", clean)
        pairs = [f"{k}=?" for k in clean]
        marks = ",".join("?" for _ in ids)
        with self._lock, self._connect() as conn:
            cur = conn.execute(f"UPDATE songs SET {', '.join(pairs)} WHERE id IN ({marks})", [*clean.values(), *ids])
            return cur.rowcount

    def append_custom_tags(self, song_ids: Iterable[str], tags: str) -> int:
        ids = list(dict.fromkeys(str(x) for x in song_ids if str(x)))
        wanted = [x.strip() for x in str(tags).replace(';', ',').split(',') if x.strip()]
        if not ids or not wanted:
            return 0
        for song_id in ids:
            self.add_song_history(song_id, "masovna izmena oznaka", {"append_tags": wanted})
        changed = 0
        with self._lock, self._connect() as conn:
            for sid in ids:
                row = conn.execute("SELECT custom_tags FROM songs WHERE id=?", (sid,)).fetchone()
                if not row:
                    continue
                existing = [x.strip() for x in str(row[0] or '').split(',') if x.strip()]
                merged = []
                seen = set()
                for item in [*existing, *wanted]:
                    key = item.casefold()
                    if key not in seen:
                        seen.add(key); merged.append(item)
                conn.execute("UPDATE songs SET custom_tags=? WHERE id=?", (", ".join(merged), sid))
                changed += 1
        return changed

    def duplicate_report(self) -> dict[str, Any]:
        with self._connect() as conn:
            title_groups = [dict(r) for r in conn.execute("""
                SELECT lower(trim(title)) AS duplicate_key, COUNT(*) AS count,
                       group_concat(title || ' [' || id || ']', ' | ') AS songs
                FROM songs WHERE trim(title)<>''
                GROUP BY lower(trim(title)), round(duration)
                HAVING COUNT(*)>1 ORDER BY count DESC, duplicate_key
            """).fetchall()]
            audio_groups = [dict(r) for r in conn.execute("""
                SELECT content_sha256 AS duplicate_key, COUNT(*) AS count,
                       group_concat(title || ' [' || id || ']', ' | ') AS songs
                FROM songs WHERE content_sha256<>''
                GROUP BY content_sha256 HAVING COUNT(*)>1 ORDER BY count DESC
            """).fetchall()]
            return {"same_title_duration": title_groups, "same_audio": audio_groups}

    def report_rows(self, report_type: str, limit: int = 500) -> list[dict[str, Any]]:
        clauses = {
            "missing_lyrics": "lyrics=''",
            "missing_audio": "local_audio='' AND local_wav=''",
            "missing_cover": "local_cover=''",
            "published_missing_url": "youtube_published_at<>'' AND youtube_url=''",
            "long_unpublished": "duration>=240 AND youtube_url='' AND youtube_published_at=''",
            "new_today": "date(first_seen_at)=date('now')",
            "archived": "archived=1",
        }
        where = clauses.get(report_type, "1=0")
        with self._connect() as conn:
            return [dict(r) for r in conn.execute(
                f"SELECT id,title,duration,created_at,source_group,local_audio,local_wav,local_cover,youtube_url,youtube_published_at,first_seen_at FROM songs WHERE {where} ORDER BY created_at DESC LIMIT ?",
                (max(1, min(int(limit), 5000)),),
            ).fetchall()]

    def clear_logs(self) -> int:
        with self._lock, self._connect() as conn:
            cur = conn.execute("DELETE FROM app_log")
            return cur.rowcount

    def is_known_local_file(self, path: str) -> bool:
        normalized = str(Path(path).resolve())
        columns = ("local_audio", "local_wav", "local_video", "local_cover", "local_lyrics", "local_lrc", "local_srt")
        where = " OR ".join(f"{column}=?" for column in columns)
        with self._connect() as conn:
            row = conn.execute(f"SELECT 1 FROM songs WHERE {where} LIMIT 1", [normalized] * len(columns)).fetchone()
            if row:
                return True
            row = conn.execute("SELECT 1 FROM derived_files WHERE path=? LIMIT 1", (normalized,)).fetchone()
            if row:
                return True
            for record in conn.execute("SELECT local_audio,local_wav,local_video,local_cover,local_lyrics,local_lrc,local_srt FROM songs"):
                for value in record:
                    if value:
                        try:
                            if str(Path(value).resolve()) == normalized:
                                return True
                        except Exception:
                            pass
            for record in conn.execute("SELECT path FROM derived_files"):
                if record[0]:
                    try:
                        if str(Path(record[0]).resolve()) == normalized:
                            return True
                    except Exception:
                        pass
        return False

    def count_songs(self) -> int:
        with self._connect() as conn:
            return int(conn.execute("SELECT COUNT(*) FROM songs").fetchone()[0])

    def stats(self) -> dict[str, Any]:
        with self._connect() as conn:
            totals = conn.execute(
                """
                SELECT COUNT(*) total,
                       SUM(CASE WHEN local_audio<>'' OR local_wav<>'' THEN 1 ELSE 0 END) downloaded,
                       SUM(CASE WHEN is_liked=1 THEN 1 ELSE 0 END) liked,
                       SUM(CASE WHEN favorite=1 THEN 1 ELSE 0 END) favorites,
                       SUM(CASE WHEN lyrics<>'' THEN 1 ELSE 0 END) with_text,
                       SUM(CASE WHEN duration>=240 THEN 1 ELSE 0 END) long_songs,
                       COALESCE(SUM(duration),0) seconds
                FROM songs
                """
            ).fetchone()
            models = [dict(r) for r in conn.execute(
                "SELECT COALESCE(NULLIF(model_version,''),'Nepoznato') name, COUNT(*) value FROM songs GROUP BY name ORDER BY value DESC LIMIT 10"
            ).fetchall()]
            months = [dict(r) for r in conn.execute(
                "SELECT substr(created_at,1,7) name, COUNT(*) value FROM songs WHERE created_at<>'' GROUP BY name ORDER BY name DESC LIMIT 12"
            ).fetchall()]
            return {
                "total": totals["total"] or 0,
                "downloaded": totals["downloaded"] or 0,
                "liked": totals["liked"] or 0,
                "favorites": totals["favorites"] or 0,
                "with_text": totals["with_text"] or 0,
                "long_songs": totals["long_songs"] or 0,
                "seconds": totals["seconds"] or 0,
                "models": models,
                "months": months,
            }

    def list_collections(self) -> list[dict[str, Any]]:
        with self._connect() as conn:
            return [dict(r) for r in conn.execute(
                """SELECT c.*, COUNT(cs.song_id) AS song_count
                   FROM collections c LEFT JOIN collection_songs cs ON cs.collection_id=c.id
                   GROUP BY c.id ORDER BY c.is_system DESC, c.name COLLATE NOCASE"""
            ).fetchall()]

    def get_collection_by_slug(self, slug: str) -> dict[str, Any] | None:
        with self._connect() as conn:
            row = conn.execute("SELECT * FROM collections WHERE slug=?", (str(slug),)).fetchone()
            return dict(row) if row else None

    @staticmethod
    def _slug(name: str) -> str:
        import re
        value = name.lower().strip()
        replacements = str.maketrans("čćžšđ", "cczsd")
        value = value.translate(replacements)
        value = re.sub(r"[^a-z0-9]+", "-", value).strip("-")
        return value or f"folder-{int(datetime.now().timestamp())}"

    def create_collection(self, name: str, color: str = "#7c3aed") -> dict[str, Any]:
        name = name.strip()
        if not name:
            raise ValueError("Naziv foldera je prazan.")
        slug = self._slug(name)
        with self._lock, self._connect() as conn:
            suffix = 2
            base = slug
            while conn.execute("SELECT 1 FROM collections WHERE slug=?", (slug,)).fetchone():
                slug = f"{base}-{suffix}"
                suffix += 1
            conn.execute("INSERT INTO collections(slug,name,color,is_system) VALUES(?,?,?,0)", (slug, name, color))
            row = conn.execute("SELECT * FROM collections WHERE id=last_insert_rowid() ").fetchone()
            return dict(row)

    def delete_collection(self, collection_id: int) -> None:
        with self._lock, self._connect() as conn:
            row = conn.execute("SELECT is_system FROM collections WHERE id=?", (collection_id,)).fetchone()
            if not row:
                raise ValueError("Folder ne postoji.")
            if row[0]:
                raise ValueError("Ugrađeni folder ne može da se obriše.")
            conn.execute("DELETE FROM collections WHERE id=?", (collection_id,))

    def set_song_collections(self, song_id: str, collection_ids: Iterable[int]) -> None:
        ids = sorted({int(x) for x in collection_ids})
        self.add_song_history(song_id, "izmena foldera", {"collection_ids": ids})
        with self._lock, self._connect() as conn:
            conn.execute("DELETE FROM collection_songs WHERE song_id=?", (song_id,))
            for cid in ids:
                conn.execute("INSERT OR IGNORE INTO collection_songs(collection_id,song_id) VALUES(?,?)", (cid, song_id))

    def add_songs_to_collection(self, collection_id: int, song_ids: Iterable[str]) -> int:
        ids = {str(x) for x in song_ids if str(x)}
        for song_id in ids:
            self.add_song_history(song_id, "dodavanje u folder", {"collection_id": int(collection_id)})
        count = 0
        with self._lock, self._connect() as conn:
            for song_id in ids:
                before = conn.total_changes
                conn.execute("INSERT OR IGNORE INTO collection_songs(collection_id,song_id) VALUES(?,?)", (collection_id, song_id))
                if conn.total_changes > before:
                    count += 1
        return count

    def remove_songs_from_collection(self, collection_id: int, song_ids: Iterable[str]) -> int:
        ids = [str(x) for x in song_ids if str(x)]
        if not ids:
            return 0
        for song_id in ids:
            self.add_song_history(song_id, "uklanjanje iz foldera", {"collection_id": int(collection_id)})
        marks = ",".join("?" for _ in ids)
        with self._lock, self._connect() as conn:
            cur = conn.execute(f"DELETE FROM collection_songs WHERE collection_id=? AND song_id IN ({marks})", [collection_id, *ids])
            return cur.rowcount

    def add_derived_file(self, song_id: str, kind: str, label: str, path: str, fmt: str, duration: float, settings: dict[str, Any]) -> int:
        normalized = str(Path(path).resolve())
        with self._lock, self._connect() as conn:
            conn.execute(
                "INSERT INTO derived_files(song_id,kind,label,path,format,duration,settings_json) VALUES(?,?,?,?,?,?,?)",
                (song_id, kind, label, normalized, fmt, float(duration or 0), json.dumps(settings, ensure_ascii=False)),
            )
            return int(conn.execute("SELECT last_insert_rowid()").fetchone()[0])

    def get_derived_file(self, file_id: int) -> dict[str, Any] | None:
        with self._connect() as conn:
            row = conn.execute("SELECT * FROM derived_files WHERE id=?", (file_id,)).fetchone()
            return dict(row) if row else None

    def delete_derived_file(self, file_id: int, delete_from_disk: bool = False) -> None:
        with self._lock, self._connect() as conn:
            row = conn.execute("SELECT path FROM derived_files WHERE id=?", (file_id,)).fetchone()
            if not row:
                return
            conn.execute("DELETE FROM derived_files WHERE id=?", (file_id,))
        if delete_from_disk:
            try:
                p = Path(row[0])
                if p.exists() and p.is_file():
                    p.unlink()
            except Exception:
                pass

    def get_setting(self, key: str, default: str = "") -> str:
        with self._connect() as conn:
            row = conn.execute("SELECT value FROM settings WHERE key=?", (key,)).fetchone()
            return row[0] if row else default

    def set_setting(self, key: str, value: str) -> None:
        with self._lock, self._connect() as conn:
            conn.execute(
                "INSERT INTO settings(key,value) VALUES(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value",
                (key, value),
            )

    def add_log(self, level: str, message: str) -> None:
        with self._lock, self._connect() as conn:
            conn.execute("INSERT INTO app_log(level,message) VALUES(?,?)", (level, message))
            conn.execute("DELETE FROM app_log WHERE id NOT IN (SELECT id FROM app_log ORDER BY id DESC LIMIT 1000)")

    def logs(self, limit: int = 200) -> list[dict[str, Any]]:
        with self._connect() as conn:
            return [dict(r) for r in conn.execute(
                "SELECT * FROM app_log ORDER BY id DESC LIMIT ?", (max(1, min(limit, 1000)),)
            ).fetchall()]

    def export_rows(self, ids: Iterable[str] | None = None) -> list[dict[str, Any]]:
        with self._connect() as conn:
            id_list = list(ids) if ids is not None else []
            if id_list:
                marks = ",".join("?" for _ in id_list)
                rows = conn.execute(f"SELECT * FROM songs WHERE id IN ({marks}) ORDER BY created_at", id_list).fetchall()
            elif ids is not None:
                return []
            else:
                rows = conn.execute("SELECT * FROM songs ORDER BY created_at").fetchall()
            result = [dict(r) for r in rows]
            for row in result:
                row["collections"] = [dict(r) for r in conn.execute(
                    "SELECT c.id,c.slug,c.name,c.color,c.is_system FROM collections c JOIN collection_songs cs ON cs.collection_id=c.id WHERE cs.song_id=? ORDER BY c.name",
                    (row["id"],),
                ).fetchall()]
                row["derived_files"] = [dict(r) for r in conn.execute(
                    "SELECT * FROM derived_files WHERE song_id=? ORDER BY id", (row["id"],)
                ).fetchall()]
            return result

    # ---------------- YouTube kanali, objave i mogući duplikati ----------------
    def upsert_youtube_channel(self, channel: dict[str, Any], is_owned: bool = True) -> dict[str, Any]:
        channel_id = str(channel.get("channel_id") or "").strip()
        if not channel_id:
            raise ValueError("YouTube channel ID je prazan.")
        with self._lock, self._connect() as conn:
            conn.execute(
                """INSERT INTO youtube_channels(channel_id,title,handle,url,uploads_playlist,is_owned,source_mode,subscriber_count,video_count,view_count,oauth_profile_id,google_email,thumbnail_url)
                   VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?)
                   ON CONFLICT(channel_id) DO UPDATE SET
                     title=excluded.title, handle=excluded.handle, url=excluded.url, uploads_playlist=excluded.uploads_playlist,
                     is_owned=excluded.is_owned, source_mode=excluded.source_mode, subscriber_count=excluded.subscriber_count,
                     video_count=excluded.video_count, view_count=excluded.view_count,
                     oauth_profile_id=CASE WHEN excluded.oauth_profile_id<>'' THEN excluded.oauth_profile_id ELSE youtube_channels.oauth_profile_id END,
                     google_email=CASE WHEN excluded.google_email<>'' THEN excluded.google_email ELSE youtube_channels.google_email END,
                     thumbnail_url=CASE WHEN excluded.thumbnail_url<>'' THEN excluded.thumbnail_url ELSE youtube_channels.thumbnail_url END""",
                (
                    channel_id, str(channel.get("title") or channel_id), str(channel.get("handle") or ""),
                    str(channel.get("url") or f"https://www.youtube.com/channel/{channel_id}"),
                    str(channel.get("uploads_playlist") or ""), 1 if is_owned else 0,
                    str(channel.get("source_mode") or "api"), int(channel.get("subscriber_count") or 0),
                    int(channel.get("video_count") or 0), int(channel.get("view_count") or 0),
                    str(channel.get("oauth_profile_id") or ""), str(channel.get("google_email") or ""),
                    str(channel.get("thumbnail_url") or ""),
                ),
            )
            row = conn.execute("SELECT * FROM youtube_channels WHERE channel_id=?", (channel_id,)).fetchone()
            return dict(row)

    def list_youtube_channels(self) -> list[dict[str, Any]]:
        with self._connect() as conn:
            return [dict(r) for r in conn.execute(
                "SELECT * FROM youtube_channels ORDER BY is_owned DESC, title COLLATE NOCASE"
            ).fetchall()]

    def get_youtube_channel(self, channel_id: str) -> dict[str, Any] | None:
        with self._connect() as conn:
            row = conn.execute("SELECT * FROM youtube_channels WHERE channel_id=?", (str(channel_id),)).fetchone()
            return dict(row) if row else None

    def delete_youtube_channel(self, channel_id: str) -> None:
        with self._lock, self._connect() as conn:
            conn.execute("DELETE FROM youtube_channels WHERE channel_id=?", (str(channel_id),))

    def update_youtube_channel_scan(self, channel_id: str, latest_video_at: str = "", video_count: int | None = None) -> None:
        with self._lock, self._connect() as conn:
            fields = ["last_scan_at=datetime('now')"]
            values: list[Any] = []
            if latest_video_at:
                fields.append("latest_video_at=?")
                values.append(latest_video_at)
            if video_count is not None:
                fields.append("video_count=MAX(video_count,?)")
                values.append(int(video_count))
            values.append(str(channel_id))
            conn.execute(f"UPDATE youtube_channels SET {', '.join(fields)} WHERE channel_id=?", values)

    def channel_shorts_report(self, channel_id: str, shorts_max_duration: float = 70.0) -> dict[str, Any]:
        """How many of this channel's known Shorts already have a real audio
        result vs. only metadata -- lets the UI show "X Shorts still need an
        audio check" instead of treating a title-only candidate as proof."""
        with self._connect() as conn:
            total = conn.execute("SELECT COUNT(*) FROM youtube_videos WHERE channel_id=?", (str(channel_id),)).fetchone()[0]
            shorts_total = conn.execute(
                "SELECT COUNT(*) FROM youtube_videos WHERE channel_id=? AND duration>0 AND duration<=?",
                (str(channel_id), shorts_max_duration),
            ).fetchone()[0]
            shorts_checked = conn.execute(
                "SELECT COUNT(*) FROM youtube_videos WHERE channel_id=? AND duration>0 AND duration<=? AND audio_analyzed_at<>''",
                (str(channel_id), shorts_max_duration),
            ).fetchone()[0]
            unknown_duration = conn.execute(
                "SELECT COUNT(*) FROM youtube_videos WHERE channel_id=? AND duration<=0",
                (str(channel_id),),
            ).fetchone()[0]
            return {
                "videos_total": int(total), "shorts_total": int(shorts_total),
                "shorts_checked": int(shorts_checked), "shorts_not_checked": int(shorts_total - shorts_checked),
                "unknown_duration": int(unknown_duration),
            }

    def upsert_youtube_video(self, video: dict[str, Any], is_owned_channel: bool = False) -> dict[str, Any]:
        video_id = str(video.get("video_id") or "").strip()
        if not video_id:
            raise ValueError("YouTube video ID je prazan.")
        raw = video.get("raw_json") if isinstance(video.get("raw_json"), dict) else {}
        with self._lock, self._connect() as conn:
            conn.execute(
                """INSERT INTO youtube_videos(video_id,channel_id,channel_title,title,description,published_at,url,thumbnail_url,duration,view_count,like_count,comment_count,privacy_status,is_owned_channel,raw_json)
                   VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)
                   ON CONFLICT(video_id) DO UPDATE SET
                     channel_id=excluded.channel_id, channel_title=excluded.channel_title, title=excluded.title,
                     description=excluded.description, published_at=excluded.published_at, url=excluded.url,
                     thumbnail_url=excluded.thumbnail_url, duration=excluded.duration, view_count=excluded.view_count,
                     like_count=excluded.like_count, comment_count=excluded.comment_count, privacy_status=excluded.privacy_status,
                     is_owned_channel=excluded.is_owned_channel, last_seen_at=CURRENT_TIMESTAMP, raw_json=excluded.raw_json""",
                (
                    video_id, str(video.get("channel_id") or ""), str(video.get("channel_title") or ""),
                    str(video.get("title") or ""), str(video.get("description") or ""), str(video.get("published_at") or ""),
                    str(video.get("url") or f"https://www.youtube.com/watch?v={video_id}"), str(video.get("thumbnail_url") or ""),
                    float(video.get("duration") or 0), int(video.get("view_count") or 0), int(video.get("like_count") or 0),
                    int(video.get("comment_count") or 0), str(video.get("privacy_status") or ""), 1 if is_owned_channel else 0,
                    json.dumps(raw, ensure_ascii=False, default=str),
                ),
            )
            row = conn.execute("SELECT * FROM youtube_videos WHERE video_id=?", (video_id,)).fetchone()
            return dict(row)

    def upsert_youtube_match(self, song_id: str, video_id: str, match: dict[str, Any]) -> dict[str, Any]:
        with self._lock, self._connect() as conn:
            conn.execute(
                """INSERT INTO youtube_matches(song_id,video_id,match_type,score,reason,status)
                   VALUES(?,?,?,?,?,'new')
                   ON CONFLICT(song_id,video_id) DO UPDATE SET
                     match_type=excluded.match_type, score=excluded.score, reason=excluded.reason,
                     last_seen_at=CURRENT_TIMESTAMP""",
                (str(song_id), str(video_id), str(match.get("match_type") or "external_candidate"), float(match.get("score") or 0), str(match.get("reason") or "")),
            )
            row = conn.execute(
                """SELECT m.*,v.title video_title,v.channel_id,v.channel_title,v.published_at,v.url video_url,v.thumbnail_url,v.duration video_duration,v.view_count,v.is_owned_channel,s.title song_title,s.youtube_url original_url,s.youtube_published_at original_published_at
                   FROM youtube_matches m JOIN youtube_videos v ON v.video_id=m.video_id JOIN songs s ON s.id=m.song_id
                   WHERE m.song_id=? AND m.video_id=?""", (str(song_id), str(video_id))
            ).fetchone()
            return dict(row)

    def get_youtube_match(self, match_id: int) -> dict[str, Any] | None:
        with self._connect() as conn:
            row = conn.execute(
                """SELECT m.*,v.title video_title,v.channel_id,v.channel_title,v.published_at,v.url video_url,v.thumbnail_url,v.duration video_duration,v.view_count,v.is_owned_channel,s.title song_title,s.duration song_duration,s.youtube_url original_url,s.youtube_published_at original_published_at,s.source_url
                   FROM youtube_matches m JOIN youtube_videos v ON v.video_id=m.video_id JOIN songs s ON s.id=m.song_id WHERE m.id=?""", (int(match_id),)
            ).fetchone()
            return dict(row) if row else None

    def update_youtube_match(self, match_id: int, fields: dict[str, Any]) -> dict[str, Any] | None:
        allowed = {"status", "contact_email", "contact_status", "contacted_at", "contact_note", "manual_link"}
        clean = {k: v for k, v in fields.items() if k in allowed}
        if clean:
            sets = ",".join(f"{k}=?" for k in clean)
            with self._lock, self._connect() as conn:
                conn.execute(f"UPDATE youtube_matches SET {sets} WHERE id=?", [*clean.values(), int(match_id)])
        with self._connect() as conn:
            row = conn.execute(
                """SELECT m.*,v.title video_title,v.channel_id,v.channel_title,v.published_at,v.url video_url,v.thumbnail_url,v.duration video_duration,v.view_count,v.is_owned_channel,s.title song_title,s.youtube_url original_url,s.youtube_published_at original_published_at
                   FROM youtube_matches m JOIN youtube_videos v ON v.video_id=m.video_id JOIN songs s ON s.id=m.song_id WHERE m.id=?""", (int(match_id),)
            ).fetchone()
            return dict(row) if row else None

    def list_youtube_matches(self, match_type: str = "", status: str = "", limit: int = 1000) -> list[dict[str, Any]]:
        where: list[str] = []
        params: list[Any] = []
        if match_type:
            where.append("m.match_type=?")
            params.append(match_type)
        if status:
            where.append("m.status=?")
            params.append(status)
        clause = " WHERE " + " AND ".join(where) if where else ""
        sql = f"""SELECT m.*,v.title video_title,v.channel_id,v.channel_title,v.published_at,v.url video_url,v.thumbnail_url,v.duration video_duration,v.view_count,v.is_owned_channel,s.title song_title,s.duration song_duration,s.youtube_url original_url,s.youtube_published_at original_published_at
                  FROM youtube_matches m JOIN youtube_videos v ON v.video_id=m.video_id JOIN songs s ON s.id=m.song_id
                  {clause} ORDER BY CASE m.status WHEN 'new' THEN 0 WHEN 'review' THEN 1 ELSE 2 END, m.score DESC, v.published_at DESC LIMIT ?"""
        params.append(max(1, min(int(limit), 5000)))
        with self._connect() as conn:
            return [dict(r) for r in conn.execute(sql, params).fetchall()]

    def publication_calendar(self, limit: int = 1000) -> list[dict[str, Any]]:
        with self._connect() as conn:
            rows = conn.execute(
                """SELECT s.id song_id,s.title song_title,v.video_id,v.title video_title,v.channel_id,v.channel_title,v.published_at,v.url video_url,v.view_count,m.score,m.status,
                          m.completeness_status,m.audio_score,m.coverage_percent,m.audio_checked_at
                   FROM youtube_matches m JOIN songs s ON s.id=m.song_id JOIN youtube_videos v ON v.video_id=m.video_id
                   WHERE m.match_type='owned_publication'
                   ORDER BY v.published_at DESC LIMIT ?""", (max(1, min(int(limit), 5000)),)
            ).fetchall()
            return [dict(r) for r in rows]

    def get_youtube_video(self, video_id: str) -> dict[str, Any] | None:
        with self._connect() as conn:
            row = conn.execute("SELECT * FROM youtube_videos WHERE video_id=?", (str(video_id),)).fetchone()
            return dict(row) if row else None

    def list_youtube_video_ids(self, channel_id: str) -> set[str]:
        """Lightweight id-only lookup so an incremental channel scan can stop
        paginating once it reaches videos already known from a prior run."""
        with self._connect() as conn:
            rows = conn.execute("SELECT video_id FROM youtube_videos WHERE channel_id=?", (str(channel_id),)).fetchall()
            return {str(r[0]) for r in rows}

    def update_youtube_video_audio_cache(self, video_id: str, path: str, sha256: str = "") -> None:
        with self._lock, self._connect() as conn:
            conn.execute(
                "UPDATE youtube_videos SET audio_cache_path=?,audio_cache_sha256=?,audio_analyzed_at=CURRENT_TIMESTAMP WHERE video_id=?",
                (str(path), str(sha256), str(video_id)),
            )

    def save_audio_fingerprint(
        self, source_type: str, source_id: str, algorithm: str, duration: float, interval: float,
        payload: bytes, source_identity: str = "", source_mtime: float = 0.0, source_size: int = 0,
    ) -> None:
        with self._lock, self._connect() as conn:
            conn.execute(
                """INSERT INTO audio_fingerprints(source_type,source_id,algorithm,duration,interval,source_identity,source_mtime,source_size,payload)
                   VALUES(?,?,?,?,?,?,?,?,?)
                   ON CONFLICT(source_type,source_id,algorithm) DO UPDATE SET
                     duration=excluded.duration,interval=excluded.interval,source_identity=excluded.source_identity,
                     source_mtime=excluded.source_mtime,source_size=excluded.source_size,payload=excluded.payload,updated_at=CURRENT_TIMESTAMP""",
                (str(source_type), str(source_id), str(algorithm), float(duration or 0), float(interval or 0.5),
                 str(source_identity or ""), float(source_mtime or 0), int(source_size or 0), sqlite3.Binary(payload)),
            )

    def get_audio_fingerprint(self, source_type: str, source_id: str, algorithm: str) -> dict[str, Any] | None:
        with self._connect() as conn:
            row = conn.execute(
                "SELECT * FROM audio_fingerprints WHERE source_type=? AND source_id=? AND algorithm=?",
                (str(source_type), str(source_id), str(algorithm)),
            ).fetchone()
            return dict(row) if row else None

    def clear_audio_fingerprints(self, source_type: str = "", source_id: str = "") -> int:
        where: list[str] = []
        params: list[Any] = []
        if source_type:
            where.append("source_type=?")
            params.append(str(source_type))
        if source_id:
            where.append("source_id=?")
            params.append(str(source_id))
        clause = " WHERE " + " AND ".join(where) if where else ""
        with self._lock, self._connect() as conn:
            cur = conn.execute("DELETE FROM audio_fingerprints" + clause, params)
            return int(cur.rowcount or 0)

    def save_youtube_audio_analysis(self, song_id: str, video_id: str, analysis: dict[str, Any]) -> dict[str, Any]:
        fields = {
            "audio_score": float(analysis.get("audio_score") or 0),
            "coverage_percent": float(analysis.get("coverage_percent") or 0),
            "matched_seconds": float(analysis.get("matched_seconds") or 0),
            "completeness_status": str(analysis.get("completeness_status") or ""),
            "confidence": str(analysis.get("confidence") or ""),
            "source_start": float(analysis.get("source_start") or 0),
            "source_end": float(analysis.get("source_end") or 0),
            "video_start": float(analysis.get("video_start") or 0),
            "video_end": float(analysis.get("video_end") or 0),
            "missing_start": float(analysis.get("missing_start") or 0),
            "missing_end": float(analysis.get("missing_end") or 0),
            "has_intro": 1 if analysis.get("has_intro") else 0,
            "has_outro": 1 if analysis.get("has_outro") else 0,
            "audio_checked_at": datetime.now(timezone.utc).isoformat(),
            "analysis_version": str(analysis.get("analysis_version") or ""),
            "segments_json": json.dumps(analysis.get("segments") or [], ensure_ascii=False),
            "missing_intervals_json": json.dumps(analysis.get("missing_intervals") or [], ensure_ascii=False),
            "segment_count": int(analysis.get("segment_count") or 0),
            "occurrence_count": int(analysis.get("occurrence_count") or 0),
            "total_matched_seconds": float(analysis.get("total_matched_seconds") or analysis.get("matched_seconds") or 0),
            "internal_gap_seconds": float(analysis.get("internal_gap_seconds") or 0),
            "version_type": str(analysis.get("version_type") or ""),
            "recommendation": str(analysis.get("recommendation") or ""),
            "match_role": str(analysis.get("match_role") or "primary"),
            "video_song_count": int(analysis.get("video_song_count") or 1),
        }
        with self._lock, self._connect() as conn:
            existing = conn.execute("SELECT id FROM youtube_matches WHERE song_id=? AND video_id=?", (str(song_id), str(video_id))).fetchone()
            if not existing:
                conn.execute(
                    "INSERT INTO youtube_matches(song_id,video_id,match_type,score,reason,status) VALUES(?,?,'owned_publication',0,'Audio analiza','new')",
                    (str(song_id), str(video_id)),
                )
            sets = ",".join(f"{key}=?" for key in fields)
            conn.execute(f"UPDATE youtube_matches SET {sets},last_seen_at=CURRENT_TIMESTAMP WHERE song_id=? AND video_id=?", [*fields.values(), str(song_id), str(video_id)])
        row = self.get_youtube_match_by_pair(song_id, video_id)
        if row is None:
            raise RuntimeError("Audio analiza je sačuvana, ali rezultat nije pronađen.")
        return row

    def get_youtube_match_by_pair(self, song_id: str, video_id: str) -> dict[str, Any] | None:
        with self._connect() as conn:
            row = conn.execute(
                """SELECT m.*,v.title video_title,v.channel_id,v.channel_title,v.published_at,v.url video_url,
                          v.thumbnail_url,v.duration video_duration,v.view_count,v.is_owned_channel,
                          s.title song_title,s.duration song_duration,s.youtube_url original_url,
                          s.youtube_published_at original_published_at,s.source_url,s.local_audio,s.local_wav,s.audio_url
                   FROM youtube_matches m JOIN youtube_videos v ON v.video_id=m.video_id JOIN songs s ON s.id=m.song_id
                   WHERE m.song_id=? AND m.video_id=?""", (str(song_id), str(video_id))
            ).fetchone()
            return dict(row) if row else None

    def list_youtube_audio_analyses(self, completeness_status: str = "", channel_id: str = "", limit: int = 5000) -> list[dict[str, Any]]:
        where = ["v.is_owned_channel=1"]
        params: list[Any] = []
        if completeness_status:
            where.append("m.completeness_status=?")
            params.append(str(completeness_status))
        if channel_id:
            where.append("v.channel_id=?")
            params.append(str(channel_id))
        sql = f"""SELECT m.*,v.title video_title,v.channel_id,v.channel_title,v.published_at,v.url video_url,
                         v.thumbnail_url,v.duration video_duration,v.view_count,
                         s.title song_title,s.duration song_duration,s.source_url,s.youtube_url original_url,
                         s.youtube_published_at original_published_at
                  FROM youtube_matches m JOIN youtube_videos v ON v.video_id=m.video_id JOIN songs s ON s.id=m.song_id
                  WHERE {' AND '.join(where)}
                  ORDER BY CASE m.completeness_status WHEN 'different_audio' THEN 0 WHEN '' THEN 1 WHEN 'short_clip' THEN 2 WHEN 'partial' THEN 3 WHEN 'almost_complete' THEN 4 WHEN 'complete' THEN 5 ELSE 6 END,
                           m.audio_checked_at DESC,v.published_at DESC LIMIT ?"""
        params.append(max(1, min(int(limit), 10000)))
        with self._connect() as conn:
            return [dict(r) for r in conn.execute(sql, params).fetchall()]

    def youtube_publication_matrix(self) -> dict[str, Any]:
        with self._connect() as conn:
            channels = [dict(r) for r in conn.execute("SELECT * FROM youtube_channels WHERE is_owned=1 ORDER BY title COLLATE NOCASE").fetchall()]
            songs = [dict(r) for r in conn.execute("SELECT id,title,duration,youtube_url,youtube_published_at FROM songs WHERE archived=0 ORDER BY title COLLATE NOCASE").fetchall()]
            analyses = [dict(r) for r in conn.execute(
                """SELECT m.song_id,v.channel_id,m.completeness_status,m.audio_score,m.coverage_percent,m.video_id,
                          v.url video_url,v.published_at,v.title video_title
                   FROM youtube_matches m JOIN youtube_videos v ON v.video_id=m.video_id
                   WHERE v.is_owned_channel=1"""
            ).fetchall()]
        by_pair: dict[tuple[str, str], dict[str, Any]] = {}
        rank = {"complete": 5, "almost_complete": 4, "partial": 3, "short_clip": 2, "different_audio": 1, "": 0}
        for row in analyses:
            key = (str(row.get("song_id") or ""), str(row.get("channel_id") or ""))
            current = by_pair.get(key)
            if current is None or (rank.get(str(row.get("completeness_status") or ""), 0), float(row.get("audio_score") or 0)) > (rank.get(str(current.get("completeness_status") or ""), 0), float(current.get("audio_score") or 0)):
                by_pair[key] = row
        matrix_rows = []
        for song in songs:
            cells = {str(channel["channel_id"]): by_pair.get((str(song["id"]), str(channel["channel_id"]))) for channel in channels}
            matrix_rows.append({"song": song, "channels": cells})
        coverage = self.youtube_coverage_report()
        coverage_map = {
            (str(item.get("song", {}).get("id") or ""), str(item.get("channel", {}).get("channel_id") or "")): item
            for item in (coverage.get("rows") or [])
        }
        for item in matrix_rows:
            sid = str((item.get("song") or {}).get("id") or "")
            cells = item.get("channels") or {}
            for channel in channels:
                cid = str(channel.get("channel_id") or "")
                aggregate = coverage_map.get((sid, cid))
                if not aggregate:
                    continue
                cell = cells.get(cid)
                if cell is None:
                    cell = {"song_id": sid, "channel_id": cid}
                    cells[cid] = cell
                cell["combined_status"] = aggregate.get("status")
                cell["combined_coverage_percent"] = aggregate.get("coverage_percent")
                cell["combined_video_count"] = aggregate.get("video_count")
                cell["combined_missing_intervals"] = aggregate.get("missing_intervals")
                cell["combined_recommendation"] = aggregate.get("recommendation")
        return {"channels": channels, "rows": matrix_rows, "song_count": len(songs), "channel_count": len(channels), "coverage_summary": coverage.get("summary") or {}}

    def youtube_coverage_report(self, song_id: str = "", channel_id: str = "") -> dict[str, Any]:
        """Aggregate source coverage across every confirmed video on each owned channel."""
        with self._connect() as conn:
            channel_sql = "SELECT * FROM youtube_channels WHERE is_owned=1"
            channel_params: list[Any] = []
            if channel_id:
                channel_sql += " AND channel_id=?"
                channel_params.append(str(channel_id))
            channel_sql += " ORDER BY title COLLATE NOCASE"
            channels = [dict(r) for r in conn.execute(channel_sql, channel_params).fetchall()]
            song_sql = "SELECT id,title,duration,source_group,youtube_url,youtube_published_at FROM songs WHERE archived=0"
            song_params: list[Any] = []
            if song_id:
                song_sql += " AND id=?"
                song_params.append(str(song_id))
            song_sql += " ORDER BY title COLLATE NOCASE"
            songs = [dict(r) for r in conn.execute(song_sql, song_params).fetchall()]
            where = ["v.is_owned_channel=1", "m.audio_score>=48", "m.completeness_status<>'different_audio'"]
            params: list[Any] = []
            if song_id:
                where.append("m.song_id=?")
                params.append(str(song_id))
            if channel_id:
                where.append("v.channel_id=?")
                params.append(str(channel_id))
            rows = [dict(r) for r in conn.execute(
                f"""SELECT m.*,v.channel_id,v.channel_title,v.video_id,v.title video_title,v.url video_url,
                           v.published_at,v.duration video_duration,s.title song_title,s.duration song_duration
                    FROM youtube_matches m JOIN youtube_videos v ON v.video_id=m.video_id
                    JOIN songs s ON s.id=m.song_id WHERE {' AND '.join(where)}
                    ORDER BY v.published_at,m.audio_score DESC""", params
            ).fetchall()]

        def merge_ranges(intervals: list[tuple[float, float]], tolerance: float = 1.5) -> list[list[float]]:
            cleaned = sorted((max(0.0, a), max(0.0, b)) for a, b in intervals if b > a)
            merged: list[list[float]] = []
            for start, end in cleaned:
                if not merged or start > merged[-1][1] + tolerance:
                    merged.append([start, end])
                else:
                    merged[-1][1] = max(merged[-1][1], end)
            return [[round(a, 2), round(b, 2)] for a, b in merged]

        grouped: dict[tuple[str, str], list[dict[str, Any]]] = {}
        for row in rows:
            grouped.setdefault((str(row.get("song_id") or ""), str(row.get("channel_id") or "")), []).append(row)
        result_rows: list[dict[str, Any]] = []
        channel_totals: dict[str, dict[str, Any]] = {
            str(c.get("channel_id") or ""): {"channel": c, "complete": 0, "almost_complete": 0, "partial": 0, "short_clip": 0, "missing": 0, "coverage_sum": 0.0}
            for c in channels
        }
        for song in songs:
            sid = str(song.get("id") or "")
            duration = float(song.get("duration") or 0)
            for channel in channels:
                cid = str(channel.get("channel_id") or "")
                pair_rows = grouped.get((sid, cid), [])
                intervals: list[tuple[float, float]] = []
                videos: list[dict[str, Any]] = []
                for row in pair_rows:
                    raw_segments = row.get("segments_json") or "[]"
                    try:
                        segments = json.loads(raw_segments) if isinstance(raw_segments, str) else raw_segments
                    except Exception:
                        segments = []
                    if isinstance(segments, list) and segments:
                        for seg in segments:
                            if isinstance(seg, dict):
                                intervals.append((float(seg.get("source_start") or 0), float(seg.get("source_end") or 0)))
                    else:
                        intervals.append((float(row.get("source_start") or 0), float(row.get("source_end") or 0)))
                    videos.append({
                        "video_id": row.get("video_id"), "title": row.get("video_title"), "url": row.get("video_url"),
                        "published_at": row.get("published_at"), "audio_score": row.get("audio_score"),
                        "coverage_percent": row.get("coverage_percent"), "status": row.get("completeness_status"),
                        "version_type": row.get("version_type"), "occurrence_count": row.get("occurrence_count"),
                        "video_song_count": row.get("video_song_count"),
                    })
                covered = merge_ranges([(min(duration, a), min(duration, b)) for a, b in intervals]) if duration > 0 else []
                covered_seconds = sum(max(0.0, b - a) for a, b in covered)
                coverage = min(100.0, covered_seconds / duration * 100.0) if duration > 0 else 0.0
                gaps: list[list[float]] = []
                cursor = 0.0
                for start, end in covered:
                    if start - cursor >= 0.75:
                        gaps.append([round(cursor, 2), round(start, 2)])
                    cursor = max(cursor, end)
                if duration - cursor >= 0.75:
                    gaps.append([round(cursor, 2), round(duration, 2)])
                missing_start = gaps[0][1] - gaps[0][0] if gaps and gaps[0][0] <= 0.01 else 0.0
                missing_end = gaps[-1][1] - gaps[-1][0] if gaps and gaps[-1][1] >= duration - 0.01 else 0.0
                internal_gap = sum(b - a for a, b in gaps if a > 0.01 and b < duration - 0.01)
                if not pair_rows or covered_seconds < 15:
                    status = "missing"
                    recommendation = "Pesma nije audio-potvrđena na ovom kanalu. Proveri da li treba objaviti kompletnu verziju."
                elif coverage >= 92 and missing_start <= max(6.0, duration * 0.04) and missing_end <= max(8.0, duration * 0.06) and internal_gap <= max(4.0, duration * 0.03):
                    status = "complete"
                    recommendation = "Kompletna pesma je objavljena na ovom kanalu."
                elif coverage >= 80:
                    status = "almost_complete"
                    recommendation = "Skoro cela pesma je objavljena. Proveri označene nedostajuće delove."
                elif coverage < 35 or covered_seconds <= 70:
                    status = "short_clip"
                    recommendation = "Pronađen je samo kratak isečak. Kompletna verzija nije potvrđena."
                else:
                    status = "partial"
                    recommendation = "Objavljen je deo pesme. Razmisli o objavi pune verzije."
                first_date = min((str(v.get("published_at") or "") for v in videos if v.get("published_at")), default="")
                last_date = max((str(v.get("published_at") or "") for v in videos if v.get("published_at")), default="")
                result_rows.append({
                    "song": song, "channel": channel, "status": status,
                    "coverage_percent": round(coverage, 1), "covered_seconds": round(covered_seconds, 2),
                    "covered_intervals": covered, "missing_intervals": gaps,
                    "missing_start": round(missing_start, 2), "missing_end": round(missing_end, 2),
                    "internal_gap_seconds": round(internal_gap, 2), "video_count": len(videos),
                    "duplicate_uploads": max(0, len(videos) - 1),
                    "mixed_video_count": sum(1 for v in videos if int(v.get("video_song_count") or 1) > 1),
                    "repeated_occurrences": sum(max(0, int(v.get("occurrence_count") or 1) - 1) for v in videos),
                    "first_published_at": first_date, "last_published_at": last_date,
                    "videos": videos, "recommendation": recommendation,
                })
                totals = channel_totals.get(cid)
                if totals is not None:
                    totals[status] = int(totals.get(status) or 0) + 1
                    totals["coverage_sum"] = float(totals.get("coverage_sum") or 0) + coverage
        channel_summary = []
        for cid, item in channel_totals.items():
            count = max(1, len(songs))
            channel_summary.append({
                **item, "average_coverage_percent": round(float(item.get("coverage_sum") or 0) / count, 1),
                "song_count": len(songs),
            })
        overall = {
            "pairs": len(result_rows), "songs": len(songs), "channels": len(channels),
            "complete": sum(1 for r in result_rows if r["status"] == "complete"),
            "almost_complete": sum(1 for r in result_rows if r["status"] == "almost_complete"),
            "partial": sum(1 for r in result_rows if r["status"] == "partial"),
            "short_clip": sum(1 for r in result_rows if r["status"] == "short_clip"),
            "missing": sum(1 for r in result_rows if r["status"] == "missing"),
        }
        return {"rows": result_rows, "channels": channel_summary, "summary": overall}

    def youtube_duplicate_mix_report(self) -> dict[str, Any]:
        with self._connect() as conn:
            duplicates = [dict(r) for r in conn.execute(
                """SELECT m.song_id,s.title song_title,v.channel_id,v.channel_title,COUNT(DISTINCT v.video_id) video_count,
                          GROUP_CONCAT(v.title,' | ') video_titles,MIN(v.published_at) first_date,MAX(v.published_at) last_date
                   FROM youtube_matches m JOIN youtube_videos v ON v.video_id=m.video_id JOIN songs s ON s.id=m.song_id
                   WHERE v.is_owned_channel=1 AND m.audio_score>=48 AND m.completeness_status<>'different_audio'
                   GROUP BY m.song_id,v.channel_id HAVING COUNT(DISTINCT v.video_id)>1
                   ORDER BY video_count DESC,s.title COLLATE NOCASE"""
            ).fetchall()]
            mixes = [dict(r) for r in conn.execute(
                """SELECT v.video_id,v.title video_title,v.url video_url,v.channel_id,v.channel_title,v.published_at,
                          COUNT(DISTINCT m.song_id) song_count,GROUP_CONCAT(s.title,' | ') song_titles
                   FROM youtube_matches m JOIN youtube_videos v ON v.video_id=m.video_id JOIN songs s ON s.id=m.song_id
                   WHERE v.is_owned_channel=1 AND m.audio_score>=55 AND m.completeness_status<>'different_audio'
                   GROUP BY v.video_id HAVING COUNT(DISTINCT m.song_id)>1
                   ORDER BY song_count DESC,v.published_at DESC"""
            ).fetchall()]
            repeats = [dict(r) for r in conn.execute(
                """SELECT m.song_id,s.title song_title,v.video_id,v.title video_title,v.url video_url,v.channel_title,
                          m.occurrence_count,m.coverage_percent,m.audio_score
                   FROM youtube_matches m JOIN youtube_videos v ON v.video_id=m.video_id JOIN songs s ON s.id=m.song_id
                   WHERE v.is_owned_channel=1 AND m.occurrence_count>1 ORDER BY m.occurrence_count DESC"""
            ).fetchall()]
            versions = [dict(r) for r in conn.execute(
                """SELECT m.version_type,COUNT(*) count FROM youtube_matches m JOIN youtube_videos v ON v.video_id=m.video_id
                   WHERE v.is_owned_channel=1 AND m.version_type<>'' GROUP BY m.version_type ORDER BY count DESC"""
            ).fetchall()]
        return {"duplicates": duplicates, "mixes": mixes, "repeats": repeats, "version_types": versions}

    def youtube_audio_summary(self) -> dict[str, Any]:
        with self._connect() as conn:
            row = conn.execute(
                """SELECT COUNT(*) total,
                          SUM(CASE WHEN completeness_status='complete' THEN 1 ELSE 0 END) complete,
                          SUM(CASE WHEN completeness_status='almost_complete' THEN 1 ELSE 0 END) almost_complete,
                          SUM(CASE WHEN completeness_status='partial' THEN 1 ELSE 0 END) partial,
                          SUM(CASE WHEN completeness_status='short_clip' THEN 1 ELSE 0 END) short_clip,
                          SUM(CASE WHEN completeness_status='different_audio' THEN 1 ELSE 0 END) different_audio,
                          SUM(CASE WHEN completeness_status='' THEN 1 ELSE 0 END) unchecked
                   FROM youtube_matches m JOIN youtube_videos v ON v.video_id=m.video_id WHERE v.is_owned_channel=1"""
            ).fetchone()
            fingerprints = conn.execute("SELECT COUNT(*) count FROM audio_fingerprints").fetchone()
            extra = conn.execute(
                """SELECT COUNT(DISTINCT CASE WHEN m.video_song_count>1 THEN m.video_id END) mix_videos,
                          SUM(CASE WHEN m.occurrence_count>1 THEN 1 ELSE 0 END) repeated_matches,
                          SUM(CASE WHEN m.segment_count>1 THEN 1 ELSE 0 END) multi_segment_matches,
                          COUNT(DISTINCT CASE WHEN m.audio_score>=48 AND m.completeness_status<>'different_audio' THEN m.song_id END) matched_songs
                   FROM youtube_matches m JOIN youtube_videos v ON v.video_id=m.video_id WHERE v.is_owned_channel=1"""
            ).fetchone()
        return {key: int(row[key] or 0) for key in row.keys()} | {
            "fingerprints": int(fingerprints["count"] or 0),
            "mix_videos": int(extra["mix_videos"] or 0),
            "repeated_matches": int(extra["repeated_matches"] or 0),
            "multi_segment_matches": int(extra["multi_segment_matches"] or 0),
            "matched_songs": int(extra["matched_songs"] or 0),
        }

    def start_youtube_scan_run(self, scan_type: str, channel_count: int, song_count: int) -> int:
        with self._lock, self._connect() as conn:
            conn.execute("INSERT INTO youtube_scan_runs(scan_type,channel_count,song_count) VALUES(?,?,?)", (str(scan_type), int(channel_count), int(song_count)))
            return int(conn.execute("SELECT last_insert_rowid()").fetchone()[0])

    def finish_youtube_scan_run(self, run_id: int, result_count: int, error_count: int, summary: str) -> None:
        with self._lock, self._connect() as conn:
            conn.execute("UPDATE youtube_scan_runs SET finished_at=CURRENT_TIMESTAMP,result_count=?,error_count=?,summary=? WHERE id=?", (int(result_count), int(error_count), str(summary), int(run_id)))

    def youtube_summary(self) -> dict[str, Any]:
        with self._connect() as conn:
            channels = conn.execute("SELECT COUNT(*) total,SUM(CASE WHEN is_owned=1 THEN 1 ELSE 0 END) owned FROM youtube_channels").fetchone()
            matches = conn.execute("SELECT COUNT(*) total,SUM(CASE WHEN match_type='owned_publication' THEN 1 ELSE 0 END) owned,SUM(CASE WHEN match_type='external_candidate' AND status IN ('new','review') THEN 1 ELSE 0 END) suspicious FROM youtube_matches").fetchone()
            latest = conn.execute("SELECT * FROM youtube_scan_runs ORDER BY id DESC LIMIT 1").fetchone()
            return {
                "channels": int(channels["total"] or 0), "owned_channels": int(channels["owned"] or 0),
                "matches": int(matches["total"] or 0), "owned_publications": int(matches["owned"] or 0),
                "suspicious": int(matches["suspicious"] or 0), "last_scan": dict(latest) if latest else None,
            }

    # ------------------------------------------------------------------
    # Trajno pamćenje lokalnih foldera i istorija „Pronalazača pesme“
    # ------------------------------------------------------------------
    def remember_watched_folder(self, path: str, recursive: bool = True) -> dict[str, Any]:
        value = str(Path(path).expanduser().resolve())
        with self._lock, self._connect() as conn:
            conn.execute(
                """INSERT INTO watched_folders(path,recursive,enabled) VALUES(?,?,1)
                   ON CONFLICT(path) DO UPDATE SET recursive=excluded.recursive,enabled=1""",
                (value, 1 if recursive else 0),
            )
            row = conn.execute("SELECT * FROM watched_folders WHERE path=?", (value,)).fetchone()
            return dict(row) if row else {"path": value, "recursive": 1 if recursive else 0, "enabled": 1}

    def list_watched_folders(self, enabled_only: bool = False) -> list[dict[str, Any]]:
        sql = "SELECT * FROM watched_folders"
        params: list[Any] = []
        if enabled_only:
            sql += " WHERE enabled=1"
        sql += " ORDER BY path COLLATE NOCASE"
        with self._connect() as conn:
            return [dict(row) for row in conn.execute(sql, params).fetchall()]

    def update_watched_folder_scan(self, path: str, file_count: int, added_count: int) -> None:
        value = str(Path(path).expanduser().resolve())
        with self._lock, self._connect() as conn:
            conn.execute(
                """UPDATE watched_folders SET last_scan_at=CURRENT_TIMESTAMP,
                   last_file_count=?,last_added_count=? WHERE path=?""",
                (int(file_count), int(added_count), value),
            )

    def set_watched_folder_enabled(self, path: str, enabled: bool) -> None:
        value = str(Path(path).expanduser().resolve())
        with self._lock, self._connect() as conn:
            conn.execute("UPDATE watched_folders SET enabled=? WHERE path=?", (1 if enabled else 0, value))

    def remove_watched_folder(self, path: str) -> None:
        value = str(Path(path).expanduser().resolve())
        with self._lock, self._connect() as conn:
            conn.execute("DELETE FROM watched_folders WHERE path=?", (value,))

    def add_recognition(self, payload: dict[str, Any]) -> dict[str, Any]:
        result = payload.get("result") if isinstance(payload.get("result"), dict) else payload
        found = bool(result.get("found"))
        raw = result.get("raw") if isinstance(result.get("raw"), dict) else result
        with self._lock, self._connect() as conn:
            cur = conn.execute(
                """INSERT INTO recognized_tracks(
                       original_filename,input_path,prepared_audio_path,provider,found,
                       artist,title,album,release_date,label,timecode,song_link,result_json,
                       library_song_id,status,error_message
                   ) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)""",
                (
                    str(payload.get("original_filename") or ""),
                    str(payload.get("input_path") or ""),
                    str(payload.get("prepared_audio_path") or ""),
                    str(result.get("provider") or "AudD"),
                    1 if found else 0,
                    str(result.get("artist") or ""), str(result.get("title") or ""),
                    str(result.get("album") or ""), str(result.get("release_date") or ""),
                    str(result.get("label") or ""), str(result.get("timecode") or ""),
                    str(result.get("song_link") or ""),
                    json.dumps(raw, ensure_ascii=False),
                    str(payload.get("library_song_id") or ""),
                    str(payload.get("status") or "done"),
                    str(payload.get("error_message") or ""),
                ),
            )
            row = conn.execute("SELECT * FROM recognized_tracks WHERE id=?", (int(cur.lastrowid),)).fetchone()
            return dict(row) if row else {"id": int(cur.lastrowid)}

    def list_recognitions(self, limit: int = 200) -> list[dict[str, Any]]:
        limit = max(1, min(int(limit), 2000))
        with self._connect() as conn:
            rows = conn.execute("SELECT * FROM recognized_tracks ORDER BY id DESC LIMIT ?", (limit,)).fetchall()
            result: list[dict[str, Any]] = []
            for row in rows:
                item = dict(row)
                try:
                    item["result"] = json.loads(str(item.get("result_json") or "{}"))
                except Exception:
                    item["result"] = {}
                result.append(item)
            return result

    def get_recognition(self, recognition_id: int) -> dict[str, Any] | None:
        with self._connect() as conn:
            row = conn.execute("SELECT * FROM recognized_tracks WHERE id=?", (int(recognition_id),)).fetchone()
            if not row:
                return None
            item = dict(row)
            try:
                item["result"] = json.loads(str(item.get("result_json") or "{}"))
            except Exception:
                item["result"] = {}
            return item

    def set_recognition_library_song(self, recognition_id: int, song_id: str) -> None:
        with self._lock, self._connect() as conn:
            conn.execute(
                "UPDATE recognized_tracks SET library_song_id=?, manual_override_at=CURRENT_TIMESTAMP WHERE id=?",
                (str(song_id), int(recognition_id)),
            )

