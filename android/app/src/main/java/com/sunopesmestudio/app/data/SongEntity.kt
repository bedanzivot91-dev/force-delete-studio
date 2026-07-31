package com.sunopesmestudio.app.data

import androidx.room.ColumnInfo
import androidx.room.Entity
import androidx.room.PrimaryKey

/**
 * Mirrors the subset of the desktop `songs` table (see
 * docs/ORIGINAL_DATABASE_SCHEMA.sql in the repo root) that the Android app
 * actually needs for library browsing, favorites/ratings and local
 * playback. Full-fidelity field parity (raw_json, custom_tags, etc.) is a
 * later-phase sync concern, not needed for the thin local-library slice.
 */
@Entity(tableName = "songs")
data class SongEntity(
    @PrimaryKey val id: String,
    val title: String,
    val displayName: String = "",
    val durationSeconds: Double = 0.0,
    val modelVersion: String = "",
    val genre: String = "",
    val isFavorite: Boolean = false,
    val rating: Int = 0,
    val localAudioUri: String? = null,
    val localCoverUri: String? = null,
    val localLyrics: String? = null,
    @ColumnInfo(defaultValue = "0") val downloadedAtEpochMs: Long = 0,
    @ColumnInfo(defaultValue = "1") val importedAtEpochMs: Long = 0,
)
