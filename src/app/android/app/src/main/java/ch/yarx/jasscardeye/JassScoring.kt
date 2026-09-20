package ch.yarx.jasscardeye

// Port of src/app/ios/Sources/JassScoring.swift - kept free of Android types, so the unit tests hold the
// numbers to account on the JVM exactly as src/tools/check_scoring.swift does for iOS.
//
// Behind the variety of disciplines there are only three value tables - trump, Obenabe, Undenufe -
// and every discipline the app offers keeps one of them for the whole round. That is not a
// simplification but the reason a finished pile can be counted at all: Guschti plays the first four
// tricks obenabe and the last five undeufe, yet "gezählt werden die Punkte wie beim Obenaben", so the
// cards are worth the same whichever trick they came from. A discipline whose *values* changed
// mid-game could not be counted from a pile, because a pile does not remember its tricks.
//
// The multiplier is not a property of the discipline but a decision the players make, so it is asked
// with the start questions, ×1 unless somebody picks another. The match needs no counting: whoever holds all 36 cards has 152, plus the five for
// the last trick makes 157, and that is the number the app shows without being told about it.
//
// Both decks score through the same four suits. A German card is worth what the French card it
// stands for is worth - Rosen is Herz as far as the rules go, and only the picture differs - so
// everything here deals in `CardLabel.role` and never in the token the model emitted.

/**
 * How a deck is laid out for the rules: four suits by role, nine ranks, 36 cards. The suits are the
 * French tokens because that is what a role is named after; a German pile counts through the same four.
 */
object DeckLayout {
    val suits = listOf("clubs", "diamonds", "hearts", "spades")
    val ranks = listOf("6", "7", "8", "9", "10", "jack", "queen", "king", "ace")
    const val COUNT = 36
}

/**
 * Every discipline is built from these (plus the plain table for the non-trump suits of a trump game).
 * Each of them totals 152 across the 36 cards, which the unit tests hold to account.
 */
object ValueTables {
    val trump = mapOf("jack" to 20, "9" to 14, "ace" to 11, "10" to 10, "king" to 4, "queen" to 3)
    val plain = mapOf("ace" to 11, "10" to 10, "king" to 4, "queen" to 3, "jack" to 2)
    val obenabe = mapOf("ace" to 11, "10" to 10, "8" to 8, "king" to 4, "queen" to 3, "jack" to 2)
    val undenufe = mapOf("6" to 11, "10" to 10, "8" to 8, "king" to 4, "queen" to 3, "jack" to 2)
}

/** Rules that do not depend on the discipline. */
object JassRules {
    /** Points for taking the last trick. Not readable from a pile, so the session asks. */
    const val LAST_TRICK_BONUS = 5

    /** What the players may multiply the written result by. The classic Coiffeur factors. */
    val multipliers = (1..8).toList()
}

/**
 * Who took the last trick - or that this round is being counted without it.
 *
 * The third case is the point: the bonus is a convention, not a rule of every table, and a round
 * counted with five points nobody awarded is wrong in a way that looks right.
 */
enum class LastTrick(val id: String) {
    MINE("mine"),
    OPPONENTS("opponents"),
    UNUSED("unused");

    /** For the segmented control, where three options share the width of a phone. */
    val shortName: String
        get() = when (this) {
            MINE -> "Wir"
            OPPONENTS -> "Gegner"
            UNUSED -> "Keiner"
        }

    /** What it adds under the score, once the round is being counted. Null when there is nothing to explain. */
    val note: String?
        get() = when (this) {
            MINE -> "+${JassRules.LAST_TRICK_BONUS} letzter Stich"
            OPPONENTS -> "letzter Stich bei den Gegnern"
            UNUSED -> null
        }
}

/**
 * The two shapes a discipline comes in, and the reason it is a sealed type rather than four nullables.
 *
 * A discipline is either named after its trump suit - and then the deck on the table decides what it
 * is called and which mark stands over it - or it is played without trump and carries its own name
 * and symbol. Stored side by side, the two shapes could be mixed: a trump with an arrow, an Obenabe
 * with no name. Here it cannot be built in the first place, and nothing downstream falls back.
 */
sealed interface ModeKind {
    /** Named after the suit whose cards use `trumpValues`, by role ("clubs", "hearts", …). */
    data class Trump(val role: String) : ModeKind

    /**
     * Played without trump: its own name, its own symbol, and a short name for a narrow cell.
     * [mark] names the drawn arrow in `Symbols`, the counterpart of the SF Symbol iOS shows.
     */
    data class Open(val label: String, val mark: String, val shortName: String) : ModeKind
}

/**
 * One way a round can be counted - an entry of the question asked before the camera starts.
 *
 * @property id Stable identity, never shown. The display name is derived, so the deck on the table
 *   can change what a mode is called without changing what it *is*.
 * @property hint One line shown under the picker for whichever entry is selected: which card is
 *   suddenly worth 11 is exactly the mistake that step exists to prevent.
 */
data class CountingMode(
    val id: String,
    val kind: ModeKind,
    val trumpValues: Map<String, Int>,
    val normalValues: Map<String, Int>,
    val hint: String,
) {
    /** The role whose cards score as trump, or null for a discipline played without one. */
    val trumpRole: String? get() = (kind as? ModeKind.Trump)?.role

    /** The suit whose mark stands above the name, or null for a discipline played without trump. */
    fun markSuit(deck: JassDeck): JassSuit? = trumpRole?.let { JassSuit.playing(it, deck) }

    /** The name under the mark. Shorter than [displayName] where a cell is narrow. */
    fun shortName(deck: JassDeck): String = when (kind) {
        is ModeKind.Trump -> suitName(deck)
        is ModeKind.Open -> kind.shortName
    }

    /**
     * The name for the deck on the table: the same trump reads "Herz" on a French deck and "Rosen" on
     * a German one. Derived from the suit table rather than rewritten from a stored string.
     */
    fun displayName(deck: JassDeck): String = when (kind) {
        is ModeKind.Trump -> suitName(deck)
        is ModeKind.Open -> kind.label
    }

    private fun suitName(deck: JassDeck): String {
        val role = trumpRole ?: return id
        return JassSuit.playing(role, deck)?.name ?: role
    }

    /**
     * Card points of one model label ("spades_10", "roses_9") in this discipline. Compared by role
     * rather than by token: with Rosen as trump the model emits "roses_9", and that card is the Nell of
     * the trump suit exactly as "hearts_9" would be.
     */
    fun points(forLabel: String): Int {
        val card = CardLabel.parse(forLabel) ?: return 0
        val table = if (card.role == trumpRole) trumpValues else normalValues
        return table[card.rank] ?: 0
    }

    /** Card points of the full deck - 152 in every discipline. Recomputed rather than hardcoded. */
    val deckTotal: Int
        get() = DeckLayout.suits.sumOf { suit -> DeckLayout.ranks.sumOf { points("${suit}_$it") } }
}

/**
 * What the mode question offers, in the order it offers it: the four trump colours first, because they
 * are the common case, then the disciplines without trump.
 *
 * Rules follow jassverzeichnis.ch, "Die 10 besten Trumpfarten beim Jassen". The arten not listed here
 * are counted through one of these: Mary counts undenufe, Mezzo and Misère count obenabe.
 */
object JassModes {
    // The suit order matches the class-ID order (clubs, diamonds, hearts, spades), so the menu reads
    // the same way the model numbers its classes.
    private val suits: List<CountingMode> = DeckLayout.suits.map { suit ->
        CountingMode("trump.$suit", ModeKind.Trump(suit), ValueTables.trump, ValueTables.plain, "Under 20, Nell 14")
    }

    private fun noTrump(id: String, label: String, values: Map<String, Int>, hint: String, mark: String, short: String) =
        CountingMode(id, ModeKind.Open(label, mark, short), values, values, hint)

    val obenabe = noTrump("obenabe", "Obenabe", ValueTables.obenabe, "Ass zählt 11, Acht 8", "arrow.down", "Obenabe")

    val undenufe = noTrump("undenufe", "Undenufe", ValueTables.undenufe, "Sechs zählt 11, Ass 0", "arrow.up", "Undenufe")

    // Slalom alternates obenabe and undeufe from trick to trick; which of the two it started with is
    // what decides the values for the round, the same way Guschti's start decides its.
    val slalomObe = noTrump("slalom.obe", "Slalom (oben)", ValueTables.obenabe,
        "Obe begonnen – gezählt wird obenabe", "arrow.up.arrow.down", "Slalom ↓")

    val slalomUnde = noTrump("slalom.unde", "Slalom (unten)", ValueTables.undenufe,
        "Unde begonnen – gezählt wird undenufe", "arrow.up.arrow.down", "Slalom ↑")

    // One entry, no direction to ask about: Guschti always begins obenabe, and the switch to undeufe
    // after the fourth trick changes how it is played, not what the cards are worth.
    val guschti = noTrump("guschti", "Guschti", ValueTables.obenabe,
        "4 Stiche obenabe, dann 5 undeufe – gezählt wird obenabe", "arrow.triangle.branch", "Guschti")

    val all: List<CountingMode> = suits + listOf(obenabe, undenufe, slalomObe, slalomUnde, guschti)

    fun mode(id: String): CountingMode? = all.firstOrNull { it.id == id }

    /** The default when nothing was chosen yet - only ever a starting point for the question. */
    val standard: CountingMode = suits[0]
}

/**
 * The arithmetic of a counted pile, with no camera around it.
 *
 * Separate from `LiveDetectionModel` on purpose, so these numbers - exactly the part that goes wrong
 * in a way nobody notices - can be checked by a plain JVM test.
 */
data class Tally(val mode: CountingMode, val lastTrick: LastTrick, val multiplier: Int, val cardPoints: Int) {
    /** The five for the last trick, when this side claimed it. */
    val bonus: Int get() = if (lastTrick == LastTrick.MINE) JassRules.LAST_TRICK_BONUS else 0

    /** What gets written on the slate for this side. */
    val points: Int get() = (cardPoints + bonus) * multiplier

    /**
     * The other side's written points: the cards this side does not hold, plus the last trick if it
     * was theirs. The factor applies to both, because both get written under it.
     */
    val opponentPoints: Int
        get() {
            val rest = mode.deckTotal - cardPoints + if (lastTrick == LastTrick.OPPONENTS) JassRules.LAST_TRICK_BONUS else 0
            return rest * multiplier
        }
}

/** What a finished counting session produced - handed back to the home screen. */
data class CountResult(
    val modeName: String,
    /** The suit token of the trump it was counted in, so the home screen can draw the same mark. */
    val modeSuit: String?,
    val cards: Int,
    val points: Int,
    val opponentPoints: Int,
    val multiplier: Int,
)
