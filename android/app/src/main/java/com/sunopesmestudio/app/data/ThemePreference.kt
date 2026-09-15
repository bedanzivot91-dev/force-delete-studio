package com.sunopesmestudio.app.data

import android.content.Context
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import com.sunopesmestudio.app.ui.theme.AppTheme
import kotlinx.coroutines.flow.map

private val Context.themeDataStore by preferencesDataStore(name = "theme_prefs")
private val THEME_KEY = stringPreferencesKey("selected_theme")

/**
 * Desktop and Android intentionally keep separate theme selections
 * (spec section 5.6: "različita tema na Windows i Android uređaju") --
 * this DataStore entry is local to this device only, never synced.
 */
class ThemePreference(private val context: Context) {
    val theme = context.themeDataStore.data.map { prefs ->
        AppTheme.entries.firstOrNull { it.id == prefs[THEME_KEY] } ?: AppTheme.ORIGINAL
    }

    suspend fun setTheme(theme: AppTheme) {
        context.themeDataStore.edit { it[THEME_KEY] = theme.id }
    }
}
