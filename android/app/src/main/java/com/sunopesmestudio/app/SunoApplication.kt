package com.sunopesmestudio.app

import android.app.Application
import com.sunopesmestudio.app.data.AppDatabase
import com.sunopesmestudio.app.data.ThemePreference

class SunoApplication : Application() {
    val database: AppDatabase by lazy { AppDatabase.get(this) }
    val themePreference: ThemePreference by lazy { ThemePreference(this) }
}
