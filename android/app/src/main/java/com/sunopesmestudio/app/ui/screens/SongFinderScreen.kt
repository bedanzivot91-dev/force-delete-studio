package com.sunopesmestudio.app.ui.screens

import android.Manifest
import android.content.pm.PackageManager
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.sunopesmestudio.app.data.RecognizedTrackEntity
import com.sunopesmestudio.app.ui.recognition.SongFinderViewModel
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

@Composable
fun SongFinderScreen(viewModel: SongFinderViewModel, modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val history by viewModel.history.collectAsState()
    val isBusy by viewModel.isBusy.collectAsState()
    val isRecording by viewModel.isRecording.collectAsState()
    val lastResult by viewModel.lastResult.collectAsState()

    val filePicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        if (uri != null) viewModel.analyzePickedFile(uri, uri.lastPathSegment ?: "isecak")
    }
    val micPermission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) viewModel.startRecording()
    }

    Column(modifier = modifier.fillMaxSize().padding(16.dp)) {
        Text("Pronalazač pesme", style = MaterialTheme.typography.headlineMedium)
        Text(
            "Lokalno poređenje sa bibliotekom preko SHA-256 otiska fajla. Internet prepoznavanje (AudD) zahteva sopstveni API token i nije uključeno u ovoj fazi.",
            style = MaterialTheme.typography.bodyLarge,
            modifier = Modifier.padding(top = 4.dp, bottom = 16.dp),
        )

        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(onClick = { filePicker.launch("audio/*") }, modifier = Modifier.fillMaxWidth(), enabled = !isBusy && !isRecording) {
                Text("Izaberi audio fajl")
            }
            Button(
                onClick = {
                    if (isRecording) {
                        viewModel.stopRecordingAndAnalyze()
                    } else {
                        val hasPermission = context.checkSelfPermission(Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED
                        if (hasPermission) viewModel.startRecording() else micPermission.launch(Manifest.permission.RECORD_AUDIO)
                    }
                },
                modifier = Modifier.fillMaxWidth(),
                enabled = !isBusy,
            ) {
                Text(if (isRecording) "Zaustavi snimanje i analiziraj" else "Snimi mikrofonom")
            }
        }

        if (isBusy) {
            Column(modifier = Modifier.padding(top = 16.dp)) { CircularProgressIndicator() }
        }

        lastResult?.let { result ->
            Card(modifier = Modifier.fillMaxWidth().padding(top = 16.dp)) {
                Column(modifier = Modifier.padding(12.dp)) {
                    Text(resultLabel(result), style = MaterialTheme.typography.titleLarge)
                    if (result.matchedTitle != null) Text("Poklapa se sa: ${result.matchedTitle}")
                }
            }
        }

        Text("Istorija", style = MaterialTheme.typography.titleLarge, modifier = Modifier.padding(top = 20.dp, bottom = 8.dp))
        LazyColumn(verticalArrangement = Arrangement.spacedBy(6.dp)) {
            items(history, key = { it.id }) { entry ->
                Card(modifier = Modifier.fillMaxWidth()) {
                    ListItem(
                        headlineContent = { Text(entry.originalFilename) },
                        supportingContent = { Text("${formatTimestamp(entry.createdAtEpochMs)} — ${resultLabel(entry)}") },
                    )
                }
            }
        }
    }
}

private fun resultLabel(entry: RecognizedTrackEntity): String = when (entry.status) {
    "local_match" -> "Pronađeno u biblioteci"
    "local_no_match" -> "Nije pronađeno u lokalnoj biblioteci"
    else -> "Greška pri analizi"
}

private fun formatTimestamp(epochMs: Long): String =
    SimpleDateFormat("dd.MM.yyyy HH:mm", Locale.getDefault()).format(Date(epochMs))
