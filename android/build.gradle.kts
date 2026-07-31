// Root build file. Plugin versions are declared here (not applied) and
// applied per-module in app/build.gradle.kts.
plugins {
    id("com.android.application") version "8.7.3" apply false
    id("org.jetbrains.kotlin.android") version "2.1.0" apply false
    // Since Kotlin 2.0, the Compose compiler is a separate Gradle plugin,
    // no longer configured via android.composeOptions.kotlinCompilerExtensionVersion
    // (confirmed by a real CI failure on this exact point: "Starting in
    // Kotlin 2.0, the Compose Compiler Gradle plugin is required when
    // compose is enabled" -- android-build.yml run 30594432082).
    id("org.jetbrains.kotlin.plugin.compose") version "2.1.0" apply false
    id("com.google.devtools.ksp") version "2.1.0-1.0.29" apply false
}
