plugins {
    id("org.jetbrains.kotlin.jvm")
    id("org.jetbrains.kotlin.plugin.serialization")
}
java { sourceCompatibility = JavaVersion.VERSION_17; targetCompatibility = JavaVersion.VERSION_17 }
kotlin { compilerOptions { jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17) } }

val hostNative by configurations.creating
dependencies {
    implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.7.3")
    implementation("org.libtorrent4j:libtorrent4j:2.1.0-39")
    testImplementation(kotlin("test"))
    hostNative("org.libtorrent4j:libtorrent4j-linux:2.1.0-39")
}

// The JVM tests run the real torrent engine, so the Linux build of libtorrent is unpacked for System.loadLibrary.
val unpackHostNative by tasks.registering(Copy::class) {
    from(provider { hostNative.files.map { zipTree(it) } }) { include("lib/x86_64/*.so"); eachFile { path = name } }
    includeEmptyDirs = false
    into(layout.buildDirectory.dir("host-native"))
}
// The native libtorrent runs in its own JVM: sharing one with the other tests crashes the JVM at exit on some hosts.
val nativeProperties: org.gradle.api.tasks.testing.Test.() -> Unit = {
    useJUnitPlatform()
    dependsOn(unpackHostNative)
    systemProperty("java.library.path", layout.buildDirectory.dir("host-native").get().asFile.absolutePath)
    testLogging { events("failed"); exceptionFormat = org.gradle.api.tasks.testing.logging.TestExceptionFormat.FULL }
}
tasks.test { nativeProperties(); exclude("**/TorrentStreamingTest*") }
val torrentTest by tasks.registering(org.gradle.api.tasks.testing.Test::class) {
    nativeProperties()
    testClassesDirs = sourceSets.test.get().output.classesDirs
    classpath = sourceSets.test.get().runtimeClasspath
    include("**/TorrentStreamingTest*")
}
tasks.check { dependsOn(torrentTest) }
