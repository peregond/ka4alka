package cc.ka4alka.core

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.jsonObject
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.Socket
import java.util.concurrent.Callable
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

/** An error to show to the user as is (the computer's own message when it sent one). */
class ComputerException(message: String, val unauthorized: Boolean = false) : IOException(message)

data class ComputerFile(val index: Int, val name: String, val size: Long, val complete: Boolean)

data class ComputerDownload(
    val id: String, val title: String, val status: String, val statusKind: String, val progress: Double,
    val paused: Boolean, val complete: Boolean, val downloadRate: Long, val totalBytes: Long,
)

/**
 * Talks to the desktop app's "phone" access (HTTP + JSON on the home network, port 45834).
 * It uses a plain socket on purpose: the app's network policy forbids clear-text HTTP stacks, and this traffic never leaves the LAN.
 * The desktop checks that Host is exactly "address:port", so the request line is written by hand.
 */
class ComputerClient(val host: String, val port: Int = PORT, var token: String? = null, private val timeoutMs: Int = 8000) {
    companion object {
        const val PORT = 45834
        private val json = Json { ignoreUnknownKeys = true }

        /** "192.168.1.5", "192.168.1.5:45834" or "http://192.168.1.5:45834/" → (host, port); null if it is not a private IPv4 address. */
        fun parseAddress(text: String): Pair<String, Int>? {
            val cleaned = text.trim().removePrefix("http://").substringBefore('/').trim()
            val parts = cleaned.split(':')
            if (parts.size > 2) return null
            val host = parts[0]
            val port = if (parts.size == 2) parts[1].toIntOrNull() ?: return null else PORT
            val octets = host.split('.').map { it.toIntOrNull() ?: return null }
            if (octets.size != 4 || octets.any { it !in 0..255 } || port !in 1..65535) return null
            val (a, b) = octets
            val private = a == 10 || (a == 172 && b in 16..31) || (a == 192 && b == 168) || (a == 169 && b == 254)
            return if (private) host to port else null
        }
    }

    private fun request(method: String, path: String, body: String?): Pair<Int, String> {
        Socket().use { socket ->
            socket.connect(InetSocketAddress(host, port), timeoutMs)
            socket.soTimeout = timeoutMs.coerceAtLeast(90_000) // searches on the computer can take a while
            val payload = body?.toByteArray(Charsets.UTF_8)
            val head = StringBuilder("$method $path HTTP/1.1\r\nHost: $host:$port\r\nConnection: close\r\n")
            token?.let { head.append("Authorization: Bearer $it\r\n") }
            if (payload != null) head.append("Content-Type: application/json\r\nContent-Length: ${payload.size}\r\n")
            head.append("\r\n")
            val out = socket.getOutputStream()
            out.write(head.toString().toByteArray(Charsets.ISO_8859_1)); if (payload != null) out.write(payload); out.flush()
            val data = ByteArrayOutputStream()
            val buffer = ByteArray(8192)
            val input = socket.getInputStream()
            while (true) {
                val n = input.read(buffer)
                if (n < 0) break
                data.write(buffer, 0, n)
                if (data.size() > 4 * 1024 * 1024) throw ComputerException("Компьютер прислал слишком большой ответ.")
            }
            val raw = data.toString(Charsets.UTF_8.name())
            val split = raw.indexOf("\r\n\r\n")
            if (split < 0) throw ComputerException("Компьютер ответил непонятно. Проверь, что на нём включён доступ с телефона.")
            val status = raw.substringBefore("\r\n").split(' ').getOrNull(1)?.toIntOrNull() ?: throw ComputerException("Компьютер ответил непонятно.")
            return status to raw.substring(split + 4)
        }
    }

    private fun post(path: String, body: String = "{}"): JsonObject {
        val (status, text) = try { request("POST", path, body) } catch (e: ComputerException) { throw e }
            catch (e: IOException) { throw ComputerException("Не получилось связаться с компьютером ($host). Он включён, и Качалка запущена?") }
        val parsed = try { json.parseToJsonElement(text).jsonObject } catch (e: Exception) { JsonObject(emptyMap()) }
        if (status == 401) throw ComputerException((parsed["error"] as? JsonPrimitive)?.contentOrNull ?: "Подключение истекло.", unauthorized = true)
        if (status !in 200..299) throw ComputerException((parsed["error"] as? JsonPrimitive)?.contentOrNull ?: "Компьютер не смог выполнить запрос.")
        return parsed
    }

    /** Is this address a computer with the app's phone access switched on? */
    fun isKachalka(): Boolean = try {
        val (status, body) = request("GET", "/", null)
        status == 200 && body.contains("Качалка · Remote")
    } catch (e: Exception) { false }

    /** Trades the code shown on the computer for a token. With [remember] the computer keeps this device for 90 days. */
    fun pair(code: String, remember: Boolean = true, deviceName: String = "Android TV"): String {
        val name = deviceName.filter { it.isLetterOrDigit() || it == ' ' || it == '-' }.take(40).ifBlank { "Android TV" }
        val result = post("/pair", """{"code":"${code.filter { it.isDigit() }}","remember":$remember,"name":"$name"}""")
        val value = (result["token"] as? JsonPrimitive)?.contentOrNull ?: throw ComputerException("Компьютер не принял код.")
        token = value
        return value
    }

    /** Adds a magnet link to the computer's queue; returns the id of the download there. */
    fun add(magnet: String): String {
        require(magnet.startsWith("magnet:?")) { "Only magnet links" }
        val escaped = magnet.replace("\\", "\\\\").replace("\"", "\\\"")
        return (post("/api/add", """{"source":"$escaped"}""")["id"] as? JsonPrimitive)?.contentOrNull ?: ""
    }

    fun downloads(): List<ComputerDownload> =
        ((post("/api/downloads")["items"] as? JsonArray) ?: JsonArray(emptyList())).mapNotNull { row ->
            val o = row as? JsonObject ?: return@mapNotNull null
            fun text(k: String) = (o[k] as? JsonPrimitive)?.contentOrNull.orEmpty()
            fun num(k: String) = (o[k] as? JsonPrimitive)?.doubleOrNull ?: 0.0
            fun flag(k: String) = (o[k] as? JsonPrimitive)?.booleanOrNull ?: false
            ComputerDownload(text("id"), text("title"), text("status"), text("statusKind"), num("progress"), flag("paused"), flag("complete"), num("downloadRate").toLong(), num("totalBytes").toLong())
        }

    /** The finished video files of a download on the computer. */
    fun files(downloadId: String): List<ComputerFile> =
        ((post("/api/files", """{"id":"${downloadId.filter { it.isLetterOrDigit() }}"}""")["items"] as? JsonArray) ?: JsonArray(emptyList())).mapNotNull { row ->
            val o = row as? JsonObject ?: return@mapNotNull null
            val index = (o["index"] as? JsonPrimitive)?.doubleOrNull?.toInt() ?: return@mapNotNull null
            ComputerFile(index, (o["name"] as? JsonPrimitive)?.contentOrNull.orEmpty(), (o["size"] as? JsonPrimitive)?.doubleOrNull?.toLong() ?: 0, (o["complete"] as? JsonPrimitive)?.booleanOrNull ?: false)
        }

    /** Opens a ranged read of one of the computer's files; the caller closes the socket. Returns the socket and the stream at the first byte. */
    fun openRange(downloadId: String, index: Int, from: Long): Pair<Socket, java.io.InputStream> {
        val socket = Socket()
        try {
            socket.connect(InetSocketAddress(host, port), timeoutMs)
            socket.soTimeout = 60_000
            val head = "GET /media/${downloadId.filter { it.isLetterOrDigit() }}/$index HTTP/1.1\r\nHost: $host:$port\r\nAuthorization: Bearer ${token.orEmpty()}\r\nRange: bytes=$from-\r\nConnection: close\r\n\r\n"
            socket.getOutputStream().write(head.toByteArray(Charsets.ISO_8859_1)); socket.getOutputStream().flush()
            val input = socket.getInputStream().buffered(64 * 1024)
            val header = StringBuilder()
            while (!header.endsWith("\r\n\r\n")) {
                val b = input.read()
                if (b < 0 || header.length > 16 * 1024) throw ComputerException("Компьютер оборвал передачу файла.")
                header.append(b.toChar())
            }
            val status = header.toString().substringBefore("\r\n").split(' ').getOrNull(1)?.toIntOrNull()
            if (status == 401) throw ComputerException("Подключение к компьютеру истекло.", unauthorized = true)
            if (status != 200 && status != 206) throw ComputerException("Компьютер не отдал файл (код $status).")
            return socket to input
        } catch (e: Exception) {
            try { socket.close() } catch (_: Exception) {}
            throw if (e is ComputerException) e else ComputerException("Не получилось связаться с компьютером ($host).")
        }
    }

    /** Sends this device's favourite marks and returns the computer's merged ones. */
    fun saved(marks: List<FavoriteMark>): List<FavoriteMark> {
        val body = buildString {
            append("""{"marks":[""")
            append(marks.joinToString(",") { """{"id":"${it.id}","at":${it.at},"removed":${it.removed}}""" })
            append("]}")
        }
        return ((post("/api/saved", body)["marks"] as? JsonArray) ?: JsonArray(emptyList())).mapNotNull { row ->
            val o = row as? JsonObject ?: return@mapNotNull null
            val id = (o["id"] as? JsonPrimitive)?.contentOrNull ?: return@mapNotNull null
            val at = (o["at"] as? JsonPrimitive)?.doubleOrNull?.toLong() ?: return@mapNotNull null
            FavoriteMark(id, at, (o["removed"] as? JsonPrimitive)?.booleanOrNull ?: false)
        }
    }

    fun control(id: String, action: String) { require(action == "pause" || action == "resume"); post("/api/control", """{"id":"$id","action":"$action"}""") }
    fun logout() { try { post("/api/logout") } catch (_: Exception) {} ; token = null }
}

/** Looks for computers on the same home network that have the app's phone access on. */
object LanScanner {
    /** Private IPv4 prefixes ("192.168.1.") of this device's network interfaces. */
    fun localPrefixes(): List<String> = try {
        NetworkInterface.getNetworkInterfaces().toList().filter { it.isUp && !it.isLoopback }.flatMap { it.interfaceAddresses }
            .mapNotNull { it.address.hostAddress }
            .filter { ComputerClient.parseAddress(it) != null }
            .map { it.substringBeforeLast('.') + "." }.distinct()
    } catch (e: Exception) { emptyList() }

    fun scan(prefixes: List<String>, port: Int = ComputerClient.PORT, connectMs: Int = 350, probe: (String) -> Boolean = { ComputerClient(it, port, null, 1500).isKachalka() }): List<String> {
        if (prefixes.isEmpty()) return emptyList()
        val pool = Executors.newFixedThreadPool(48)
        try {
            val tasks = prefixes.take(2).flatMap { prefix -> (1..254).map { prefix + it } }.map { ip ->
                Callable<String?> {
                    val open = try { Socket().use { it.connect(InetSocketAddress(ip, port), connectMs); true } } catch (e: Exception) { false }
                    if (open && probe(ip)) ip else null
                }
            }
            return pool.invokeAll(tasks, 20, TimeUnit.SECONDS).mapNotNull { f -> try { if (f.isCancelled) null else f.get() } catch (e: Exception) { null } }
        } finally { pool.shutdownNow() }
    }
}

/**
 * A file on the computer, readable through the stream server like a file of the local torrent:
 * the player talks to the loopback server, which asks the computer for the bytes it needs. One ranged request is kept open while
 * the player reads forward; a seek opens a new one.
 */
class RemoteFileSource(
    private val client: ComputerClient,
    private val downloadId: String,
    private val index: Int,
    override val length: Long,
    override val contentType: String,
) : StreamSource, AutoCloseable {
    private var socket: Socket? = null
    private var input: java.io.InputStream? = null
    private var next = -1L

    override fun await(start: Long, length: Long, timeoutMs: Long): Boolean = true // the computer's file is already complete

    @Synchronized
    override fun read(position: Long, buffer: ByteArray, offset: Int, length: Int): Int {
        if (input == null || position != next) {
            closeConnection()
            val (s, i) = client.openRange(downloadId, index, position)
            socket = s; input = i; next = position
        }
        var total = 0
        while (total < length) {
            val n = input!!.read(buffer, offset + total, length - total)
            if (n < 0) break
            total += n
        }
        next += total
        if (total == 0) closeConnection()
        return total
    }

    private fun closeConnection() { try { socket?.close() } catch (_: Exception) {}; socket = null; input = null; next = -1 }
    @Synchronized override fun close() = closeConnection()
}
