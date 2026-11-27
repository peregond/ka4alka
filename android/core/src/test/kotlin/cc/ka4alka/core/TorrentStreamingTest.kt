package cc.ka4alka.core

import org.libtorrent4j.SessionManager
import org.libtorrent4j.SessionParams
import org.libtorrent4j.SettingsPack
import org.libtorrent4j.TcpEndpoint
import org.libtorrent4j.TorrentBuilder
import org.libtorrent4j.TorrentInfo
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.nio.file.Files
import kotlin.random.Random
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/** End to end over the loopback: a real seeder, the real engine, and the real stream server a player would talk to. */
class TorrentStreamingTest {
    @Test fun playsAFileWhileItDownloads() {
        val root = Files.createTempDirectory("ka4alka-stream").toFile()
        val seedDir = File(root, "seed").apply { mkdirs() }
        val content = Random(7).nextBytes(24 * 1024 * 1024 + 12345)
        val video = File(File(seedDir, "Film"), "film.mkv").apply { parentFile.mkdirs(); writeBytes(content) }
        val builder = TorrentBuilder().path(File(seedDir, "Film")).pieceSize(256 * 1024)
        val torrent = builder.generate().entry().bencode()
        val info = TorrentInfo(torrent)
        val hash = info.infoHash().toHex().uppercase()

        val seeder = SessionManager(false)
        val seederSettings = SettingsPack().apply { listenInterfaces("127.0.0.1:16881"); setEnableDht(false); setEnableLsd(false) }
        seeder.start(SessionParams(seederSettings))
        seeder.download(info, seedDir)

        val engineDir = File(root, "engine")
        File(engineDir, "torrents").apply { mkdirs() }.let { File(it, "$hash.torrent").writeBytes(torrent) }
        val engine = TorrentEngine(engineDir, "127.0.0.1:16882", enableDht = false)
        engine.start()
        try {
            Thread.sleep(2500) // let the seeder finish checking its files
            engine.add(
                DownloadRecord(hash, "m1", "Film", "Film 2026 1080p", null, Magnet.build(hash, "Film"), File(root, "dl").absolutePath, 1),
                listOf(TcpEndpoint("127.0.0.1", 16881)),
            )
            val deadline = System.currentTimeMillis() + 20_000
            while (engine.videoFiles(hash).isEmpty() && System.currentTimeMillis() < deadline) Thread.sleep(200)
            val files = engine.videoFiles(hash)
            assertEquals(1, files.size, "metadata should be available from the .torrent file")
            val stream = engine.openStream(hash, files[0].index)
            StreamServer(stream).use { server ->
                // Like a player: the head of the file first, then a seek into the middle, then the tail.
                fun range(from: Long, to: Long): ByteArray {
                    val c = URL(server.url).openConnection() as HttpURLConnection
                    c.readTimeout = 60_000
                    c.setRequestProperty("Range", "bytes=$from-$to")
                    assertEquals(206, c.responseCode)
                    return c.inputStream.readBytes()
                }
                val head = range(0, 999_999)
                assertTrue(head.contentEquals(content.copyOfRange(0, 1_000_000)), "head differs")
                val middle = range(12_000_000, 12_499_999)
                assertTrue(middle.contentEquals(content.copyOfRange(12_000_000, 12_500_000)), "middle differs")
                val tail = range(content.size - 300_000L, content.size - 1L)
                assertTrue(tail.contentEquals(content.copyOfRange(content.size - 300_000, content.size)), "tail differs")
            }
            stream.close()
            val state = engine.state(hash)!!
            assertTrue(state.phase != DownloadPhase.Error, "state: $state")
        } finally {
            engine.stop(); seeder.stop(); root.deleteRecursively()
        }
        assertTrue(video.name == "film.mkv")
    }
}
