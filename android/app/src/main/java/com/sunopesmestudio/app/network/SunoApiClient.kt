package com.sunopesmestudio.app.network

import android.util.Base64
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL

/**
 * Minimal Kotlin port of app/suno_client.py's SunoClient, scoped to what
 * the Android "Suno nalog" screen needs (list the signed-in user's clips,
 * download a finished one). Mirrors the same endpoint, headers and
 * browser-token scheme the desktop app already uses successfully against
 * the real Suno API, rather than inventing a new integration from
 * scratch.
 */
data class SunoClip(
    val id: String,
    val title: String,
    val audioUrl: String?,
    val imageUrl: String?,
    val status: String?,
    val modelVersion: String?,
    val createdAt: String?,
)

class SunoApiException(message: String, val statusCode: Int? = null) : IOException(message)

object SunoApiClient {
    private const val API_BASE = "https://studio-api.prod.suno.com"
    private const val USER_AGENT =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36"

    private fun browserTokenHeader(): String {
        val compact = JSONObject().put("timestamp", System.currentTimeMillis()).toString()
        val encoded = Base64.encodeToString(compact.toByteArray(Charsets.UTF_8), Base64.NO_WRAP or Base64.NO_PADDING)
        return JSONObject().put("token", encoded).toString()
    }

    private fun shouldSendAuth(urlString: String): Boolean {
        val host = runCatching { URL(urlString).host }.getOrNull()?.lowercase() ?: return false
        return host == "studio-api.prod.suno.com" || host.endsWith(".studio-api.prod.suno.com")
    }

    suspend fun fetchFeed(token: String, deviceId: String, limit: Int = 50): List<SunoClip> =
        withContext(Dispatchers.IO) {
            val body = JSONObject().apply {
                put("limit", limit.coerceIn(1, 100))
                put(
                    "filters",
                    JSONObject().apply {
                        put("disliked", "False")
                        put("trashed", "False")
                        put("fromStudioProject", JSONObject().put("presence", "False"))
                    },
                )
            }
            val connection = openConnection("$API_BASE/api/feed/v3", "POST", token, deviceId)
            connection.setRequestProperty("browser-token", browserTokenHeader())
            connection.doOutput = true
            connection.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }
            val raw = readResponseOrThrow(connection)
            parseClips(JSONObject(raw))
        }

    suspend fun downloadTo(url: String, token: String, deviceId: String, destination: File): File =
        withContext(Dispatchers.IO) {
            val connection = openConnection(url, "GET", if (shouldSendAuth(url)) token else null, deviceId)
            connection.setRequestProperty("Accept", "audio/*,video/*,application/octet-stream;q=0.9,*/*;q=0.5")
            if (connection.responseCode !in 200..299) {
                throw SunoApiException("Preuzimanje nije uspelo (HTTP ${connection.responseCode}).", connection.responseCode)
            }
            destination.parentFile?.mkdirs()
            connection.inputStream.use { input ->
                destination.outputStream().use { output -> input.copyTo(output) }
            }
            destination
        }

    private fun openConnection(urlString: String, method: String, token: String?, deviceId: String): HttpURLConnection {
        val connection = URL(urlString).openConnection() as HttpURLConnection
        connection.requestMethod = method
        connection.connectTimeout = 20_000
        connection.readTimeout = 40_000
        connection.setRequestProperty("User-Agent", USER_AGENT)
        connection.setRequestProperty("Accept", "application/json, text/plain, */*")
        connection.setRequestProperty("Accept-Language", "sr-RS,sr;q=0.9,en-US;q=0.8,en;q=0.7")
        connection.setRequestProperty("Origin", "https://suno.com")
        connection.setRequestProperty("Referer", "https://suno.com/")
        connection.setRequestProperty("device-id", deviceId)
        connection.setRequestProperty("Content-Type", "application/json")
        if (!token.isNullOrBlank()) {
            connection.setRequestProperty("Authorization", "Bearer $token")
        }
        return connection
    }

    private fun readResponseOrThrow(connection: HttpURLConnection): String {
        val code = connection.responseCode
        val stream = if (code in 200..299) connection.inputStream else connection.errorStream
        val text = stream?.bufferedReader()?.use { it.readText() } ?: ""
        if (code == 401) {
            throw SunoApiException("Suno sesija više nije važeća. Poveži nalog ponovo.", 401)
        }
        if (code !in 200..299) {
            throw SunoApiException("Suno je odgovorio sa greškom (HTTP $code): ${text.take(200)}", code)
        }
        return text
    }

    /** Recursive search for an array of clip-like objects, mirroring extract_items() in suno_client.py. */
    private fun parseClips(root: JSONObject): List<SunoClip> {
        val preferredKeys = listOf(
            "project_clips", "playlist_clips", "playlist_songs", "clips", "items", "songs", "tracks", "entries", "results", "generations",
        )
        val containerKeys = listOf("data", "playlist", "feed", "library", "result", "payload")

        fun looksLikeSong(value: JSONObject): Boolean {
            if (value.has("id") && listOf("title", "audio_url", "image_url", "metadata", "created_at", "major_model_version").any(value::has)) {
                return true
            }
            for (key in listOf("clip", "song", "track")) {
                val nested = value.optJSONObject(key)
                if (nested != null && nested.has("id")) return true
            }
            val content = value.optJSONObject("content")
            return content != null && looksLikeSong(content)
        }

        fun arrayLooksLikeSongs(array: JSONArray): Boolean {
            for (i in 0 until array.length()) {
                val item = array.optJSONObject(i) ?: continue
                if (looksLikeSong(item)) return true
            }
            return array.length() == 0
        }

        fun findArray(node: Any?, depth: Int = 0, seen: MutableSet<Int> = mutableSetOf()): JSONArray? {
            if (depth > 6 || node == null) return null
            when (node) {
                is JSONArray -> {
                    if (!seen.add(System.identityHashCode(node))) return null
                    if (arrayLooksLikeSongs(node) && node.length() > 0) return node
                    for (i in 0 until node.length()) {
                        findArray(node.opt(i), depth + 1, seen)?.let { return it }
                    }
                    return null
                }
                is JSONObject -> {
                    if (!seen.add(System.identityHashCode(node))) return null
                    for (key in preferredKeys) {
                        val value = node.opt(key)
                        if (value is JSONArray && (value.length() == 0 || arrayLooksLikeSongs(value))) return value
                    }
                    for (key in containerKeys) {
                        val value = node.opt(key)
                        if (value is JSONObject || value is JSONArray) {
                            findArray(value, depth + 1, seen)?.let { return it }
                        }
                    }
                    val keys = node.keys()
                    while (keys.hasNext()) {
                        val value = node.opt(keys.next())
                        if (value is JSONObject || value is JSONArray) {
                            findArray(value, depth + 1, seen)?.let { return it }
                        }
                    }
                    return null
                }
                else -> return null
            }
        }

        fun unwrap(item: JSONObject): JSONObject {
            var current = item
            repeat(4) {
                val nested = listOf("clip", "song", "track", "content").firstNotNullOfOrNull { key -> current.optJSONObject(key) }
                current = nested ?: return current
            }
            return current
        }

        val array = findArray(root) ?: return emptyList()
        val result = mutableListOf<SunoClip>()
        val seenIds = mutableSetOf<String>()
        for (i in 0 until array.length()) {
            val raw = array.optJSONObject(i) ?: continue
            val item = unwrap(raw)
            val id = (item.optString("id", "").ifBlank { item.optString("clip_id", "") }.ifBlank { item.optString("song_id", "") })
            if (id.isBlank() || !seenIds.add(id)) continue
            result.add(
                SunoClip(
                    id = id,
                    title = item.optString("title", "Bez naslova"),
                    audioUrl = item.optString("audio_url", "").ifBlank { null },
                    imageUrl = item.optString("image_url", "").ifBlank { null },
                    status = item.optString("status", "").ifBlank { null },
                    modelVersion = item.optString("major_model_version", "").ifBlank { null },
                    createdAt = item.optString("created_at", "").ifBlank { null },
                ),
            )
        }
        return result
    }
}
