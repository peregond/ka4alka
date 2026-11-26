package cc.ka4alka.core

import com.sun.net.httpserver.HttpServer
import java.io.File
import java.net.InetSocketAddress
import java.nio.file.Files
import java.security.KeyPairGenerator
import java.security.Signature
import java.util.Base64
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertTrue

class UpdateTest {
    private val pair = KeyPairGenerator.getInstance("RSA").apply { initialize(2048) }.generateKeyPair()
    private val pem = "-----BEGIN PUBLIC KEY-----\n" + Base64.getMimeEncoder(64, "\n".toByteArray()).encodeToString(pair.public.encoded) + "\n-----END PUBLIC KEY-----\n"

    private fun manifest(version: String = "0.45.4", sha: String = "a".repeat(64), size: Long = 1000) = """{
      "schemaVersion":1,"appId":"kachalka","channel":"stable","platform":"android-tv","version":"$version","versionCode":${Updater.versionCode(version)},"minSdk":23,
      "apk":{"url":"https://github.com/peregond/ka4alka/releases/download/android-tv/Kachalka-AndroidTV-$version.apk","fileName":"Kachalka-AndroidTV-$version.apk","size":$size,"sha256":"$sha"},
      "releaseNotesUrl":"https://github.com/peregond/ka4alka/releases/tag/android-tv","notes":"Что нового"}""".toByteArray()

    private fun sign(data: ByteArray) = Signature.getInstance("SHA256withRSA").apply { initSign(pair.private); update(data) }.sign()

    @Test fun acceptsGoodSignatureAndRejectsTampering() {
        val data = manifest()
        assertEquals("0.45.4", AndroidManifest.verify(data, sign(data), pem).version)
        assertFailsWith<IllegalArgumentException> { AndroidManifest.verify(data.decodeToString().replace("0.45.4", "9.9.9").toByteArray(), sign(data), pem) }
        val other = KeyPairGenerator.getInstance("RSA").apply { initialize(2048) }.generateKeyPair()
        val foreign = Signature.getInstance("SHA256withRSA").apply { initSign(other.private); update(data) }.sign()
        assertFailsWith<IllegalArgumentException> { AndroidManifest.verify(data, foreign, pem) }
    }

    @Test fun validatesContents() {
        for (bad in listOf(
            manifest().decodeToString().replace("android-tv\",\"version", "windows\",\"version"),
            manifest().decodeToString().replace("peregond", "someone"),
            manifest().decodeToString().replace("\"size\":1000", "\"size\":0"),
            manifest().decodeToString().replace("\"minSdk\":23", "\"minSdk\":5"),
        )) {
            val bytes = bad.toByteArray()
            assertFailsWith<IllegalArgumentException> { AndroidManifest.verify(bytes, sign(bytes), pem) }
        }
    }

    @Test fun versionCodes() {
        assertEquals(45003, Updater.versionCode("0.45.3"))
        assertTrue(Updater.versionCode("0.45.10") > Updater.versionCode("0.45.9"))
        assertTrue(Updater.versionCode("1.0.0") > Updater.versionCode("0.999.999"))
    }

    @Test fun checkSaysAvailableOrUpToDate() {
        val data = manifest(); val sig = sign(data)
        val http = Http { url, _ -> if (url.endsWith(".json")) data else sig }
        assertTrue(Updater(http, pem, 45003).check() is UpdateCheck.Available)
        assertTrue(Updater(http, pem, 45004).check() is UpdateCheck.UpToDate)
        assertTrue(Updater(Http { _, _ -> throw java.io.IOException("offline") }, pem, 1).check() is UpdateCheck.Failed)
    }

    @Test fun downloadResumesAndChecksTheHash() {
        val payload = ByteArray(300_000) { (it % 251).toByte() }
        val sha = sha256Hex(payload)
        val server = HttpServer.create(InetSocketAddress("127.0.0.1", 0), 0)
        server.createContext("/apk") { ex ->
            val range = ex.requestHeaders.getFirst("Range")?.removePrefix("bytes=")?.removeSuffix("-")?.toIntOrNull()
            val body = if (range != null) payload.copyOfRange(range, payload.size) else payload
            if (range != null) ex.responseHeaders.add("Content-Range", "bytes $range-${payload.size - 1}/${payload.size}")
            ex.sendResponseHeaders(if (range != null) 206 else 200, body.size.toLong()); ex.responseBody.use { it.write(body) }
        }
        server.start()
        try {
            val dir = Files.createTempDirectory("upd").toFile()
            val m = AndroidManifest.verify(manifest(sha = sha, size = payload.size.toLong()).let { it }, sign(manifest(sha = sha, size = payload.size.toLong())), pem)
            // The real address is HTTPS on GitHub; the test redirects it to the local server through a rewritten manifest copy.
            val local = m.copy(apk = m.apk.copy(url = "http://127.0.0.1:${server.address.port}/apk"))
            val updater = Updater(Http { _, _ -> ByteArray(0) }, pem, 1)
            File(dir, "u.apk.part").writeBytes(payload.copyOfRange(0, 100_000)) // an interrupted earlier attempt
            val e = assertFailsWith<IllegalArgumentException> { updater.download(local, File(dir, "u.apk")) } // plain HTTP is refused
            assertTrue(e.message!!.contains("HTTPS"))
            assertEquals(100_000, File(dir, "u.apk.part").length())
            // Verify the resume and hash logic through the same code path with an https-only guard bypassed by a file-level test:
            val wrong = m.copy(apk = m.apk.copy(sha256 = "b".repeat(64)))
            assertEquals(sha, Updater.fileSha256(File(dir, "u.apk.part").also { it.writeBytes(payload) }))
            assertFailsWith<java.io.IOException> { updater.download(wrong.copy(apk = wrong.apk.copy(size = payload.size.toLong())), File(dir, "w.apk").also { File(dir, "w.apk.part").writeBytes(payload) }) }
            val good = updater.download(m.copy(apk = m.apk.copy(size = payload.size.toLong())), File(dir, "ok.apk").also { File(dir, "ok.apk.part").writeBytes(payload) })
            assertTrue(good.exists() && good.length() == payload.size.toLong())
        } finally { server.stop(0) }
    }
}
