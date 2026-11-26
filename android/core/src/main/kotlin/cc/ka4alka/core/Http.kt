package cc.ka4alka.core

import java.io.ByteArrayOutputStream
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL

/** Blocking GET with a hard size limit. Implemented over HttpURLConnection so it runs on the JVM and on Android alike. */
fun interface Http {
    fun get(url: String, maxBytes: Int): ByteArray
}

class JavaHttp(private val userAgent: String = "Kachalka-TV/1", private val timeoutMs: Int = 20_000) : Http {
    override fun get(url: String, maxBytes: Int): ByteArray {
        var address = url
        repeat(5) {
            require(address.startsWith("https://") || address.startsWith("http://127.0.0.1")) { "Only HTTPS addresses are allowed" }
            val connection = URL(address).openConnection() as HttpURLConnection
            connection.connectTimeout = timeoutMs
            connection.readTimeout = timeoutMs
            connection.instanceFollowRedirects = false
            connection.setRequestProperty("User-Agent", userAgent)
            connection.setRequestProperty("Accept", "application/json, */*")
            try {
                val code = connection.responseCode
                if (code in 300..399) {
                    val next = connection.getHeaderField("Location") ?: throw IOException("Redirect without a target")
                    address = URL(URL(address), next).toString()
                    return@repeat
                }
                if (code !in 200..299) throw IOException("HTTP $code")
                if (connection.contentLengthLong > maxBytes) throw IOException("Response is too large")
                connection.inputStream.use { input ->
                    val out = ByteArrayOutputStream()
                    val buffer = ByteArray(16 * 1024)
                    while (true) {
                        val n = input.read(buffer)
                        if (n < 0) break
                        if (out.size() + n > maxBytes) throw IOException("Response is too large")
                        out.write(buffer, 0, n)
                    }
                    return out.toByteArray()
                }
            } finally {
                connection.disconnect()
            }
        }
        throw IOException("Too many redirects")
    }
}
