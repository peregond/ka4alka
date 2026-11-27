package cc.ka4alka.tv

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import cc.ka4alka.core.DownloadPhase
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

/** Keeps the process (and so the torrent engine) alive while something is downloading. Stops itself when the queue is idle. */
class DownloadService : Service() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private var watcher: Job? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val manager = getSystemService(NotificationManager::class.java)
        if (Build.VERSION.SDK_INT >= 26) manager.createNotificationChannel(NotificationChannel(CHANNEL, "Загрузки", NotificationManager.IMPORTANCE_LOW))
        val notification = build("Идёт загрузка")
        if (Build.VERSION.SDK_INT >= 29) startForeground(ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC) else startForeground(ID, notification)
        watcher?.cancel()
        watcher = scope.launch {
            val app = KachalkaApp.of(this@DownloadService)
            var idleRounds = 0
            while (isActive) {
                val states = app.engine.states()
                val active = states.filter { it.phase == DownloadPhase.Downloading || it.phase == DownloadPhase.Starting || it.phase == DownloadPhase.Checking }
                idleRounds = if (active.isEmpty()) idleRounds + 1 else 0
                if (idleRounds >= 6) { stopSelf(); return@launch }
                if (active.isNotEmpty()) {
                    val speed = active.sumOf { it.downloadRate.toLong() } / 1024
                    manager.notify(ID, build("Загружается: ${active.size} · ${speed} КБ/с"))
                }
                delay(5000)
            }
        }
        return START_NOT_STICKY
    }

    private fun build(text: String): Notification {
        val builder = if (Build.VERSION.SDK_INT >= 26) Notification.Builder(this, CHANNEL) else @Suppress("DEPRECATION") Notification.Builder(this)
        return builder.setSmallIcon(android.R.drawable.stat_sys_download).setContentTitle("Качалка").setContentText(text).setOngoing(true).build()
    }

    override fun onDestroy() { scope.cancel(); super.onDestroy() }

    companion object {
        private const val CHANNEL = "downloads"
        private const val ID = 7
        /** Starts the service when there is work for it; harmless when it already runs. */
        fun sync(context: Context) {
            val app = KachalkaApp.of(context)
            val busy = runCatching { app.engine.states().any { it.phase != DownloadPhase.Done && it.phase != DownloadPhase.Paused } }.getOrDefault(false)
            if (busy) runCatching {
                val intent = Intent(context, DownloadService::class.java)
                if (Build.VERSION.SDK_INT >= 26) context.startForegroundService(intent) else context.startService(intent)
            }
        }
    }
}
