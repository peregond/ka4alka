package cc.ka4alka.tv

import android.app.Application
import androidx.compose.runtime.mutableStateListOf
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import cc.ka4alka.core.AndroidManifest
import cc.ka4alka.core.ComputerClient
import cc.ka4alka.core.ComputerDownload
import cc.ka4alka.core.ComputerException
import cc.ka4alka.core.LanScanner
import cc.ka4alka.core.DownloadPhase
import cc.ka4alka.core.DownloadRecord
import cc.ka4alka.core.DownloadState
import cc.ka4alka.core.Release
import cc.ka4alka.core.ReleaseSearch
import cc.ka4alka.core.StreamServer
import cc.ka4alka.core.Title
import cc.ka4alka.core.TorrentStream
import cc.ka4alka.core.UpdateCheck
import cc.ka4alka.core.VideoFile
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.flowOn
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File

sealed interface Screen {
    data object Home : Screen
    data object Movies : Screen
    data object Series : Screen
    data object Search : Screen
    data object Saved : Screen
    data object Downloads : Screen
    data object Computer : Screen
    data object Settings : Screen
    data class Detail(val id: String) : Screen
    data object Player : Screen
}

val Screen.isTopLevel: Boolean get() = this !is Screen.Detail && this !is Screen.Player

sealed interface Releases {
    data object Loading : Releases
    data class Loaded(val items: List<Release>, val failed: List<String>) : Releases
}

/** What the "watch" action is doing right now. */
sealed interface Playback {
    data object Idle : Playback
    data class Preparing(val title: String, val release: String, val seconds: Int, val peers: Int) : Playback
    data class ChooseFile(val title: Title, val downloadId: String, val files: List<VideoFile>) : Playback
    data class Playing(val title: String, val url: String, val downloadId: String, val resumeKey: String) : Playback
    data class Failed(val message: String) : Playback
}

/** The computer this TV sends downloads to. */
data class ComputerUi(
    val host: String? = null,
    val linked: Boolean = false,
    val expired: Boolean = false,
    val scanning: Boolean = false,
    val found: List<String> = emptyList(),
    val busy: Boolean = false,
    val message: String? = null,
    val downloads: List<ComputerDownload> = emptyList(),
)

sealed interface UpdateUi {
    data object Idle : UpdateUi
    data object Checking : UpdateUi
    data object UpToDate : UpdateUi
    data class Available(val manifest: AndroidManifest) : UpdateUi
    data class Downloading(val manifest: AndroidManifest, val done: Long, val total: Long) : UpdateUi
    data class NeedsPermission(val manifest: AndroidManifest, val apk: File) : UpdateUi
    data class Installing(val manifest: AndroidManifest) : UpdateUi
    data class Failed(val reason: String) : UpdateUi
}

class AppViewModel(application: Application) : AndroidViewModel(application) {
    private val app = KachalkaApp.of(application)
    val library: StateFlow<LibraryState> = app.repository.state

    val stack = mutableStateListOf<Screen>(Screen.Home)
    val current: Screen get() = stack.last()

    val saved = MutableStateFlow(app.prefs.saved)
    val playback = MutableStateFlow<Playback>(Playback.Idle)
    val update = MutableStateFlow<UpdateUi>(UpdateUi.Idle)
    val releases = MutableStateFlow<Map<String, Releases>>(emptyMap())
    val backdrops = MutableStateFlow<Map<String, String?>>(emptyMap())

    /** The queue, refreshed once a second while a screen shows it. */
    val downloads: StateFlow<List<DownloadState>> = flow {
        while (true) {
            emit(if (engineReady()) app.engine.states() else emptyList())
            delay(1000)
        }
    }.flowOn(Dispatchers.IO).stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    val computer = MutableStateFlow(
        ComputerUi(host = app.prefs.computerHost, linked = app.prefs.computerHost != null && app.prefs.computerToken != null),
    )
    /** True while the one-time "you can download on your computer" hint is on screen. */
    val linkTip = MutableStateFlow(false)
    private var pendingAfterTip: (() -> Unit)? = null

    private var stream: TorrentStream? = null
    private var server: StreamServer? = null
    private var playbackJob: Job? = null

    init {
        viewModelScope.launch { app.repository.load() }
        viewModelScope.launch { autoCheckUpdates() }
        viewModelScope.launch { showLinkTipAtStart() }
        viewModelScope.launch { pollComputer() }
    }

    private fun engineReady(): Boolean = runCatching { app.ensureEngine(); true }.getOrDefault(false)

    // ---- navigation

    fun open(screen: Screen) {
        if (screen.isTopLevel) { stack.clear(); stack.add(screen) } else stack.add(screen)
    }

    /** Opens a top-level page by name; used by the launcher intent (and by the screenshot script). */
    fun openByName(name: String?) {
        when (name) {
            "home" -> open(Screen.Home); "movies" -> open(Screen.Movies); "series" -> open(Screen.Series)
            "search" -> open(Screen.Search); "saved" -> open(Screen.Saved); "downloads" -> open(Screen.Downloads); "settings" -> open(Screen.Settings)
        }
    }

    /** Returns false when there is nothing left to go back to. */
    fun back(): Boolean {
        if (current is Screen.Player) { stopPlayback(); stack.removeAt(stack.lastIndex); return true }
        if (stack.size > 1) { stack.removeAt(stack.lastIndex); return true }
        if (current != Screen.Home) { open(Screen.Home); return true }
        return false
    }

    // ---- saved

    fun toggleSaved(id: String) {
        val next = if (id in saved.value) saved.value - id else saved.value + id
        saved.value = next
        app.prefs.saved = next
    }

    // ---- title page data

    fun loadReleases(title: Title, force: Boolean = false) {
        if (!force && releases.value[title.id] != null) return
        releases.value = releases.value + (title.id to Releases.Loading)
        viewModelScope.launch {
            val result = withContext(Dispatchers.IO) { ReleaseSearch(app.http).run(title) }
            releases.value = releases.value + (title.id to Releases.Loaded(result.releases, result.failed))
        }
    }

    fun loadBackdrop(title: Title) {
        if (backdrops.value.containsKey(title.id)) return
        backdrops.value = backdrops.value + (title.id to null)
        viewModelScope.launch {
            val url = withContext(Dispatchers.IO) { app.backdrops.backdrop(title.id) }
            if (url != null) backdrops.value = backdrops.value + (title.id to url)
        }
    }

    // ---- downloads

    // One folder per release: two releases with the same file name must never share a file, nor delete each other's video.
    private fun record(title: Title, release: Release) = DownloadRecord(
        id = release.hash, mediaId = title.id, title = title.title, releaseTitle = release.title, poster = title.poster,
        magnet = release.magnet, savePath = File(app.prefs.downloadDir(), release.hash.take(10).lowercase()).absolutePath, addedUtc = System.currentTimeMillis(),
    )

    fun download(title: Title, release: Release) {
        viewModelScope.launch(Dispatchers.IO) {
            app.ensureEngine()
            app.engine.add(record(title, release))
            DownloadService.sync(app)
        }
    }

    fun pause(id: String) { viewModelScope.launch(Dispatchers.IO) { app.engine.pause(id) } }
    fun resume(id: String) { viewModelScope.launch(Dispatchers.IO) { app.engine.resumeDownload(id); DownloadService.sync(app) } }
    fun remove(id: String, deleteFiles: Boolean) { viewModelScope.launch(Dispatchers.IO) { app.engine.remove(id, deleteFiles) } }
    fun applyLimits() { viewModelScope.launch(Dispatchers.IO) { if (engineReady()) app.engine.setLimits(app.prefs.downloadLimitKbps, app.prefs.uploadLimitKbps) } }

    // ---- watching while it downloads

    fun watch(title: Title, release: Release) {
        playbackJob?.cancel()
        playback.value = Playback.Preparing(title.title, release.title, 0, 0)
        if (current !is Screen.Player) stack.add(Screen.Player)
        playbackJob = viewModelScope.launch(Dispatchers.IO) {
            try {
                app.ensureEngine()
                app.engine.add(record(title, release))
                DownloadService.sync(app)
                val started = System.currentTimeMillis()
                var files = emptyList<VideoFile>()
                while (isActive && files.isEmpty()) {
                    val state = app.engine.state(release.hash)
                    if (state?.phase == DownloadPhase.Error && state.error != null) throw IllegalStateException(state.error)
                    val seconds = ((System.currentTimeMillis() - started) / 1000).toInt()
                    if (seconds > 150) throw IllegalStateException("Не нашлось источников для этой раздачи. Попробуй другую.")
                    playback.value = Playback.Preparing(title.title, release.title, seconds, state?.peers ?: 0)
                    files = app.engine.videoFiles(release.hash)
                    if (files.isEmpty()) delay(500)
                }
                if (files.size == 1 || title.section == cc.ka4alka.core.Section.Movies && files.maxOf { it.size } > files.sumOf { it.size } * 0.6) {
                    startStream(title.title, release.hash, files.maxBy { it.size }, title.id)
                } else {
                    playback.value = Playback.ChooseFile(title, release.hash, files)
                }
            } catch (e: kotlinx.coroutines.CancellationException) {
                throw e
            } catch (e: Exception) {
                playback.value = Playback.Failed(e.message ?: "Не удалось начать просмотр")
            }
        }
    }

    /** Plays a download that is already in the queue (from the downloads screen). */
    fun watchDownload(state: DownloadState) {
        playbackJob?.cancel()
        playback.value = Playback.Preparing(state.record.title, state.record.releaseTitle, 0, state.peers)
        if (current !is Screen.Player) stack.add(Screen.Player)
        playbackJob = viewModelScope.launch(Dispatchers.IO) {
            try {
                app.ensureEngine()
                if (state.record.paused) app.engine.resumeDownload(state.record.id)
                val files = app.engine.videoFiles(state.record.id)
                when {
                    files.isEmpty() -> throw IllegalStateException("В этой раздаче нет видеофайлов или список файлов ещё не получен.")
                    files.size == 1 -> startStream(state.record.title, state.record.id, files[0], state.record.mediaId ?: state.record.id)
                    else -> {
                        val title = state.record.mediaId?.let { id -> (library.value as? LibraryState.Ready)?.library?.get(id) }
                            ?: Title(state.record.id, cc.ka4alka.core.Section.Series, state.record.title, 0, null, null, null, null, null, "", emptyList(), emptyList(), "", emptyList(), emptyList(), "")
                        playback.value = Playback.ChooseFile(title, state.record.id, files)
                    }
                }
            } catch (e: kotlinx.coroutines.CancellationException) {
                throw e
            } catch (e: Exception) {
                playback.value = Playback.Failed(e.message ?: "Не удалось начать просмотр")
            }
        }
    }

    fun chooseFile(choice: Playback.ChooseFile, file: VideoFile) {
        playbackJob?.cancel()
        playback.value = Playback.Preparing(choice.title.title, file.name.substringAfterLast('/'), 0, 0)
        playbackJob = viewModelScope.launch(Dispatchers.IO) {
            try { startStream(choice.title.title, choice.downloadId, file, choice.title.id) }
            catch (e: kotlinx.coroutines.CancellationException) { throw e }
            catch (e: Exception) { playback.value = Playback.Failed(e.message ?: "Не удалось начать просмотр") }
        }
    }

    private fun startStream(title: String, downloadId: String, file: VideoFile, mediaId: String) {
        closeStream()
        val opened = app.engine.openStream(downloadId, file.index)
        stream = opened
        val started = StreamServer(opened)
        server = started
        playback.value = Playback.Playing(title, started.url, downloadId, "$downloadId:${file.index}")
    }

    private fun closeStream() {
        runCatching { server?.close() }
        runCatching { stream?.close() }
        server = null; stream = null
    }

    fun stopPlayback() {
        playbackJob?.cancel()
        closeStream()
        playback.value = Playback.Idle
    }

    fun savePosition(key: String, millis: Long) = app.prefs.setPosition(key, millis)
    fun position(key: String): Long = app.prefs.position(key)

    // ---- updates

    private suspend fun autoCheckUpdates() {
        if (!app.prefs.autoCheckUpdates) return
        if (System.currentTimeMillis() - app.prefs.lastUpdateCheck < 6 * 3600_000L) return
        delay(4000)
        checkForUpdates(silent = true)
    }

    fun checkForUpdates(silent: Boolean = false) {
        if (update.value is UpdateUi.Checking || update.value is UpdateUi.Downloading) return
        if (!silent) update.value = UpdateUi.Checking
        viewModelScope.launch {
            val result = withContext(Dispatchers.IO) { app.updater.check() }
            app.prefs.lastUpdateCheck = System.currentTimeMillis()
            update.value = when (result) {
                is UpdateCheck.Available -> UpdateUi.Available(result.manifest)
                UpdateCheck.UpToDate -> if (silent) UpdateUi.Idle else UpdateUi.UpToDate
                is UpdateCheck.Failed -> if (silent) UpdateUi.Idle else UpdateUi.Failed(result.reason)
            }
        }
    }

    fun installUpdate(manifest: AndroidManifest) {
        viewModelScope.launch {
            val dir = File(app.cacheDir, "updates")
            val target = File(dir, manifest.apk.fileName)
            try {
                update.value = UpdateUi.Downloading(manifest, 0, manifest.apk.size)
                withContext(Dispatchers.IO) {
                    dir.listFiles()?.filter { it.name != manifest.apk.fileName && it.name != manifest.apk.fileName + ".part" }?.forEach { it.delete() }
                    app.updater.download(manifest, target, progress = { done, total -> update.value = UpdateUi.Downloading(manifest, done, total) })
                }
                continueInstall(manifest, target)
            } catch (e: Exception) {
                update.value = UpdateUi.Failed(e.message ?: "Не удалось скачать обновление")
            }
        }
    }

    /** Called again when the user comes back from the permission page. */
    fun continueInstall(manifest: AndroidManifest, apk: File) {
        if (!ApkInstaller.canInstall(app)) {
            update.value = UpdateUi.NeedsPermission(manifest, apk)
            ApkInstaller.openPermissionPage(app)
            return
        }
        UpdateEvents.failure = null
        update.value = UpdateUi.Installing(manifest)
        runCatching { ApkInstaller.install(app, apk) }.onFailure { update.value = UpdateUi.Failed(it.message ?: "Установка не удалась") }
    }

    fun resumeAfterPermission() {
        val state = update.value
        if (state is UpdateUi.NeedsPermission && ApkInstaller.canInstall(app)) continueInstall(state.manifest, state.apk)
        UpdateEvents.failure?.let { update.value = UpdateUi.Failed(it); UpdateEvents.failure = null }
    }

    // ---- the computer on the home network

    private fun client(): ComputerClient? {
        val host = app.prefs.computerHost ?: return null
        return ComputerClient(host, app.prefs.computerPort, app.prefs.computerToken)
    }

    fun scanForComputers() {
        if (computer.value.scanning) return
        computer.value = computer.value.copy(scanning = true, message = null)
        viewModelScope.launch {
            val found = withContext(Dispatchers.IO) { LanScanner.scan(LanScanner.localPrefixes()) }
            computer.value = computer.value.copy(scanning = false, found = found,
                message = if (found.isEmpty()) "Компьютеров с включённым доступом не нашлось. Включи его на компьютере (шаг 1) и поищи ещё раз или введи адрес вручную." else null)
        }
    }

    /** [address] is what the user typed or picked; [code] is the six digits shown on the computer. */
    fun linkComputer(address: String, code: String) {
        val parsed = ComputerClient.parseAddress(address)
        if (parsed == null) { computer.value = computer.value.copy(message = "Адрес выглядит неверно. Он должен быть вида 192.168.1.25 (его показывает компьютер)."); return }
        if (code.count { it.isDigit() } != 6) { computer.value = computer.value.copy(message = "Код состоит из шести цифр. Его показывает компьютер."); return }
        computer.value = computer.value.copy(busy = true, message = null)
        viewModelScope.launch {
            try {
                val token = withContext(Dispatchers.IO) { ComputerClient(parsed.first, parsed.second).pair(code) }
                app.prefs.computerHost = parsed.first; app.prefs.computerPort = parsed.second; app.prefs.computerToken = token
                computer.value = computer.value.copy(host = parsed.first, linked = true, expired = false, busy = false, message = "Готово: компьютер подключён.")
            } catch (e: ComputerException) {
                computer.value = computer.value.copy(busy = false, message = e.message)
            }
        }
    }

    fun unlinkComputer() {
        viewModelScope.launch(Dispatchers.IO) { runCatching { client()?.logout() } }
        app.prefs.computerHost = null; app.prefs.computerToken = null
        computer.value = ComputerUi(message = "Компьютер отключён.")
    }

    /** Sends a release to the linked computer's own download queue. */
    fun sendToComputer(title: Title, release: Release) {
        val link = client() ?: run { open(Screen.Computer); return }
        computer.value = computer.value.copy(message = null)
        viewModelScope.launch {
            try {
                withContext(Dispatchers.IO) { link.add(release.magnet) }
                computer.value = computer.value.copy(message = "«${title.title}» отправлено на компьютер ${link.host}.")
            } catch (e: ComputerException) {
                if (e.unauthorized) computer.value = computer.value.copy(expired = true, message = "Подключение к компьютеру истекло. Введи новый код.")
                else computer.value = computer.value.copy(message = e.message)
            }
        }
    }

    fun clearComputerMessage() { computer.value = computer.value.copy(message = null) }

    fun computerControl(id: String, pause: Boolean) {
        val link = client() ?: return
        viewModelScope.launch(Dispatchers.IO) { runCatching { link.control(id, if (pause) "pause" else "resume") } }
    }

    private suspend fun pollComputer() {
        while (true) {
            val link = if (current is Screen.Computer) client() else null
            if (link != null) {
                try {
                    val rows = withContext(Dispatchers.IO) { link.downloads() }
                    computer.value = computer.value.copy(downloads = rows, expired = false)
                } catch (e: ComputerException) {
                    if (e.unauthorized) computer.value = computer.value.copy(expired = true, downloads = emptyList())
                }
            }
            delay(3000)
        }
    }

    // ---- the one-time hint

    private suspend fun showLinkTipAtStart() {
        delay(9000)
        if (!app.prefs.linkTipHidden && app.prefs.computerHost == null && app.prefs.linkTipShown < 2 && current !is Screen.Player) {
            app.prefs.linkTipShown += 1
            linkTip.value = true
        }
    }

    /** Wraps a download: before the first one the hint may appear once, then the download goes on. */
    fun downloadWithTip(action: () -> Unit) {
        if (!app.prefs.linkTipHidden && app.prefs.computerHost == null && app.prefs.linkTipShown < 3) {
            app.prefs.linkTipShown += 1
            pendingAfterTip = action
            linkTip.value = true
        } else action()
    }

    fun closeLinkTip(hideForever: Boolean = false, setup: Boolean = false) {
        if (hideForever) app.prefs.linkTipHidden = true
        linkTip.value = false
        pendingAfterTip?.invoke(); pendingAfterTip = null
        if (setup) open(Screen.Computer)
    }

    override fun onCleared() { closeStream(); super.onCleared() }
}
