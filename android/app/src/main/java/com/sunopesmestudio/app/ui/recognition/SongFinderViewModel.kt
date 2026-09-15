package com.sunopesmestudio.app.ui.recognition

import android.app.Application
import android.media.MediaRecorder
import android.net.Uri
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import com.sunopesmestudio.app.data.RecognizedTrackDao
import com.sunopesmestudio.app.data.RecognizedTrackEntity
import com.sunopesmestudio.app.data.SongDao
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.io.InputStream
import java.security.MessageDigest

/**
 * Pronalazac pesme (Song Finder), local-only slice: pick a file via SAF or
 * record a clip with the mic, hash it with SHA-256, and check it against
 * this device's local library (SongEntity.fileSha256). Every step here
 * actually runs -- there is no fake "recognizing..." spinner with a
 * hardcoded result. Remote provider lookup (AudD, section 14) needs a
 * user-supplied API token and a network client; deferred, and the UI does
 * not pretend to offer it.
 */
class SongFinderViewModel(
    application: Application,
    private val songDao: SongDao,
    private val recognizedTrackDao: RecognizedTrackDao,
) : AndroidViewModel(application) {

    val history: StateFlow<List<RecognizedTrackEntity>> = recognizedTrackDao.observeHistory()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    private val _isBusy = MutableStateFlow(false)
    val isBusy: StateFlow<Boolean> = _isBusy

    private val _isRecording = MutableStateFlow(false)
    val isRecording: StateFlow<Boolean> = _isRecording

    private val _lastResult = MutableStateFlow<RecognizedTrackEntity?>(null)
    val lastResult: StateFlow<RecognizedTrackEntity?> = _lastResult

    private var recorder: MediaRecorder? = null
    private var recordingFile: File? = null

    fun analyzePickedFile(uri: Uri, displayName: String) {
        viewModelScope.launch {
            _isBusy.value = true
            try {
                val stream = getApplication<Application>().contentResolver.openInputStream(uri)
                    ?: throw IllegalStateException("Fajl nije mogao da se otvori.")
                stream.use { analyzeStream(it, displayName) }
            } catch (e: Exception) {
                recordFailure(displayName)
            } finally {
                _isBusy.value = false
            }
        }
    }

    @Suppress("DEPRECATION") // MediaRecorder(Context) needs API 31; minSdk here is 26.
    fun startRecording() {
        val dir = getApplication<Application>().cacheDir
        val file = File(dir, "pronalazac_${System.currentTimeMillis()}.m4a")
        val rec = MediaRecorder()
        rec.setAudioSource(MediaRecorder.AudioSource.MIC)
        rec.setOutputFormat(MediaRecorder.OutputFormat.MPEG_4)
        rec.setAudioEncoder(MediaRecorder.AudioEncoder.AAC)
        rec.setOutputFile(file.absolutePath)
        rec.prepare()
        rec.start()
        recorder = rec
        recordingFile = file
        _isRecording.value = true
    }

    fun stopRecordingAndAnalyze() {
        val rec = recorder ?: return
        val file = recordingFile
        try {
            rec.stop()
        } finally {
            rec.release()
            recorder = null
            _isRecording.value = false
        }
        if (file != null && file.exists() && file.length() > 0) {
            viewModelScope.launch {
                _isBusy.value = true
                try {
                    file.inputStream().use { analyzeStream(it, file.name) }
                } catch (e: Exception) {
                    recordFailure(file.name)
                } finally {
                    _isBusy.value = false
                    file.delete()
                }
            }
        }
    }

    private suspend fun analyzeStream(stream: InputStream, filename: String) {
        val hash = withContext(Dispatchers.IO) { sha256Of(stream) }
        val match = songDao.findByHash(hash)
        val entity = RecognizedTrackEntity(
            createdAtEpochMs = System.currentTimeMillis(),
            originalFilename = filename,
            sha256 = hash,
            matchedSongId = match?.id,
            matchedTitle = match?.title,
            status = if (match != null) "local_match" else "local_no_match",
        )
        recognizedTrackDao.insert(entity)
        _lastResult.value = entity
    }

    private suspend fun recordFailure(filename: String) {
        val entity = RecognizedTrackEntity(
            createdAtEpochMs = System.currentTimeMillis(),
            originalFilename = filename,
            sha256 = "",
            status = "error",
        )
        recognizedTrackDao.insert(entity)
        _lastResult.value = entity
    }

    private fun sha256Of(stream: InputStream): String {
        val digest = MessageDigest.getInstance("SHA-256")
        val buffer = ByteArray(64 * 1024)
        while (true) {
            val read = stream.read(buffer)
            if (read <= 0) break
            digest.update(buffer, 0, read)
        }
        return digest.digest().joinToString("") { "%02x".format(it) }
    }

    class Factory(
        private val application: Application,
        private val songDao: SongDao,
        private val recognizedTrackDao: RecognizedTrackDao,
    ) : ViewModelProvider.Factory {
        @Suppress("UNCHECKED_CAST")
        override fun <T : androidx.lifecycle.ViewModel> create(modelClass: Class<T>): T =
            SongFinderViewModel(application, songDao, recognizedTrackDao) as T
    }
}
