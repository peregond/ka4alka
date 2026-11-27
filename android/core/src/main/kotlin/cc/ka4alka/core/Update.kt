package cc.ka4alka.core

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import java.io.File
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.security.KeyFactory
import java.security.MessageDigest
import java.security.Signature
import java.security.spec.X509EncodedKeySpec
import java.util.Base64

@Serializable
data class ApkPackage(val url: String = "", val fileName: String = "", val size: Long = 0, val sha256: String = "")

/** The signed description of the newest Android TV build, published next to the APK. */
@Serializable
data class AndroidManifest(
    val schemaVersion: Int = 0,
    val appId: String = "",
    val channel: String = "",
    val platform: String = "",
    val version: String = "",
    val versionCode: Long = 0,
    val minSdk: Int = 0,
    val apk: ApkPackage = ApkPackage(),
    val releaseNotesUrl: String = "",
    val notes: String = "",
) {
    fun validate() {
        require(schemaVersion == 1 && appId == "kachalka" && channel == "stable" && platform == "android-tv") { "Not an Android TV update manifest" }
        require(Regex("\\d+\\.\\d+\\.\\d+").matches(version)) { "Bad version" }
        require(versionCode in 1..2_000_000_000L) { "Bad version code" }
        require(minSdk in 21..40) { "Bad minimum SDK" }
        val base = "https://github.com/peregond/ka4alka/releases/download/android-tv/"
        require(apk.fileName == "Kachalka-AndroidTV-$version.apk") { "Unexpected package name" }
        require(apk.url == base + apk.fileName) { "Unexpected package address" }
        require(apk.size in 1..MAX_APK_BYTES) { "Bad package size" }
        require(Regex("[a-f0-9]{64}").matches(apk.sha256)) { "Bad package checksum" }
        require(releaseNotesUrl == "https://github.com/peregond/ka4alka/releases/tag/android-tv") { "Bad notes address" }
        require(notes.length <= 4000) { "Notes are too long" }
    }

    companion object {
        const val MAX_APK_BYTES = 200L * 1024 * 1024
        const val BASE = "https://github.com/peregond/ka4alka/releases/download/android-tv/"
        const val MANIFEST_URL = BASE + "android-latest.json"
        const val SIGNATURE_URL = BASE + "android-latest.sig"
        private val json = Json { ignoreUnknownKeys = true }

        /** Checks the RSA/SHA-256 signature made with the project's update key, then the contents. */
        fun verify(manifest: ByteArray, signature: ByteArray, publicKeyPem: String): AndroidManifest {
            require(manifest.size <= 64 * 1024 && signature.size <= 1024) { "Invalid update envelope" }
            val key = KeyFactory.getInstance("RSA").generatePublic(X509EncodedKeySpec(pemBody(publicKeyPem)))
            val check = Signature.getInstance("SHA256withRSA").apply { initVerify(key); update(manifest) }
            require(check.verify(signature)) { "Invalid update signature" }
            return json.decodeFromString<AndroidManifest>(manifest.decodeToString()).also { it.validate() }
        }

        private fun pemBody(pem: String): ByteArray =
            Base64.getMimeDecoder().decode(pem.lineSequence().filter { !it.startsWith("-----") }.joinToString(""))
    }
}

/** What the settings screen shows after asking for updates. */
sealed interface UpdateCheck {
    data object UpToDate : UpdateCheck
    data class Available(val manifest: AndroidManifest) : UpdateCheck
    data class Failed(val reason: String) : UpdateCheck
}

class Updater(private val http: Http, private val publicKeyPem: String, private val installedVersionCode: Long) {
    fun check(): UpdateCheck = try {
        val manifest = AndroidManifest.verify(
            http.get(AndroidManifest.MANIFEST_URL, 64 * 1024),
            http.get(AndroidManifest.SIGNATURE_URL, 1024),
            publicKeyPem,
        )
        if (manifest.versionCode > installedVersionCode) UpdateCheck.Available(manifest) else UpdateCheck.UpToDate
    } catch (e: Exception) {
        UpdateCheck.Failed(e.message ?: e.javaClass.simpleName)
    }

    /**
     * Fetches the APK into [target], resuming a partial file, and checks size and SHA-256 before it is handed to the installer.
     * [progress] receives (bytes so far, total).
     */
    fun download(manifest: AndroidManifest, target: File, progress: (Long, Long) -> Unit = { _, _ -> }, cancelled: () -> Boolean = { false }): File {
        val part = File(target.path + ".part")
        target.parentFile?.mkdirs()
        var have = if (part.exists()) part.length() else 0L
        if (have > manifest.apk.size) { part.delete(); have = 0 }
        if (have < manifest.apk.size) {
            var address = manifest.apk.url
            var connection: HttpURLConnection
            var redirects = 0
            while (true) {
                require(address.startsWith("https://")) { "Only HTTPS downloads are allowed" }
                connection = URL(address).openConnection() as HttpURLConnection
                connection.instanceFollowRedirects = false
                connection.connectTimeout = 20_000; connection.readTimeout = 30_000
                if (have > 0) connection.setRequestProperty("Range", "bytes=$have-")
                val code = connection.responseCode
                if (code in 300..399 && redirects++ < 5) { address = URL(URL(address), connection.getHeaderField("Location")).toString(); connection.disconnect(); continue }
                if (have > 0 && code == 200) { have = 0; part.delete() } // the server ignored the range: start again
                else if (code != 200 && code != 206) { connection.disconnect(); throw IOException("HTTP $code") }
                break
            }
            try {
                connection.inputStream.use { input ->
                    java.io.FileOutputStream(part, have > 0).use { out ->
                        val buffer = ByteArray(64 * 1024)
                        var total = have
                        while (true) {
                            if (cancelled()) throw IOException("Cancelled")
                            val n = input.read(buffer)
                            if (n < 0) break
                            if (total + n > manifest.apk.size) throw IOException("The package is larger than announced")
                            out.write(buffer, 0, n); total += n
                            progress(total, manifest.apk.size)
                        }
                    }
                }
            } finally { connection.disconnect() }
        }
        if (part.length() != manifest.apk.size) throw IOException("The download is incomplete")
        if (fileSha256(part) != manifest.apk.sha256) { part.delete(); throw IOException("The package checksum does not match") }
        target.delete()
        if (!part.renameTo(target)) throw IOException("Cannot save the package")
        return target
    }

    companion object {
        fun fileSha256(file: File): String {
            val digest = MessageDigest.getInstance("SHA-256")
            file.inputStream().use { input -> val b = ByteArray(64 * 1024); while (true) { val n = input.read(b); if (n < 0) break; digest.update(b, 0, n) } }
            return digest.digest().joinToString("") { "%02x".format(it) }
        }

        /** versionCode for x.y.z: 0.45.3 → 45003. */
        fun versionCode(version: String): Long {
            val (a, b, c) = version.split('.').map { it.toLong() }
            require(b in 0..999 && c in 0..999)
            return a * 1_000_000 + b * 1_000 + c
        }
    }
}
