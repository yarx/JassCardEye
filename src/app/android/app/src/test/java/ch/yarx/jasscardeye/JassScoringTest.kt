package ch.yarx.jasscardeye

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Holds the Jass scoring to account, for both decks - the port of src/tools/check_scoring.swift, with the
 * same seven invariants in the same order, so a rule that changes in both apps has one obvious place on
 * each side.
 *
 * The rules are the one part of the app that is neither obvious from the code nor visible when it is
 * wrong: a German card scoring as a non-trump card, or a discipline totalling 150 instead of 152,
 * produces a plausible number on screen and a wrong one on the slate.
 *
 *     ./gradlew :app:testDebugUnitTest
 */
class JassScoringTest {

    private val decks = listOf(
        JassDeck.FRENCH to listOf("clubs", "diamonds", "hearts", "spades"),
        JassDeck.GERMAN to listOf("acorns", "roses", "bells", "shields"),
    )

    private fun mode(id: String): CountingMode {
        val found = JassModes.mode(id)
        assertNotNull("no discipline with id '$id'", found)
        return found!!
    }

    /** 1. Every discipline totals 152 per deck - the number the opponent arithmetic subtracts from. */
    @Test
    fun everyDisciplineTotals152OnBothDecks() {
        for (mode in JassModes.all) {
            for ((deck, suits) in decks) {
                val total = suits.sumOf { suit -> DeckLayout.ranks.sumOf { mode.points("${suit}_$it") } }
                assertEquals("${mode.id}/$deck", 152, total)
            }
            assertEquals("${mode.id}: deckTotal", 152, mode.deckTotal)
        }
        assertEquals("four trump colours and five disciplines without trump", 9, JassModes.all.size)
        assertEquals("the ids are distinct", JassModes.all.size, JassModes.all.map { it.id }.toSet().size)
    }

    /** 2. Card for card, a German deck scores like the French one it maps onto. */
    @Test
    fun germanCardsScoreLikeTheirFrenchCounterparts() {
        val pairs = listOf("clubs" to "acorns", "hearts" to "roses", "diamonds" to "bells", "spades" to "shields")
        for (mode in JassModes.all) for ((french, german) in pairs) for (rank in DeckLayout.ranks) {
            assertEquals("${mode.id}: ${french}_$rank vs ${german}_$rank",
                mode.points("${french}_$rank"), mode.points("${german}_$rank"))
        }
    }

    /** 3. Trump reaches the German suit that plays its role, and is named for the deck in play. */
    @Test
    fun trumpLandsOnTheRightSuitAndIsNamedForTheDeck() {
        val hearts = mode("trump.hearts")
        assertEquals("Rosen Under under Rosen trump is 20", 20, hearts.points("roses_jack"))
        assertEquals("Rosen Nell under Rosen trump is 14", 14, hearts.points("roses_9"))
        assertEquals("Schilten Under is not trump, so 2", 2, hearts.points("shields_jack"))
        assertEquals("Rosen", hearts.displayName(JassDeck.GERMAN))
        assertEquals("Herz", hearts.displayName(JassDeck.FRENCH))
        assertEquals("Schilten", mode("trump.spades").displayName(JassDeck.GERMAN))
        val obenabe = mode("obenabe")
        assertEquals("Obenabe is named the same on both decks",
            obenabe.displayName(JassDeck.FRENCH), obenabe.displayName(JassDeck.GERMAN))

        assertEquals("roses", CardLabel.parse("roses_jack")?.suit?.token)
        assertEquals("Under", CardLabel.parse("roses_jack")?.rankName)
        assertEquals("spades", CardLabel.parse("spades_10")?.suit?.token)
        assertEquals("10", CardLabel.parse("spades_10")?.rankName)
        assertEquals("Schellen follows Ecken, so red", true, CardLabel.parse("bells_ace")?.isRed)
        assertNull("an unknown suit is no card", CardLabel.parse("nonsense"))
    }

    /** 4. Each deck owns four suits with a mark of its own. */
    @Test
    fun eachDeckOwnsFourSuitsWithMarksOfTheirOwn() {
        assertEquals("two decks", 2, JassDeck.entries.size)
        assertEquals("Französisch", JassDeck.FRENCH.displayName)
        assertEquals("Deutsch", JassDeck.GERMAN.displayName)
        for (deck in JassDeck.entries) {
            val suits = JassSuit.all.filter { it.deck == deck }
            assertEquals("$deck has four suits", 4, suits.size)
            assertEquals("$deck picker shows all four of its suits",
                suits.map { it.token }.toSet(), deck.suits.map { it.token }.toSet())
            assertEquals("$deck picker is in role order", DeckLayout.suits, deck.suits.map { it.role })
        }
        assertEquals("all eight marks differ", 8, JassSuit.all.map { it.token }.toSet().size)
        assertEquals("all eight names differ", 8, JassSuit.all.map { it.name }.toSet().size)
    }

    /** 5. The rule the frame loop applies to every detection: a card of the other deck is not a card tonight. */
    @Test
    fun theDeckFilterKeepsExactlyTheDeckInPlay() {
        val everyLabel = JassSuit.all.flatMap { suit -> DeckLayout.ranks.map { "${suit.token}_$it" } }
        assertEquals("the model emits 72 labels", 72, everyLabel.size)
        for (deck in JassDeck.entries) {
            val kept = (everyLabel + "nonsense").filter { CardLabel.parse(it)?.deck == deck }
            assertEquals("$deck keeps 36 of 73", 36, kept.size)
            assertTrue("$deck keeps only its own suits",
                kept.all { JassSuit.named(it.substringBefore('_'))?.deck == deck })
        }
        assertEquals(JassDeck.GERMAN, CardLabel.parse("shields_ace")?.deck)
        assertEquals(JassDeck.FRENCH, CardLabel.parse("spades_ace")?.deck)
    }

    /** 6. Each discipline counts with the table its rules prescribe - Guschti counted obenabe throughout. */
    @Test
    fun eachDisciplineCountsWithItsTable() {
        val expected = listOf(
            Triple("obenabe", ValueTables.obenabe, "Ass 11"),
            Triple("undenufe", ValueTables.undenufe, "Sechs 11"),
            Triple("slalom.obe", ValueTables.obenabe, "obe begonnen"),
            Triple("slalom.unde", ValueTables.undenufe, "unde begonnen"),
            Triple("guschti", ValueTables.obenabe, "gezählt wird obenabe"),
        )
        for ((id, table, why) in expected) {
            val found = mode(id)
            for (rank in DeckLayout.ranks) {
                assertEquals("$id ($why): $rank", table[rank] ?: 0, found.points("hearts_$rank"))
            }
            assertNull("$id has no trump suit", found.trumpRole)
        }
        assertEquals("Guschti: Ass 11", 11, mode("guschti").points("hearts_ace"))
        assertEquals("Guschti: Sechs 0", 0, mode("guschti").points("hearts_6"))
        assertEquals("Slalom unten: Sechs 11", 11, mode("slalom.unde").points("hearts_6"))
        assertEquals("Slalom unten: Ass 0", 0, mode("slalom.unde").points("hearts_ace"))
    }

    /** 7. The written result: a full pile with the last trick is 157, and the factor moves both sides. */
    @Test
    fun theWrittenResultHolds() {
        for (mode in JassModes.all) {
            val full = Tally(mode, LastTrick.MINE, 1, mode.deckTotal)
            assertEquals("${mode.id}: full pile with the last trick", 157, full.points)
            assertEquals("${mode.id}: opponents then", 0, full.opponentPoints)
            val unused = Tally(mode, LastTrick.UNUSED, 1, mode.deckTotal)
            assertEquals("${mode.id}: full pile without the last trick", 152, unused.points)
            assertEquals("${mode.id}: unused gives the opponents nothing", 0, unused.opponentPoints)
            val none = Tally(mode, LastTrick.OPPONENTS, 1, 0)
            assertEquals("${mode.id}: empty pile", 0, none.points)
            assertEquals("${mode.id}: opponents then", 157, none.opponentPoints)
        }
        val obenabe = mode("obenabe")
        for (factor in JassRules.multipliers) {
            val split = Tally(obenabe, LastTrick.MINE, factor, 60)
            assertEquals("×$factor: own points", (60 + 5) * factor, split.points)
            assertEquals("×$factor: opponent points", (152 - 60) * factor, split.opponentPoints)
            assertEquals("×$factor: both sides add up", 157 * factor, split.points + split.opponentPoints)
        }
        assertEquals("the factors on offer are ×1 to ×8", (1..8).toList(), JassRules.multipliers)
        assertEquals("the last trick is worth five", 5, JassRules.LAST_TRICK_BONUS)
    }
}
