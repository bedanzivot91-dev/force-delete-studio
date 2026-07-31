package com.sunopesmestudio.app.data

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.Query
import kotlinx.coroutines.flow.Flow

@Dao
interface RecognizedTrackDao {
    @Query("SELECT * FROM recognized_tracks ORDER BY createdAtEpochMs DESC")
    fun observeHistory(): Flow<List<RecognizedTrackEntity>>

    @Insert
    suspend fun insert(track: RecognizedTrackEntity): Long
}
