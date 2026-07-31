package com.sunopesmestudio.app.data

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase

// exportSchema=false for this thin-slice pass: schema JSON export (needed
// for real Room migration testing per spec section 12/18) requires wiring
// room.schemaLocation via the Room Gradle plugin, deferred to the phase
// that actually adds a version-2 migration to test.
@Database(entities = [SongEntity::class, RecognizedTrackEntity::class], version = 1, exportSchema = false)
abstract class AppDatabase : RoomDatabase() {
    abstract fun songDao(): SongDao
    abstract fun recognizedTrackDao(): RecognizedTrackDao

    companion object {
        @Volatile private var instance: AppDatabase? = null

        fun get(context: Context): AppDatabase =
            instance ?: synchronized(this) {
                instance ?: Room.databaseBuilder(
                    context.applicationContext,
                    AppDatabase::class.java,
                    "suno_biblioteka.db",
                ).build().also { instance = it }
            }
    }
}
