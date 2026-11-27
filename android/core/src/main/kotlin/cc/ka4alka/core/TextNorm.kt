package cc.ka4alka.core

object TextNorm {
    /** Lower case, ё→е, punctuation to single spaces: the folded form every search compares. */
    fun fold(value: String): String {
        val out = StringBuilder(value.length)
        var gap = true
        for (c in value.lowercase()) {
            val ch = if (c == 'ё') 'е' else c
            if (ch.isLetterOrDigit()) { out.append(ch); gap = false }
            else if (!gap) { out.append(' '); gap = true }
        }
        return out.toString().trim()
    }

    fun tokens(value: String): List<String> = fold(value).split(' ').filter { it.isNotEmpty() }
}
