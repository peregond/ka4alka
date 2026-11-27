package cc.ka4alka.tv

import android.app.Application
import android.content.Context
import cc.ka4alka.core.BackdropClient
import cc.ka4alka.core.JavaHttp
import cc.ka4alka.core.TorrentEngine
import cc.ka4alka.core.Updater
import java.io.File
import java.util.concurrent.Executors

/** Process-wide services. Everything heavy is created on first use. */
class KachalkaApp : Application() {
    val http by lazy { JavaHttp("Kachalka-TV/${BuildConfig.VERSION_NAME}") }
    val prefs by lazy { Prefs(this) }
    val repository by lazy { CatalogRepository(this, http) }
    val backdrops by lazy { BackdropClient(http) }
    val engine: TorrentEngine by lazy { TorrentEngine(File(filesDir, "engine")) }
    val updater by lazy { Updater(http, publicKey(), BuildConfig.VERSION_CODE.toLong()) }
    private val startup = Executors.newSingleThreadExecutor()
    @Volatile private var engineStarted = false

    /** Starts the torrent engine once; saved downloads resume with it. Safe to call from anywhere. */
    @Synchronized
    fun ensureEngine() {
        if (engineStarted) return
        engine.start()
        engine.setLimits(prefs.downloadLimitKbps, prefs.uploadLimitKbps)
        engineStarted = true
        DownloadService.sync(this)
    }

    override fun onCreate() {
        super.onCreate()
        // Resume the queue in the background so a restart does not leave downloads idle.
        startup.execute { if (prefs.hasDownloads(this)) runCatching { ensureEngine() } }
    }

    private fun publicKey(): String =
        resources.openRawResource(R.raw.update_public).bufferedReader().use { it.readText() }

    companion object {
        fun of(context: Context): KachalkaApp = context.applicationContext as KachalkaApp
    }
}
