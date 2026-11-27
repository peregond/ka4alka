package cc.ka4alka.core

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.longOrNull
import java.net.URLEncoder
import kotlin.math.ln

/** A downloadable variant of a title found at a tracker index. */
data class Release(
    val id: String,
    val title: String,
    val source: String,
    val hash: String,
    val size: Long?,
    val seeds: Int,
    val leechers: Int,
    val resolution: Int,
    val poor: Boolean,
    val russianAudio: Boolean,
    val englishAudio: Boolean,
    val dubbed: Boolean,
    val subtitles: Boolean,
    val hdr: Boolean,
    val season: Int?,
    val episode: Int?,
    val recommended: Boolean = false,
) {
    val magnet: String get() = Magnet.build(hash, title)
    val qualityLabel: String
        get() = when {
            poor -> "Экранка"
            resolution >= 2160 -> "4K"
            resolution >= 1080 -> "Full HD"
            resolution >= 720 -> "HD"
            resolution > 0 -> "SD"
            else -> "?"
        }
}

object Magnet {
    val TRACKERS = listOf(
        "udp://tracker.opentrackr.org:1337/announce",
        "udp://open.stealth.si:80/announce",
        "udp://tracker.torrent.eu.org:451/announce",
        "udp://exodus.desync.com:6969/announce",
    )

    fun build(hash: String, name: String): String =
        "magnet:?xt=urn:btih:${hash.uppercase()}&dn=${enc(name)}" + TRACKERS.joinToString("") { "&tr=${enc(it)}" }

    fun hashOf(uri: String): String? =
        Regex("(?i)(?:^|[?&])xt=urn:btih:([a-f0-9]{40})(?:&|$)").find(uri)?.groupValues?.get(1)?.uppercase()

    private fun enc(value: String) = URLEncoder.encode(value, "UTF-8").replace("+", "%20")
}

object ReleaseInfo {
    // Word edges are spelled out: Java's \\b does not treat Cyrillic as word characters.
    private const val B = "(?<![\\p{L}\\p{N}])"
    private const val E = "(?![\\p{L}\\p{N}])"
    private val poorPattern = Regex("(?iu)$B(camrip|cam-rip|hdcam|hd-cam|cam|ts|hdts|telesync|telecine|tc|dvdscr|scr|screener|vhsrip|workprint)$E|экранк|камрип")
    private val dubPattern = Regex("(?iu)дубляж|${B}dub$E|лицензи")
    private val russianAudioPattern = Regex("(?iu)дубляж|многоголос|${B}(mvo|dvo|avo|vo|dub|l1|l2)$E|озвучк|${B}рус|${B}rus$E|лицензи|профессиональн|закадров|двухголос|одноголос")
    private val englishAudioPattern = Regex("(?iu)${B}(eng|en)$E|english|original|оригинал")
    private val subtitlePattern = Regex("(?iu)${B}(sub|subs|srt|forced)$E|субтитр")
    private val hdrPattern = Regex("(?iu)${B}hdr(10\\+?)?$E|dolby ?vision|${B}dv$E|hlg")

    fun resolution(title: String): Int {
        val t = title.lowercase()
        return when {
            Regex("2160|${B}4k$E|${B}uhd$E").containsMatchIn(t) -> 2160
            Regex("1080|full ?hd|${B}fhd$E").containsMatchIn(t) -> 1080
            Regex("720|${B}hdtv$E|hd ?ready").containsMatchIn(t) -> 720
            Regex("480|${B}(dvdrip|satrip|webrip|dvd)$E").containsMatchIn(t) -> 480
            else -> 0
        }
    }

    fun season(title: String): Pair<Int?, Int?> {
        Regex("(?iu)${B}S(\\d{1,2})\\s*E(\\d{1,3})$E").find(title)?.let { return it.groupValues[1].toInt() to it.groupValues[2].toInt() }
        Regex("(?iu)${B}S(\\d{1,2})(?:\\s*[-–]\\s*S?\\d{1,2})?$E").find(title)?.let { return it.groupValues[1].toInt() to null }
        Regex("(?iu)(?:сезон[ыа]?|season)[:\\s]*(\\d{1,2})").find(title)?.let { return it.groupValues[1].toInt() to null }
        Regex("(?iu)(\\d{1,2})\\s*сезон").find(title)?.let { return it.groupValues[1].toInt() to null }
        return null to null
    }

    fun make(id: String, title: String, source: String, hash: String, size: Long?, seeds: Int, leechers: Int): Release {
        val poor = poorPattern.containsMatchIn(title)
        val dub = dubPattern.containsMatchIn(title)
        val russian = russianAudioPattern.containsMatchIn(title) || title.any { it in 'а'..'я' || it in 'А'..'Я' } && !englishAudioPattern.containsMatchIn(title)
        val (season, episode) = season(title)
        return Release(
            id = id, title = title, source = source, hash = hash.uppercase(), size = size, seeds = seeds, leechers = leechers,
            resolution = resolution(title), poor = poor, russianAudio = russian, englishAudio = englishAudioPattern.containsMatchIn(title),
            dubbed = dub, subtitles = subtitlePattern.containsMatchIn(title), hdr = hdrPattern.containsMatchIn(title),
            season = season, episode = episode,
        )
    }

    /** Does a tracker row belong to this title? A release must carry the title's name; films must carry the year too. */
    fun matches(item: Title, release: String): Boolean {
        val folded = " " + TextNorm.fold(release) + " "
        val names = listOfNotNull(item.originalTitle, item.title).map { TextNorm.fold(it) }.filter { it.length >= 2 }
        if (names.none { folded.contains(" $it ") || folded.contains(" ${it.replace(" ", "")} ") }) return false
        if (item.section == Section.Movies && item.year > 0) {
            if (!Regex("(?<!\\d)(${item.year})(?!\\d)").containsMatchIn(release)) return false
        }
        return true
    }

    /** Best first: watchable quality, Russian audio, healthy swarm. The first row that is clearly good is marked recommended. */
    fun rank(rows: List<Release>): List<Release> {
        fun score(r: Release): Double {
            var s = ln(1.0 + r.seeds) * 10
            s += when { r.resolution == 1080 -> 40.0; r.resolution == 2160 -> 30.0; r.resolution == 720 -> 18.0; r.resolution > 0 -> 4.0; else -> 0.0 }
            if (r.russianAudio) s += 25
            if (r.dubbed) s += 8
            if (r.poor) s -= 200
            if (r.seeds == 0) s -= 60
            return s
        }
        val sorted = rows.sortedByDescending { score(it) }
        val pick = sorted.firstOrNull { !it.poor && it.seeds >= 3 && it.russianAudio && it.resolution >= 720 }
        return sorted.map { if (it === pick) it.copy(recommended = true) else it }
    }
}

object KnabenSource {
    const val NAME = "RuTracker"
    fun url(query: String) =
        "https://api.knaben.org/v2/search?q=${URLEncoder.encode(query.trim(), "UTF-8")}&sf=title&t=rutracker,thepiratebay&o=seeders&d=desc&s=150"

    private val video = Regex("(?i)(movie|tv|anime|video|film|кино|сериал)")

    fun parse(data: ByteArray, item: Title): List<Release> {
        val root = Json.parseToJsonElement(data.decodeToString()).jsonObject
        val hits = (root["hits"] as? JsonArray) ?: throw java.io.IOException("The index did not return releases")
        val out = ArrayList<Release>()
        for (row in hits.take(150)) {
            val o = row as? JsonObject ?: continue
            val tracker = o.text("tracker")?.trim()
            val source = when {
                tracker.equals("RuTracker", true) || tracker.equals("RuTracker.org", true) -> "RuTracker"
                tracker.equals("The Pirate Bay", true) -> "The Pirate Bay"
                else -> continue
            }
            val title = o.text("title")?.trim() ?: continue
            if (title.length > 500 || !video.containsMatchIn(o.text("category").orEmpty())) continue
            val hash = (o.text("hash")?.takeIf { Regex("(?i)[a-f0-9]{40}").matches(it) } ?: Magnet.hashOf(o.text("magnetUrl").orEmpty())) ?: continue
            if (!ReleaseInfo.matches(item, title)) continue
            out.add(ReleaseInfo.make("$source:${hash.uppercase()}", title, source, hash, o.long("bytes"), o.long("seeders")?.toInt() ?: 0, o.long("leechers")?.toInt() ?: 0))
        }
        return out
    }
}

object ApibaySource {
    const val NAME = "The Pirate Bay"
    fun url(query: String) = "https://apibay.org/q.php?q=${URLEncoder.encode(query.trim(), "UTF-8")}&cat=200"

    fun parse(data: ByteArray, item: Title): List<Release> {
        val rows = Json.parseToJsonElement(data.decodeToString()) as? JsonArray ?: throw java.io.IOException("Unexpected answer")
        val out = ArrayList<Release>()
        for (row in rows) {
            val o = row as? JsonObject ?: continue
            val name = o.text("name")?.trim() ?: continue
            val hash = o.text("info_hash") ?: continue
            if (!Regex("(?i)[a-f0-9]{40}").matches(hash) || hash.all { it == '0' }) continue
            if (!ReleaseInfo.matches(item, name)) continue
            out.add(ReleaseInfo.make("$NAME:${hash.uppercase()}", name, NAME, hash, o.long("size"), o.long("seeders")?.toInt() ?: 0, o.long("leechers")?.toInt() ?: 0))
        }
        return out
    }
}

private fun JsonObject.text(name: String): String? = (this[name] as? JsonPrimitive)?.takeIf { it.isString }?.contentOrNull
private fun JsonObject.long(name: String): Long? = (this[name] as? JsonPrimitive)?.let { it.longOrNull ?: it.contentOrNull?.toLongOrNull() }?.takeIf { it >= 0 }

/** Runs the sources for one title. A source that fails is reported, not fatal. */
class ReleaseSearch(private val http: Http, private val pause: (Long) -> Unit = { Thread.sleep(it) }) {
    data class Result(val releases: List<Release>, val failed: List<String>)

    fun queries(item: Title): List<String> {
        val base = listOfNotNull(item.originalTitle, item.title).distinct()
        return if (item.section == Section.Movies && item.year > 0) base.map { "$it ${item.year}" } + base else base
    }

    fun run(item: Title): Result {
        val found = LinkedHashMap<String, Release>()
        val failed = LinkedHashSet<String>()
        val queries = queries(item).take(3)
        queries.forEachIndexed { index, query ->
            if (index > 0) pause(2100) // Knaben allows one request every two seconds.
            try { KnabenSource.parse(http.get(KnabenSource.url(query), 2_000_000), item).forEach { found.putIfAbsent(it.hash, it) } }
            catch (e: Exception) { failed.add("RuTracker") }
        }
        // The Pirate Bay is a second chance when the first index is empty or down.
        if (found.size < 3) {
            for (query in queries.take(2)) {
                try { ApibaySource.parse(http.get(ApibaySource.url(query), 2_000_000), item).forEach { found.putIfAbsent(it.hash, it) } }
                catch (e: Exception) { failed.add("The Pirate Bay") }
            }
        }
        if (found.isNotEmpty()) failed.clear()
        return Result(ReleaseInfo.rank(found.values.toList()), failed.toList())
    }
}
