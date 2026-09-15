package com.sunopesmestudio.app.data

import androidx.room.Entity
import androidx.room.PrimaryKey

/**
 * Local Pronalazac pesme (Song Finder) history -- mirrors the shape of the
 * desktop `recognized_tracks` table (docs/ORIGINAL_DATABASE_SCHEMA.sql),
 * scoped to what this Android pass actually implements: SHA-256 based
 * local-library matching. Remote provider lookup (AudD, section 14) is not
 * wired up here -- matchedSongId stays null and status stays
 * "local_no_match" until that phase.
 */
@Entity(tableName = "recognized_tracks")
data class RecognizedTrackEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val createdAtEpochMs: Long,
    val originalFilename: String,
    val sha256: String,
    val matchedSongId: String? = null,
    val matchedTitle: String? = null,
    val status: String, // "local_match" | "local_no_match" | "error"
)
