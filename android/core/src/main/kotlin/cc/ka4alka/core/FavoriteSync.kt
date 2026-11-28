package cc.ka4alka.core

/** One favourite as devices exchange it: the shared-catalog id, when it last changed (Unix ms) and whether it was removed. */
data class FavoriteMark(val id: String, val at: Long, val removed: Boolean)

/**
 * Last-writer-wins merge of the favourites with the computer (the same rules as Kachalka.FavoriteSync on the desktop).
 * A removal is a mark too, so it travels to the other device. Marks are derived from the saved list when a sync happens.
 */
object FavoriteSync {
    const val MAX_MARKS = 5000
    private val validId = Regex("(movies|series):[A-Za-z0-9_\\-:]{1,130}")

    fun valid(id: String) = validId.matches(id)

    /** Brings the marks in line with the favourites as they are now. */
    fun reconcile(marks: MutableMap<String, FavoriteMark>, current: Collection<String>, now: Long) {
        val set = current.filter { valid(it) }.toSet()
        for (id in set) { val m = marks[id]; if (m == null || m.removed) marks[id] = FavoriteMark(id, now, false) }
        for (m in marks.values.toList()) if (!m.removed && m.id !in set) marks[m.id] = FavoriteMark(m.id, now, true)
        if (marks.size > MAX_MARKS) marks.values.filter { it.removed }.sortedBy { it.at }.take(marks.size - MAX_MARKS).forEach { marks.remove(it.id) }
    }

    data class Merged(val add: List<String>, val remove: List<String>)

    /** Applies what the computer sent; says which titles must now be added to or removed from the saved list. */
    fun merge(marks: MutableMap<String, FavoriteMark>, incoming: Collection<FavoriteMark>): Merged {
        val add = ArrayList<String>(); val remove = ArrayList<String>()
        for (m in incoming.filter { valid(it.id) && it.at > 0 }.take(MAX_MARKS)) {
            val known = marks[m.id]
            if (known != null && m.at <= known.at) continue
            marks[m.id] = m
            (if (m.removed) remove else add).add(m.id)
        }
        return Merged(add, remove)
    }

    fun encode(marks: Collection<FavoriteMark>): String = marks.joinToString(";") { "${it.id}|${it.at}|${if (it.removed) 1 else 0}" }

    fun decode(text: String?): MutableMap<String, FavoriteMark> {
        val out = LinkedHashMap<String, FavoriteMark>()
        for (part in text.orEmpty().split(';')) {
            val f = part.split('|')
            if (f.size != 3) continue
            val at = f[1].toLongOrNull() ?: continue
            if (valid(f[0])) out[f[0]] = FavoriteMark(f[0], at, f[2] == "1")
        }
        return out
    }
}
