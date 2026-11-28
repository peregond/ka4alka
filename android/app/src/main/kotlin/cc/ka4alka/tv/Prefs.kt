package cc.ka4alka.tv

import android.content.Context
import android.os.Environment
import java.io.File

/** Small settings and the saved list; everything else lives in the engine and the library cache. */
class Prefs(private val context: Context) {
    private val store = context.getSharedPreferences("kachalka", Context.MODE_PRIVATE)

    var saved: Set<String>
        get() = store.getStringSet("saved", emptySet()) ?: emptySet()
        set(value) { store.edit().putStringSet("saved", value.toSet()).apply() }

    var folder: String?
        get() = store.getString("folder", null)
        set(value) { store.edit().putString("folder", value).apply() }

    var downloadLimitKbps: Int
        get() = store.getInt("downloadLimit", 0)
        set(value) { store.edit().putInt("downloadLimit", value).apply() }

    var uploadLimitKbps: Int
        get() = store.getInt("uploadLimit", 0)
        set(value) { store.edit().putInt("uploadLimit", value).apply() }

    var autoCheckUpdates: Boolean
        get() = store.getBoolean("autoCheckUpdates", true)
        set(value) { store.edit().putBoolean("autoCheckUpdates", value).apply() }

    var lastUpdateCheck: Long
        get() = store.getLong("lastUpdateCheck", 0)
        set(value) { store.edit().putLong("lastUpdateCheck", value).apply() }

    var computerHost: String?
        get() = store.getString("computerHost", null)
        set(value) { store.edit().putString("computerHost", value).apply() }

    var computerPort: Int
        get() = store.getInt("computerPort", 45834)
        set(value) { store.edit().putInt("computerPort", value).apply() }

    var computerToken: String?
        get() = store.getString("computerToken", null)
        set(value) { store.edit().putString("computerToken", value).apply() }

    /** The "download on your computer" hint: how many times it was shown and whether the user asked not to see it again. */
    var linkTipHidden: Boolean
        get() = store.getBoolean("linkTipHidden", false)
        set(value) { store.edit().putBoolean("linkTipHidden", value).apply() }

    var linkTipShown: Int
        get() = store.getInt("linkTipShown", 0)
        set(value) { store.edit().putInt("linkTipShown", value).apply() }

    /** The favourites marks exchanged with the computer (see FavoriteSync). */
    var favoriteMarks: String?
        get() = store.getString("favoriteMarks", null)
        set(value) { store.edit().putString("favoriteMarks", value).apply() }

    fun position(key: String): Long = store.getLong("pos:$key", 0)
    fun setPosition(key: String, value: Long) { store.edit().putLong("pos:$key", value).apply() }

    /** Folders the user can pick: the app's own space on every volume the device offers (internal storage, USB drives, SD cards). */
    fun folders(): List<Pair<String, File>> {
        val dirs = context.getExternalFilesDirs(Environment.DIRECTORY_MOVIES).filterNotNull()
        return dirs.mapIndexed { index, dir -> (if (index == 0) "Память устройства" else "Накопитель ${index}") to dir }
    }

    /** The folder downloads go to; falls back to the device's own space if a chosen drive was removed. */
    fun downloadDir(): File {
        val chosen = folder?.let { File(it) }?.takeIf { it.isDirectory && it.canWrite() }
        val dir = chosen ?: folders().firstOrNull()?.second ?: File(context.filesDir, "Movies")
        return File(dir, "Ka4alka").apply { mkdirs() }
    }

    fun hasDownloads(context: Context): Boolean = File(context.filesDir, "engine/downloads.json").let { it.exists() && it.length() > 4 }
}
