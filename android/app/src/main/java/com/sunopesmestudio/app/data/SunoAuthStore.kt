package com.sunopesmestudio.app.data

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey

private const val TOKEN_KEY = "suno_bearer_token"
private const val DEVICE_ID_KEY = "suno_device_id"

/**
 * Android Keystore-backed storage for the Suno session bearer token (a
 * short-lived Clerk JWT captured from the logged-in WebView, mirroring
 * app/cdp.py's refresh_session_activity() on the desktop side). Stored in
 * secure_prefs.xml, which backup_rules.xml/data_extraction_rules.xml
 * already exclude from Auto Backup and device transfer -- this file
 * predates this feature (added for exactly this purpose per the comment
 * in build.gradle.kts) but was unused until now.
 */
class SunoAuthStore(context: Context) {
    private val prefs: SharedPreferences by lazy {
        val masterKey = MasterKey.Builder(context)
            .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
            .build()
        EncryptedSharedPreferences.create(
            context,
            "secure_prefs.xml",
            masterKey,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM,
        )
    }

    var token: String?
        get() = prefs.getString(TOKEN_KEY, null)
        set(value) {
            prefs.edit().apply {
                if (value.isNullOrBlank()) remove(TOKEN_KEY) else putString(TOKEN_KEY, value)
            }.apply()
        }

    val isConnected: Boolean get() = !token.isNullOrBlank()

    /** Stable per-install device id sent as the `device-id` header, matching SunoClient's convention. */
    val deviceId: String
        get() = prefs.getString(DEVICE_ID_KEY, null) ?: java.util.UUID.randomUUID().toString().also {
            prefs.edit().putString(DEVICE_ID_KEY, it).apply()
        }

    fun clear() {
        prefs.edit().remove(TOKEN_KEY).apply()
    }
}
