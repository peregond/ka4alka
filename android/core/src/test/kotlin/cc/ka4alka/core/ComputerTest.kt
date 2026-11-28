package cc.ka4alka.core

import java.net.InetAddress
import java.net.ServerSocket
import kotlin.concurrent.thread
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertNull
import kotlin.test.assertTrue

class ComputerTest {
    /** A stand-in for the desktop's phone access: same framing, same checks (Host must be address:port, Bearer token after /pair). */
    private class FakeDesktop : AutoCloseable {
        val server = ServerSocket(0, 8, InetAddress.getByName("127.0.0.1"))
        val port get() = server.localPort
        val added = mutableListOf<String>()
        @Volatile var sessionToken = "T0KEN"
        val movie = ByteArray(700_000) { (it * 7 + it / 13).toByte() }
        @Volatile var lastPairBody = ""
        init {
            thread(isDaemon = true) {
                while (!server.isClosed) {
                    val s = try { server.accept() } catch (e: Exception) { break }
                    thread(isDaemon = true) { s.use { handle(it) } }
                }
            }
        }
        private fun handle(s: java.net.Socket) {
            val input = s.getInputStream()
            val sb = StringBuilder()
            while (!sb.endsWith("\r\n\r\n")) { val b = input.read(); if (b < 0) return; sb.append(b.toChar()) }
            val lines = sb.toString().trimEnd().split("\r\n")
            val (method, path) = lines[0].split(' ')
            val headers = lines.drop(1).associate { it.substringBefore(':').lowercase() to it.substringAfter(':').trim() }
            val body = headers["content-length"]?.toIntOrNull()?.let { n -> String(ByteArray(n).also { input.readNBytes(it, 0, n) }) } ?: ""
            fun reply(code: Int, text: String, type: String = "application/json") {
                val bytes = text.toByteArray()
                s.getOutputStream().write("HTTP/1.1 $code R\r\nContent-Type: $type\r\nContent-Length: ${bytes.size}\r\nConnection: close\r\n\r\n".toByteArray() + bytes)
            }
            if (headers["host"] != "127.0.0.1:$port") return reply(403, "Local origin required", "text/plain")
            if (method == "GET" && path.startsWith("/media/")) {
                if (headers["authorization"] != "Bearer $sessionToken") return reply(401, """{"error":"Pair again"}""")
                if (path != "/media/abc/0") return reply(404, "Not found", "text/plain")
                val from = Regex("bytes=(\\d+)-").find(headers["range"].orEmpty())?.groupValues?.get(1)?.toInt() ?: 0
                val chunk = movie.copyOfRange(from, movie.size)
                s.getOutputStream().write("HTTP/1.1 206 Partial Content\r\nContent-Length: ${chunk.size}\r\nContent-Range: bytes $from-${movie.size - 1}/${movie.size}\r\nConnection: close\r\n\r\n".toByteArray() + chunk)
                return
            }
            if (method == "GET") return reply(200, "<title>Качалка · Remote</title>", "text/html")
            if (path == "/pair") lastPairBody = body
            if (path == "/pair") return if (body.contains("\"123456\"")) reply(200, """{"token":"$sessionToken"}""") else reply(401, """{"error":"Код неверен или истёк."}""")
            if (headers["authorization"] != "Bearer $sessionToken") return reply(401, """{"error":"Подключись заново"}""")
            when (path) {
                "/api/add" -> { added.add(body); reply(200, """{"id":"d1"}""") }
                "/api/downloads" -> reply(200, """{"items":[{"id":"d1","title":"Фильм","status":"Скачивается","statusKind":"downloading","progress":42.5,"paused":false,"complete":false,"downloadRate":1048576,"totalBytes":900000000}]}""")
                "/api/files" -> reply(200, """{"items":[{"index":0,"name":"Фильм.mkv","size":${movie.size},"complete":true}]}""")
                "/api/saved" -> reply(200, """{"marks":[{"id":"movies:z","at":5,"removed":false},{"id":"series:y","at":9,"removed":true}]}""")
                "/api/control" -> reply(200, """{"id":"d1"}""")
                else -> reply(404, "Not found", "text/plain")
            }
        }
        override fun close() = server.close()
    }

    @Test fun pairsAddsAndListsThroughTheWireFormat() {
        FakeDesktop().use { desktop ->
            val client = ComputerClient("127.0.0.1", desktop.port)
            assertTrue(client.isKachalka())
            assertFailsWith<ComputerException> { client.pair("000000") }.also { assertTrue(it.message!!.contains("Код")) }
            assertEquals("T0KEN", client.pair("123 456"))
            assertEquals("d1", client.add("magnet:?xt=urn:btih:${"A".repeat(40)}&dn=\"x\""))
            assertTrue(desktop.added.single().contains("btih"))
            val row = client.downloads().single()
            assertEquals("Фильм", row.title); assertEquals(42.5, row.progress); assertEquals(1048576L, row.downloadRate)
            client.control("d1", "pause")
        }
    }

    @Test fun anExpiredSessionIsReportedAsUnauthorized() {
        FakeDesktop().use { desktop ->
            val client = ComputerClient("127.0.0.1", desktop.port, token = "OLD")
            assertTrue(assertFailsWith<ComputerException> { client.downloads() }.unauthorized)
        }
    }

    @Test fun refusesNonMagnetsAndUnreachableComputers() {
        assertFailsWith<IllegalArgumentException> { ComputerClient("127.0.0.1", 1).add("https://evil.example/x.torrent") }
        assertTrue(assertFailsWith<ComputerException> { ComputerClient("127.0.0.1", 1, timeoutMs = 300).pair("123456") }.message!!.contains("связаться"))
    }

    @Test fun addressParsing() {
        assertEquals("192.168.1.25" to 45834, ComputerClient.parseAddress("192.168.1.25"))
        assertEquals("10.0.0.7" to 45834, ComputerClient.parseAddress("http://10.0.0.7:45834/"))
        assertEquals("172.20.1.2" to 9, ComputerClient.parseAddress(" 172.20.1.2:9 "))
        assertNull(ComputerClient.parseAddress("8.8.8.8"))
        assertNull(ComputerClient.parseAddress("example.com"))
        assertNull(ComputerClient.parseAddress("192.168.1.300"))
        assertNull(ComputerClient.parseAddress("192.168.1.1:99999"))
    }

    @Test fun scannerFindsTheComputerAndIgnoresOtherHosts() {
        FakeDesktop().use { desktop ->
            val found = LanScanner.scan(listOf("127.0.0."), desktop.port, 200) { ip -> ComputerClient(ip, desktop.port, null, 1000).isKachalka() }
            assertEquals(listOf("127.0.0.1"), found)
            assertTrue(LanScanner.scan(emptyList()).isEmpty())
        }
    }

    @Test fun rememberingTheDeviceIsRequestedWhenPairing() {
        FakeDesktop().use { desktop ->
            ComputerClient("127.0.0.1", desktop.port).pair("123456", deviceName = "Гостиная <ТВ>")
            assertTrue(desktop.lastPairBody.contains("\"remember\":true"))
            assertTrue(desktop.lastPairBody.contains("\"name\":\"Гостиная ТВ\""), desktop.lastPairBody)
        }
    }

    @Test fun listsFilesAndPlaysThemThroughTheStreamServerWithSeeking() {
        FakeDesktop().use { desktop ->
            val client = ComputerClient("127.0.0.1", desktop.port, token = "T0KEN")
            val file = client.files("abc").single()
            assertEquals("Фильм.mkv", file.name); assertEquals(700_000L, file.size); assertTrue(file.complete)
            RemoteFileSource(client, "abc", file.index, file.size, "video/x-matroska").use { source ->
                StreamServer(source, chunk = 64 * 1024).use { server ->
                    fun get(range: String?): ByteArray {
                        val c = java.net.URL(server.url).openConnection() as java.net.HttpURLConnection
                        if (range != null) c.setRequestProperty("Range", range)
                        return c.inputStream.readBytes()
                    }
                    assertTrue(get(null).contentEquals(desktop.movie))
                    assertTrue(get("bytes=500000-500999").contentEquals(desktop.movie.copyOfRange(500_000, 501_000)))
                    assertTrue(get("bytes=10-19").contentEquals(desktop.movie.copyOfRange(10, 20)))
                    assertTrue(get("bytes=-100").contentEquals(desktop.movie.copyOfRange(699_900, 700_000)))
                }
            }
        }
    }

    @Test fun anExpiredTokenIsReportedWhilePlaying() {
        FakeDesktop().use { desktop ->
            val client = ComputerClient("127.0.0.1", desktop.port, token = "OLD")
            assertTrue(assertFailsWith<ComputerException> { client.openRange("abc", 0, 0) }.unauthorized)
        }
    }

    @Test fun favouritesAreExchangedAsMarks() {
        FakeDesktop().use { desktop ->
            val client = ComputerClient("127.0.0.1", desktop.port, token = "T0KEN")
            val back = client.saved(listOf(FavoriteMark("movies:a", 100, false)))
            assertEquals(listOf(FavoriteMark("movies:z", 5, false), FavoriteMark("series:y", 9, true)), back)
        }
    }
}
