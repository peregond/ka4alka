package cc.ka4alka.core

import java.io.IOException
import java.io.OutputStream
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.security.SecureRandom
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean

/** Where one file of a torrent sits inside the torrent's piece grid. */
class PiecePlan(val pieceLength: Long, val totalLength: Long, val fileOffset: Long, val fileLength: Long) {
    init {
        require(pieceLength > 0 && fileLength >= 0 && fileOffset >= 0 && fileOffset + fileLength <= totalLength)
    }

    val pieceCount: Int get() = ((totalLength + pieceLength - 1) / pieceLength).toInt()

    /** The piece that holds this byte of the file. */
    fun pieceAt(position: Long): Int = ((fileOffset + position.coerceIn(0, maxOf(0, fileLength - 1))) / pieceLength).toInt()

    /** First and last piece (inclusive) that cover the file bytes [start, start+length). */
    fun pieces(start: Long, length: Long): IntRange {
        if (length <= 0) return IntRange.EMPTY
        val first = pieceAt(start)
        val last = pieceAt(minOf(fileLength - 1, start + length - 1))
        return first..last
    }

    /** How many bytes of the file lie in the pieces that follow `position`, to size a read-ahead window. */
    fun piecesFor(bytes: Long): Int = ((bytes + pieceLength - 1) / pieceLength).toInt().coerceAtLeast(1)
}

/** What the stream server needs from a download that is still running. */
interface StreamSource {
    val length: Long
    val contentType: String

    /** Blocks until the file bytes [start, start+length) are on disk. False if they did not arrive in time or the source closed. */
    fun await(start: Long, length: Long, timeoutMs: Long): Boolean

    /** Reads bytes that `await` has confirmed. */
    fun read(position: Long, buffer: ByteArray, offset: Int, length: Int): Int
}

data class ByteRange(val start: Long, val end: Long) { // end inclusive
    val length: Long get() = end - start + 1
}

object RangeHeader {
    /** Parses a single "bytes=a-b" range against a file of `size` bytes. Null: no header. Throws: not satisfiable. */
    fun parse(header: String?, size: Long): ByteRange? {
        if (header.isNullOrBlank()) return null
        val match = Regex("^\\s*bytes\\s*=\\s*(\\d*)\\s*-\\s*(\\d*)\\s*(?:,.*)?$").find(header) ?: return null
        val first = match.groupValues[1]
        val last = match.groupValues[2]
        if (first.isEmpty() && last.isEmpty()) return null
        val range = if (first.isEmpty()) {
            val n = last.toLongOrNull() ?: throw IllegalArgumentException("range")
            if (n == 0L) throw IllegalArgumentException("range")
            ByteRange(maxOf(0, size - n), size - 1)
        } else {
            val a = first.toLongOrNull() ?: throw IllegalArgumentException("range")
            val b = if (last.isEmpty()) size - 1 else minOf(last.toLongOrNull() ?: throw IllegalArgumentException("range"), size - 1)
            ByteRange(a, b)
        }
        if (size <= 0 || range.start >= size || range.end < range.start) throw IllegalArgumentException("range")
        return range
    }
}

/**
 * A tiny loopback HTTP/1.1 server that lets a player read a file while it is still downloading.
 * Requests wait for the pieces they need; seeking just starts a new ranged request, which the source turns into new piece priorities.
 * The address contains an unguessable token so other apps on the device cannot read the stream.
 */
class StreamServer(private val source: StreamSource, private val chunk: Int = 256 * 1024, private val waitMs: Long = 45_000) : AutoCloseable {
    private val server = ServerSocket(0, 16, InetAddress.getByName("127.0.0.1"))
    private val token = ByteArray(12).also { SecureRandom().nextBytes(it) }.joinToString("") { "%02x".format(it) }
    private val pool = Executors.newCachedThreadPool { r -> Thread(r, "stream-conn").apply { isDaemon = true } }
    private val closed = AtomicBoolean(false)

    val url: String get() = "http://127.0.0.1:${server.localPort}/$token"

    init {
        Thread({
            while (!closed.get()) {
                val socket = try { server.accept() } catch (e: IOException) { break }
                pool.execute { try { serve(socket) } catch (_: Exception) {} finally { try { socket.close() } catch (_: Exception) {} } }
            }
        }, "stream-accept").apply { isDaemon = true }.start()
    }

    private fun serve(socket: Socket) {
        socket.soTimeout = 30_000
        val input = socket.getInputStream().buffered()
        val out = socket.getOutputStream()
        while (!closed.get()) {
            val request = readHeaders(input) ?: return
            val lines = request.split("\r\n")
            val parts = lines[0].split(' ')
            if (parts.size < 2) { reply(out, 400, "Bad Request"); return }
            val method = parts[0]
            val headers = lines.drop(1).mapNotNull { line -> line.indexOf(':').takeIf { it > 0 }?.let { line.substring(0, it).trim().lowercase() to line.substring(it + 1).trim() } }.toMap()
            if (parts[1].substringBefore('?') != "/$token") { reply(out, 404, "Not Found"); return }
            if (method != "GET" && method != "HEAD") { reply(out, 405, "Method Not Allowed"); return }
            val range = try { RangeHeader.parse(headers["range"], source.length) } catch (e: IllegalArgumentException) {
                reply(out, 416, "Range Not Satisfiable", "Content-Range" to "bytes */${source.length}"); return
            }
            val size = source.length
            val sent = range ?: ByteRange(0, size - 1)
            val status = if (range != null) "206 Partial Content" else "200 OK"
            val header = StringBuilder("HTTP/1.1 $status\r\nContent-Type: ${source.contentType}\r\nAccept-Ranges: bytes\r\nContent-Length: ${if (size == 0L) 0 else sent.length}\r\nConnection: keep-alive\r\n")
            if (range != null) header.append("Content-Range: bytes ${sent.start}-${sent.end}/$size\r\n")
            header.append("\r\n")
            out.write(header.toString().toByteArray(Charsets.ISO_8859_1))
            out.flush()
            if (method == "GET" && size > 0) { if (!body(out, sent)) return }
        }
    }

    private fun body(out: OutputStream, range: ByteRange): Boolean {
        val buffer = ByteArray(chunk)
        var position = range.start
        while (position <= range.end) {
            val want = minOf(chunk.toLong(), range.end - position + 1)
            if (closed.get() || !source.await(position, want, waitMs)) return false
            val n = source.read(position, buffer, 0, want.toInt())
            if (n <= 0) return false
            out.write(buffer, 0, n)
            position += n
        }
        out.flush()
        return true
    }

    private fun readHeaders(input: java.io.InputStream): String? {
        val text = StringBuilder()
        while (text.length < 16 * 1024) {
            val b = try { input.read() } catch (e: IOException) { return null }
            if (b < 0) return null
            text.append(b.toChar())
            if (text.endsWith("\r\n\r\n")) return text.dropLast(4).toString()
        }
        return null
    }

    private fun reply(out: OutputStream, code: Int, text: String, vararg extra: Pair<String, String>) {
        val sb = StringBuilder("HTTP/1.1 $code $text\r\nContent-Length: 0\r\nConnection: close\r\n")
        extra.forEach { sb.append("${it.first}: ${it.second}\r\n") }
        sb.append("\r\n")
        out.write(sb.toString().toByteArray(Charsets.ISO_8859_1)); out.flush()
    }

    override fun close() {
        if (closed.compareAndSet(false, true)) {
            try { server.close() } catch (_: Exception) {}
            pool.shutdownNow()
        }
    }
}
