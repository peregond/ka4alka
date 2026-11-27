package cc.ka4alka.core

import kotlinx.serialization.ExperimentalSerializationApi
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.decodeFromStream
import java.io.InputStream
import java.security.MessageDigest
import java.util.zip.GZIPInputStream

/** A cast or crew member as the shared library stores it. */
@Serializable
data class Person(
    val name: String = "",
    val role: String = "",
    val photoUrl: String? = null,
    val originalName: String? = null,
    val character: String? = null,
)

/** One film or series entry of the shared library (catalog.json). Unknown fields are ignored. */
@Serializable
data class RawItem(
    val id: String = "",
    val section: String = "",
    val title: String = "",
    val year: Int = 0,
    val poster: String? = null,
    val pageUrl: String = "",
    val originalTitle: String? = null,
    val imdbId: String? = null,
    val imdb: String? = null,
    val kinopoisk: String? = null,
    val description: String? = null,
    val genre: String? = null,
    val genreKeys: List<String> = emptyList(),
    val country: String? = null,
    val people: List<Person> = emptyList(),
    val kinopoiskDescription: String? = null,
    val kinopoiskGenre: String? = null,
    val kinopoiskCountry: String? = null,
    val kinopoiskPeople: List<Person> = emptyList(),
    val tmdbPoster: String? = null,
    val tmdbOverview: String? = null,
    val tmdbGenre: String? = null,
    val tmdbCountry: String? = null,
)

@Serializable
private data class RawCatalog(val schemaVersion: Int = 0, val generatedAtUtc: String = "", val items: List<RawItem> = emptyList())

enum class Section(val key: String, val label: String) {
    Movies("movies", "Фильмы"),
    Series("series", "Сериалы");

    companion object {
        fun of(key: String): Section? = entries.firstOrNull { it.key == key }
    }
}

/** A catalog entry normalised for the screens: gaps are filled from the secondary sources and text is pre-folded for search. */
data class Title(
    val id: String,
    val section: Section,
    val title: String,
    val year: Int,
    val poster: String?,
    val originalTitle: String?,
    val imdbId: String?,
    val imdb: Double?,
    val kinopoisk: Double?,
    val description: String,
    val genres: List<String>,
    val genreKeys: List<String>,
    val country: String,
    val cast: List<Person>,
    val directors: List<Person>,
    val pageUrl: String,
) {
    /** Kinopoisk first, then IMDb, as the desktop apps show it. */
    val rating: Double? get() = kinopoisk ?: imdb
    val ratingSource: String? get() = if (kinopoisk != null) "КП" else if (imdb != null) "IMDb" else null
    internal val searchTitle: String = TextNorm.fold(title)
    internal val searchOriginal: String = TextNorm.fold(originalTitle.orEmpty())
}

class Library(val items: List<Title>, val generatedAtUtc: String) {
    private val byId = items.associateBy { it.id }
    operator fun get(id: String): Title? = byId[id]
    val newestYear: Int = items.maxOfOrNull { it.year } ?: 0
}

object CatalogParser {
    const val MAX_BYTES = 32L * 1024 * 1024
    private val json = Json { ignoreUnknownKeys = true; isLenient = true }

    @OptIn(ExperimentalSerializationApi::class)
    fun parse(input: InputStream): Library {
        val raw = json.decodeFromStream<RawCatalog>(Limited(input, MAX_BYTES))
        if (raw.schemaVersion != 1) throw IllegalArgumentException("Unsupported library version ${raw.schemaVersion}")
        val items = ArrayList<Title>(raw.items.size)
        val seen = HashSet<String>()
        for (row in raw.items) {
            val title = normalise(row) ?: continue
            if (seen.add(title.id)) items.add(title)
        }
        return Library(items, raw.generatedAtUtc)
    }

    fun parseGzip(input: InputStream): Library = GZIPInputStream(input).use { parse(it) }

    fun normalise(row: RawItem): Title? {
        val section = Section.of(row.section) ?: return null
        if (row.id.isBlank() || row.title.isBlank()) return null
        val genre = firstText(row.genre, row.kinopoiskGenre, row.tmdbGenre)
        val people = row.people.ifEmpty { row.kinopoiskPeople }
        val poster = row.poster?.takeIf { it.startsWith("https://") }
            ?: row.tmdbPoster?.takeIf { it.startsWith("/") }?.let { "https://image.tmdb.org/t/p/w500$it" }
        return Title(
            id = row.id,
            section = section,
            title = row.title.trim(),
            year = row.year,
            poster = poster,
            originalTitle = row.originalTitle?.trim()?.takeIf { it.isNotEmpty() && !it.equals(row.title.trim(), true) },
            imdbId = row.imdbId?.takeIf { Regex("tt\\d{5,10}").matches(it) },
            imdb = score(row.imdb),
            kinopoisk = score(row.kinopoisk),
            description = firstText(row.description, row.kinopoiskDescription, row.tmdbOverview),
            genres = genre.split(',').map { it.trim() }.filter { it.isNotEmpty() },
            genreKeys = row.genreKeys,
            country = firstText(row.country, row.kinopoiskCountry, row.tmdbCountry),
            cast = people.filter { it.role.startsWith("Актёр") || it.role.startsWith("Актер") }.distinctBy { it.name },
            directors = people.filter { it.role.startsWith("Режисс") }.distinctBy { it.name },
            pageUrl = row.pageUrl,
        )
    }

    private fun firstText(vararg values: String?): String = values.firstOrNull { !it.isNullOrBlank() }?.trim().orEmpty()

    private fun score(value: String?): Double? =
        value?.replace(',', '.')?.toDoubleOrNull()?.takeIf { it in 0.1..10.0 }

    private class Limited(private val input: InputStream, private val limit: Long) : InputStream() {
        private var count = 0L
        override fun read(): Int {
            val value = input.read()
            if (value >= 0 && ++count > limit) throw java.io.IOException("The library is larger than allowed")
            return value
        }
        override fun read(buffer: ByteArray, offset: Int, length: Int): Int {
            val n = input.read(buffer, offset, length)
            if (n > 0) { count += n; if (count > limit) throw java.io.IOException("The library is larger than allowed") }
            return n
        }
    }
}

/** The small manifest that points at the immutable compressed snapshot (same one the desktop apps use). */
@Serializable
data class CatalogManifest(
    val schemaVersion: Int = 0,
    val revision: String = "",
    val generatedAtUtc: String = "",
    val downloadUrl: String = "",
    val sha256: String = "",
    val bytes: Long = 0,
    val uncompressedBytes: Long = 0,
) {
    fun validate() {
        require(schemaVersion == 1) { "Unsupported manifest" }
        require(downloadUrl.startsWith("https://github.com/peregond/ka4alka/releases/download/catalog-data/")) { "Unexpected snapshot address" }
        require(Regex("[a-f0-9]{64}").matches(sha256)) { "Bad checksum" }
        require(bytes in 1..CatalogParser.MAX_BYTES && uncompressedBytes in 1..CatalogParser.MAX_BYTES) { "Bad size" }
    }

    companion object {
        const val URL = "https://github.com/peregond/ka4alka/releases/download/catalog-data/manifest.json"
        fun parse(text: String): CatalogManifest = Json { ignoreUnknownKeys = true }.decodeFromString<CatalogManifest>(text).also { it.validate() }
    }
}

fun sha256Hex(bytes: ByteArray): String = MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it) }
