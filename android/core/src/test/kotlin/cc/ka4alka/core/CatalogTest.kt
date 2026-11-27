package cc.ka4alka.core

import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.util.zip.GZIPOutputStream
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class CatalogTest {
    private fun library(): Library {
        val json = """{"schemaVersion":1,"generatedAtUtc":"2026-11-08T00:00:00Z","unknownTop":1,"items":[
          {"id":"movies:a","section":"movies","title":"Бегущий по лезвию 2049","year":2017,"poster":"https://img1.zonapic.com/a.jpg","originalTitle":"Blade Runner 2049","imdbId":"tt1856101","imdb":"8.0","kinopoisk":"7,5","description":"Через тридцать лет после событий первого фильма в Лос-Анджелесе появляется новый охотник.","genre":"фантастика, драма","genreKeys":["fantastika","drama"],"country":"США","people":[{"name":"Райан Гослинг","role":"Актёры"},{"name":"Дени Вильнёв","role":"Режиссёры"}],"extra":{"x":1}},
          {"id":"movies:b","section":"movies","title":"Ёжик в тумане","year":2020,"tmdbPoster":"/xyz.jpg","kinopoiskDescription":"Описание от Кинопоиска","tmdbGenre":"мультфильм","kinopoiskPeople":[{"name":"Иван","role":"Актёры"}]},
          {"id":"series:c","section":"series","title":"Игра престолов","year":2011,"imdb":"9.2","genre":"фэнтези","genreKeys":["fentezi"]},
          {"id":"games:d","section":"games","title":"Не фильм","year":2020},
          {"id":"movies:a","section":"movies","title":"Дубликат","year":2017}
        ]}"""
        return CatalogParser.parse(ByteArrayInputStream(json.toByteArray()))
    }

    @Test fun parsesAndFillsGaps() {
        val lib = library()
        assertEquals(3, lib.items.size) // games skipped, duplicate id dropped
        val a = lib["movies:a"]!!
        assertEquals(7.5, a.kinopoisk); assertEquals(8.0, a.imdb); assertEquals(7.5, a.rating); assertEquals("КП", a.ratingSource)
        assertEquals(listOf("фантастика", "драма"), a.genres)
        assertEquals("Райан Гослинг", a.cast.single().name); assertEquals("Дени Вильнёв", a.directors.single().name)
        val b = lib["movies:b"]!!
        assertEquals("https://image.tmdb.org/t/p/w500/xyz.jpg", b.poster)
        assertEquals("Описание от Кинопоиска", b.description); assertEquals(listOf("мультфильм"), b.genres)
        assertEquals("Иван", b.cast.single().name)
        assertEquals("IMDb", lib["series:c"]!!.ratingSource)
        assertEquals(2020, lib.newestYear)
    }

    @Test fun readsGzip() {
        val raw = """{"schemaVersion":1,"items":[]}"""
        val gz = ByteArrayOutputStream().also { GZIPOutputStream(it).use { s -> s.write(raw.toByteArray()) } }.toByteArray()
        assertEquals(0, CatalogParser.parseGzip(ByteArrayInputStream(gz)).items.size)
    }

    @Test fun rejectsUnknownSchema() {
        assertFailsWith<IllegalArgumentException> { CatalogParser.parse(ByteArrayInputStream("""{"schemaVersion":2,"items":[]}""".toByteArray())) }
    }

    @Test fun searchIgnoresYoCaseAndOrder() {
        val lib = library()
        assertEquals("movies:b", Search.run(lib.items, "ежик").first().id)
        assertEquals("movies:a", Search.run(lib.items, "BLADE runner").first().id)
        assertEquals("movies:a", Search.run(lib.items, "2049 бегущий").first().id)
        assertEquals("movies:a", Search.run(lib.items, "бегущий 2017").first().id)
        assertTrue(Search.run(lib.items, "бегущий 1999").isEmpty())
        assertEquals("series:c", Search.run(lib.items, "игра пре").first().id)
        assertTrue(Search.run(lib.items, "   ").isEmpty())
        assertTrue(Search.run(lib.items, "ежик", section = Section.Series).isEmpty())
    }

    @Test fun manifestIsChecked() {
        val ok = """{"schemaVersion":1,"revision":"x","generatedAtUtc":"t","downloadUrl":"https://github.com/peregond/ka4alka/releases/download/catalog-data/catalog-a.json.gz","sha256":"${"a".repeat(64)}","bytes":100,"uncompressedBytes":500}"""
        assertNotNull(CatalogManifest.parse(ok))
        assertFailsWith<IllegalArgumentException> { CatalogManifest.parse(ok.replace("peregond", "evil")) }
        assertFailsWith<IllegalArgumentException> { CatalogManifest.parse(ok.replace("a".repeat(64), "zz")) }
        assertFailsWith<IllegalArgumentException> { CatalogManifest.parse(ok.replace("\"bytes\":100", "\"bytes\":999999999")) }
    }

    @Test fun shelvesHaveHeroAndRows() {
        val items = (1..40).map {
            CatalogParser.normalise(RawItem(id = "movies:$it", section = "movies", title = "Фильм $it", year = 2026 - it % 3, poster = "https://x/p$it.jpg",
                description = "Длинное описание фильма номер $it, достаточно длинное для баннера.", genreKeys = listOf("boevik"), imdb = "${6 + it % 4}.5"))!!
        }
        val lib = Library(items, "t")
        assertTrue(Shelves.hero(lib).isNotEmpty())
        val ids = Shelves.home(lib).map { it.id }
        assertTrue("new" in ids && "genre:boevik" in ids)
        assertNull(Shelves.home(lib).firstOrNull { it.id == "series" })
    }
}

class BackdropTest {
    @Test fun parsesTheServerAnswer() {
        val ok = """{"id":"movies:a","backdrop":{"source":"TMDB","url":"https://image.tmdb.org/t/p/w1280/abc_DEF-1.jpg"}}"""
        assertEquals("https://image.tmdb.org/t/p/w1280/abc_DEF-1.jpg", BackdropClient.parse(ok.toByteArray(), "movies:a"))
        assertNull(BackdropClient.parse("""{"id":"movies:a","backdrop":null}""".toByteArray(), "movies:a"))
        assertFailsWith<IllegalArgumentException> { BackdropClient.parse(ok.toByteArray(), "movies:b") }
        assertFailsWith<IllegalArgumentException> { BackdropClient.parse(ok.replace("image.tmdb.org", "evil.example").toByteArray(), "movies:a") }
    }

    @Test fun triesTheNextMirrorAndRemembersAnswers() {
        val calls = mutableListOf<String>()
        val http = Http { url, _ ->
            calls.add(url)
            if (url.startsWith("https://one/")) throw java.io.IOException("down")
            """{"id":"movies:a","backdrop":{"source":"TMDB","url":"https://image.tmdb.org/t/p/w1280/x.jpg"}}""".toByteArray()
        }
        val client = BackdropClient(http, listOf("https://one/", "https://two/"))
        assertEquals("https://image.tmdb.org/t/p/w1280/x.jpg", client.backdrop("movies:a"))
        assertEquals("https://image.tmdb.org/t/p/w1280/x.jpg", client.backdrop("movies:a"))
        assertEquals(2, calls.size)
    }
}
