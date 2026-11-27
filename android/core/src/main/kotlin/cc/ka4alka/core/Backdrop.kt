package cc.ka4alka.core

import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonObject
import java.net.URLEncoder
import java.util.concurrent.ConcurrentHashMap

/** Wide artwork for the banner and the title page, from the same server the desktop apps ask. Never needs a TMDB key in the app. */
class BackdropClient(
    private val http: Http,
    private val bases: List<String> = listOf(
        "https://catalog-production-e329.up.railway.app/",
        "https://ka4alka-online-new.peregon.chatgpt.site/",
    ),
) {
    private val cache = ConcurrentHashMap<String, String>()
    private val none = ConcurrentHashMap.newKeySet<String>()

    /** The landscape image address for a catalog id, or null when the title has none (or the server cannot be reached). */
    fun backdrop(id: String): String? {
        cache[id]?.let { return it }
        if (id in none) return null
        for (base in bases) {
            try {
                val bytes = http.get("${base}api/backdrop?id=${URLEncoder.encode(id, "UTF-8")}&language=ru", 64 * 1024)
                val url = parse(bytes, id)
                if (url != null) { cache[id] = url; return url }
                none.add(id); return null
            } catch (e: Exception) { /* try the next mirror */ }
        }
        return null
    }

    companion object {
        private val valid = Regex("^https://image\\.tmdb\\.org/t/p/w1280/[a-zA-Z0-9_-]+\\.jpg$")

        fun parse(bytes: ByteArray, id: String): String? {
            val root = Json.parseToJsonElement(bytes.decodeToString()).jsonObject
            require((root["id"] as? JsonPrimitive)?.contentOrNull == id) { "The backdrop belongs to another card" }
            val backdrop = root["backdrop"] ?: throw IllegalArgumentException("Unexpected answer")
            if (backdrop is JsonNull) return null
            val url = ((backdrop as? JsonObject)?.get("url") as? JsonPrimitive)?.contentOrNull
            require(url != null && valid.matches(url)) { "Unexpected backdrop address" }
            return url
        }
    }
}
