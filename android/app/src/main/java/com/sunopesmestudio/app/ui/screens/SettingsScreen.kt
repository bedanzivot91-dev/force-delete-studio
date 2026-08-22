package com.sunopesmestudio.app.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.sunopesmestudio.app.R
import com.sunopesmestudio.app.data.ThemePreference
import com.sunopesmestudio.app.ui.theme.AppTheme
import kotlinx.coroutines.launch

/**
 * Theme picker for this device only -- section 5.6's "razlicita tema na
 * Windows i Android uredaju" -- the choice made here is never sent to the
 * desktop app or synced, it lives purely in this device's ThemePreference
 * DataStore.
 */
@Composable
fun SettingsScreen(themePreference: ThemePreference, modifier: Modifier = Modifier) {
    val selected by themePreference.theme.collectAsState(initial = AppTheme.ORIGINAL)
    val scope = rememberCoroutineScope()

    Column(modifier = modifier.fillMaxSize().padding(16.dp)) {
        Text(stringResource(R.string.nav_settings), style = MaterialTheme.typography.headlineMedium)
        Text(
            "Tema važi samo za ovaj uređaj; ne utiče na Windows aplikaciju.",
            style = MaterialTheme.typography.bodyLarge,
            modifier = Modifier.padding(top = 4.dp, bottom = 16.dp),
        )
        LazyColumn(verticalArrangement = Arrangement.spacedBy(8.dp)) {
            items(AppTheme.entries) { theme ->
                Card(modifier = Modifier.fillMaxWidth()) {
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(12.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        RadioButton(
                            selected = theme == selected,
                            onClick = { scope.launch { themePreference.setTheme(theme) } },
                        )
                        Text(theme.label, modifier = Modifier.padding(start = 8.dp))
                    }
                }
            }
        }
    }
}
