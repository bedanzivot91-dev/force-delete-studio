// Root build file. Plugin versions are declared here (not applied) and
// applied per-module in app/build.gradle.kts.
plugins {
    // API 36 (Android 16) requires a newer AGP than the previous 8.7.3.
    // AGP 8.10 supports API 36 and requires Gradle >= 8.11.1; this project
    // already uses Gradle 8.14.3 and JDK 17.
    id("com.android.application") version "8.10.1" apply false
    id("org.jetbrains.kotlin.android") version "2.1.0" apply false
    // Since Kotlin 2.0, the Compose compiler is a separate Gradle plugin,
    // no longer configured via android.composeOptions.kotlinCompilerExtensionVersion
    // (confirmed by a real CI failure on this exact point: "Starting in
    // Kotlin 2.0, the Compose Compiler Gradle plugin is required when
    // compose is enabled" -- android-build.yml run 30594432082).
    id("org.jetbrains.kotlin.plugin.compose") version "2.1.0" apply false
    id("com.google.devtools.ksp") version "2.1.0-1.0.29" apply false
}
