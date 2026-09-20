import Foundation

// Jass scoring for a pile counted after the game.
//
// Behind the variety of disciplines there are only three value tables - trump, Obenabe, Undenufe -
// and every discipline the app offers keeps one of them for the whole round. That is not a
// simplification but the reason a finished pile can be counted at all: Guschti plays the first four
// tricks obenabe and the last five undeufe, yet "gezählt werden die Punkte wie beim Obenaben", so
// the cards are worth the same whichever trick they came from. A discipline whose *values* changed
// mid-game could not be counted from a pile, because a pile does not remember its tricks.
//
// There are no rule sets, no per-mode multipliers and no match bonus. The multiplier is not a
// property of the discipline but a decision the players make, so it is asked with the start
// questions, ×1 unless somebody picks another. The match needs no counting: whoever holds all 36
// cards has 152, plus the five for the last trick makes 157, and that is the number the app shows
// without being told about it.
//
// Both decks score through the same four suits. A German card is worth what the French card it
// stands for is worth - Rosen is Herz as far as the rules go, and only the picture differs - so
// everything here deals in `CardLabel.role` and never in the token the model emitted.

/// How a deck is laid out for the rules: four suits by role, nine ranks, 36 cards. The suits are
/// the French tokens because that is what a role is named after; a German pile counts through the
/// same four.
enum DeckLayout {
    static let suits = ["clubs", "diamonds", "hearts", "spades"]
    static let ranks = ["6", "7", "8", "9", "10", "jack", "queen", "king", "ace"]
    static let count = 36
}

// MARK: - The three value tables

/// Every discipline is built from these (plus the plain table for the non-trump suits of a trump
/// game). Each of them totals 152 across the 36 cards, which `check_scoring.swift` holds to account.
enum ValueTables {
    static let trump:    [String: Int] = ["jack": 20, "9": 14, "ace": 11, "10": 10, "king": 4, "queen": 3]
    static let plain:    [String: Int] = ["ace": 11, "10": 10, "king": 4, "queen": 3, "jack": 2]
    static let obenabe:  [String: Int] = ["ace": 11, "10": 10, "8": 8, "king": 4, "queen": 3, "jack": 2]
    static let undenufe: [String: Int] = ["6": 11, "10": 10, "8": 8, "king": 4, "queen": 3, "jack": 2]
}

// MARK: - Rules that do not depend on the discipline

enum JassRules {
    /// Points for taking the last trick. Not readable from a pile, so the session asks.
    static let lastTrickBonus = 5
    /// What the players may multiply the written result by. The classic Coiffeur factors.
    static let multipliers = Array(1...8)
}

/// Who took the last trick - or that this round is being counted without it.
///
/// The third case is the point: the bonus is a convention, not a rule of every table, and a round
/// counted with five points nobody awarded is wrong in a way that looks right.
enum LastTrick: String, CaseIterable, Identifiable, Codable {
    case mine, opponents, unused

    var id: String { rawValue }

    /// For the segmented control, where three options share the width of a phone.
    var shortName: String {
        switch self {
        case .mine:      return "Wir"
        case .opponents: return "Gegner"
        case .unused:    return "Keiner"
        }
    }

    /// What it adds under the score, once the round is being counted. Empty when there is nothing
    /// to explain.
    var note: String? {
        switch self {
        case .mine:      return "+\(JassRules.lastTrickBonus) letzter Stich"
        case .opponents: return "letzter Stich bei den Gegnern"
        case .unused:    return nil
        }
    }
}

// MARK: - Disciplines

/// The two shapes a discipline comes in, and the reason it is an enum rather than four optionals.
///
/// A discipline is either named after its trump suit - and then the deck on the table decides what
/// it is called and which mark stands over it - or it is played without trump and carries its own
/// name and symbol. Stored as four optionals side by side, the two shapes could be mixed: a trump
/// with an arrow, an Obenabe with no name, and every reader would have to fall back on something
/// for a state that cannot exist. Here it cannot be built in the first place, and nothing
/// downstream falls back.
enum ModeKind: Hashable {
    /// Named after the suit whose cards use `trumpValues`, by role ("clubs", "hearts", …).
    case trump(role: String)
    /// Played without trump: its own name, its own SF Symbol, and a short name for a narrow cell.
    ///
    /// An SF Symbol rather than an arrow emoji: next to the drawn suit marks an emoji arrow reads
    /// as a different kind of thing, and a symbol takes the picker's own colour.
    case open(label: String, mark: String, shortName: String)
}

/// One way a round can be counted - an entry of the question asked before the camera starts.
struct CountingMode: Identifiable, Hashable {
    /// Stable identity, never shown. The display name is derived, so the deck on the table can
    /// change what a mode is called without changing what it *is*.
    let id: String
    /// Whether it is named after a trump suit or carries its own name - see `ModeKind`.
    let kind: ModeKind
    /// Points per rank token in the trump suit.
    let trumpValues: [String: Int]
    /// Points per rank token in every other suit.
    let normalValues: [String: Int]
    /// One line shown under the picker for whichever entry is selected: which card is suddenly
    /// worth 11 is exactly the mistake that step exists to prevent. Shown for the selection only -
    /// nine hints at once is a wall nobody reads, and a screen that has to be scrolled hides half
    /// the choices.
    let hint: String

    /// The role whose cards score as trump, or nil for a discipline played without one. Derived
    /// from `kind`, so it cannot disagree with the name or the mark.
    var trumpRole: String? {
        if case .trump(let role) = kind { return role }
        return nil
    }

    /// The suit whose mark stands above the name in the picker, or `nil` for a discipline that is
    /// played without trump and shows its own symbol instead.
    func markSuit(deck: JassDeck) -> JassSuit? {
        trumpRole.flatMap { JassSuit.playing($0, in: deck) }
    }

    /// The name under the mark. Shorter than `displayName` where a cell is narrow: the score row
    /// has room for "Slalom (oben)", a fifth of a phone's width does not.
    func shortName(deck: JassDeck) -> String {
        switch kind {
        case .trump:                     return suitName(deck: deck)
        case .open(_, _, let shortName): return shortName
        }
    }

    /// The name for the deck on the table: the same trump reads "Herz" on a French deck and
    /// "Rosen" on a German one. Derived from the suit table rather than rewritten from a stored
    /// string, so a name and the cards it refers to cannot drift apart.
    ///
    /// Text only. It travels into capture and recording file names, and it is what VoiceOver
    /// reads; the mark that goes with it is drawn separately wherever there is room for it.
    func displayName(deck: JassDeck) -> String {
        switch kind {
        case .trump:                return suitName(deck: deck)
        case .open(let label, _, _): return label
        }
    }

    /// The trump suit as this deck prints it. The role always resolves - both decks own all four -
    /// so the role itself is the last resort, and one place carries that rather than every caller.
    private func suitName(deck: JassDeck) -> String {
        guard let role = trumpRole else { return id }
        return JassSuit.playing(role, in: deck)?.name ?? role
    }

    /// Card points of one model label ("spades_10", "roses_9") in this discipline.
    ///
    /// Compared by role rather than by token: with Rosen as trump the model emits "roses_9", and
    /// that card is the Nell of the trump suit exactly as "hearts_9" would be.
    func points(forLabel label: String) -> Int {
        guard let card = CardLabel(label) else { return 0 }
        let table = card.role == trumpRole ? trumpValues : normalValues
        return table[card.rank] ?? 0
    }

    /// Card points of the full deck - 152 in every discipline. Recomputed from the tables rather
    /// than hardcoded, so the opponent arithmetic stays honest if a table is ever edited.
    var deckTotal: Int {
        DeckLayout.suits.reduce(0) { sum, suit in
            sum + DeckLayout.ranks.reduce(0) { $0 + points(forLabel: "\(suit)_\($1)") }
        }
    }
}

/// What the mode question offers, in the order it offers it: the four trump colours first, because
/// they are the common case, then the disciplines without trump.
///
/// Rules follow jassverzeichnis.ch, "Die 10 besten Trumpfarten beim Jassen". The arten not listed
/// here are counted through one of these: Mary counts undenufe, Mezzo and Misère count obenabe.
enum JassModes {

    static let all: [CountingMode] = suits + [obenabe, undenufe, slalomObe, slalomUnde, guschti]

    static func mode(id: String) -> CountingMode? { all.first { $0.id == id } }

    /// The default when nothing was chosen yet - only ever a starting point for the question.
    static let standard = suits[0]

    // The suit order matches the class-ID order (clubs, diamonds, hearts, spades), so the menu
    // reads the same way the model numbers its classes.
    private static let suits: [CountingMode] = DeckLayout.suits.map { suit in
        CountingMode(id: "trump.\(suit)", kind: .trump(role: suit),
                     trumpValues: ValueTables.trump, normalValues: ValueTables.plain,
                     hint: "Under 20, Nell 14")
    }

    private static func noTrump(_ id: String, _ label: String, _ values: [String: Int],
                                _ hint: String, _ mark: String, _ short: String) -> CountingMode {
        CountingMode(id: id, kind: .open(label: label, mark: mark, shortName: short),
                     trumpValues: values, normalValues: values, hint: hint)
    }

    static let obenabe = noTrump("obenabe", "Obenabe", ValueTables.obenabe,
                                 "Ass zählt 11, Acht 8", "arrow.down", "Obenabe")

    static let undenufe = noTrump("undenufe", "Undenufe", ValueTables.undenufe,
                                  "Sechs zählt 11, Ass 0", "arrow.up", "Undenufe")

    // Slalom alternates obenabe and undeufe from trick to trick; which of the two it started with
    // is what decides the values for the round, the same way Guschti's start decides its.
    static let slalomObe = noTrump("slalom.obe", "Slalom (oben)", ValueTables.obenabe,
                                   "Obe begonnen – gezählt wird obenabe", "arrow.up.arrow.down", "Slalom ↓")

    static let slalomUnde = noTrump("slalom.unde", "Slalom (unten)", ValueTables.undenufe,
                                    "Unde begonnen – gezählt wird undenufe", "arrow.up.arrow.down", "Slalom ↑")

    // One entry, no direction to ask about: Guschti always begins obenabe, and the switch to
    // undeufe after the fourth trick changes how it is played, not what the cards are worth.
    static let guschti = noTrump("guschti", "Guschti", ValueTables.obenabe,
                                 "4 Stiche obenabe, dann 5 undeufe – gezählt wird obenabe",
                                 "arrow.triangle.branch", "Guschti")
}

/// The arithmetic of a counted pile, with no camera around it.
///
/// Separate from `LiveDetectionModel` on purpose. The model is `@MainActor` and cannot be compiled
/// on its own, so anything living there cannot be held to account by `check_scoring.swift` - and
/// these numbers are exactly the part that goes wrong in a way nobody notices: a plausible figure
/// on screen and a wrong one on the slate.
struct Tally {
    let mode: CountingMode
    let lastTrick: LastTrick
    let multiplier: Int
    /// Card points of the pile counted so far.
    let cardPoints: Int

    /// The five for the last trick, when this side claimed it.
    var bonus: Int { lastTrick == .mine ? JassRules.lastTrickBonus : 0 }

    /// What gets written on the slate for this side.
    var points: Int { (cardPoints + bonus) * multiplier }

    /// The other side's written points: the cards this side does not hold, plus the last trick if
    /// it was theirs. The factor applies to both, because both get written under it.
    var opponentPoints: Int {
        let rest = mode.deckTotal - cardPoints
            + (lastTrick == .opponents ? JassRules.lastTrickBonus : 0)
        return rest * multiplier
    }
}

/// What a finished counting session produced - handed back to the home screen.
struct CountResult: Identifiable, Hashable {
    let id = UUID()
    var modeName: String
    /// The suit token of the trump it was counted in, so the home screen can draw the same mark
    /// the session showed. `nil` for a discipline played without trump.
    var modeSuit: String?
    var cards: Int
    var points: Int
    var opponentPoints: Int
    var multiplier: Int
}
