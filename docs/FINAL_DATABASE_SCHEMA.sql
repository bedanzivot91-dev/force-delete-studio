-- Regenerated in this session by actually instantiating app.database.LibraryDB() with python3.13 and dumping sqlite_master.
-- This reflects the CURRENT code, not a stale snapshot of the originally-delivered suno_biblioteka.db.

CREATE TABLE app_log (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    level TEXT NOT NULL,
                    message TEXT NOT NULL
                );

CREATE TABLE audio_fingerprints (
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

CREATE TABLE backup_items (
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

CREATE TABLE collection_songs (
                    collection_id INTEGER NOT NULL,
                    song_id TEXT NOT NULL,
                    added_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY(collection_id, song_id),
                    FOREIGN KEY(collection_id) REFERENCES collections(id) ON DELETE CASCADE,
                    FOREIGN KEY(song_id) REFERENCES songs(id) ON DELETE CASCADE
                );

CREATE TABLE collections (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    slug TEXT NOT NULL UNIQUE,
                    name TEXT NOT NULL UNIQUE,
                    color TEXT DEFAULT '#7c3aed',
                    is_system INTEGER DEFAULT 0,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP
                );

CREATE TABLE derived_files (
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

CREATE TABLE job_queue (
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

CREATE TABLE recognized_tracks (
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

CREATE TABLE scheduled_tasks (
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

CREATE TABLE settings (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );

CREATE TABLE song_history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    song_id TEXT NOT NULL,
                    action TEXT NOT NULL DEFAULT 'edit',
                    fields_json TEXT NOT NULL DEFAULT '{}',
                    snapshot_json TEXT NOT NULL DEFAULT '{}',
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY(song_id) REFERENCES songs(id) ON DELETE CASCADE
                );

CREATE TABLE songs (
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
                , album TEXT DEFAULT '', genre TEXT DEFAULT '', year TEXT DEFAULT '', track_number TEXT DEFAULT '', copyright TEXT DEFAULT '', website TEXT DEFAULT '', bpm TEXT DEFAULT '', key_signature TEXT DEFAULT '', youtube_url TEXT DEFAULT '', youtube_published_at TEXT DEFAULT '', source_group TEXT DEFAULT '', title_locked INTEGER DEFAULT 0, artist_locked INTEGER DEFAULT 0, lyrics_locked INTEGER DEFAULT 0, content_sha256 TEXT DEFAULT '', first_seen_at TEXT DEFAULT '', last_seen_at TEXT DEFAULT '', last_checked_at TEXT DEFAULT '', archived INTEGER DEFAULT 0);

CREATE TABLE sqlite_sequence(name,seq);

CREATE TABLE subtitle_cues (
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

CREATE TABLE sync_checkpoints (
                    source_key TEXT PRIMARY KEY,
                    source_label TEXT DEFAULT '',
                    cursor TEXT DEFAULT '',
                    page_no INTEGER DEFAULT 0,
                    records_processed INTEGER DEFAULT 0,
                    mode TEXT DEFAULT '',
                    completed INTEGER DEFAULT 0,
                    updated_at TEXT DEFAULT CURRENT_TIMESTAMP
                );

CREATE TABLE text_comparisons (
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

CREATE TABLE watched_folders (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    path TEXT NOT NULL UNIQUE,
                    recursive INTEGER DEFAULT 1,
                    enabled INTEGER DEFAULT 1,
                    last_scan_at TEXT DEFAULT '',
                    last_file_count INTEGER DEFAULT 0,
                    last_added_count INTEGER DEFAULT 0,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP
                );

CREATE TABLE youtube_channels (
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

CREATE TABLE youtube_matches (
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
                    last_seen_at TEXT DEFAULT CURRENT_TIMESTAMP, audio_score REAL DEFAULT 0, coverage_percent REAL DEFAULT 0, matched_seconds REAL DEFAULT 0, completeness_status TEXT DEFAULT '', confidence TEXT DEFAULT '', source_start REAL DEFAULT 0, source_end REAL DEFAULT 0, video_start REAL DEFAULT 0, video_end REAL DEFAULT 0, missing_start REAL DEFAULT 0, missing_end REAL DEFAULT 0, has_intro INTEGER DEFAULT 0, has_outro INTEGER DEFAULT 0, audio_checked_at TEXT DEFAULT '', analysis_version TEXT DEFAULT '', manual_link INTEGER DEFAULT 0, segments_json TEXT DEFAULT '[]', missing_intervals_json TEXT DEFAULT '[]', segment_count INTEGER DEFAULT 0, occurrence_count INTEGER DEFAULT 0, total_matched_seconds REAL DEFAULT 0, internal_gap_seconds REAL DEFAULT 0, version_type TEXT DEFAULT '', recommendation TEXT DEFAULT '', match_role TEXT DEFAULT 'primary', video_song_count INTEGER DEFAULT 1,
                    UNIQUE(song_id, video_id),
                    FOREIGN KEY(song_id) REFERENCES songs(id) ON DELETE CASCADE,
                    FOREIGN KEY(video_id) REFERENCES youtube_videos(video_id) ON DELETE CASCADE
                );

CREATE TABLE youtube_scan_runs (
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

CREATE TABLE youtube_videos (
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
                , audio_cache_path TEXT DEFAULT '', audio_cache_sha256 TEXT DEFAULT '', audio_analyzed_at TEXT DEFAULT '');

CREATE INDEX idx_audio_fingerprints_source ON audio_fingerprints(source_type, source_id, algorithm);

CREATE INDEX idx_backup_items_backup ON backup_items(backup_id, song_id);

CREATE INDEX idx_collection_songs_song ON collection_songs(song_id);

CREATE INDEX idx_derived_song ON derived_files(song_id, created_at DESC);

CREATE INDEX idx_job_queue_status ON job_queue(status, id);

CREATE INDEX idx_recognized_tracks_created ON recognized_tracks(created_at DESC);

CREATE INDEX idx_recognized_tracks_title ON recognized_tracks(title COLLATE NOCASE, artist COLLATE NOCASE);

CREATE INDEX idx_song_history_song ON song_history(song_id, id DESC);

CREATE INDEX idx_songs_created ON songs(created_at DESC);

CREATE INDEX idx_songs_downloaded ON songs(downloaded_at);

CREATE INDEX idx_songs_liked ON songs(is_liked);

CREATE INDEX idx_songs_title ON songs(title COLLATE NOCASE);

CREATE INDEX idx_subtitle_cues_song ON subtitle_cues(song_id, cue_index);

CREATE INDEX idx_watched_folders_enabled ON watched_folders(enabled, path);

CREATE INDEX idx_youtube_channels_owned ON youtube_channels(is_owned, title);

CREATE INDEX idx_youtube_matches_status ON youtube_matches(status, match_type, score DESC);

CREATE INDEX idx_youtube_videos_channel ON youtube_videos(channel_id, published_at DESC);

