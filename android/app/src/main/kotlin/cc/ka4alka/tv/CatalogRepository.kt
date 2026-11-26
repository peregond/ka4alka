package cc.ka4alka.tv

import android.content.Context
import cc.ka4alka.core.CatalogManifest
import cc.ka4alka.core.CatalogParser
import cc.ka4alka.core.Http
import cc.ka4alka.core.Library
import cc.ka4alka.core.sha256Hex
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.withContext
import java.io.File

sealed interface LibraryState {
    data object Loading : LibraryState
    data class Ready(val library: Library, val refreshing: Boolean = false, val offline: Boolean = false) : LibraryState
    data class Failed(val reason: String) : LibraryState
}

/**
 * The shared library, the same file the desktop apps read. A saved copy opens at once; a newer one is fetched
 * in the background and swapped in when it passes its checksum.
 */
class CatalogRepository(context: Context, private val http: Http) {
    private val dir = File(context.filesDir, "library").apply { mkdirs() }
    private val snapshot = File(dir, "catalog.json.gz")
    private val revisionFile = File(dir, "revision.txt")
    private val mutable = MutableStateFlow<LibraryState>(LibraryState.Loading)
    val state: StateFlow<LibraryState> = mutable

    suspend fun load() = withContext(Dispatchers.IO) {
        val cached = if (snapshot.exists()) runCatching { snapshot.inputStream().buffered().use { CatalogParser.parseGzip(it) } }.getOrNull() else null
        if (cached != null) mutable.value = LibraryState.Ready(cached, refreshing = true)
        try {
            val manifest = CatalogManifest.parse(http.get(CatalogManifest.URL, 64 * 1024).decodeToString())
            if (cached != null && revisionFile.exists() && revisionFile.readText() == manifest.revision) {
                mutable.value = LibraryState.Ready(cached)
                return@withContext
            }
            val bytes = http.get(manifest.downloadUrl, manifest.bytes.toInt() + 1024)
            check(bytes.size.toLong() == manifest.bytes && sha256Hex(bytes) == manifest.sha256) { "Контрольная сумма библиотеки не совпала" }
            val library = CatalogParser.parseGzip(bytes.inputStream())
            snapshot.writeBytes(bytes)
            revisionFile.writeText(manifest.revision)
            mutable.value = LibraryState.Ready(library)
        } catch (e: Exception) {
            mutable.value = if (cached != null) LibraryState.Ready(cached, offline = true)
            else LibraryState.Failed("Не удалось загрузить библиотеку: ${e.message ?: e.javaClass.simpleName}")
        }
    }
}
