// Holds the stability rules and the corrections to account.
//
// The pile is where a slip is silent: a card counted twice, a deleted card that comes back on its own, or
// a rival that should have blocked a misreading all produce a plausible number on the slate. The cases
// below are the same as src/app/android/app/src/test/.../PileTrackerTest.kt, in the same order.
//
//   swiftc -o /tmp/check_pile src/app/ios/Sources/PileTracker.swift src/app/ios/Sources/StabilityRule.swift \
//       src/tools/check_pile.swift && /tmp/check_pile
//
// The files import nothing but Foundation, so this also runs on Linux, next to check_scoring.swift.

import Foundation

@main
enum PileCheck {

    static var failures = 0

    static func check(_ ok: Bool, _ what: @autoclosure () -> String) {
        if !ok { print("FAIL: \(what())"); failures += 1 }
    }

    /// Feeds `labels` frame by frame and returns the cards the pile ends up with.
    static func feed(_ tracker: inout PileTracker, _ rule: StabilityRule, _ votes: Int,
                     _ labels: [String?], counting: Bool = true) -> [String] {
        for label in labels { _ = tracker.observe(label, rule: rule, votes: votes, counting: counting) }
        return tracker.cards
    }

    static func main() {
        // Run commits after the required frames in a row, and only once.
        do {
            var t = PileTracker()
            check(feed(&t, .run, 3, ["a", "a"]) == [], "run: two of three frames commit nothing")
            check(t.observe("a", rule: .run, votes: 3, counting: true), "run: the third frame commits")
            check(feed(&t, .run, 3, ["a", "a", "a"]) == ["a"], "run: a counted card is not committed again")
        }
        // Run starts over after a different frame.
        do {
            var t = PileTracker()
            check(feed(&t, .run, 3, ["a", "a", nil, "a", "a"]) == [], "run: an empty frame breaks the series")
            check(feed(&t, .run, 3, ["a"]) == ["a"], "run: a new series of three commits")
        }
        // Majority tolerates a dropout.
        do {
            var t = PileTracker()
            check(feed(&t, .majority, 3, ["a", nil, "a", "a"]) == ["a"], "majority: a dropout breaks nothing")
        }
        // Majority refuses when a rival appears twice.
        do {
            var t = PileTracker()
            check(feed(&t, .majority, 3, ["a", "b", "a", "b", "a"]) == [], "majority: a rival seen twice blocks")
        }
        // A counted card is no rival.
        do {
            var t = PileTracker()
            _ = t.add("b")
            check(feed(&t, .majority, 3, ["a", "b", "a", "b", "a"]) == ["b", "a"],
                  "majority: the card already on the pile does not block the next")
        }
        // Nothing is committed before counting starts.
        do {
            var t = PileTracker()
            check(feed(&t, .run, 1, ["a", "a"], counting: false) == [], "nothing is committed before counting")
        }
        // A removed card stays off while it is still on top.
        do {
            var t = PileTracker()
            _ = feed(&t, .run, 2, ["a", "a"])
            check(t.remove("a"), "remove: a card on the pile can be removed")
            check(feed(&t, .run, 2, ["a", "a", "a"]) == [], "remove: the card stays off while it is on top")
            check(feed(&t, .run, 2, ["b", "a", "a"]) == ["a"], "remove: after another card it may come back")
        }
        // Corrections report whether they changed anything.
        do {
            var t = PileTracker()
            check(!t.remove("a"), "remove: a card not on the pile changes nothing")
            check(t.add("a"), "add: a new card is added")
            check(!t.add("a"), "add: a card on the pile is not added twice")
            check(t.cards == ["a"], "add: the pile holds the card once")
        }
        // Reset empties the pile and the window.
        do {
            var t = PileTracker()
            _ = feed(&t, .run, 2, ["a", "a", "b"])
            t.reset()
            check(t.cards == [], "reset: the pile is empty")
            check(feed(&t, .run, 2, ["b"]) == [], "reset: frames from before do not count")
        }
        // Every change raises the version, and nothing else does.
        do {
            var t = PileTracker()
            var versions = [t.version]
            _ = t.observe("a", rule: .run, votes: 2, counting: true); versions.append(t.version)
            _ = t.observe("a", rule: .run, votes: 2, counting: true); versions.append(t.version)
            _ = t.remove("a"); versions.append(t.version)
            _ = t.add("a"); versions.append(t.version)
            t.reset(); versions.append(t.version)
            check(versions == [0, 0, 1, 2, 3, 4], "version: \(versions)")
        }

        if failures > 0 {
            print("\(failures) check(s) failed")
            exit(1)
        }
        print("pile: all checks passed")
    }
}
