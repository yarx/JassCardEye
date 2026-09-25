package ch.yarx.jasscardeye

import androidx.annotation.StringRes

// Port of src/app/ios/Sources/StabilityRule.swift. The texts are string resources, as in JassScoring.kt, so
// the rule stays free of Android types.

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

    /** The picker's line, formatted with the votes and the [windowSize] - a run only shows the votes. */
    @get:StringRes
    val displayName: Int
        get() = when (this) {
            RUN -> R.string.rule_run
            MAJORITY -> R.string.rule_majority
        }

    @get:StringRes
    val explanation: Int
        get() = when (this) {
            RUN -> R.string.rule_run_note
            MAJORITY -> R.string.rule_majority_note
        }

    companion object {
        fun fromId(id: String?): StabilityRule? = entries.firstOrNull { it.id == id }
    }
}
