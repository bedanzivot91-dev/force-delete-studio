package com.sunopesmestudio.app.data

import android.content.Context
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringSetPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map

private val Context.watchedFoldersDataStore by preferencesDataStore(name = "watched_folders")
private val FOLDERS_KEY = stringSetPreferencesKey("folder_uris")

/**
 * Persisted Storage Access Framework tree URIs the user has granted for
 * library auto-import (section 18.1/18.4). Only the URI strings are stored
 * here; the actual read permission is granted separately via
 * ContentResolver.takePersistableUriPermission at the point the folder is
 * picked (see LibraryScreen's "Dodaj folder" button).
 */
class WatchedFoldersPreference(private val context: Context) {
    val folders = context.watchedFoldersDataStore.data.map { it[FOLDERS_KEY] ?: emptySet() }

    suspend fun addFolder(uri: String) {
        context.watchedFoldersDataStore.edit { prefs ->
            prefs[FOLDERS_KEY] = (prefs[FOLDERS_KEY] ?: emptySet()) + uri
        }
    }

    suspend fun removeFolder(uri: String) {
        context.watchedFoldersDataStore.edit { prefs ->
            prefs[FOLDERS_KEY] = (prefs[FOLDERS_KEY] ?: emptySet()) - uri
        }
    }

    suspend fun snapshot(): Set<String> = folders.first()
}
