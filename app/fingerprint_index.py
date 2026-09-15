"""Fast candidate retrieval for song recognition.

The problem this solves, measured on real data: comparing one Shorts clip
against one song with compare_signatures() costs ~99 ms. Doing that for every
song in the library is linear, so a 3091-song library needs ~306 s per search
and a 10000-song library ~16 min. The library only grows, so the search only
gets slower -- which is exactly the wrong direction.

The fix is the standard landmark/inverted-index idea used by Shazam-style
matchers: do not compare against everything. Look up which songs share
fingerprint hashes with the clip, keep the handful that also agree on a
consistent time offset, and run the existing precise comparison on just those.

No new dependency and no third-party code: this indexes the Chromaprint values
audio_match already computes and stores, using only sqlite3 and the standard
library.

Robustness to lossy re-encoding is the whole difficulty, and it is not
hypothetical. Measured on the user's real Shorts against its real Suno
original, only 22 of 204 frames were bit-identical; on average 1.98 of the 32
bits differ per frame. An exact-hash index would therefore miss ~89% of
frames. Bit-sampling LSH handles that: each frame is indexed under BAND_COUNT
keys, each built from a fixed pseudo-random subset of BAND_BITS bit positions.
A key survives whenever its sampled positions happen to miss the corrupted
bits, and with several independent keys at least one nearly always does.
Against the measured error distribution above, BAND_BITS=14 with BAND_COUNT=8
recovers ~87% of frames.
"""
from __future__ import annotations

import random
import sqlite3
import threading
from collections import defaultdict
from pathlib import Path
from typing import Any, Iterable, Sequence

INDEX_VERSION = 1

# See the module docstring: these are chosen against the measured per-frame
# bit-error distribution of a real Shorts re-encode, not against a guess.
BAND_COUNT = 8
BAND_BITS = 14

# Songs are sampled rather than fully indexed. A fixed stride (not a fixed
# count) keeps the density constant, so a short clip's window always contains
# enough indexed frames to vote with, while long songs do not bloat the index.
SAMPLE_STRIDE = 3

# Offsets are bucketed before voting so slight timing drift between the clip
# and the original still lands in the same bucket.
OFFSET_BUCKET = 4

# SQLite's default host-parameter limit is 999.
_SQL_CHUNK = 900

_MASK_SEED = 0x5EED  # fixed: the same masks must be used to build and to query


def _build_masks() -> list[tuple[int, ...]]:
    rnd = random.Random(_MASK_SEED)
    return [tuple(sorted(rnd.sample(range(32), BAND_BITS))) for _ in range(BAND_COUNT)]


_MASKS = _build_masks()

# Precomputed so hashing a frame is table lookups instead of 32 bit tests.
# Each band's 32 bit positions are split into two 16-bit halves; for each half
# a 65536-entry table maps the half-word straight to its contribution.
def _build_tables() -> list[tuple[list[int], list[int], int]]:
    tables: list[tuple[list[int], list[int], int]] = []
    for band, positions in enumerate(_MASKS):
        low = [0] * 65536
        high = [0] * 65536
        for out_index, position in enumerate(positions):
            bit = 1 << out_index
            if position < 16:
                mask = 1 << position
                for value in range(65536):
                    if value & mask:
                        low[value] |= bit
            else:
                mask = 1 << (position - 16)
                for value in range(65536):
                    if value & mask:
                        high[value] |= bit
        tables.append((low, high, band << BAND_BITS))
    return tables


_TABLES = _build_tables()


def _keys_for_value(value: int) -> list[int]:
    """The BAND_COUNT lookup keys for one 32-bit Chromaprint frame.

    Keys are tagged with their band number because all bands share one table,
    and a key value from one band must not collide with the same key value
    from another.
    """
    value &= 0xFFFFFFFF
    low_half = value & 0xFFFF
    high_half = value >> 16
    return [tag | low[low_half] | high[high_half] for low, high, tag in _TABLES]


def _sample_positions(total: int) -> range:
    return range(0, total, SAMPLE_STRIDE)


class FingerprintIndex:
    """Persistent inverted index over Chromaprint frames.

    Kept in its own SQLite file so it can be rebuilt or deleted without ever
    touching the library database.
    """

    def __init__(self, path: Path):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._lock = threading.RLock()
        self._conn: sqlite3.Connection | None = None
        self._init_db()

    # -- connection -----------------------------------------------------

    def _cx(self) -> sqlite3.Connection:
        """One long-lived connection. Reopening per song made bulk indexing
        dominated by connection setup and WAL checkpoints."""
        with self._lock:
            if self._conn is None:
                conn = sqlite3.connect(self.path, timeout=30, check_same_thread=False)
                conn.execute("PRAGMA journal_mode=WAL")
                conn.execute("PRAGMA synchronous=NORMAL")
                conn.execute("PRAGMA temp_store=MEMORY")
                conn.execute("PRAGMA cache_size=-40000")  # ~40 MB page cache
                self._conn = conn
            return self._conn

    def checkpoint(self) -> None:
        """Fold the write-ahead log back into the database file.

        Bulk indexing leaves a WAL several times larger than the data it
        represents, so without this the index looks (and on disk, is) far
        bigger than it needs to be.
        """
        with self._lock:
            try:
                self._cx().execute("PRAGMA wal_checkpoint(TRUNCATE)")
            except sqlite3.Error:
                pass

    def close(self) -> None:
        with self._lock:
            if self._conn is not None:
                try:
                    self._conn.commit()
                    self._conn.close()
                finally:
                    self._conn = None

    def _init_db(self) -> None:
        with self._lock:
            conn = self._cx()
            # fingerprint_hashes is WITHOUT ROWID and keyed by the exact
            # columns it is queried on. That makes it a single B-tree instead
            # of a table plus a duplicate secondary index, and songs are
            # referenced by a small integer rather than by repeating the
            # song id in all ~11 million rows. Measured, this is the
            # difference between a 529 MB and a ~120 MB index for a
            # 3000-song library.
            conn.executescript(
                """
                CREATE TABLE IF NOT EXISTS index_meta (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL DEFAULT ''
                );
                CREATE TABLE IF NOT EXISTS indexed_songs (
                    song_ref INTEGER PRIMARY KEY AUTOINCREMENT,
                    song_id TEXT NOT NULL UNIQUE,
                    frames INTEGER NOT NULL DEFAULT 0,
                    fingerprint_version TEXT NOT NULL DEFAULT '',
                    updated_at TEXT DEFAULT CURRENT_TIMESTAMP
                );
                CREATE TABLE IF NOT EXISTS fingerprint_hashes (
                    hash_key INTEGER NOT NULL,
                    song_ref INTEGER NOT NULL,
                    frame_pos INTEGER NOT NULL,
                    PRIMARY KEY (hash_key, song_ref, frame_pos)
                ) WITHOUT ROWID;
                """
            )
            conn.execute(
                "INSERT INTO index_meta(key,value) VALUES('version',?) "
                "ON CONFLICT(key) DO UPDATE SET value=excluded.value",
                (str(INDEX_VERSION),),
            )
            conn.commit()

    # -- building -------------------------------------------------------

    def _song_ref(self, conn: sqlite3.Connection, song_id: str) -> tuple[int, bool]:
        """Returns (song_ref, existed_before)."""
        row = conn.execute("SELECT song_ref FROM indexed_songs WHERE song_id=?", (song_id,)).fetchone()
        if row is not None:
            return int(row[0]), True
        cur = conn.execute("INSERT INTO indexed_songs(song_id) VALUES(?)", (song_id,))
        return int(cur.lastrowid), False

    def add_song(self, song_id: str, chromaprint: Sequence[int], version: str = "") -> int:
        """(Re)index one song. Returns how many index rows were written."""
        return self.add_songs([(song_id, chromaprint, version)])

    def add_songs(self, items: Iterable[tuple[str, Sequence[int], str]]) -> int:
        """Index many songs in one transaction.

        Indexing is overwhelmingly a bulk operation (the first run over a
        whole library), so committing once per song would make it needlessly
        slow. Rows of songs that are being *re*-indexed are cleared with a
        single statement for the whole batch rather than one scan per song.
        """
        written = 0
        with self._lock:
            conn = self._cx()
            pending: list[tuple[int, str, Sequence[int], str]] = []
            stale_refs: list[int] = []
            for song_id, chromaprint, version in items:
                song_id = str(song_id or "")
                if not song_id or not chromaprint:
                    continue
                ref, existed = self._song_ref(conn, song_id)
                if existed:
                    stale_refs.append(ref)
                pending.append((ref, song_id, chromaprint, str(version or "")))
            if not pending:
                conn.commit()
                return 0
            for start in range(0, len(stale_refs), _SQL_CHUNK):
                chunk = stale_refs[start:start + _SQL_CHUNK]
                marks = ",".join("?" for _ in chunk)
                conn.execute(f"DELETE FROM fingerprint_hashes WHERE song_ref IN ({marks})", chunk)
            rows: list[tuple[int, int, int]] = []
            append = rows.append
            for ref, song_id, chromaprint, version in pending:
                count = 0
                for pos in _sample_positions(len(chromaprint)):
                    for key in _keys_for_value(int(chromaprint[pos])):
                        append((key, ref, pos))
                        count += 1
                conn.execute(
                    "UPDATE indexed_songs SET frames=?, fingerprint_version=?, "
                    "updated_at=CURRENT_TIMESTAMP WHERE song_ref=?",
                    (count, version, ref),
                )
                written += count
            conn.executemany(
                "INSERT OR REPLACE INTO fingerprint_hashes(hash_key,song_ref,frame_pos) VALUES(?,?,?)",
                rows,
            )
            conn.commit()
        return written

    def remove_song(self, song_id: str) -> None:
        self.remove_songs([song_id])

    def remove_songs(self, song_ids: Iterable[str]) -> int:
        with self._lock:
            conn = self._cx()
            refs: list[int] = []
            for song_id in song_ids:
                row = conn.execute(
                    "SELECT song_ref FROM indexed_songs WHERE song_id=?", (str(song_id),)
                ).fetchone()
                if row is not None:
                    refs.append(int(row[0]))
            for start in range(0, len(refs), _SQL_CHUNK):
                chunk = refs[start:start + _SQL_CHUNK]
                marks = ",".join("?" for _ in chunk)
                conn.execute(f"DELETE FROM fingerprint_hashes WHERE song_ref IN ({marks})", chunk)
                conn.execute(f"DELETE FROM indexed_songs WHERE song_ref IN ({marks})", chunk)
            conn.commit()
            return len(refs)

    def indexed_song_ids(self) -> set[str]:
        rows = self._cx().execute("SELECT song_id FROM indexed_songs").fetchall()
        return {str(r[0]) for r in rows}

    def indexed_versions(self) -> dict[str, str]:
        """song_id -> the fingerprint version it was indexed from, so a song
        can be re-indexed when its fingerprint is recomputed."""
        rows = self._cx().execute("SELECT song_id,fingerprint_version FROM indexed_songs").fetchall()
        return {str(r[0]): str(r[1] or "") for r in rows}

    def stats(self) -> dict[str, Any]:
        conn = self._cx()
        songs = int(conn.execute("SELECT COUNT(*) FROM indexed_songs").fetchone()[0])
        hashes = int(conn.execute("SELECT COUNT(*) FROM fingerprint_hashes").fetchone()[0])
        size = 0
        for suffix in ("", "-wal"):
            candidate = Path(str(self.path) + suffix)
            if candidate.exists():
                size += candidate.stat().st_size
        return {
            "songs": songs,
            "hashes": hashes,
            "version": INDEX_VERSION,
            "path": str(self.path),
            "size_bytes": size,
        }

    def clear(self) -> None:
        with self._lock:
            conn = self._cx()
            conn.execute("DELETE FROM fingerprint_hashes")
            conn.execute("DELETE FROM indexed_songs")
            conn.commit()

    def prune(self, keep_song_ids: Iterable[str]) -> int:
        """Drop songs no longer in the library. Returns how many were removed."""
        keep = {str(s) for s in keep_song_ids}
        stale = [s for s in self.indexed_song_ids() if s not in keep]
        return self.remove_songs(stale) if stale else 0

    # -- querying -------------------------------------------------------

    def candidates(self, chromaprint: Sequence[int], limit: int = 20) -> list[dict[str, Any]]:
        """Songs most likely to contain this clip, best first.

        Votes are counted per (song, time-offset) rather than per song, so a
        song only scores highly when its matching frames line up at ONE
        consistent offset -- which is what a genuine occurrence looks like and
        what scattered hash collisions do not.
        """
        if not chromaprint:
            return []
        wanted: dict[int, list[int]] = defaultdict(list)
        for clip_pos, value in enumerate(chromaprint):
            for key in _keys_for_value(int(value)):
                wanted[key].append(clip_pos)
        if not wanted:
            return []

        votes: dict[tuple[int, int], int] = defaultdict(int)
        keys = list(wanted.keys())
        conn = self._cx()
        bucket = OFFSET_BUCKET
        for start in range(0, len(keys), _SQL_CHUNK):
            chunk = keys[start:start + _SQL_CHUNK]
            marks = ",".join("?" for _ in chunk)
            for key, song_ref, frame_pos in conn.execute(
                f"SELECT hash_key,song_ref,frame_pos FROM fingerprint_hashes "
                f"WHERE hash_key IN ({marks})",
                chunk,
            ):
                for clip_pos in wanted[key]:
                    votes[(song_ref, (frame_pos - clip_pos) // bucket)] += 1
        if not votes:
            return []

        best: dict[int, tuple[int, int]] = {}
        for (song_ref, offset_bucket), count in votes.items():
            current = best.get(song_ref)
            if current is None or count > current[0]:
                best[song_ref] = (count, offset_bucket)
        ranked = sorted(best.items(), key=lambda kv: kv[1][0], reverse=True)[:max(1, int(limit))]

        refs = [ref for ref, _ in ranked]
        marks = ",".join("?" for _ in refs)
        names = {
            int(r[0]): str(r[1])
            for r in conn.execute(
                f"SELECT song_ref,song_id FROM indexed_songs WHERE song_ref IN ({marks})", refs
            )
        }
        return [
            {
                "song_id": names.get(ref, ""),
                "votes": count,
                "offset_frames": offset_bucket * OFFSET_BUCKET,
            }
            for ref, (count, offset_bucket) in ranked
            if names.get(ref)
        ]
