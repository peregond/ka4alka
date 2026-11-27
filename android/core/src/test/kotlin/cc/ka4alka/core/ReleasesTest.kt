package cc.ka4alka.core

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class ReleasesTest {
    private fun title(section: Section = Section.Movies, year: Int = 2022, ru: String = "Бэтмен", en: String? = "The Batman") =
        Title("x", section, ru, year, null, en, null, null, null, "", emptyList(), emptyList(), "", emptyList(), emptyList(), "")

    @Test fun classifiesQualityAudioAndSeason() {
        val a = ReleaseInfo.make("1", "Бэтмен / The Batman (2022) BDRip 1080p | Дубляж", "RuTracker", "a".repeat(40), 1, 50, 5)
        assertEquals(1080, a.resolution); assertTrue(a.russianAudio); assertTrue(a.dubbed); assertFalse(a.poor)
        val b = ReleaseInfo.make("2", "The Batman 2022 CAMRip 720p", "The Pirate Bay", "b".repeat(40), 1, 9, 0)
        assertTrue(b.poor); assertEquals("Экранка", b.qualityLabel); assertFalse(b.russianAudio)
        val c = ReleaseInfo.make("3", "The Batman 2022 2160p UHD BluRay HDR10 DV", "x", "c".repeat(40), 1, 9, 0)
        assertEquals(2160, c.resolution); assertTrue(c.hdr); assertEquals("4K", c.qualityLabel)
        assertEquals(2 to 5, ReleaseInfo.season("Show S02E05 1080p"))
        assertEquals(3 to null, ReleaseInfo.season("Сериал / Show (Сезон 3) WEB-DL"))
        assertEquals(1 to null, ReleaseInfo.season("Show S01-S04 complete"))
    }

    @Test fun matchesTitleAndYear() {
        val movie = title()
        assertTrue(ReleaseInfo.matches(movie, "Бэтмен / The Batman (2022) BDRip"))
        assertTrue(ReleaseInfo.matches(movie, "The.Batman.2022.1080p.WEB-DL"))
        assertFalse(ReleaseInfo.matches(movie, "The Batman Begins (2005)"))
        assertFalse(ReleaseInfo.matches(movie, "The Batman 1989"))
        assertTrue(ReleaseInfo.matches(title(Section.Series, 0, "Игра престолов", "Game of Thrones"), "Game of Thrones S01 1080p"))
    }

    @Test fun ranksGoodRussianReleaseFirstAndRecommendsIt() {
        val rows = listOf(
            ReleaseInfo.make("1", "The Batman 2022 CAMRip", "x", "1".repeat(40), 1, 500, 0),
            ReleaseInfo.make("2", "The Batman 2022 BDRip 1080p Дубляж", "x", "2".repeat(40), 1, 40, 0),
            ReleaseInfo.make("3", "The Batman 2022 WEB-DL 2160p eng", "x", "3".repeat(40), 1, 80, 0),
        )
        val ranked = ReleaseInfo.rank(rows)
        assertEquals("2", ranked.first().id); assertTrue(ranked.first().recommended)
        assertEquals("1", ranked.last().id); assertEquals(1, ranked.count { it.recommended })
    }

    @Test fun parsesKnaben() {
        val json = """{"hits":[
          {"tracker":"RuTracker","title":"Бэтмен / The Batman (2022) BDRip 1080p | Дубляж","category":"Movies","hash":"${"A".repeat(40)}","seeders":120,"leechers":3,"bytes":9000000000},
          {"tracker":"RuTracker","title":"Бэтмен (2022) музыка","category":"Audio","hash":"${"B".repeat(40)}","seeders":9},
          {"tracker":"Other","title":"The Batman 2022","category":"Movies","hash":"${"C".repeat(40)}"},
          {"tracker":"The Pirate Bay","title":"The Batman 2022 720p","category":"Video","magnetUrl":"magnet:?xt=urn:btih:${"d".repeat(40)}&dn=x","seeders":7},
          {"tracker":"RuTracker","title":"Другой фильм 2022","category":"Movies","hash":"${"E".repeat(40)}"}]}"""
        val rows = KnabenSource.parse(json.toByteArray(), title())
        assertEquals(2, rows.size)
        assertEquals(120, rows[0].seeds); assertEquals("D".repeat(40), rows[1].hash)
        assertTrue(rows[0].magnet.startsWith("magnet:?xt=urn:btih:${"A".repeat(40)}&dn="))
        assertEquals("A".repeat(40), Magnet.hashOf(rows[0].magnet))
    }

    @Test fun parsesApibayAndSkipsEmptyAnswer() {
        val json = """[{"id":"1","name":"The Batman 2022 1080p WEB","info_hash":"${"F".repeat(40)}","leechers":"2","seeders":"30","size":"4000000000"},
          {"id":"0","name":"No results returned","info_hash":"${"0".repeat(40)}","seeders":"0"}]"""
        assertEquals(1, ApibaySource.parse(json.toByteArray(), title()).size)
    }

    @Test fun searchFallsBackAndReportsFailures() {
        val item = title()
        val calls = mutableListOf<String>()
        val http = Http { url, _ ->
            calls.add(url)
            if (url.startsWith("https://api.knaben.org")) throw java.io.IOException("down")
            """[{"name":"The Batman 2022 1080p","info_hash":"${"9".repeat(40)}","seeders":"20","leechers":"1","size":"1"}]""".toByteArray()
        }
        val result = ReleaseSearch(http) { }.run(item)
        assertEquals(1, result.releases.size); assertTrue(result.failed.isEmpty())
        assertTrue(calls.any { it.startsWith("https://apibay.org") })
        val dead = ReleaseSearch(Http { _, _ -> throw java.io.IOException("x") }) { }.run(item)
        assertTrue(dead.releases.isEmpty()); assertTrue(dead.failed.isNotEmpty())
    }
}
