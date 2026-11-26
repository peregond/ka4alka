plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
    id("org.jetbrains.kotlin.plugin.serialization")
}

val appVersion = (project.findProperty("appVersion") as String?) ?: "0.45.3"
val versionParts = appVersion.split('.').map { it.toInt() }
require(versionParts.size == 3) { "appVersion must be x.y.z" }

android {
    namespace = "cc.ka4alka.tv"
    compileSdk = 35
    defaultConfig {
        applicationId = "cc.ka4alka.tv"
        minSdk = 23
        targetSdk = 34
        // 0.45.3 -> 45003: the same rule the update manifest uses (Updater.versionCode in :core).
        versionCode = versionParts[0] * 1_000_000 + versionParts[1] * 1_000 + versionParts[2]
        versionName = appVersion
        ndk { abiFilters += listOf("arm64-v8a", "armeabi-v7a", "x86_64") }
    }
    buildTypes {
        release { isMinifyEnabled = false }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
    buildFeatures { compose = true; buildConfig = true }
    packaging {
        jniLibs { useLegacyPackaging = true } // the native torrent library is loaded from the extracted files
        resources.excludes += listOf("/META-INF/{AL2.0,LGPL2.1}", "META-INF/INDEX.LIST", "META-INF/io.netty.versions.properties")
    }
    sourceSets["main"].res.srcDir(layout.buildDirectory.dir("generated/update-key"))
    lint { abortOnError = false; checkReleaseBuilds = false }
}

// The updater verifies new builds with the project's update key; the same PEM the desktop apps embed.
val copyUpdateKey by tasks.registering(Copy::class) {
    from(rootProject.file("../update-core/update-public.pem")) { rename { "update_public.pem" } }
    into(layout.buildDirectory.dir("generated/update-key/raw"))
}
tasks.named("preBuild") { dependsOn(copyUpdateKey) }

dependencies {
    implementation(project(":core"))
    val composeBom = platform("androidx.compose:compose-bom:2024.12.01")
    implementation(composeBom)
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")
    implementation("androidx.activity:activity-compose:1.9.3")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.8.7")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.8.7")
    implementation("androidx.core:core-ktx:1.15.0")
    implementation("io.coil-kt:coil-compose:2.7.0")
    implementation("androidx.media3:media3-exoplayer:1.5.1")
    implementation("androidx.media3:media3-ui:1.5.1")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.9.0")
    implementation("org.libtorrent4j:libtorrent4j-android-arm64:2.1.0-39")
    implementation("org.libtorrent4j:libtorrent4j-android-arm:2.1.0-39")
    implementation("org.libtorrent4j:libtorrent4j-android-x86_64:2.1.0-39")
    debugImplementation("androidx.compose.ui:ui-tooling")
    testImplementation(kotlin("test"))
}
