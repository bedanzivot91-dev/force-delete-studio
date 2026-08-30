package com.sunopesmestudio.app.ui.screens

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.sunopesmestudio.app.R
import com.sunopesmestudio.app.ui.library.LibraryViewModel

@Composable
fun HomeScreen(viewModel: LibraryViewModel, modifier: Modifier = Modifier) {
    val count by viewModel.songCount.collectAsState()
    Column(modifier = modifier.fillMaxSize().padding(20.dp)) {
        Text(stringResource(R.string.home_greeting), style = MaterialTheme.typography.headlineMedium)
        Card(modifier = Modifier.fillMaxWidth().padding(top = 20.dp)) {
            Column(modifier = Modifier.padding(18.dp)) {
                Text("$count", style = MaterialTheme.typography.headlineMedium)
                Text(stringResource(R.string.nav_library), style = MaterialTheme.typography.bodyLarge)
            }
        }
    }
}
