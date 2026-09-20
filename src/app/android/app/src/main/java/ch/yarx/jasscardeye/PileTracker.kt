package ch.yarx.jasscardeye

// Port of src/app/ios/Sources/PileTracker.swift.

/**
 * The virtual pile and the rule that decides what joins it - everything the frame loop decides about
 * cards, with no camera, model or thread in it, so `PileTrackerTest` can hold it to account.
 *
 * One object owns all of it: the cards, the cards removed by hand, the running candidate and the window of
 * recent frames. The live model keeps it behind a single lock and changes it only as a whole - a frame, a
 * correction, a reset - so no half-applied state can be seen from the other thread. What the screen shows
 * is a [Snapshot]; its [Snapshot.version] grows with every change, so a snapshot that arrives after a newer
 * one has been shown is recognised as stale and dropped.
 *
 * Not thread-safe by itself; the owner provides the lock.
 */
class PileTracker {

    /** The pile at one moment: cards oldest first, and a version that grows with every change. */
    data class Snapshot(val cards: List<String>, val version: Int)

    var snapshot = Snapshot(emptyList(), 0); private set

    /**
     * Cards the user just removed by hand. They must not be committed again while they are still the top
     * detection - otherwise deleting a wrong card would undo itself within a few frames. Lifted as soon as a
     * different card (or none) tops the frame.
     */
    private val suppressed = HashSet<String>()
    private var candidate: String? = null
    private var candidateCount = 0

    /** What the last frames said, newest last; null = nothing seen, which still takes up a slot. */
    private val recent = ArrayDeque<String?>()

    /**
     * Feeds the top label of one frame. Returns true when this frame committed it to the pile.
     *
     * The app counts a pile after the game, where each of the deck's 36 cards occurs exactly once, so a card
     * already on the pile is never committed again. Nothing is committed while [counting] is off, and only
     * the card currently on top can be - counting one that is no longer in view would be indefensible.
     */
    fun observe(label: String?, rule: StabilityRule, votes: Int, counting: Boolean): Boolean {
        // The window both rules read from. A frame with no card is kept as null rather than skipped: it takes
        // up a slot, so a card cannot win a window it was mostly absent from.
        recent.addLast(label)
        while (recent.size > rule.windowSize(votes)) recent.removeFirst()

        if (label != candidate) {
            candidate = label
            candidateCount = 0
            // A different card tops the frame, so a card removed by hand may be recognised again.
            suppressed.clear()
        }
        if (label == null) return false
        candidateCount += 1

        if (!counting || label in suppressed || label in snapshot.cards || !qualifies(label, rule, votes)) return false
        change(snapshot.cards + label)
        return true
    }

    /** Removes a card - the remedy when the model named the wrong one. Returns false when it is not on the pile. */
    fun remove(label: String): Boolean {
        if (label !in snapshot.cards) return false
        suppressed += label
        change(snapshot.cards - label)
        return true
    }

    /** Adds a card by hand. Returns false when it is already on the pile. */
    fun add(label: String): Boolean {
        if (label in snapshot.cards) return false
        change(snapshot.cards + label)
        return true
    }

    /** Empties the pile and forgets what the last frames said. */
    fun reset() {
        suppressed.clear()
        candidate = null
        candidateCount = 0
        recent.clear()
        change(emptyList())
    }

    private fun change(cards: List<String>) {
        snapshot = Snapshot(cards, snapshot.version + 1)
    }

    /**
     * Whether [label] has earned its place under [rule]. `RUN` is an unbroken series, where a single stray
     * frame starts it over. `MAJORITY` asks instead that the card win the window *and* that no rival appear in
     * it more than once: a dropout does not reset anything, while a misreading during a movement needs a
     * real majority rather than a few neighbouring frames that happened to agree.
     *
     * Cards already counted are not rivals: the previous card stays in view while the next one is laid down,
     * and it must not block it.
     */
    private fun qualifies(label: String, rule: StabilityRule, votes: Int): Boolean = when (rule) {
        StabilityRule.RUN -> candidateCount >= votes
        StabilityRule.MAJORITY -> {
            val tally = recent.filterNotNull().groupingBy { it }.eachCount()
            (tally[label] ?: 0) >= votes &&
                tally.none { (other, count) -> other != label && other !in snapshot.cards && count > 1 }
        }
    }
}
