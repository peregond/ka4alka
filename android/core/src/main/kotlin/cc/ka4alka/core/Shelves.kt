package cc.ka4alka.core

data class Shelf(val id: String, val title: String, val items: List<Title>)

/** The home page of the TV app: a hero list and themed rows, all derived from the library without any extra requests. */
object Shelves {
    private const val ROW = 24

    fun hero(library: Library, count: Int = 6): List<Title> {
        val recent = library.items.filter { it.backdropReady() && it.year >= library.newestYear - 1 && (it.rating ?: 0.0) >= 6.5 }
        return recent.sortedWith(compareByDescending<Title> { it.rating ?: 0.0 }.thenByDescending { it.year }).take(count)
    }

    private fun Title.backdropReady() = poster != null && description.length > 40

    fun home(library: Library): List<Shelf> {
        val all = library.items.filter { it.poster != null }
        val recentYear = library.newestYear - 1
        val rows = ArrayList<Shelf>()
        fun add(id: String, title: String, items: List<Title>) { if (items.size >= 6) rows.add(Shelf(id, title, items.take(ROW))) }
        add("new", "Новинки", all.filter { it.year >= recentYear }.sortedByDescending { it.year * 100 + ((it.rating ?: 0.0) * 5).toInt() })
        add("top", "Лучшее по оценкам", all.filter { (it.rating ?: 0.0) >= 7.8 }.sortedByDescending { it.rating })
        add("series", "Сериалы", all.filter { it.section == Section.Series }.sortedWith(compareByDescending<Title> { it.year >= recentYear }.thenByDescending { it.rating ?: 0.0 }))
        for ((key, label) in GENRE_ROWS) {
            add("genre:$key", label, all.filter { key in it.genreKeys }.sortedByDescending { (it.rating ?: 5.0) + (if (it.year >= recentYear) 1.0 else 0.0) })
        }
        return rows
    }

    val GENRE_ROWS = listOf(
        "boevik" to "Боевики", "komediia" to "Комедии", "triller" to "Триллеры", "fantastika" to "Фантастика",
        "uzhasy" to "Ужасы", "drama" to "Драмы", "kriminal" to "Криминал", "multfilm" to "Мультфильмы",
        "semeinyi" to "Для всей семьи", "fentezi" to "Фэнтези", "detektiv" to "Детективы",
    )

    /** The browse screens: a section filter plus an optional genre key, best rated first within the newest years. */
    fun browse(library: Library, section: Section?, genreKey: String? = null): List<Title> =
        library.items.filter { (section == null || it.section == section) && (genreKey == null || genreKey in it.genreKeys) }
            .sortedWith(compareByDescending<Title> { it.year >= library.newestYear - 1 }.thenByDescending { it.rating ?: 0.0 })
}
