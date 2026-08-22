package com.sunopesmestudio.app

import android.app.Application
import androidx.work.Constraints
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.NetworkType
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import com.sunopesmestudio.app.data.AppDatabase
import com.sunopesmestudio.app.data.SunoAuthStore
import com.sunopesmestudio.app.data.ThemePreference
import com.sunopesmestudio.app.data.WatchedFoldersPreference
import com.sunopesmestudio.app.work.LibraryRescanWorker
import java.util.concurrent.TimeUnit

class SunoApplication : Application() {
    val database: AppDatabase by lazy { AppDatabase.get(this) }
    val themePreference: ThemePreference by lazy { ThemePreference(this) }
    val watchedFoldersPreference: WatchedFoldersPreference by lazy { WatchedFoldersPreference(this) }
    val sunoAuthStore: SunoAuthStore by lazy { SunoAuthStore(this) }

    override fun onCreate() {
        super.onCreate()
        scheduleLibraryRescan()
    }

    private fun scheduleLibraryRescan() {
        // Local SAF folder scan only -- no network involved, so the
        // section 18.4 constraint that actually applies here is
        // battery-not-low, not Wi-Fi (that condition is for the YouTube/
        // Suno sync workers this phase does not add yet).
        val constraints = Constraints.Builder()
            .setRequiredNetworkType(NetworkType.NOT_REQUIRED)
            .setRequiresBatteryNotLow(true)
            .build()
        val request = PeriodicWorkRequestBuilder<LibraryRescanWorker>(6, TimeUnit.HOURS)
            .setConstraints(constraints)
            .build()
        WorkManager.getInstance(this).enqueueUniquePeriodicWork(
            "library-rescan",
            ExistingPeriodicWorkPolicy.KEEP,
            request,
        )
    }
}
