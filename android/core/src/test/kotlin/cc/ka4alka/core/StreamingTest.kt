package cc.ka4alka.core

import java.net.HttpURLConnection
import java.net.URL
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertNull
import kotlin.test.assertTrue

class StreamingTest {
    /** A file whose bytes "arrive" over time: bytes below `available` can be read, the rest block until they do. */
    private class FakeSource(val data: ByteArray) : StreamSource {
        @Volatile var available = 0L
        val requested = java.util.concurrent.CopyOnWriteArrayList<Long>()
        override val length = data.size.toLong()
        override val contentType = "video/mp4"
        override fun await(start: Long, length: Long, timeoutMs: Long): Boolean {
            requested.add(start)
            val deadline = System.currentTimeMillis() + timeoutMs
            while (available < start + length) {
                if (System.currentTimeMillis() > deadline) return false
                Thread.sleep(5)
            }
            return true
        }
        override fun read(position: Long, buffer: ByteArray, offset: Int, length: Int): Int {
            System.arraycopy(data, position.toInt(), buffer, offset, length); return length
        }
    }

    private fun bytes(n: Int) = ByteArray(n) { (it * 31 + it / 7).toByte() }

    private fun get(url: String, range: String? = null): Triple<Int, Map<String, List<String>>, ByteArray> {
        val c = URL(url).openConnection() as HttpURLConnection
        if (range != null) c.setRequestProperty("Range", range)
        val code = c.responseCode
        val body = if (code < 400) c.inputStream.readBytes() else ByteArray(0)
        return Triple(code, c.headerFields, body)
    }

    @Test fun servesWholeFileAndRanges() {
        val src = FakeSource(bytes(1_000_000)).also { it.available = 1_000_000 }
        StreamServer(src, chunk = 64 * 1024).use { server ->
            val (code, _, body) = get(server.url)
            assertEquals(200, code); assertTrue(body.contentEquals(src.data))
            val (c2, h2, b2) = get(server.url, "bytes=1000-1999")
            assertEquals(206, c2); assertEquals(1000, b2.size)
            assertTrue(b2.contentEquals(src.data.copyOfRange(1000, 2000)))
            assertEquals("bytes 1000-1999/1000000", h2["Content-Range"]?.first())
            val (c3, _, b3) = get(server.url, "bytes=999000-")
            assertEquals(206, c3); assertEquals(1000, b3.size)
            val (c4, _, b4) = get(server.url, "bytes=-500")
            assertEquals(206, c4); assertTrue(b4.contentEquals(src.data.copyOfRange(999_500, 1_000_000)))
        }
    }

    @Test fun waitsForPiecesThatHaveNotArrivedYet() {
        val src = FakeSource(bytes(600_000))
        StreamServer(src, chunk = 50_000).use { server ->
            Thread { for (i in 1..12) { Thread.sleep(40); src.available = i * 50_000L } }.start()
            val (code, _, body) = get(server.url)
            assertEquals(200, code); assertTrue(body.contentEquals(src.data))
        }
    }

    @Test fun seekingAsksForTheNewPosition() {
        val src = FakeSource(bytes(400_000)).also { it.available = 400_000 }
        StreamServer(src, chunk = 50_000).use { server ->
            get(server.url, "bytes=300000-309999")
            assertTrue(src.requested.contains(300_000L))
        }
    }

    @Test fun refusesWrongTokenAndBadRanges() {
        val src = FakeSource(bytes(1000)).also { it.available = 1000 }
        StreamServer(src).use { server ->
            assertEquals(404, get(server.url.dropLast(1) + "x").first)
            assertEquals(416, get(server.url, "bytes=5000-6000").first)
        }
    }

    @Test fun givesUpWhenDataNeverComes() {
        val src = FakeSource(bytes(100_000))
        StreamServer(src, waitMs = 200).use { server ->
            val c = URL(server.url).openConnection() as HttpURLConnection
            c.readTimeout = 5000
            assertEquals(200, c.responseCode)
            assertFailsWith<Exception> { c.inputStream.readBytes().also { if (it.size < 100_000) throw java.io.IOException("short") } }
        }
    }

    @Test fun rangeHeaderParsing() {
        assertNull(RangeHeader.parse(null, 100))
        assertEquals(ByteRange(0, 9), RangeHeader.parse("bytes=0-9", 100))
        assertEquals(ByteRange(90, 99), RangeHeader.parse("bytes=90-", 100))
        assertEquals(ByteRange(80, 99), RangeHeader.parse("bytes=-20", 100))
        assertEquals(ByteRange(10, 99), RangeHeader.parse("bytes=10-500", 100))
        assertFailsWith<IllegalArgumentException> { RangeHeader.parse("bytes=100-", 100) }
        assertFailsWith<IllegalArgumentException> { RangeHeader.parse("bytes=50-10", 100) }
    }

    @Test fun piecePlan() {
        // 10 pieces of 100 bytes; the file starts in the middle of piece 2 and is 450 bytes long.
        val plan = PiecePlan(100, 1000, 250, 450)
        assertEquals(10, plan.pieceCount)
        assertEquals(2, plan.pieceAt(0))
        assertEquals(2, plan.pieceAt(49)); assertEquals(3, plan.pieceAt(50))
        assertEquals(2..6, plan.pieces(0, 450))
        assertEquals(3..3, plan.pieces(50, 100))
        assertEquals(6, plan.pieceAt(449))
        assertEquals(4, plan.piecesFor(350))
    }
}
