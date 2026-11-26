package cc.ka4alka.core

/** Title search over the in-memory library: Russian and original names, any word order, prefix matches, best first. */
object Search {
    fun run(items: List<Title>, query: String, limit: Int = 60, section: Section? = null): List<Title> {
        val words = TextNorm.tokens(query)
        if (words.isEmpty()) return emptyList()
        val year = words.firstOrNull { it.length == 4 && it.toIntOrNull() in 1900..2100 }?.toInt()
        // A year next to other words narrows the search; if that finds nothing the number is part of a title ("Blade Runner 2049").
        if (year != null && words.size > 1) {
            val narrowed = rank(items, words - year.toString(), year, section, limit)
            if (narrowed.isNotEmpty()) return narrowed
        }
        return rank(items, words, null, section, limit)
    }

    private fun rank(items: List<Title>, terms: List<String>, year: Int?, section: Section?, limit: Int): List<Title> {
        val scored = ArrayList<Pair<Int, Title>>()
        for (item in items) {
            if (section != null && item.section != section) continue
            val score = score(item, terms, year)
            if (score > 0) scored.add(score to item)
        }
        return scored.sortedWith(compareByDescending<Pair<Int, Title>> { it.first }.thenByDescending { it.second.rating ?: 0.0 }.thenByDescending { it.second.year })
            .take(limit).map { it.second }
    }

    private fun score(item: Title, terms: List<String>, year: Int?): Int {
        if (year != null && item.year != year) return 0
        val phrase = terms.joinToString(" ")
        var best = 0
        for (name in listOf(item.searchTitle, item.searchOriginal)) {
            if (name.isEmpty()) continue
            val value = when {
                name == phrase -> 100
                name.startsWith("$phrase ") || name.startsWith(phrase) -> 80
                name.contains(" $phrase") -> 60
                else -> {
                    val parts = name.split(' ')
                    if (terms.all { term -> parts.any { it.startsWith(term) } }) 40 else 0
                }
            }
            if (value > best) best = value
        }
        return best
    }
}
