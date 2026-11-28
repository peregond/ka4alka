package cc.ka4alka.core

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

class FavoriteSyncTest {
    @Test fun marksFollowTheSavedList() {
        val marks = mutableMapOf<String, FavoriteMark>()
        FavoriteSync.reconcile(marks, listOf("movies:a", "series:b", "games:x", "bad id"), 100)
        assertEquals(setOf("movies:a", "series:b"), marks.keys)
        FavoriteSync.reconcile(marks, listOf("movies:a"), 200)
        assertEquals(100, marks["movies:a"]!!.at); assertEquals(FavoriteMark("series:b", 200, true), marks["series:b"])
        FavoriteSync.reconcile(marks, listOf("movies:a", "series:b"), 300)
        assertEquals(FavoriteMark("series:b", 300, false), marks["series:b"])
    }

    @Test fun newerMarksWinAndRepeatsChangeNothing() {
        val marks = mutableMapOf("movies:a" to FavoriteMark("movies:a", 100, false), "series:b" to FavoriteMark("series:b", 300, false))
        val merged = FavoriteSync.merge(marks, listOf(FavoriteMark("movies:c", 250, false), FavoriteMark("movies:a", 50, true), FavoriteMark("series:b", 400, true), FavoriteMark("evil:../x", 999, false), FavoriteMark("movies:ok", 0, false)))
        assertEquals(listOf("movies:c"), merged.add); assertEquals(listOf("series:b"), merged.remove)
        assertEquals(false, marks["movies:a"]!!.removed)
        val again = FavoriteSync.merge(marks, listOf(FavoriteMark("movies:c", 250, false), FavoriteMark("series:b", 400, true)))
        assertTrue(again.add.isEmpty() && again.remove.isEmpty())
    }

    @Test fun twoDevicesConverge() {
        val laptop = mutableMapOf<String, FavoriteMark>(); val tv = mutableMapOf<String, FavoriteMark>()
        FavoriteSync.reconcile(laptop, listOf("movies:one", "movies:two"), 1000)
        FavoriteSync.reconcile(tv, listOf("movies:two", "movies:three"), 1100)
        FavoriteSync.merge(tv, laptop.values.toList()); FavoriteSync.merge(laptop, tv.values.toList())
        assertEquals(laptop.keys, tv.keys); assertEquals(3, laptop.size)
    }

    @Test fun marksSurviveStorage() {
        val marks = mutableMapOf("movies:a" to FavoriteMark("movies:a", 5, false), "series:b" to FavoriteMark("series:b", 7, true))
        assertEquals(marks, FavoriteSync.decode(FavoriteSync.encode(marks.values)))
        assertTrue(FavoriteSync.decode("garbage;;a|b|c;movies:ok|x|1").isEmpty())
    }
}
