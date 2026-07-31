package com.sunopesmestudio.app.ui.nav

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.LibraryMusic
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.Icon
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.navigation.NavDestination.Companion.hierarchy
import androidx.navigation.NavGraph.Companion.findStartDestination
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import com.sunopesmestudio.app.R
import com.sunopesmestudio.app.data.ThemePreference
import com.sunopesmestudio.app.data.WatchedFoldersPreference
import com.sunopesmestudio.app.ui.library.LibraryViewModel
import com.sunopesmestudio.app.ui.recognition.SongFinderViewModel
import com.sunopesmestudio.app.ui.screens.HomeScreen
import com.sunopesmestudio.app.ui.screens.LibraryScreen
import com.sunopesmestudio.app.ui.screens.SettingsScreen
import com.sunopesmestudio.app.ui.screens.SongFinderScreen

private enum class Destination(val route: String, val labelRes: Int, val icon: androidx.compose.ui.graphics.vector.ImageVector) {
    HOME("home", R.string.nav_home, Icons.Filled.Home),
    LIBRARY("library", R.string.nav_library, Icons.Filled.LibraryMusic),
    FINDER("finder", R.string.nav_finder, Icons.Filled.Search),
    SETTINGS("settings", R.string.nav_settings, Icons.Filled.Settings),
}

@Composable
fun SunoNavHost(
    libraryViewModel: LibraryViewModel,
    songFinderViewModel: SongFinderViewModel,
    themePreference: ThemePreference,
    watchedFoldersPreference: WatchedFoldersPreference,
) {
    val navController = rememberNavController()
    Scaffold(
        bottomBar = {
            NavigationBar {
                val backStackEntry by navController.currentBackStackEntryAsState()
                val currentDestination = backStackEntry?.destination
                Destination.entries.forEach { destination ->
                    NavigationBarItem(
                        selected = currentDestination?.hierarchy?.any { it.route == destination.route } == true,
                        onClick = {
                            navController.navigate(destination.route) {
                                popUpTo(navController.graph.findStartDestination().id) { saveState = true }
                                launchSingleTop = true
                                restoreState = true
                            }
                        },
                        icon = { Icon(destination.icon, contentDescription = null) },
                        label = { Text(stringResource(destination.labelRes)) },
                    )
                }
            }
        },
    ) { padding ->
        NavHost(
            navController = navController,
            startDestination = Destination.HOME.route,
            modifier = Modifier.padding(padding),
        ) {
            composable(Destination.HOME.route) { HomeScreen(libraryViewModel) }
            composable(Destination.LIBRARY.route) { LibraryScreen(libraryViewModel, watchedFoldersPreference) }
            composable(Destination.FINDER.route) { SongFinderScreen(songFinderViewModel) }
            composable(Destination.SETTINGS.route) { SettingsScreen(themePreference) }
        }
    }
}
