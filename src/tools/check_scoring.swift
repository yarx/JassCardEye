// Holds the Jass scoring to account, for both decks.
//
// The rules are the one part of the app that is neither obvious from the code nor visible when it
// is wrong: a German card scoring as a non-trump card, or a discipline totalling 150 instead of
// 152, produces a plausible number on screen and a wrong one on the slate. The invariants are
// cheap to state:
//
//   1. Every discipline totals 152 across a deck - French or German.
//   2. A German card is worth exactly what the French card it stands for is worth.
//   3. Trump lands on the right suit, and the question names it for the deck in play.
//   4. Each deck owns four suits with a mark of its own.
//   5. The frame loop's rule: a card of the other deck is not a card tonight.
//   6. Each discipline counts with the table its rules prescribe.
//   7. The written result: a full pile with the last trick is 157, and the factor moves both sides.
//
//   swiftc -o /tmp/check_scoring src/app/ios/Sources/JassScoring.swift src/app/ios/Sources/JassDeck.swift \
//       src/tools/check_scoring.swift && /tmp/check_scoring
//
// The three files import nothing but Foundation, so this also compiles and runs on Linux - where a
// runner is free, unlike the macOS ones this repository's Actions budget has to ration.

import Foundation

@main
enum ScoringCheck {

    static var failures = 0

    static func check(_ ok: Bool, _ what: @autoclosure () -> String) {
        if !ok { print("FAIL: \(what())"); failures += 1 }
    }

    /// Looks a discipline up and reports a miss instead of trapping - a checker that crashes on a
    /// missing entry stops before the sections after it have run.
    static func mode(_ id: String) -> CountingMode? {
        guard let found = JassModes.mode(id: id) else {
            check(false, "no discipline with id '\(id)'")
            return nil
        }
        return found
    }

    static func main() {
        let decks: [(JassDeck, [String])] = [
            (.french, ["clubs", "diamonds", "hearts", "spades"]),
            (.german, ["acorns", "roses", "bells", "shields"]),
        ]

        // 1. Every discipline totals 152 per deck - the number the opponent arithmetic subtracts
        //    from, so a wrong one moves both scores at once.
        for mode in JassModes.all {
            for (deck, suits) in decks {
                let total = suits.reduce(0) { sum, suit in
                    sum + DeckLayout.ranks.reduce(0) { $0 + mode.points(forLabel: "\(suit)_\($1)") }
                }
                check(total == 152, "\(mode.id)/\(deck): \(total) instead of 152")
            }
            check(mode.deckTotal == 152, "\(mode.id): deckTotal \(mode.deckTotal)")
        }
        check(JassModes.all.count == 9, "four trump colours and five disciplines without trump")
        check(Set(JassModes.all.map(\.id)).count == JassModes.all.count, "the ids are distinct")

        // 2. Card for card, a German deck scores like the French one it maps onto.
        let pairs = [("clubs", "acorns"), ("hearts", "roses"), ("diamonds", "bells"),
                     ("spades", "shields")]
        for mode in JassModes.all {
            for (french, german) in pairs {
                for rank in DeckLayout.ranks {
                    let a = mode.points(forLabel: "\(french)_\(rank)")
                    let b = mode.points(forLabel: "\(german)_\(rank)")
                    check(a == b, "\(mode.id): \(french)_\(rank)=\(a) but \(german)_\(rank)=\(b)")
                }
            }
        }

        // 3. Trump reaches the German suit that plays its role, and is named for the deck in play.
        if let hearts = mode("trump.hearts") {
            check(hearts.points(forLabel: "roses_jack") == 20, "Rosen Under under Rosen trump is 20")
            check(hearts.points(forLabel: "roses_9") == 14, "Rosen Nell under Rosen trump is 14")
            check(hearts.points(forLabel: "shields_jack") == 2, "Schilten Under is not trump, so 2")
            check(hearts.displayName(deck: .german) == "Rosen", hearts.displayName(deck: .german))
            check(hearts.displayName(deck: .french) == "Herz", hearts.displayName(deck: .french))
        }
        if let spades = mode("trump.spades") {
            check(spades.displayName(deck: .german) == "Schilten",
                  spades.displayName(deck: .german))
        }
        // A discipline without trump reads the same on both decks - it is about the cards' values,
        // not their pictures.
        if let obenabe = mode("obenabe") {
            check(obenabe.displayName(deck: .german) == obenabe.displayName(deck: .french),
                  "Obenabe is named the same on both decks")
        }

        // The labels themselves, since everything above goes through them. A chip is a drawn mark
        // beside a rank rather than one string, so the two halves are checked apart.
        check(CardLabel("roses_jack")?.suit.token == "roses", "roses_jack suit")
        check(CardLabel("roses_jack")?.rankName == "Under", "roses_jack rank")
        check(CardLabel("spades_10")?.suit.token == "spades", "spades_10 suit")
        check(CardLabel("spades_10")?.rankName == "10", "spades_10 rank")
        check(CardLabel("bells_ace")?.isRed == true, "Schellen follows Ecken, so red")
        check(CardLabel("nonsense") == nil, "an unknown suit is no card")

        // 4. The deck is a choice with exactly two options, and each one owns four suits with a
        //    mark of its own - which is what lets the frame loop drop everything from the other
        //    deck and the start question show which is which.
        check(JassDeck.allCases.count == 2, "two decks")
        check(JassDeck.french.name == "Französisch" && JassDeck.german.name == "Deutsch",
              "deck names")
        for deck in JassDeck.allCases {
            let suits = JassSuit.all.filter { $0.deck == deck }
            check(suits.count == 4, "\(deck) has four suits")
            // What the picker offers, in role order so it reads like the trump row under it.
            check(Set(deck.suits.map(\.token)) == Set(suits.map(\.token)),
                  "\(deck) picker shows all four of its suits")
            check(deck.suits.map(\.role) == DeckLayout.suits, "\(deck) picker is in role order")
        }
        // Each suit's mark is vector art in the asset catalogue under its own token, so a token
        // that repeats would silently draw one suit as another.
        check(Set(JassSuit.all.map(\.token)).count == 8, "all eight marks differ")
        check(Set(JassSuit.all.map(\.name)).count == 8, "all eight names differ")

        // 5. The rule the frame loop applies to every detection: a card of the other deck is not a
        //    card tonight. Written here over all 72 labels the model can emit, plus one it cannot,
        //    because in the app it is one line in a loop that only runs with a camera in front.
        let everyLabel = JassSuit.all.flatMap { suit in
            DeckLayout.ranks.map { "\(suit.token)_\($0)" }
        }
        check(everyLabel.count == 72, "the model emits 72 labels, not \(everyLabel.count)")
        for deck in JassDeck.allCases {
            // "nonsense" stands in for a label no model should emit: it has to be dropped like a
            // foreign card, not slip through and not crash.
            let kept = (everyLabel + ["nonsense"]).filter { CardLabel($0)?.deck == deck }
            check(kept.count == 36, "\(deck) keeps 36 of 73, not \(kept.count)")
            check(kept.allSatisfy { JassSuit.named(String($0.split(separator: "_")[0]))?.deck == deck },
                  "\(deck) keeps only its own suits")
        }
        // Named cases, so a mapping that silently flipped would be caught rather than counted.
        check(CardLabel("shields_ace")?.deck == .german, "Schilten belongs to the German deck")
        check(CardLabel("spades_ace")?.deck == .french, "Schaufel belongs to the French deck")

        // 6. Each discipline counts with the table its rules prescribe. Guschti is the one worth
        //    stating: it is played as four tricks obenabe and five undeufe, but counted obenabe
        //    throughout - which is the only reason a finished pile can be counted at all.
        let expected: [(String, [String: Int], String)] = [
            ("obenabe",     ValueTables.obenabe,  "Ass 11"),
            ("undenufe",    ValueTables.undenufe, "Sechs 11"),
            ("slalom.obe",  ValueTables.obenabe,  "obe begonnen"),
            ("slalom.unde", ValueTables.undenufe, "unde begonnen"),
            ("guschti",     ValueTables.obenabe,  "gezählt wird obenabe"),
        ]
        for (id, table, why) in expected {
            guard let found = mode(id) else { continue }
            for rank in DeckLayout.ranks {
                let want = table[rank] ?? 0
                let got = found.points(forLabel: "hearts_\(rank)")
                check(got == want, "\(id) (\(why)): \(rank) is \(got), should be \(want)")
            }
            check(found.trumpRole == nil, "\(id) has no trump suit")
        }
        // The two that decide whether the Ace or the Six is the eleven.
        if let guschti = mode("guschti") {
            check(guschti.points(forLabel: "hearts_ace") == 11, "Guschti: Ass 11")
            check(guschti.points(forLabel: "hearts_6") == 0, "Guschti: Sechs 0")
        }
        if let unde = mode("slalom.unde") {
            check(unde.points(forLabel: "hearts_6") == 11, "Slalom unten: Sechs 11")
            check(unde.points(forLabel: "hearts_ace") == 0, "Slalom unten: Ass 0")
        }

        // 7. The written result. A pile of all 36 cards is 152, and with the last trick that is
        //    157 - the number a full haul is written down as, with no match bonus anywhere.
        for mode in JassModes.all {
            let full = Tally(mode: mode, lastTrick: .mine, multiplier: 1, cardPoints: mode.deckTotal)
            check(full.points == 157, "\(mode.id): a full pile with the last trick is \(full.points)")
            check(full.opponentPoints == 0, "\(mode.id): the opponents then have \(full.opponentPoints)")

            // Without the last trick the same pile is the bare 152, and nobody gets the five.
            let unused = Tally(mode: mode, lastTrick: .unused, multiplier: 1, cardPoints: mode.deckTotal)
            check(unused.points == 152, "\(mode.id): without the last trick a full pile is \(unused.points)")
            check(unused.opponentPoints == 0, "\(mode.id): unused gives the opponents nothing")

            // An empty pile: everything is the opponents', including their last trick.
            let none = Tally(mode: mode, lastTrick: .opponents, multiplier: 1, cardPoints: 0)
            check(none.points == 0, "\(mode.id): an empty pile is 0")
            check(none.opponentPoints == 157, "\(mode.id): the opponents then have \(none.opponentPoints)")
        }
        // The factor multiplies both sides, and both parties' points always add up to the same
        // round total - which is what makes the opponent column trustworthy at a glance.
        if let obenabe = mode("obenabe") {
            for factor in JassRules.multipliers {
                let split = Tally(mode: obenabe, lastTrick: .mine, multiplier: factor, cardPoints: 60)
                check(split.points == (60 + 5) * factor, "×\(factor): own points")
                check(split.opponentPoints == (152 - 60) * factor, "×\(factor): opponent points")
                check(split.points + split.opponentPoints == 157 * factor,
                      "×\(factor): the two sides add up to 157×\(factor)")
            }
        }
        check(JassRules.multipliers == Array(1...8), "the factors on offer are ×1 to ×8")
        check(JassRules.lastTrickBonus == 5, "the last trick is worth five")

        guard failures == 0 else {
            print("\(failures) check(s) failed")
            // Without this the process ends with 0 and every caller believes the scoring holds -
            // the `&&` in the invocation above, a git hook, a CI step. A checker that cannot fail
            // is worse than none, because it is quoted as evidence.
            exit(1)
        }
        print("OK: scoring, decks, marks, the deck filter and the written result hold")
    }
}
