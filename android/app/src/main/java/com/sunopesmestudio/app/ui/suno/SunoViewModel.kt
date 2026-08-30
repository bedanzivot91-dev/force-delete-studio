package com.sunopesmestudio.app.ui.suno

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import com.sunopesmestudio.app.data.SongDao
import com.sunopesmestudio.app.data.SongEntity
import com.sunopesmestudio.app.data.SunoAuthStore
import com.sunopesmestudio.app.network.SunoApiClient
import com.sunopesmestudio.app.network.SunoClip
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.security.MessageDigest

data class SunoUiState(
    val connected: Boolean = false,
    val loadingFeed: Boolean = false,
    val songs: List<SunoClip> = emptyList(),
    val downloadingIds: Set<String> = emptySet(),
    val downloadedIds: Set<String> = emptySet(),
    val error: String? = null,
)

/**
 * Real Suno account connection for Android: the bearer token is captured
 * from a live, user-driven WebView login (see SunoConnectScreen), not
 * hardcoded or faked. Once connected this talks to the actual Suno API
 * (studio-api.prod.suno.com) the same way app/suno_client.py does on
 * desktop. Unlike everything else verified in this project's CI, the
 * live network behavior here can only be confirmed by a real device with
 * a real Suno account -- CI can only prove this compiles.
 */
class SunoViewModel(
    application: Application,
    private val authStore: SunoAuthStore,
    private val songDao: SongDao,
) : AndroidViewModel(application) {

    private val _state = MutableStateFlow(SunoUiState(connected = authStore.isConnected))
    val state: StateFlow<SunoUiState> = _state

    init {
        if (authStore.isConnected) refreshFeed()
    }

    fun onTokenCaptured(token: String) {
        authStore.token = token
        _state.value = _state.value.copy(connected = true, error = null)
        refreshFeed()
    }

    fun disconnect() {
        authStore.clear()
        _state.value = SunoUiState(connected = false)
    }

    fun refreshFeed() {
        val token = authStore.token ?: return
        viewModelScope.launch {
            _state.value = _state.value.copy(loadingFeed = true, error = null)
            try {
                val songs = SunoApiClient.fetchFeed(token, authStore.deviceId)
                _state.value = _state.value.copy(loadingFeed = false, songs = songs)
            } catch (e: Exception) {
                _state.value = _state.value.copy(loadingFeed = false, error = e.message ?: "Greška pri učitavanju.")
            }
        }
    }

    fun downloadSong(clip: SunoClip) {
        val token = authStore.token ?: return
        val audioUrl = clip.audioUrl ?: return
        viewModelScope.launch {
            _state.value = _state.value.copy(downloadingIds = _state.value.downloadingIds + clip.id)
            try {
                val dir = File(getApplication<Application>().filesDir, "suno_downloads")
                val dest = File(dir, "${clip.id}.mp3")
                SunoApiClient.downloadTo(audioUrl, token, authStore.deviceId, dest)
                val sha256 = withContext(Dispatchers.IO) { sha256Of(dest) }
                songDao.upsert(
                    SongEntity(
                        id = clip.id,
                        title = clip.title,
                        displayName = clip.title,
                        modelVersion = clip.modelVersion ?: "",
                        localAudioUri = dest.toURI().toString(),
                        localCoverUri = clip.imageUrl,
                        downloadedAtEpochMs = System.currentTimeMillis(),
                        importedAtEpochMs = System.currentTimeMillis(),
                        fileSha256 = sha256,
                    ),
                )
                _state.value = _state.value.copy(
                    downloadingIds = _state.value.downloadingIds - clip.id,
                    downloadedIds = _state.value.downloadedIds + clip.id,
                )
            } catch (e: Exception) {
                _state.value = _state.value.copy(
                    downloadingIds = _state.value.downloadingIds - clip.id,
                    error = e.message ?: "Preuzimanje nije uspelo.",
                )
            }
        }
    }

    private fun sha256Of(file: File): String {
        val digest = MessageDigest.getInstance("SHA-256")
        file.inputStream().use { stream ->
            val buffer = ByteArray(64 * 1024)
            while (true) {
                val read = stream.read(buffer)
                if (read <= 0) break
                digest.update(buffer, 0, read)
            }
        }
        return digest.digest().joinToString("") { "%02x".format(it) }
    }

    class Factory(
        private val application: Application,
        private val authStore: SunoAuthStore,
        private val songDao: SongDao,
    ) : ViewModelProvider.Factory {
        @Suppress("UNCHECKED_CAST")
        override fun <T : androidx.lifecycle.ViewModel> create(modelClass: Class<T>): T =
            SunoViewModel(application, authStore, songDao) as T
    }
}
