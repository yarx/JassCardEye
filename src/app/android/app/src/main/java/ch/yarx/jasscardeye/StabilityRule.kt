package ch.yarx.jasscardeye

// Port of src/app/ios/Sources/StabilityRule.swift.

/**
 * How a detection earns its place on the pile.
 *
 * The problem both rules exist for: a wrong reading is worse than a missed one, because it slips past
 * unnoticed. The detector gives no help there: a wrong answer can be as confident as a right one, so
 * no threshold on a single frame separates them; the evidence has to come from several frames.
 */
enum class StabilityRule(val id: String) {
    /** The default: the same card on top of N frames in a row. */
    RUN("run"),

    /** A majority of a window instead of an unbroken run - see [windowSize]. */
    MAJORITY("majority");

    /**
     * How many of the last frames are looked at. A majority of `2N-1` is exactly N, so the same number
     * the user already tunes keeps its meaning: "this many frames have to agree".
     */
    fun windowSize(votes: Int): Int = maxOf(1, votes * 2 - 1)

    fun displayName(votes: Int): String = when (this) {
        RUN -> "Serie – $votes hintereinander"
        MAJORITY -> "Mehrheit – $votes von ${windowSize(votes)}"
    }

    val explanation: String
        get() = when (this) {
            RUN -> "Eine Karte wird gezählt, wenn sie in so vielen Bildern hintereinander zuoberst liegt. " +
                "Ein einziges abweichendes Bild setzt den Zähler zurück."
            MAJORITY -> "Eine Karte wird gezählt, wenn sie die Mehrheit der letzten Bilder für sich hat und " +
                "keine andere darin mehr als einmal vorkommt. Einzelne Aussetzer brechen nichts mehr " +
                "ab, aber eine Fehlerkennung während einer Bewegung braucht jetzt eine echte Mehrheit " +
                "statt drei zufällig benachbarter Bilder."
        }

    companion object {
        fun fromId(id: String?): StabilityRule? = entries.firstOrNull { it.id == id }
    }
}
