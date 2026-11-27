pluginManagement {
    repositories { mavenCentral(); gradlePluginPortal(); google() }
    plugins {
        id("com.android.application") version "8.7.3"
        id("org.jetbrains.kotlin.android") version "2.0.21"
        id("org.jetbrains.kotlin.jvm") version "2.0.21"
        id("org.jetbrains.kotlin.plugin.compose") version "2.0.21"
        id("org.jetbrains.kotlin.plugin.serialization") version "2.0.21"
    }
}
dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories { mavenCentral(); google() }
}
rootProject.name = "kachalka-tv"
include(":core")
// The Android module needs the Android SDK and Google's Maven. CI has both; a plain JDK machine can still build and test :core.
if (System.getenv("CI") != null || providers.gradleProperty("withApp").isPresent) include(":app")
