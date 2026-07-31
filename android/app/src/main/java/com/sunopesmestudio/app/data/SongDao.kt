package com.sunopesmestudio.app.data

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.Update
import kotlinx.coroutines.flow.Flow

@Dao
interface SongDao {
    @Query("SELECT * FROM songs ORDER BY importedAtEpochMs DESC")
    fun observeAll(): Flow<List<SongEntity>>

    @Query("SELECT * FROM songs WHERE isFavorite = 1 ORDER BY importedAtEpochMs DESC")
    fun observeFavorites(): Flow<List<SongEntity>>

    @Query("SELECT * FROM songs WHERE title LIKE '%' || :query || '%' OR displayName LIKE '%' || :query || '%' ORDER BY importedAtEpochMs DESC")
    fun search(query: String): Flow<List<SongEntity>>

    @Query("SELECT COUNT(*) FROM songs")
    fun observeCount(): Flow<Int>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertAll(songs: List<SongEntity>)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(song: SongEntity)

    @Update
    suspend fun update(song: SongEntity)

    @Query("UPDATE songs SET isFavorite = :favorite WHERE id = :id")
    suspend fun setFavorite(id: String, favorite: Boolean)

    @Query("UPDATE songs SET rating = :rating WHERE id = :id")
    suspend fun setRating(id: String, rating: Int)

    @Query("DELETE FROM songs WHERE id = :id")
    suspend fun delete(id: String)
}
