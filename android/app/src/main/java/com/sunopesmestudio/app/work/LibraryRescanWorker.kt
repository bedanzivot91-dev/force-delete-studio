package com.sunopesmestudio.app.work

import android.content.Context
import android.net.Uri
import androidx.documentfile.provider.DocumentFile
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import com.sunopesmestudio.app.SunoApplication
import java.security.MessageDigest
import java.util.UUID

private val AUDIO_EXTENSIONS = setOf("mp3", "wav", "flac", "m4a", "aac", "ogg")

/**
 * Real (not no-op) periodic rescan of every SAF folder the user granted via
 * WatchedFoldersPreference: lists files, skips anything already matched by
 * SHA-256 in the local library (mirrors the desktop "samo nove/promenjene
 * fajlove" incremental-scan behavior from section 13), and inserts the rest
 * as new SongEntity rows.
 */
class LibraryRescanWorker(appContext: Context, params: WorkerParameters) : CoroutineWorker(appContext, params) {

    override suspend fun doWork(): Result {
        val app = applicationContext as SunoApplication
        val songDao = app.database.songDao()
        val folderUris = app.watchedFoldersPreference.snapshot()
        if (folderUris.isEmpty()) return Result.success()

        var imported = 0
        for (folderUriString in folderUris) {
            val treeUri = Uri.parse(folderUriString)
            val tree = DocumentFile.fromTreeUri(applicationContext, treeUri) ?: continue
            val files = tree.listFiles().filter { it.isFile && it.name != null && isAudioFile(it.name!!) }
            for (file in files) {
                val hash = try {
                    applicationContext.contentResolver.openInputStream(file.uri)?.use { sha256Of(it) }
                } catch (e: Exception) {
                    null
                } ?: continue
                if (songDao.findByHash(hash) != null) continue
                songDao.upsert(
                    com.sunopesmestudio.app.data.SongEntity(
                        id = UUID.randomUUID().toString(),
                        title = file.name?.substringBeforeLast('.') ?: "Nepoznato",
                        localAudioUri = file.uri.toString(),
                        fileSha256 = hash,
                        importedAtEpochMs = System.currentTimeMillis(),
                    ),
                )
                imported++
            }
        }
        return Result.success()
    }

    private fun isAudioFile(name: String): Boolean = AUDIO_EXTENSIONS.contains(name.substringAfterLast('.', "").lowercase())

    private fun sha256Of(stream: java.io.InputStream): String {
        val digest = MessageDigest.getInstance("SHA-256")
        val buffer = ByteArray(64 * 1024)
        while (true) {
            val read = stream.read(buffer)
            if (read <= 0) break
            digest.update(buffer, 0, read)
        }
        return digest.digest().joinToString("") { "%02x".format(it) }
    }
}
