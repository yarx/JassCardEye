package ch.yarx.jasscardeye

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Holds the stability rules and the corrections to account - the port of src/tools/check_pile.swift, with the
 * same cases in the same order.
 *
 *     ./gradlew :app:testDebugUnitTest
 */
class PileTrackerTest {

    /** Feeds [labels] frame by frame and returns the cards the pile ends up with. */
    private fun PileTracker.feed(rule: StabilityRule, votes: Int, vararg labels: String?, counting: Boolean = true): List<String> {
        for (label in labels) observe(label, rule, votes, counting)
        return snapshot.cards
    }

    @Test
    fun runCommitsAfterTheRequiredFramesInARowAndOnlyOnce() {
        val tracker = PileTracker()
        assertEquals(emptyList<String>(), tracker.feed(StabilityRule.RUN, 3, "a", "a"))
        assertTrue(tracker.observe("a", StabilityRule.RUN, 3, counting = true))
        assertEquals(listOf("a"), tracker.feed(StabilityRule.RUN, 3, "a", "a", "a"))
    }

    @Test
    fun runStartsOverAfterADifferentFrame() {
        val tracker = PileTracker()
        assertEquals(emptyList<String>(), tracker.feed(StabilityRule.RUN, 3, "a", "a", null, "a", "a"))
        assertEquals(listOf("a"), tracker.feed(StabilityRule.RUN, 3, "a"))
    }

    @Test
    fun majorityToleratesADropout() {
        assertEquals(listOf("a"), PileTracker().feed(StabilityRule.MAJORITY, 3, "a", null, "a", "a"))
    }

    @Test
    fun majorityRefusesWhenARivalAppearsTwice() {
        assertEquals(emptyList<String>(), PileTracker().feed(StabilityRule.MAJORITY, 3, "a", "b", "a", "b", "a"))
    }

    @Test
    fun aCountedCardIsNoRival() {
        val tracker = PileTracker()
        tracker.add("b")
        assertEquals(listOf("b", "a"), tracker.feed(StabilityRule.MAJORITY, 3, "a", "b", "a", "b", "a"))
    }

    @Test
    fun nothingIsCommittedBeforeCountingStarts() {
        assertEquals(emptyList<String>(), PileTracker().feed(StabilityRule.RUN, 1, "a", "a", counting = false))
    }

    @Test
    fun aRemovedCardStaysOffWhileItIsStillOnTop() {
        val tracker = PileTracker()
        tracker.feed(StabilityRule.RUN, 2, "a", "a")
        assertTrue(tracker.remove("a"))
        assertEquals(emptyList<String>(), tracker.feed(StabilityRule.RUN, 2, "a", "a", "a"))
        // Once another card has been on top, it may be recognised again.
        assertEquals(listOf("a"), tracker.feed(StabilityRule.RUN, 2, "b", "a", "a"))
    }

    @Test
    fun correctionsReportWhetherTheyChangedAnything() {
        val tracker = PileTracker()
        assertFalse(tracker.remove("a"))
        assertTrue(tracker.add("a"))
        assertFalse(tracker.add("a"))
        assertEquals(listOf("a"), tracker.snapshot.cards)
    }

    @Test
    fun resetEmptiesThePileAndTheWindow() {
        val tracker = PileTracker()
        tracker.feed(StabilityRule.RUN, 2, "a", "a", "b")
        tracker.reset()
        assertEquals(emptyList<String>(), tracker.snapshot.cards)
        // The "b" seen before the reset does not count towards the run.
        assertEquals(emptyList<String>(), tracker.feed(StabilityRule.RUN, 2, "b"))
    }

    @Test
    fun everyChangeRaisesTheVersionAndNothingElseDoes() {
        val tracker = PileTracker()
        val versions = mutableListOf(tracker.snapshot.version)
        tracker.observe("a", StabilityRule.RUN, 2, counting = true); versions += tracker.snapshot.version   // no change
        tracker.observe("a", StabilityRule.RUN, 2, counting = true); versions += tracker.snapshot.version   // commit
        tracker.remove("a"); versions += tracker.snapshot.version
        tracker.add("a"); versions += tracker.snapshot.version
        tracker.reset(); versions += tracker.snapshot.version
        assertEquals(listOf(0, 0, 1, 2, 3, 4), versions)
    }
}
