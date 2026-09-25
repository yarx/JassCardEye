package ch.yarx.jasscardeye

import androidx.annotation.StringRes

// Port of src/app/ios/Sources/JassDeck.swift.
//
// The names are string resources rather than text: this file stays free of Android types so the unit
// tests run on the JVM, and a resource id is resolved by whoever has a Context - the screen, as a rule.

/**
 * The two decks a Swiss Jass is played with. A game uses one or the other, never both, so the deck is
 * a property of the table - not of a single card.
 */
enum class JassDeck(val id: String) {
    FRENCH("french"),
    GERMAN("german");

    /** What it is called: "Französisch", "Deutsch". */
    @get:StringRes
    val displayName: Int get() = if (this == FRENCH) R.string.deck_french else R.string.deck_german

    /**
     * Its four suits, shown next to the name wherever the deck is offered - seeing the marks is the
     * fastest way to know which deck is meant.
     *
     * In role order rather than class order, so this row and the trump row underneath it read the
     * same way. They differ for the German deck: the model numbers it Eichel, Rosen, Schellen,
     * Schilten, while by role it is Eichel, Schellen, Rosen, Schilten.
     */
    val suits: List<JassSuit> get() = DeckLayout.suits.mapNotNull { JassSuit.playing(it, this) }

    companion object {
        fun fromId(id: String?): JassDeck? = entries.firstOrNull { it.id == id }
    }
}

/**
 * One suit of one deck, and everything the app needs to know about it.
 *
 * The eight suits are one table rather than a map per question, so a suit's deck, its role in the
 * rules and its two names cannot drift apart - and so the German names are written once.
 *
 * @property token As the model emits it in a label: "spades", "shields". Also the name of the drawn
 *   mark (`R.drawable.suit_<token>`); `SuitMarkTest` keeps that list in step with this one.
 * @property role The French suit whose rules this one follows. Swiss convention: Eichel ≙ Kreuz,
 *   Rosen ≙ Herz, Schellen ≙ Ecken, Schilten ≙ Schaufel. Scoring and trump therefore only ever deal
 *   with four suits, whichever deck is on the table.
 * @property name The suit on its own: "Schaufel", "Schilten". What TalkBack reads for the mark and
 *   what stands under the mark in the compact picker. A filename carries the [token] instead, which does
 *   not change with the language.
 * @property markCredit Who drew the mark and under which licence - what the *Über* page credits. A column of
 *   this table, so no suit can be drawn without being credited.
 */
data class JassSuit(val token: String, val deck: JassDeck, val role: String, @StringRes val name: Int, val markCredit: MarkCredit) {
    companion object {
        val all: List<JassSuit> = listOf(
            JassSuit("clubs", JassDeck.FRENCH, "clubs", R.string.suit_clubs, MarkCredit("SuitClubs.svg", "F l a n k e r", publicDomain = true)),
            JassSuit("diamonds", JassDeck.FRENCH, "diamonds", R.string.suit_diamonds, MarkCredit("Ecke_Neu.svg", "Jensche", publicDomain = false)),
            JassSuit("hearts", JassDeck.FRENCH, "hearts", R.string.suit_hearts, MarkCredit("Herz_Neu.svg", "Jensche", publicDomain = false)),
            JassSuit("spades", JassDeck.FRENCH, "spades", R.string.suit_spades, MarkCredit("Schaufel_Neu.svg", "Jensche", publicDomain = false)),
            JassSuit("acorns", JassDeck.GERMAN, "clubs", R.string.suit_acorns, MarkCredit("Eichel_Neu.svg", "Jensche", publicDomain = false)),
            JassSuit("roses", JassDeck.GERMAN, "hearts", R.string.suit_roses, MarkCredit("Rosen_Neu.svg", "Jensche", publicDomain = false)),
            JassSuit("bells", JassDeck.GERMAN, "diamonds", R.string.suit_bells, MarkCredit("Schellen_Neu.svg", "Jensche", publicDomain = false)),
            JassSuit("shields", JassDeck.GERMAN, "spades", R.string.suit_shields, MarkCredit("Schilten_Neu.svg", "Jensche", publicDomain = false)),
        )

        private val byToken = all.associateBy { it.token }

        fun named(token: String): JassSuit? = byToken[token]

        /**
         * The suit that plays `role` in `deck` - what the trump menu needs to name the same trump Herz
         * on a French deck and Rosen on a German one.
         */
        fun playing(role: String, deck: JassDeck): JassSuit? =
            all.firstOrNull { it.role == role && it.deck == deck }
    }
}

/**
 * Where a drawn suit mark comes from. All eight are vector drawings from Wikimedia Commons: seven by Jensche
 * under CC BY-SA 4.0, which asks for author, licence and a link; Kreuz by F l a n k e r, dedicated to the
 * public domain.
 *
 * @property file The file's name on Wikimedia Commons.
 */
data class MarkCredit(val file: String, val author: String, val publicDomain: Boolean) {
    val page: String get() = "https://commons.wikimedia.org/wiki/File:$file"
}

/**
 * A class label as the model emits it, taken apart.
 *
 * The model has 72 classes - both decks, 36 cards each - and writes them as `suit_rank` ("hearts_ace",
 * "roses_9"). The suit tokens are unique across the decks, so splitting at the first underscore is
 * all it takes to read one.
 */
class CardLabel private constructor(val rank: String, val suit: JassSuit) {

    val deck: JassDeck get() = suit.deck

    /** The French suit whose rules this card follows. */
    val role: String get() = suit.role

    /**
     * The rank as a Jass player says it. The model emits English tokens; on screen the Jass names
     * belong there. They are the same on both decks - a Swiss player calls the jack Under and the
     * queen Ober whether the card shows a Schilte or a Schaufel. Null for the numbered ranks, which read
     * as their [rank].
     */
    @get:StringRes
    val rankName: Int? get() = jassRanks[rank]

    /**
     * Whether the suit is printed in a warm colour, which is also the pair the multipliers of a
     * Schieber call "rot": Herz and Ecken, and by role Rosen and Schellen.
     */
    val isRed: Boolean get() = role == "hearts" || role == "diamonds"

    companion object {
        /** Resolved once, when the label is parsed: an unknown suit means there is no CardLabel at all. */
        fun parse(label: String): CardLabel? {
            val parts = label.split("_", limit = 2)
            if (parts.size != 2) return null
            val suit = JassSuit.named(parts[0]) ?: return null
            return CardLabel(parts[1], suit)
        }

        private val jassRanks = mapOf(
            "jack" to R.string.rank_jack, "queen" to R.string.rank_queen,
            "king" to R.string.rank_king, "ace" to R.string.rank_ace,
        )
    }
}
