package com.sunopesmestudio.app.ui.screens

import android.content.Intent
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Favorite
import androidx.compose.material.icons.filled.FavoriteBorder
import androidx.compose.material.icons.filled.LibraryMusic
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkManager
import com.sunopesmestudio.app.R
import com.sunopesmestudio.app.data.SongEntity
import com.sunopesmestudio.app.data.WatchedFoldersPreference
import com.sunopesmestudio.app.ui.library.LibraryViewModel
import com.sunopesmestudio.app.work.LibraryRescanWorker
import kotlinx.coroutines.launch

@Composable
fun LibraryScreen(viewModel: LibraryViewModel, watchedFoldersPreference: WatchedFoldersPreference, modifier: Modifier = Modifier) {
    val songs by viewModel.songs.collectAsState()
    val query by viewModel.searchQuery.collectAsState()
    val context = LocalContext.current
    val scope = rememberCoroutineScope()

    val folderPicker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri ->
        if (uri != null) {
            context.contentResolver.takePersistableUriPermission(
                uri,
                Intent.FLAG_GRANT_READ_URI_PERMISSION,
            )
            scope.launch {
                watchedFoldersPreference.addFolder(uri.toString())
                WorkManager.getInstance(context).enqueue(OneTimeWorkRequestBuilder<LibraryRescanWorker>().build())
            }
        }
    }

    Column(modifier = modifier.fillMaxSize().padding(16.dp)) {
        OutlinedTextField(
            value = query,
            onValueChange = viewModel::setQuery,
            label = { Text(stringResourceCompat(R.string.nav_library)) },
            modifier = Modifier.fillMaxWidth().padding(bottom = 8.dp),
            singleLine = true,
        )
        Button(onClick = { folderPicker.launch(null) }, modifier = Modifier.fillMaxWidth().padding(bottom = 12.dp)) {
            Text("Dodaj folder za praćenje")
        }
        if (songs.isEmpty()) {
            LibraryEmptyState()
        } else {
            LazyColumn(
                verticalArrangement = Arrangement.spacedBy(8.dp),
                contentPadding = PaddingValues(bottom = 24.dp),
            ) {
                items(songs, key = { it.id }) { song ->
                    SongRow(song = song, onToggleFavorite = { viewModel.toggleFavorite(song) })
                }
            }
        }
    }
}

@Composable
private fun SongRow(song: SongEntity, onToggleFavorite: () -> Unit) {
    Card(modifier = Modifier.fillMaxWidth()) {
        ListItem(
            headlineContent = { Text(song.title.ifBlank { song.displayName }) },
            supportingContent = { Text(song.genre.ifBlank { formatDuration(song.durationSeconds) }) },
            trailingContent = {
                IconButton(onClick = onToggleFavorite) {
                    Icon(
                        imageVector = if (song.isFavorite) Icons.Filled.Favorite else Icons.Filled.FavoriteBorder,
                        contentDescription = null,
                        tint = if (song.isFavorite) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            },
        )
    }
}

@Composable
private fun LibraryEmptyState() {
    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column {
            Icon(Icons.Filled.LibraryMusic, contentDescription = null, modifier = Modifier.padding(bottom = 8.dp))
            Text(stringResourceCompat(R.string.library_empty_title), style = MaterialTheme.typography.titleLarge)
            Text(stringResourceCompat(R.string.library_empty_body), style = MaterialTheme.typography.bodyLarge)
        }
    }
}

private fun formatDuration(seconds: Double): String {
    val total = seconds.toInt()
    return "%d:%02d".format(total / 60, total % 60)
}

@Composable
private fun stringResourceCompat(id: Int) = androidx.compose.ui.res.stringResource(id)
