import Foundation

/// The virtual pile and the rule that decides what joins it - everything the frame loop decides about
/// cards, with no camera, model or queue in it, so `src/tools/check_pile.swift` can hold it to account.
///
/// One value owns all of it: the cards, the cards removed by hand, the running candidate and the window
/// of recent frames. The live model keeps it behind a single lock and changes it only as a whole - a
/// frame, a correction, a reset - so no half-applied state can be seen from the other side. What the
/// screen shows is a copy; its `version` grows with every change, so a copy that arrives after a newer
/// one has been shown is recognised as stale and dropped.
struct PileTracker {

    /// Cards committed to the pile, oldest first.
    private(set) var cards: [String] = []
    /// Grows with every change to `cards`.
    private(set) var version = 0

    /// Cards the user just removed by hand. They must not be committed again while they are still the
    /// top detection - otherwise deleting a wrong card would undo itself within a few frames. Lifted as
    /// soon as a different card (or none) tops the frame.
    private var suppressed: Set<String> = []
    private var candidate: String?
    private var candidateCount = 0
    /// What the last frames said, newest last; nil = nothing seen, which still takes up a slot so a card
    /// cannot win a window it was mostly absent from.
    private var recent: [String?] = []

    /// Feeds the top label of one frame. Returns true when this frame committed it to the pile.
    ///
    /// The app counts a pile after the game, where each of the deck's 36 cards occurs exactly once, so a
    /// card already on the pile is never committed again. Nothing is committed while `counting` is off:
    /// a frame can still arrive after the session has closed, and a pile without a known discipline
    /// would be worthless. And only the card currently on top can be committed - counting one that is
    /// no longer in view would be indefensible.
    mutating func observe(_ label: String?, rule: StabilityRule, votes: Int, counting: Bool) -> Bool {
        recent.append(label)
        let window = rule.windowSize(votes: votes)
        if recent.count > window { recent.removeFirst(recent.count - window) }

        if label != candidate {
            candidate = label
            candidateCount = 0
            // A different card tops the frame, so a card removed by hand is no longer the one being
            // looked at - it may be recognised again from here on.
            suppressed.removeAll()
        }
        guard let label else { return false }
        candidateCount += 1

        guard counting, !suppressed.contains(label), !cards.contains(label),
              qualifies(label, rule: rule, votes: votes) else { return false }
        change { $0.append(label) }
        return true
    }

    /// Removes a card - the remedy when the model named the wrong one. Returns false when it is not on
    /// the pile.
    mutating func remove(_ label: String) -> Bool {
        guard let index = cards.firstIndex(of: label) else { return false }
        suppressed.insert(label)
        change { $0.remove(at: index) }
        return true
    }

    /// Adds a card by hand. Returns false when it is already on the pile.
    mutating func add(_ label: String) -> Bool {
        guard !cards.contains(label) else { return false }
        change { $0.append(label) }
        return true
    }

    /// Empties the pile and forgets what the last frames said.
    mutating func reset() {
        suppressed.removeAll()
        candidate = nil
        candidateCount = 0
        recent.removeAll()
        change { $0.removeAll() }
    }

    private mutating func change(_ edit: (inout [String]) -> Void) {
        edit(&cards)
        version &+= 1
    }

    /// Whether `label` has earned its place under the selected rule.
    ///
    /// `run` is the plain rule: an unbroken series, where a single stray frame starts it over.
    /// `majority` asks instead that the card win a window of recent frames *and* that no rival
    /// appear in it more than once. A dropout resets nothing there, so fewer cards are missed -
    /// while a misreading during a movement needs a real majority rather than a few neighbouring
    /// frames that happen to agree.
    ///
    /// Cards already counted are not treated as rivals: the previous card stays in view while the
    /// next one is laid down, and it must not block it.
    private func qualifies(_ label: String, rule: StabilityRule, votes: Int) -> Bool {
        switch rule {
        case .run:
            return candidateCount >= votes
        case .majority:
            var tally: [String: Int] = [:]
            for entry in recent { if let entry { tally[entry, default: 0] += 1 } }
            guard (tally[label] ?? 0) >= votes else { return false }
            return !tally.contains { $0.key != label && !cards.contains($0.key) && $0.value > 1 }
        }
    }
}
