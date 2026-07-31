package com.sunopesmestudio.app

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Surface
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.lifecycle.viewmodel.compose.viewModel
import com.sunopesmestudio.app.ui.library.LibraryViewModel
import com.sunopesmestudio.app.ui.nav.SunoNavHost
import com.sunopesmestudio.app.ui.recognition.SongFinderViewModel
import com.sunopesmestudio.app.ui.theme.AppTheme
import com.sunopesmestudio.app.ui.theme.SunoPesmeStudioTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()

        val app = application as SunoApplication
        setContent {
            val selectedTheme by app.themePreference.theme.collectAsState(initial = AppTheme.ORIGINAL)
            SunoPesmeStudioTheme(appTheme = selectedTheme) {
                Surface(modifier = Modifier.fillMaxSize()) {
                    val libraryViewModel: LibraryViewModel = viewModel(
                        factory = LibraryViewModel.Factory(app.database.songDao()),
                    )
                    val songFinderViewModel: SongFinderViewModel = viewModel(
                        factory = SongFinderViewModel.Factory(app, app.database.songDao(), app.database.recognizedTrackDao()),
                    )
                    SunoNavHost(
                        libraryViewModel = libraryViewModel,
                        songFinderViewModel = songFinderViewModel,
                        themePreference = app.themePreference,
                        watchedFoldersPreference = app.watchedFoldersPreference,
                    )
                }
            }
        }
    }
}
