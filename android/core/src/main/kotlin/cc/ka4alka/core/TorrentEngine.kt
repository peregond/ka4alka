package cc.ka4alka.core

import kotlinx.serialization.Serializable
import kotlinx.serialization.builtins.ListSerializer
import kotlinx.serialization.json.Json
import org.libtorrent4j.Priority
import org.libtorrent4j.SessionManager
import org.libtorrent4j.SessionParams
import org.libtorrent4j.SettingsPack
import org.libtorrent4j.Sha1Hash
import org.libtorrent4j.TcpEndpoint
import org.libtorrent4j.TorrentFlags
import org.libtorrent4j.TorrentHandle
import org.libtorrent4j.TorrentInfo
import org.libtorrent4j.TorrentStatus
import java.io.File
import java.io.RandomAccessFile
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.Executors

/** One queued download as it is kept on disk between runs. */
@Serializable
data class DownloadRecord(
    val id: String,            // upper-case info hash
    val mediaId: String? = null,
    val title: String,
    val releaseTitle: String,
    val poster: String? = null,
    val magnet: String,
    val savePath: String,
    val addedUtc: Long,
    val paused: Boolean = false,
)

enum class DownloadPhase { Starting, Checking, Downloading, Done, Paused, Error }

data class DownloadState(
    val record: DownloadRecord,
    val phase: DownloadPhase,
    val progress: Float,
    val totalBytes: Long,
    val doneBytes: Long,
    val downloadRate: Int,
    val uploadRate: Int,
    val peers: Int,
    val error: String? = null,
) {
    /** Whole seconds left at the current speed, or null while it cannot be known. */
    val etaSeconds: Long? get() = if (phase == DownloadPhase.Downloading && downloadRate > 1024 && totalBytes > doneBytes) (totalBytes - doneBytes) / downloadRate else null
}

data class VideoFile(val index: Int, val name: String, val size: Long, val progress: Float)

object VideoNames {
    private val extensions = setOf("mkv", "mp4", "avi", "m4v", "mov", "ts", "m2ts", "webm", "wmv", "mpg", "mpeg")
    fun isVideo(path: String) = path.substringAfterLast('.', "").lowercase() in extensions
    fun contentType(path: String) = when (path.substringAfterLast('.', "").lowercase()) {
        "mp4", "m4v" -> "video/mp4"
        "mkv" -> "video/x-matroska"
        "webm" -> "video/webm"
        "avi" -> "video/x-msvideo"
        "mov" -> "video/quicktime"
        "ts", "m2ts" -> "video/mp2t"
        else -> "video/mpeg"
    }
}

/**
 * The download side of the app: queue, metadata, progress, and streaming a file that is still being fetched.
 * [listenOn] and [bootstrapPeers] exist for tests and tools; the app leaves them empty.
 */
class TorrentEngine(
    private val stateDir: File,
    private val listenOn: String? = null,
    private val enableDht: Boolean = true,
) {
    private val session = SessionManager(false)
    private val json = Json { ignoreUnknownKeys = true }
    private val records = ConcurrentHashMap<String, DownloadRecord>()
    private val errors = ConcurrentHashMap<String, String>()
    private val pending = ConcurrentHashMap.newKeySet<String>()
    private val worker = Executors.newFixedThreadPool(3) { r -> Thread(r, "torrent-meta").apply { isDaemon = true } }
    private val torrentDir = File(stateDir, "torrents").apply { mkdirs() }
    private val recordsFile = File(stateDir, "downloads.json")
    @Volatile private var started = false

    fun start() {
        if (started) return
        started = true
        val settings = SettingsPack()
        settings.activeDownloads(6).activeSeeds(4).activeLimit(30).connectionsLimit(200)
        if (listenOn != null) settings.listenInterfaces(listenOn)
        if (!enableDht) settings.setEnableDht(false)
        session.start(SessionParams(settings))
        loadRecords().forEach { record ->
            records[record.id] = record
            resume(record)
        }
    }

    fun stop() {
        worker.shutdownNow()
        session.stop()
        started = false
    }

    fun setLimits(downloadKbps: Int, uploadKbps: Int) {
        session.downloadRateLimit(if (downloadKbps <= 0) 0 else downloadKbps * 1024)
        session.uploadRateLimit(if (uploadKbps <= 0) 0 else uploadKbps * 1024)
    }

    /** Queues a release. Returns at once; the metadata arrives in the background and [states] shows the progress. */
    fun add(record: DownloadRecord, peers: List<TcpEndpoint> = emptyList()) {
        check(started) { "The engine is not started" }
        val existing = records[record.id]
        if (existing != null) { if (existing.paused) resumeDownload(record.id); return }
        records[record.id] = record
        saveRecords()
        resume(record, peers)
    }

    private fun resume(record: DownloadRecord, peers: List<TcpEndpoint> = emptyList()) {
        val torrentFile = File(torrentDir, record.id + ".torrent")
        val saveDir = File(record.savePath).apply { mkdirs() }
        val flags = if (record.paused) TorrentFlags.PAUSED else org.libtorrent4j.swig.torrent_flags_t()
        if (torrentFile.exists()) {
            try { session.download(TorrentInfo(torrentFile), saveDir, null, null, peers, flags); return }
            catch (e: Exception) { torrentFile.delete() }
        }
        if (!pending.add(record.id)) return
        worker.execute {
            try {
                val data = session.fetchMagnet(record.magnet, 120, File(stateDir, "tmp").apply { mkdirs() })
                if (data == null || data.isEmpty()) { errors[record.id] = "Не удалось получить список файлов раздачи. Нет источников."; return@execute }
                torrentFile.writeBytes(data)
                if (records.containsKey(record.id)) session.download(TorrentInfo(data), saveDir, null, null, peers, flags)
            } catch (e: Exception) {
                errors[record.id] = e.message ?: "Ошибка получения раздачи"
            } finally { pending.remove(record.id) }
        }
    }

    private fun handle(id: String): TorrentHandle? =
        session.find(Sha1Hash(org.libtorrent4j.swig.sha1_hash.from_hex(id.lowercase()))).takeIf { it != null && it.isValid }

    fun states(): List<DownloadState> = records.values.sortedByDescending { it.addedUtc }.map { stateOf(it) }

    fun state(id: String): DownloadState? = records[id]?.let { stateOf(it) }

    private fun stateOf(record: DownloadRecord): DownloadState {
        val h = handle(record.id)
        if (h == null) {
            val error = errors[record.id]
            return DownloadState(record, if (error != null) DownloadPhase.Error else DownloadPhase.Starting, 0f, 0, 0, 0, 0, 0, error)
        }
        val s = h.status()
        val paused = record.paused || s.flags().and_(TorrentFlags.PAUSED).non_zero()
        val phase = when {
            s.errorCode().isError -> DownloadPhase.Error
            paused -> DownloadPhase.Paused
            s.state() == TorrentStatus.State.CHECKING_FILES || s.state() == TorrentStatus.State.CHECKING_RESUME_DATA -> DownloadPhase.Checking
            s.state() == TorrentStatus.State.DOWNLOADING_METADATA -> DownloadPhase.Starting
            s.isFinished || s.isSeeding -> DownloadPhase.Done
            else -> DownloadPhase.Downloading
        }
        val wanted = s.totalWanted()
        val done = s.totalWantedDone()
        val progress = if (wanted > 0) (done.toDouble() / wanted).toFloat().coerceIn(0f, 1f) else 0f
        val message: String? = if (phase == DownloadPhase.Error) s.errorCode().message else null
        return DownloadState(record, phase, progress, wanted, done, s.downloadPayloadRate(), s.uploadPayloadRate(), s.numPeers(), message)
    }

    fun pause(id: String) { setPaused(id, true) }
    fun resumeDownload(id: String) { setPaused(id, false) }

    private fun setPaused(id: String, paused: Boolean) {
        val record = records[id] ?: return
        records[id] = record.copy(paused = paused)
        saveRecords()
        val h = handle(id) ?: return
        if (paused) { h.unsetFlags(TorrentFlags.AUTO_MANAGED); h.pause() } else { h.setFlags(TorrentFlags.AUTO_MANAGED); h.resume() }
    }

    fun remove(id: String, deleteFiles: Boolean) {
        val record = records.remove(id) ?: return
        errors.remove(id)
        saveRecords()
        handle(id)?.let { session.remove(it, if (deleteFiles) org.libtorrent4j.SessionHandle.DELETE_FILES else null) }
        File(torrentDir, "$id.torrent").delete()
        if (deleteFiles) File(record.savePath).takeIf { it.isDirectory && it.list().isNullOrEmpty() }?.delete()
    }

    fun videoFiles(id: String): List<VideoFile> {
        val h = handle(id) ?: return emptyList()
        val info = h.torrentFile() ?: return emptyList()
        val files = info.files()
        val progress = h.fileProgress()
        return (0 until files.numFiles()).filter { VideoNames.isVideo(files.filePath(it)) }.map {
            val size = files.fileSize(it)
            VideoFile(it, files.filePath(it), size, if (size > 0) (progress[it].toDouble() / size).toFloat() else 0f)
        }.sortedBy { it.name }
    }

    /** Opens a file of a download for playing while it downloads. Close it when playback ends. */
    fun openStream(id: String, fileIndex: Int): TorrentStream {
        val record = records[id] ?: throw IllegalStateException("Unknown download")
        val h = handle(id) ?: throw IllegalStateException("The download has no metadata yet")
        val info = h.torrentFile() ?: throw IllegalStateException("The download has no metadata yet")
        val files = info.files()
        val relative = files.filePath(fileIndex)
        // The streamed file goes first; the rest keep their normal order but yield to it.
        h.setFlags(TorrentFlags.SEQUENTIAL_DOWNLOAD)
        h.filePriority(fileIndex, Priority.TOP_PRIORITY)
        if (record.paused) setPaused(id, false)
        return TorrentStream(h, info, fileIndex, File(record.savePath, relative))
    }

    private fun loadRecords(): List<DownloadRecord> = try {
        if (recordsFile.exists()) json.decodeFromString(ListSerializer(DownloadRecord.serializer()), recordsFile.readText()) else emptyList()
    } catch (e: Exception) { emptyList() }

    @Synchronized private fun saveRecords() {
        val temporary = File(stateDir, "downloads.json.tmp")
        temporary.writeText(json.encodeToString(ListSerializer(DownloadRecord.serializer()), records.values.toList()))
        if (!temporary.renameTo(recordsFile)) { recordsFile.delete(); temporary.renameTo(recordsFile) }
    }

    val downloadRate: Long get() = session.downloadRate()
    val uploadRate: Long get() = session.uploadRate()
    val listenPort: Int get() = session.listenEndpoints().firstOrNull()?.substringAfterLast(':')?.toIntOrNull() ?: 0
}

/** A file of a running torrent that can be read as its pieces arrive. */
class TorrentStream(
    private val handle: TorrentHandle,
    info: TorrentInfo,
    fileIndex: Int,
    private val file: File,
    private val readAheadBytes: Long = 32L * 1024 * 1024,
) : StreamSource, AutoCloseable {
    private val files = info.files()
    override val length: Long = files.fileSize(fileIndex)
    override val contentType: String = VideoNames.contentType(file.name)
    private val plan = PiecePlan(info.pieceLength().toLong(), info.totalSize(), files.fileOffset(fileIndex), length)
    private var reader: RandomAccessFile? = null
    @Volatile private var closed = false
    private var lastFocus = -1
    private var lastFocusAt = 0L

    /** Bytes of the file from the start that are present, for the "buffered" indicator. */
    fun bufferedFrom(position: Long): Long {
        var piece = plan.pieceAt(position)
        val last = plan.pieceAt(length - 1)
        var bytes = 0L
        while (piece <= last && handle.havePiece(piece)) { bytes += plan.pieceLength; piece++ }
        return minOf(bytes, length - position)
    }

    override fun await(start: Long, length: Long, timeoutMs: Long): Boolean {
        val range = plan.pieces(start, length)
        if (range.isEmpty()) return true
        val deadline = System.currentTimeMillis() + timeoutMs
        while (!closed) {
            focus(start)
            if (range.all { handle.havePiece(it) }) return true
            if (System.currentTimeMillis() > deadline) return false
            Thread.sleep(120)
        }
        return false
    }

    /** Tells the torrent which pieces are needed next: the piece under the playhead first, then the read-ahead window. */
    @Synchronized private fun focus(position: Long) {
        val first = plan.pieceAt(position)
        val now = System.currentTimeMillis()
        if (first == lastFocus && now - lastFocusAt < 2000) return
        lastFocus = first; lastFocusAt = now
        val window = plan.piecesFor(readAheadBytes).coerceIn(4, 96)
        val last = plan.pieceAt(length - 1)
        for (i in 0 until window) {
            val piece = first + i
            if (piece > last) break
            if (!handle.havePiece(piece)) handle.setPieceDeadline(piece, 150 + i * 120)
        }
    }

    @Synchronized override fun read(position: Long, buffer: ByteArray, offset: Int, length: Int): Int {
        val raf = reader ?: RandomAccessFile(file, "r").also { reader = it }
        raf.seek(position)
        return raf.read(buffer, offset, length)
    }

    override fun close() {
        closed = true
        try { reader?.close() } catch (_: Exception) {}
        try { if (handle.isValid) handle.clearPieceDeadlines() } catch (_: Exception) {}
    }
}
